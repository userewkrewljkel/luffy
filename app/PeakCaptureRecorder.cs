using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NAudio.Wave;

namespace LufsTaskbarMeter;

// Owns a rolling five-second (configurable) PCM/float history. All calls into
// ProcessBuffer are made from the WASAPI callback, with no synchronous disk IO.
// The completed clip is written on a background thread. No network activity.
internal sealed class PeakCaptureRecorder : IDisposable
{
    private readonly object gate=new();
    private readonly Queue<(byte[] Data,int Frames)> recent=new();
    private readonly List<Task> writers=new();
    private WaveFormat? format;
    private int recentFrames, byteAlign;
    private MemoryStream? active;
    private int recordedFrames, framesRemaining;
    private int firstPeakFrame, nextPeakMaxFrames;
    private double strongestPeak;
    private DateTime triggeredAt;
    private bool armed=true, enabled;
    private double thresholdDb=-1;
    private int beforeSeconds=5,afterSeconds=5;
    private string folder=Settings.DefaultPeakFolder;
    private const int MaximumClipSeconds=60;
    // Bound allocations even on multichannel / high-sample-rate devices.
    private const int MaximumHistoryBytes=64*1024*1024;
    private const int MaximumClipBytes=128*1024*1024;
    public string? LastSavedFile {get;private set;}
    public string? LastError {get;private set;}

    public void Configure(bool use,double limit,int before,int after,string directory)
    {
        lock(gate)
        {
            bool switching=!use || limit!=thresholdDb || beforeSeconds!=before || afterSeconds!=after || folder!=directory;
            if(switching)FinishActive(partial:true);
            enabled=use;
            thresholdDb=double.IsFinite(limit)?Math.Clamp(limit,-30,6):-1;
            beforeSeconds=Math.Clamp(before,1,30);
            afterSeconds=Math.Clamp(after,1,30);
            folder=string.IsNullOrWhiteSpace(directory)?Settings.DefaultPeakFolder:directory;
            if(switching){recent.Clear();recentFrames=0;armed=true;}
        }
    }
    public void ChangeFormat(WaveFormat newFormat)
    {
        lock(gate)
        {
            FinishActive(partial:true);
            recent.Clear();recentFrames=0;armed=true;
            format=newFormat;
            byteAlign=newFormat.BlockAlign;
        }
    }
    public void Interrupt()
    {
        lock(gate)
        {
            FinishActive(partial:true);
            recent.Clear();recentFrames=0;armed=true;
        }
    }
    public void ProcessBuffer(byte[] buffer,int length,int frames,double peak)
    {
        lock(gate)
        {
            if(!enabled || format==null || byteAlign==0 || frames<=0 || length<=0)return;
            try
            {
            var block=new byte[length];
            Buffer.BlockCopy(buffer,0,block,0,length);
            double trigger=Math.Pow(10,thresholdDb/20);
            bool loud=peak>=trigger;
            bool rising=loud && armed;
            if(!loud && peak < trigger*Math.Pow(10,-0.5/20))armed=true;
            if(loud)armed=false;
            if(active==null && rising)
            {
                active=new MemoryStream((int)Math.Min((long)format.AverageBytesPerSecond * (beforeSeconds+afterSeconds+1), 32L*1024*1024));
                foreach(var part in recent)active.Write(part.Data,0,part.Data.Length);
                recordedFrames=recentFrames;
                firstPeakFrame=recordedFrames;
                framesRemaining=afterSeconds*format.SampleRate;
                nextPeakMaxFrames=MaximumClipSeconds*format.SampleRate;
                strongestPeak=peak;
                triggeredAt=DateTime.Now;
            }
            if(active!=null)
            {
                active.Write(block,0,block.Length);
                recordedFrames+=frames;
                strongestPeak=Math.Max(strongestPeak,peak);
                if(loud)framesRemaining=afterSeconds*format.SampleRate;
                else framesRemaining-=frames;
                if(framesRemaining<=0 || recordedFrames>=nextPeakMaxFrames || active.Length>=MaximumClipBytes)
                    FinishActive(partial:recordedFrames>=nextPeakMaxFrames || active.Length>=MaximumClipBytes);
            }
            recent.Enqueue((block,frames));
            recentFrames+=frames;
            int maxHistory=beforeSeconds*format.SampleRate;
            while((recentFrames>maxHistory || (long)recentFrames*byteAlign>MaximumHistoryBytes) && recent.Count>0)
            {
                // Ring buffer precision is one callback, typically 10 ms.
                var drop=recent.Dequeue();recentFrames-=drop.Frames;
            }
            }
            catch(Exception ex)
            {
                // Peak Capture must never break the audio callback or loudness
                // calculation. Disable recording and expose the error in Settings.
                LastError="Peak Capture disabled: "+ex.Message;
                enabled=false;
                active?.Dispose();active=null;
                recent.Clear();recentFrames=0;
            }
        }
    }
    // Called only under gate.
    private void FinishActive(bool partial)
    {
        if(active==null || recordedFrames<=0 || format==null){active?.Dispose();active=null;return;}
        MemoryStream clip=active;active=null;
        int peakAt=firstPeakFrame;
        int sr=format.SampleRate;
        double db=strongestPeak>0?20*Math.Log10(strongestPeak):thresholdDb;
        string destination=folder;
        string prefix=$"LUFFY_Peak_{triggeredAt:yyyy-MM-dd_HH-mm-ss}_{db.ToString("+0.0;-0.0;0.0",CultureInfo.InvariantCulture)}dBTP";
        if(partial)prefix+="_partial";
        string id=Guid.NewGuid().ToString("N")[..6];
        string path=Path.Combine(destination,$"{prefix}_{id}.wav");
        WaveFormat waveFormat=format;
        var task=Task.Run(()=>
        {
            try
            {
                Directory.CreateDirectory(destination);
                clip.Position=0;
                using(var writer=new WaveFileWriter(path,waveFormat))
                    clip.CopyTo(writer);
                // Text metadata documents the approximate location of the first
                // triggering buffer (nearest audio callback, not sub-sample precision).
                File.WriteAllText(path+".txt",$"First peak at approximately {(double)peakAt/sr:0.000} seconds in the WAV.\nMaximum trigger: {db:0.00} dBTP\nSource format: {waveFormat}\n");
                lock(gate){LastSavedFile=path;LastError=null;}
            }
            catch(Exception ex)
            {
                lock(gate)LastError=ex.Message;
                Program.LogError(ex,"Saving Peak Capture WAV");
            }
            finally {clip.Dispose();}
        });
        writers.Add(task);
        writers.RemoveAll(x=>x.IsCompleted);
    }
    // After recording has been disabled, wait until any already queued WAV
    // and metadata writes finish before enumerating files for bulk deletion.
    public Task WaitForPendingWritesAsync()
    {
        lock(gate) return Task.WhenAll(writers.ToArray());
    }
    public void Dispose()
    {
        Task[] remaining;
        lock(gate)
        {
            FinishActive(partial:true);
            recent.Clear();recentFrames=0;enabled=false;
            remaining=writers.ToArray();
        }
        try {Task.WaitAll(remaining,TimeSpan.FromSeconds(5));}catch(Exception ex){Program.LogError(ex,"Finishing Peak Capture");}
    }
}
