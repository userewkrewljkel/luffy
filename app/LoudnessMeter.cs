using System;
using System.Collections.Generic;
using System.Linq;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace LufsTaskbarMeter;

// EBU R128 400ms gated integrated + 3s short-term. True peak: 4x polyphase FIR, normalized windowed-sinc.
internal sealed class LoudnessMeter : IDisposable
{
    private readonly object sync = new();
    private readonly PeakCaptureRecorder peakRecorder = new();
    public string? LastPeakCaptureFile => peakRecorder.LastSavedFile;
    public string? LastPeakCaptureError => peakRecorder.LastError;
    public void ConfigurePeakCapture(Settings options) => peakRecorder.Configure(
        options.PeakCaptureEnabled,options.PeakLimit,options.PeakBeforeSeconds,
        options.PeakAfterSeconds,options.PeakCaptureFolder);
    public Task WaitForPeakCaptureWritesAsync() => peakRecorder.WaitForPendingWritesAsync();
    private WasapiCapture? capture;
    private volatile bool captureActive;
    private volatile bool captureStopped;
    private long capturedBuffers;
    public long CapturedBuffers => System.Threading.Interlocked.Read(ref capturedBuffers);
    private MMDevice? selected;
    private Biquad[] shelves = [], highPasses = [];
    private double[] weights = [];
    private int channels, hopSize, hopCount, sampleBytes = 4;
    private enum SampleKind { Float32, Pcm16, Pcm24, Pcm32 }
    private SampleKind sampleKind = SampleKind.Float32;
    private string captureFormat = "Not started";
    private long processedBuffers, processedFrames, emptyBuffers;
    private double lastInputPeak, lastInputRms;
    private double pendingStereoLeft, pendingStereoRight;
    public (double Left, double Right) ConsumeStereoPeaks()
    {
        lock(sync)
        {
            double left=pendingStereoLeft,right=pendingStereoRight;
            pendingStereoLeft=pendingStereoRight=0;
            return (left,right);
        }
    }
    private DateTime lastInputAtUtc = DateTime.MinValue;
    public InputSignalSnapshot ReadInputSignal()
    {
        lock(sync)
        {
            double? peak = lastInputPeak > 0 ? 20*Math.Log10(lastInputPeak) : null;
            double? rms = lastInputRms > 0 ? 20*Math.Log10(lastInputRms) : null;
            return new InputSignalSnapshot(captureFormat, processedBuffers, processedFrames,
                emptyBuffers, peak, rms, lastInputAtUtc, paused, Error, captureActive && !captureStopped);
        }
    }
    private double hopEnergy, rollingEnergy;
    private readonly Queue<double> fourHops = new();
    private readonly Queue<double> thirtyHops = new();
    private double shortEnergy;
    // Block energies are kept in an ordered, balanced index, so the gated
    // integrated result can be retrieved without rescanning hour-long sessions.
    private readonly LoudnessEnergyIndex gatedBlocks = new();
    private readonly double absoluteGateEnergy = Math.Pow(10, (-70 - Offset) / 10.0);
    private bool integratedDirty = true;
    private double? integratedCache;
    private double maxShort = double.NegativeInfinity;
    private TruePeakDetector[] tpDetectors = [];
    private double maxTP = double.NegativeInfinity;
    private double lastBufferTP;
    private long latestBufferSequence;
    private readonly Queue<double> bufferedPeaks = new();
    private bool peakArmed = true;
    private double peakTriggerLimit = double.NaN;
    public (long sequence,double peak) ReadLatestBufferPeak() {lock(sync) return (latestBufferSequence,lastBufferTP); }
    public void ClearPendingPeakEvents() { lock(sync) { bufferedPeaks.Clear(); peakArmed=true; } }
    public bool ConsumePeakCrossing(double limitDbtp)
    {
        lock(sync)
        {
            if(!double.IsFinite(limitDbtp))return false;
            if(limitDbtp != peakTriggerLimit)
            {
                peakTriggerLimit=limitDbtp;
                peakArmed=true;
            }
            double trigger=Math.Pow(10,limitDbtp/20.0);
            double release=trigger*Math.Pow(10,-0.5/20.0);
            bool crossed=false;
            while(bufferedPeaks.Count>0)
            {
                double current=bufferedPeaks.Dequeue();
                if(current>=trigger && peakArmed) { crossed=true;peakArmed=false; }
                else if(current<release) peakArmed=true;
            }
            return crossed;
        }
    }
    private bool paused;
    private long frameIndex;
    private int sampleRate;
    private double maxSamplePeak = double.NegativeInfinity;
    private long maxSampleFrame = -1, maxTrueFrame = -1;
    private int maxSampleChannel = -1, maxTrueChannel = -1;
    private double maxTrueAtSample = 0;
    private string? pendingEventLog;
    public string AudioFormat { get { lock(sync) return captureFormat; } }
    public string? DiagnosticLogPath {get { lock(sync) return pendingEventLog; } }
    private readonly List<string> peakEvents = new();
    public string[] GetPeakEvents() { lock(sync) return peakEvents.TakeLast(30).ToArray(); }
    public double SecondsForFrame(long frame) { lock(sync) return frame < 0 || sampleRate <= 0 ? -1 : (double)frame / sampleRate; }
    public DiagnosticSnapshot ReadDiagnostics()
    {
        lock(sync)
        {
            double? sp = maxSamplePeak > 0 ? 20 * Math.Log10(maxSamplePeak) : null;
            double? tp = maxTP > 0 ? 20 * Math.Log10(maxTP) : null;
            return new DiagnosticSnapshot(sp, tp, SecondsForFrameInternal(maxSampleFrame), SecondsForFrameInternal(maxTrueFrame),
                maxSampleChannel+1, maxTrueChannel+1, maxTrueAtSample>0 ? 20*Math.Log10(maxTrueAtSample) : null,
                AudioFormat, frameIndex>0 ? (double)frameIndex/sampleRate : 0);
        }
    }
    private double? SecondsForFrameInternal(long frame) => frame < 0 || sampleRate <= 0 ? null : (double)frame / sampleRate;

    private const double Offset = -0.691;
    public string? Error { get; private set; }
    public string SourceName { get; private set; } = "Default output";
    public bool IsRunning => captureActive && !captureStopped;
    public bool Paused { get { lock(sync) return paused; } }

    public void Start(string? deviceId = null, bool inputSource = false, bool startPaused = false)
    {
        Stop();
        lock(sync)
        {
            Error = null; ResetInternal(); paused = startPaused;
            captureFormat="Connecting…";
            processedBuffers=0; processedFrames=0; emptyBuffers=0;
            lastInputPeak=0; lastInputRms=0; lastInputAtUtc=DateTime.MinValue;
            pendingStereoLeft=pendingStereoRight=0;
        }
        System.Threading.Interlocked.Exchange(ref capturedBuffers, 0);
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            selected = deviceId == null
                ? enumerator.GetDefaultAudioEndpoint(inputSource ? DataFlow.Capture : DataFlow.Render, Role.Multimedia)
                : enumerator.GetDevice(deviceId);
            if (selected.DataFlow != (inputSource ? DataFlow.Capture : DataFlow.Render))
                throw new InvalidOperationException("Saved audio device type changed. Choose the source again.");
            SourceName = selected.FriendlyName;
            capture = inputSource ? new WasapiCapture(selected) : new WasapiLoopbackCapture(selected);
            var format = capture.WaveFormat;
            bool floatSamples = format.Encoding == WaveFormatEncoding.IeeeFloat ||
                (format is WaveFormatExtensible ext && ext.SubFormat == new Guid("00000003-0000-0010-8000-00AA00389B71"));
            bool pcmSamples = format.Encoding == WaveFormatEncoding.Pcm ||
                (format is WaveFormatExtensible pcmExt && pcmExt.SubFormat == new Guid("00000001-0000-0010-8000-00AA00389B71"));
            sampleKind = (floatSamples,pcmSamples,format.BitsPerSample) switch
            {
                (true, _, 32) => SampleKind.Float32,
                (_, true, 16) => SampleKind.Pcm16,
                (_, true, 24) => SampleKind.Pcm24,
                (_, true, 32) => SampleKind.Pcm32,
                _ => throw new NotSupportedException($"Unsupported input format: {format}. Supported: 16/24/32-bit PCM and 32-bit float.")
            };
            sampleBytes = format.BitsPerSample / 8;
            captureFormat = $"{format.SampleRate} Hz / {format.Channels} channels / {format.BitsPerSample}-bit {sampleKind}";
            channels = format.Channels; sampleRate = format.SampleRate;
            peakRecorder.ChangeFormat(format);
            if (channels < 1) throw new InvalidOperationException("No audio channels.");
            hopSize = (int)Math.Round(format.SampleRate / 10.0);
            shelves = new Biquad[channels]; highPasses = new Biquad[channels];
            weights = new double[channels]; tpDetectors = new TruePeakDetector[channels];
            for (int c = 0; c < channels; c++)
            {
                shelves[c] = Biquad.Shelf(format.SampleRate);
                highPasses[c] = Biquad.HighPass(format.SampleRate);
                weights[c] = channels >= 6 && c == 3 ? 0 : channels >= 6 && c >= 4 ? 1.41 : 1;
                tpDetectors[c] = new TruePeakDetector();
            }
            capture.DataAvailable += Receive;
            var currentCapture = capture;
            capture.RecordingStopped += (_, e) =>
            {
                if (!ReferenceEquals(capture, currentCapture)) return;
                captureStopped = true;
                captureActive = false;
                lock(sync) Error = e.Exception?.Message ?? "Windows audio capture stopped. Select Restart audio capture.";
            };
            capture.StartRecording();
            captureStopped = false;
            captureActive = true;
        }
        catch(Exception ex) { Error = ex.Message; Stop(); }
    }
    // Use a completely separate instance for preview playback. Do not attach
    // to a Windows input, do not configure PeakCaptureRecorder, and do not
    // change the ongoing live measurement instance.
    public void StartOffline(WaveFormat format)
    {
        if(format.Channels < 1 || format.SampleRate < 8000 || format.SampleRate > 384000 ||
           format.Encoding != WaveFormatEncoding.IeeeFloat || format.BitsPerSample != 32)
            throw new NotSupportedException("Playback analysis needs 32-bit float PCM from AudioFileReader.");
        Stop();
        lock(sync)
        {
            Error=null;channels=format.Channels;sampleRate=format.SampleRate;
            sampleBytes=4;sampleKind=SampleKind.Float32;
            hopSize=(int)Math.Round(sampleRate/10.0);
            shelves=new Biquad[channels];highPasses=new Biquad[channels];
            weights=new double[channels];tpDetectors=new TruePeakDetector[channels];
            for(int c=0;c<channels;c++)
            {
                shelves[c]=Biquad.Shelf(sampleRate);
                highPasses[c]=Biquad.HighPass(sampleRate);
                weights[c]=channels>=6 && c==3?0:channels>=6 && c>=4?1.41:1;
                tpDetectors[c]=new TruePeakDetector();
            }
            ResetInternal();paused=false;
            captureFormat=$"{sampleRate} Hz / {channels} channels / 32-bit Float (WAV preview)";
            SourceName="Peak Capture WAV (original level)";
            captureActive=true;captureStopped=false;
        }
    }
    public void FeedOffline(float[] samples,int offset,int count)
    {
        if(count<=0 || !captureActive)return;
        // The existing BS.1770/True Peak path processes an IEEE float buffer;
        // this is a distinct meter whose recorder remains disabled.
        var bytes=new byte[count*sizeof(float)];
        Buffer.BlockCopy(samples,offset*sizeof(float),bytes,0,bytes.Length);
        Receive(null,new WaveInEventArgs(bytes,bytes.Length));
    }

    public void SetPaused(bool state)
    {
        lock(sync)
        {
            if(paused==state)return;
            if(state)peakRecorder.Interrupt();
            paused=state;
            bufferedPeaks.Clear();
            pendingStereoLeft=pendingStereoRight=0;
            peakArmed=true; // Never replay queued warning peaks.
            if(!state)
            {
                // Resume with a fresh ~3 s short-term window while preserving
                // the integrated loudness blocks and max-short statistic.
                thirtyHops.Clear();
                shortEnergy=0;
                hopCount=0;
                hopEnergy=0;
            }
        }
    }
    private void Receive(object? sender, WaveInEventArgs e)
    {
        System.Threading.Interlocked.Increment(ref capturedBuffers);
        lock(sync)
        {
            if (paused || channels == 0 || !captureActive) return;
            int frames = e.BytesRecorded / (channels * sampleBytes);
            lastInputAtUtc=DateTime.UtcNow;
            processedBuffers++;
            if(frames==0) { emptyBuffers++; return; }
            processedFrames+=frames;
            double inputPeak=0, inputSquared=0;
            double bufferMax=0;
            double bufferLeft=0, bufferRight=0;
            for(int f=0; f<frames; f++)
            {
                double energy=0;
                for(int c=0;c<channels;c++)
                {
                    int offset = (f * channels + c) * sampleBytes;
                    double sample;
                    switch(sampleKind)
                    {
                        case SampleKind.Float32: sample=BitConverter.ToSingle(e.Buffer,offset);break;
                        case SampleKind.Pcm16: sample=BitConverter.ToInt16(e.Buffer,offset)/32768.0;break;
                        case SampleKind.Pcm24:
                            int packed=e.Buffer[offset] | (e.Buffer[offset+1]<<8) | (e.Buffer[offset+2]<<16);
                            if((packed & 0x800000)!=0)packed|=unchecked((int)0xff000000);
                            sample=packed/8388608.0;
                            break;
                        case SampleKind.Pcm32: sample=BitConverter.ToInt32(e.Buffer,offset)/2147483648.0;break;
                        default: sample=0;break;
                    }
                    if (!double.IsFinite(sample)) sample=0;
                    inputPeak=Math.Max(inputPeak,Math.Abs(sample));
                    // Stereo: use channels 1 and 2, or duplicate mono.
                    // Multichannel captures deliberately show front L/R only.
                    if(c==0)bufferLeft=Math.Max(bufferLeft,Math.Abs(sample));
                    if(c==1)bufferRight=Math.Max(bufferRight,Math.Abs(sample));
                    inputSquared+=sample*sample;
                    double z=highPasses[c].Run(shelves[c].Run(sample));
                    energy+=weights[c]*z*z;
                    // True-peak sampling is separate from K-weighting. The detector
                    // uses an oversampled low-pass FIR and a per-channel delay line.
                    double abs = Math.Abs(sample);
                    if(abs>maxSamplePeak)
                    {
                        maxSamplePeak=abs;maxSampleFrame=frameIndex;maxSampleChannel=c;
                        LogPeakEvent("SP",abs,frameIndex,c);
                    }
                    double detected=tpDetectors[c].Process(sample);
                    bufferMax=Math.Max(bufferMax,detected);
                    if(detected>maxTP)
                    {
                        maxTP=detected;maxTrueFrame=frameIndex;maxTrueChannel=c;maxTrueAtSample=abs;
                        LogPeakEvent("TP",detected,frameIndex,c);
                    }
                }
                frameIndex++;
                hopEnergy+=energy;
                if(++hopCount==hopSize)
                {
                    double hop=hopEnergy/hopCount;
                    hopCount=0; hopEnergy=0;
                    fourHops.Enqueue(hop); rollingEnergy+=hop;
                    if(fourHops.Count>4) rollingEnergy-=fourHops.Dequeue();
                    if(fourHops.Count==4)
                    {
                        double blockEnergy=Math.Max(0,rollingEnergy/4);
                        if(blockEnergy>=absoluteGateEnergy)
                        {
                            gatedBlocks.Add(blockEnergy);
                            integratedDirty=true;
                        }
                    }
                    thirtyHops.Enqueue(hop); shortEnergy+=hop;
                    if(thirtyHops.Count>30) shortEnergy-=thirtyHops.Dequeue();
                    if(thirtyHops.Count==30 && shortEnergy>0)
                        maxShort=Math.Max(maxShort,Offset+10*Math.Log10(shortEnergy/30));
                }
            }
            if(channels==1)bufferRight=bufferLeft;
            pendingStereoLeft=Math.Max(pendingStereoLeft,bufferLeft);
            pendingStereoRight=Math.Max(pendingStereoRight,bufferRight);
            lastInputPeak=inputPeak;
            lastInputRms=Math.Sqrt(inputSquared/(frames*channels));
            peakRecorder.ProcessBuffer(e.Buffer,frames*channels*sampleBytes,frames,bufferMax);
            lastBufferTP=bufferMax; latestBufferSequence++;
            // Retain each audio callback's peak until the slower UI polls it.
            // Brief peaks must not vanish between UI timer ticks.
            bufferedPeaks.Enqueue(bufferMax);
            if(bufferedPeaks.Count>4096) bufferedPeaks.Dequeue();
        }
    }
    private void LogPeakEvent(string kind,double value,long frame,int channel)
    {
        // Keep only significant peak records, avoiding huge logs from every sample.
        if(value <= 0.01) return;
        string row=$"{kind}  {20*Math.Log10(value):+0.00;-0.00;0.00} dB  {((double)frame/sampleRate):0.000}s  ch {channel+1}";
        if(peakEvents.Count == 0 || !peakEvents[^1].Equals(row,StringComparison.Ordinal))
        {
            peakEvents.Add(row);
            if(peakEvents.Count>250) peakEvents.RemoveRange(0,peakEvents.Count-250);
        }
    }
    public Snapshot Read()
    {
        lock(sync)
        {
            if(integratedDirty)
            {
                integratedCache=gatedBlocks.Integrated(absoluteGateEnergy,Offset);
                integratedDirty=false;
            }
            double? shortTerm=thirtyHops.Count==30 && shortEnergy>0 ? Offset+10*Math.Log10(shortEnergy/30) : null;
            return new Snapshot(integratedCache,shortTerm,double.IsFinite(maxShort)?maxShort:null,
                maxTP>0?20*Math.Log10(maxTP):null,paused,Error);
        }
    }
    // Reset the displayed maximum without changing capture, filters, or loudness history.
    public void ResetTruePeakMaximum()
    {
        lock(sync)
        {
            maxTP=double.NegativeInfinity;
            maxTrueFrame=-1;
            maxTrueChannel=-1;
            maxTrueAtSample=0;
        }
    }
    public void Reset() { lock(sync) ResetInternal(); }
    private void ResetInternal()
    {
        pendingStereoLeft=pendingStereoRight=0;
        lastBufferTP=0;latestBufferSequence++;
        bufferedPeaks.Clear();peakArmed=true;peakTriggerLimit=double.NaN;
        hopCount=0;hopEnergy=0;rollingEnergy=0;shortEnergy=0;maxShort=double.NegativeInfinity;maxTP=double.NegativeInfinity;
        maxSamplePeak=double.NegativeInfinity;maxSampleFrame=-1;maxTrueFrame=-1;maxSampleChannel=-1;maxTrueChannel=-1;
        maxTrueAtSample=0;frameIndex=0;peakEvents.Clear();
        fourHops.Clear();thirtyHops.Clear();gatedBlocks.Clear();
        integratedCache=null;integratedDirty=true;
        foreach(var b in shelves) b.Reset(); foreach(var b in highPasses) b.Reset();
        foreach(var detector in tpDetectors) detector.Reset();
    }
    public void Stop()
    {
        captureActive=false; captureStopped=true;
        peakRecorder.Interrupt();
        var old=capture; capture=null;
        if(old!=null) { try { old.StopRecording(); } catch {} old.Dispose(); }
        selected?.Dispose(); selected=null;
    }
    public void Dispose() { Stop(); peakRecorder.Dispose(); }

    // Reconstructs inter-sample peaks with 4x polyphase FIR interpolation.
    // 49-tap Kaiser-windowed sinc, beta=8.6 (about 80 dB sidelobe attenuation).
    // Four phases include the input-sample phase. A full tap window is required
    // before output, so no startup transients are mistaken for peaks.
    // Oversampling estimates can differ slightly from reference TP meters:
    // verify against ITU-R BS.1770 test signals before compliance use.
    private sealed class TruePeakDetector
    {
        private const int Radius = 24;
        private const int TapCount = 2 * Radius + 1;
        private const int Phases = 4;
        private const double Beta = 8.6;
        private static readonly double[][] Coefficients = MakeCoefficients();
        private readonly double[] delay = new double[TapCount];
        private int writeIndex, count;

        public double Process(double sample)
        {
            delay[writeIndex] = sample;
            writeIndex = (writeIndex + 1) % TapCount;
            if (count < TapCount) count++;
            // Always include the actual sampled peak, including startup.
            double peak = Math.Abs(sample);
            if (count < TapCount) return peak;
            // writeIndex now points at the oldest element in the ring buffer.
            for (int phase = 0; phase < Phases; phase++)
            {
                double value = 0;
                var coeff = Coefficients[phase];
                for (int k = 0; k < TapCount; k++)
                    value += delay[(writeIndex + k) % TapCount] * coeff[k];
                peak = Math.Max(peak, Math.Abs(value));
            }
            return peak;
        }

        public void Reset()
        {
            Array.Clear(delay);
            count = 0;
            writeIndex = 0;
        }

        private static double[][] MakeCoefficients()
        {
            var result = new double[Phases][];
            double denom = Bessel0(Beta);
            for (int p = 0; p < Phases; p++)
            {
                var coeff = new double[TapCount];
                double sum = 0;
                for (int k = 0; k < TapCount; k++)
                {
                    double t = k - Radius - p / (double)Phases;
                    double sinc = Math.Abs(t) < 1e-12 ? 1 : Math.Sin(Math.PI * t) / (Math.PI * t);
                    double r = (k - Radius) / (double)Radius;
                    double window = Bessel0(Beta * Math.Sqrt(Math.Max(0, 1 - r*r))) / denom;
                    coeff[k] = sinc * window;
                    sum += coeff[k];
                }
                // Constant amplitude must remain constant on all phases.
                for (int k = 0; k < TapCount; k++) coeff[k] /= sum;
                result[p] = coeff;
            }
            return result;
        }

        private static double Bessel0(double x)
        {
            double sum = 1, term = 1, z = x*x/4;
            for (int k = 1; k < 30; k++)
            {
                term *= z / (k*k);
                sum += term;
                if (term < 1e-15 * sum) break;
            }
            return sum;
        }
    }

    private sealed class Biquad
    {
        private readonly double b0,b1,b2,a1,a2;
        private double x1,x2,y1,y2;
        private Biquad(double nb0,double nb1,double nb2,double a0,double na1,double na2)
        { b0=nb0/a0;b1=nb1/a0;b2=nb2/a0;a1=na1/a0;a2=na2/a0; }
        public double Run(double x){double y=b0*x+b1*x1+b2*x2-a1*y1-a2*y2;x2=x1;x1=x;y2=y1;y1=y;return y;}
        public void Reset(){x1=x2=y1=y2=0;}
        public static Biquad Shelf(double fs)
        {
            double k=Math.Tan(Math.PI*1681.974450955533/fs), q=0.7071752369554196;
            double vh=Math.Pow(10,3.999843853973347/20), vb=Math.Pow(vh,0.4996667741545416);
            double a0=1+k/q+k*k;
            return new Biquad(vh+vb*k/q+k*k,2*(k*k-vh),vh-vb*k/q+k*k,a0,2*(k*k-1),1-k/q+k*k);
        }
        public static Biquad HighPass(double fs)
        {
            double k=Math.Tan(Math.PI*38.13547087602444/fs),q=0.5003270373238773;
            double a0=1+k/q+k*k;
            return new Biquad(1,-2,1,a0,2*(k*k-1),1-k/q+k*k);
        }
    }
}
internal record InputSignalSnapshot(string Format,long Buffers,long Frames,long EmptyBuffers,
    double? LastPeakDbfs,double? LastRmsDbfs,DateTime LastBufferAtUtc,bool Paused,string? Error,bool Running);
internal record Snapshot(double? Integrated,double? ShortTerm,double? MaxShortTerm,double? TruePeak,bool Paused,string? Error);

internal record DiagnosticSnapshot(double? SamplePeak, double? TruePeak, double? SampleTime, double? TrueTime, int SampleChannel, int TrueChannel, double? SampleAtTruePeak, string Format, double MeasuredSeconds);
