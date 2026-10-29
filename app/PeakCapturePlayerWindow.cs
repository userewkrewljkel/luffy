using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace LufsTaskbarMeter;

// A separate listening tool, not part of the capture or loudness calculation.
// All output uses Windows WASAPI shared mode. Original WAV files are read-only.
internal sealed class PeakCapturePlayerWindow : Form
{
    private sealed record Clip(string Path)
    {
        public override string ToString() => System.IO.Path.GetFileNameWithoutExtension(Path);
    }
    private sealed record OutputChoice(string Label, string? Id)
    {
        public override string ToString() => Label;
    }

    private readonly Settings cfg;
    private readonly Action<bool> listeningChanged;
    private readonly ListBox clipList=new(){Dock=DockStyle.Fill,IntegralHeight=false};
    private readonly ComboBox outputChoices=new(){DropDownStyle=ComboBoxStyle.DropDownList,Dock=DockStyle.Fill};
    private readonly Label title=new(){Dock=DockStyle.Fill,Text="Choose a Peak Capture",AutoEllipsis=true};
    private readonly Label details=new(){Dock=DockStyle.Fill,ForeColor=Color.DimGray};
    private readonly Label timeInfo=new(){Dock=DockStyle.Fill,Text="0:00.0 / 0:00.0"};
    private readonly Label gainInfo=new(){AutoSize=true,TextAlign=ContentAlignment.MiddleRight};
    private readonly Label status=new(){Dock=DockStyle.Fill,ForeColor=Color.DimGray};
    private readonly Button play=new(){Text="Play",Width=82,Height=32};
    private readonly Button stop=new(){Text="Stop",Width=72,Height=32};
    private readonly TrackBar gain=new(){Minimum=-24,Maximum=12,TickFrequency=6,SmallChange=1,LargeChange=3,Width=300};
    private readonly WaveformView waveform=new(){Dock=DockStyle.Fill,BackColor=Color.FromArgb(31,33,38)};
    private readonly System.Windows.Forms.Timer clock=new(){Interval=50};
    private WasapiOut? output;
    private MMDevice? playbackEndpoint;
    private AudioFileReader? reader;
    private PreviewGain? gainProvider;
    private LoudnessMeter? playbackMeter;
    private readonly object readerGate=new();
    private string? currentPath;
    private string? requestedPath;
    private bool listening;
    private int waveformGeneration;
    private CancellationTokenSource? waveformCancellation;
    private int refreshCounter;
    private string[] lastClips=[];
    private bool updatingOutputs;

    public PeakCapturePlayerWindow(Settings settings,Action<bool> onListeningChanged)
    {
        cfg=settings;listeningChanged=onListeningChanged;
        Text="LUFFY v3.14 – Peak Capture Player";
        StartPosition=FormStartPosition.CenterScreen;
        Size=new Size(935,615);MinimumSize=new Size(750,550);
        FormBorderStyle=FormBorderStyle.Sizable;ShowInTaskbar=true;

        var root=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Padding=new Padding(12)};
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,250));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        Controls.Add(root);

        var left=new TableLayoutPanel{Dock=DockStyle.Fill,RowCount=4,ColumnCount=1};
        left.RowStyles.Add(new RowStyle(SizeType.Absolute,35));
        left.RowStyles.Add(new RowStyle(SizeType.Absolute,35));
        left.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        left.RowStyles.Add(new RowStyle(SizeType.Absolute,35));
        left.Controls.Add(new Label{Text="Saved Peak Captures",Dock=DockStyle.Fill,Font=new Font("Segoe UI",11,FontStyle.Bold)},0,0);
        var refreshList=new Button{Text="Refresh",Dock=DockStyle.Fill};
        refreshList.Click+=(_,_)=>RefreshClips();
        left.Controls.Add(refreshList,0,1);
        clipList.SelectedIndexChanged+=(_,_)=>LoadSelected();
        left.Controls.Add(clipList,0,2);
        var openFolder=new Button{Text="Open capture folder",Dock=DockStyle.Fill};
        openFolder.Click+=(_,_)=>OpenFolder();
        left.Controls.Add(openFolder,0,3);
        root.Controls.Add(left,0,0);

        var right=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=9,Padding=new Padding(12,0,0,0)};
        right.RowStyles.Add(new RowStyle(SizeType.Absolute,35));
        right.RowStyles.Add(new RowStyle(SizeType.Absolute,28));
        right.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        right.RowStyles.Add(new RowStyle(SizeType.Absolute,29));
        right.RowStyles.Add(new RowStyle(SizeType.Absolute,45));
        right.RowStyles.Add(new RowStyle(SizeType.Absolute,87));
        right.RowStyles.Add(new RowStyle(SizeType.Absolute,64));
        right.RowStyles.Add(new RowStyle(SizeType.Absolute,48));
        right.RowStyles.Add(new RowStyle(SizeType.Absolute,55));
        root.Controls.Add(right,1,0);
        title.Font=new Font("Segoe UI",12,FontStyle.Bold);
        right.Controls.Add(title,0,0);
        right.Controls.Add(details,0,1);
        waveform.SeekRequested+=Seek;
        right.Controls.Add(waveform,0,2);
        right.Controls.Add(timeInfo,0,3);
        var controls=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false};
        var prev=new Button{Text="Previous",Height=32,Width=85};
        var next=new Button{Text="Next",Height=32,Width=70};
        prev.Click+=(_,_)=>MoveSelection(-1); next.Click+=(_,_)=>MoveSelection(1);
        play.Click+=(_,_)=>TogglePlayback();
        stop.Click+=(_,_)=>StopPlayback();
        controls.Controls.AddRange([prev,play,stop,next]);
        right.Controls.Add(controls,0,4);
        var gainArea=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=2};
        gainArea.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,75));
        gainArea.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,25));
        gainArea.Controls.Add(new Label{Text="Listening gain (WAV remains unchanged)",AutoSize=true},0,0);
        gainArea.Controls.Add(gainInfo,1,0);
        gain.Value=Math.Clamp(cfg.PreviewGainDb,-24,12);
        gain.ValueChanged+=(_,_)=>
        {
            cfg.PreviewGainDb=gain.Value;cfg.Save();UpdateGain();
        };
        gainArea.Controls.Add(gain,0,1);
        var zeroGain=new Button{Text="Original 0 dB",Width=130,Height=30};
        zeroGain.Click+=(_,_)=>gain.Value=0;
        gainArea.Controls.Add(zeroGain,1,1);
        right.Controls.Add(gainArea,0,5);
        var audioArea=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=2};
        audioArea.RowStyles.Add(new RowStyle(SizeType.Absolute,22));
        audioArea.RowStyles.Add(new RowStyle(SizeType.Absolute,38));
        audioArea.Controls.Add(new Label{Text="Playback output (Windows shared mode)",Dock=DockStyle.Fill},0,0);
        outputChoices.SelectedIndexChanged+=(_,_)=>
        {
            if(updatingOutputs || outputChoices.SelectedItem is not OutputChoice choice)return;
            if(listening)StopPlayback();
            cfg.PreviewOutputDeviceId=choice.Id;cfg.Save();
        };
        audioArea.Controls.Add(outputChoices,0,1);
        right.Controls.Add(audioArea,0,6);
        var tools=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false};
        var openWav=new Button{Text="Open WAV externally",Height=32,Width=170};
        openWav.Click+=(_,_)=>OpenExternally();
        var refreshDevices=new Button{Text="Refresh output devices",Height=32,Width=180};
        refreshDevices.Click+=(_,_)=>RefreshOutputDevices();
        tools.Controls.AddRange([openWav,refreshDevices]);
        right.Controls.Add(tools,0,7);
        status.Text="PLAYBACK mode measures the original WAV. The live session stays paused and resumes when playback stops.";
        right.Controls.Add(status,0,8);
        clock.Tick+=(_,_)=>
        {
            UpdatePosition();
            if(++refreshCounter%100==0 && Visible && !listening)RefreshClips();
        };
        clock.Start();
        FormClosed+=(_,_)=>
        {
            ++waveformGeneration;
            waveformCancellation?.Cancel();waveformCancellation?.Dispose();
            clock.Stop();StopPlayback();clock.Dispose();
        };
        RefreshOutputDevices();RefreshClips();UpdateGain();
    }

    public void ShowClip(string? path)
    {
        if(!string.IsNullOrWhiteSpace(path))requestedPath=path;
        RefreshClips();
        if(!Visible)Show();
        if(WindowState==FormWindowState.Minimized)WindowState=FormWindowState.Normal;
        BringToFront();Activate();
    }

    private void RefreshClips()
    {
        try
        {
            string[] paths=Directory.Exists(cfg.PeakCaptureFolder)
                ? Directory.GetFiles(cfg.PeakCaptureFolder,"LUFFY_Peak_*.wav")
                    .OrderByDescending(File.GetLastWriteTimeUtc).Take(300).ToArray()
                : [];
            if(paths.SequenceEqual(lastClips) && string.IsNullOrWhiteSpace(requestedPath))return;
            string? target=requestedPath ?? (clipList.SelectedItem as Clip)?.Path ?? currentPath;
            requestedPath=null;lastClips=paths;
            clipList.BeginUpdate();
            clipList.Items.Clear();
            foreach(string p in paths)clipList.Items.Add(new Clip(p));
            int match=Array.FindIndex(paths,p=>string.Equals(p,target,StringComparison.OrdinalIgnoreCase));
            clipList.EndUpdate();
            clipList.SelectedIndex=match>=0?match:paths.Length>0?0:-1;
            if(paths.Length==0)
            {
                StopPlayback();currentPath=null;
                title.Text="No Peak Captures yet";details.Text="Enable Peak Capture in Settings → True Peak";
                waveform.SetSamples([],null);timeInfo.Text="0:00.0 / 0:00.0";
            }
        }
        catch(Exception ex){status.Text="Unable to list clips: "+ex.Message;Program.LogError(ex,"Listing Peak Captures");}
    }
    private void LoadSelected()
    {
        if(clipList.SelectedItem is not Clip clip)return;
        if(string.Equals(currentPath,clip.Path,StringComparison.OrdinalIgnoreCase))return;
        waveformCancellation?.Cancel();waveformCancellation?.Dispose();
        waveformCancellation=new CancellationTokenSource();
        StopPlayback();requestedSeek=0;currentPath=clip.Path;
        title.Text=Path.GetFileName(clip.Path);
        double? marker=ReadMarker(clip.Path);
        waveform.SetSamples([],marker);
        try
        {
            using var input=new WaveFileReader(clip.Path);
            var duration=input.TotalTime;
            string? peakDb=ReadPeakValue(clip.Path);
            details.Text=$"{duration.TotalSeconds:0.00} sec  ·  {input.WaveFormat.SampleRate} Hz  ·  {input.WaveFormat.Channels} ch"+
                (peakDb==null?"":$"  ·  max {peakDb} dBTP");
            timeInfo.Text=$"0:00.0 / {FormatTime(duration)}  ·  trigger near {marker?.ToString("0.00",CultureInfo.InvariantCulture)??"?"} s";
            waveform.SetDuration(duration.TotalSeconds);
        }
        catch(Exception ex)
        {
            details.Text="Cannot read WAV: "+ex.Message;
            Program.LogError(ex,"Reading Peak Capture");
            return;
        }
        int generation=++waveformGeneration;
        string path=clip.Path;
        CancellationToken token=waveformCancellation.Token;
        _=Task.Run(()=>BuildWaveform(path,token),token)
            .ContinueWith(task=>
            {
                if(IsDisposed || !IsHandleCreated || generation!=waveformGeneration)return;
                try
                {
                    BeginInvoke((Action)(()=>
                    {
                        if(IsDisposed || generation!=waveformGeneration)return;
                        if(task.IsCompletedSuccessfully)waveform.SetSamples(task.Result,marker);
                        else if(task.IsFaulted && task.Exception!=null)Program.LogError(task.Exception,"Drawing Peak Capture waveform");
                    }));
                }
                catch(ObjectDisposedException){}
                catch(InvalidOperationException){}
            },TaskScheduler.Default);
    }

    private static float[] BuildWaveform(string path,CancellationToken cancellation)
    {
        const int bins=700;
        var peaks=new float[bins];
        using var input=new AudioFileReader(path);
        int channels=Math.Max(1,input.WaveFormat.Channels);
        long frames=Math.Max(1,(long)Math.Ceiling(input.TotalTime.TotalSeconds*input.WaveFormat.SampleRate));
        long at=0;
        var samples=new float[8192*channels];
        int count;
        while((count=input.Read(samples,0,samples.Length))>0)
        {
            cancellation.ThrowIfCancellationRequested();
            int frameCount=count/channels;
            for(int f=0;f<frameCount;f++)
            {
                float magnitude=0;
                for(int c=0;c<channels;c++)magnitude=Math.Max(magnitude,Math.Abs(samples[f*channels+c]));
                int bin=(int)Math.Clamp((at+f)*bins/frames,0,bins-1);
                peaks[bin]=Math.Max(peaks[bin],magnitude);
            }
            at+=frameCount;
        }
        return peaks;
    }
    private static double? ReadMarker(string wav)
    {
        try
        {
            string text=File.ReadAllText(wav+".txt");
            var match=Regex.Match(text,@"First peak at approximately\s+([0-9]+(?:[.,][0-9]+)?)",RegexOptions.IgnoreCase);
            if(match.Success && double.TryParse(match.Groups[1].Value.Replace(',','.'),NumberStyles.Float,CultureInfo.InvariantCulture,out var seconds))return seconds;
        }
        catch(IOException){}
        catch(UnauthorizedAccessException){}
        return null;
    }

    private static string? ReadPeakValue(string wav)
    {
        try
        {
            var match=Regex.Match(File.ReadAllText(wav+".txt"),@"Maximum trigger:\s*([+\-]?[0-9]+(?:[.,][0-9]+)?)",RegexOptions.IgnoreCase);
            if(match.Success)return match.Groups[1].Value;
        }
        catch(IOException){}
        catch(UnauthorizedAccessException){}
        return null;
    }
    private void RefreshOutputDevices()
    {
        updatingOutputs=true;
        try
        {
            outputChoices.Items.Clear();
            outputChoices.Items.Add(new OutputChoice("Windows default output",null));
            using var enumerator=new MMDeviceEnumerator();
            foreach(var dev in enumerator.EnumerateAudioEndPoints(DataFlow.Render,DeviceState.Active))
                outputChoices.Items.Add(new OutputChoice(dev.FriendlyName,dev.ID));
            int index=-1;
            for(int i=0;i<outputChoices.Items.Count;i++)
            {
                if((outputChoices.Items[i] as OutputChoice)?.Id==cfg.PreviewOutputDeviceId){index=i;break;}
            }
            outputChoices.SelectedIndex=index>=0?index:0;
        }
        catch(Exception ex)
        {
            status.Text="Output devices unavailable: "+ex.Message;
            if(outputChoices.Items.Count>0)outputChoices.SelectedIndex=0;
        }
        finally {updatingOutputs=false;}
    }
    public PlaybackReadout? ReadPlayback()
    {
        lock(readerGate)
        {
            if(!listening || reader==null || playbackMeter==null)return null;
            bool paused=output?.PlaybackState==PlaybackState.Paused;
            return new PlaybackReadout(playbackMeter.Read() with {Paused=paused},
                reader.CurrentTime,paused);
        }
    }
    public (double Left,double Right) ConsumePlaybackStereo()
    {
        lock(readerGate)return playbackMeter?.ConsumeStereoPeaks() ?? (0,0);
    }
    public void ToggleFromMeter()
    {
        if(listening)TogglePlayback();
    }
    public void ResetFromMeter()
    {
        lock(readerGate)
        {
            if(!listening || reader==null)return;
            reader.CurrentTime=TimeSpan.Zero;
            playbackMeter?.Reset();
            waveform.PositionSeconds=0;
        }
    }

    private void TogglePlayback()
    {
        if(output!=null)
        {
            if(output.PlaybackState==PlaybackState.Playing)
            {
                output.Pause();play.Text="Resume";
            }
            else
            {
                output.Play();play.Text="Pause";
            }
            return;
        }
        StartPlayback();
    }
    private void StartPlayback()
    {
        if(currentPath==null)return;
        AudioFileReader? pendingReader=null;
        MMDevice? pendingDevice=null;
        WasapiOut? pendingOutput=null;
        LoudnessMeter? pendingAnalyzer=null;
        try
        {
            var newReader=new AudioFileReader(currentPath);
            pendingReader=newReader;
            var analyzer=new LoudnessMeter();
            pendingAnalyzer=analyzer;
            analyzer.StartOffline(newReader.WaveFormat);
            var provider=new PreviewGain(newReader,readerGate,
                (samples,offset,count)=>analyzer.FeedOffline(samples,offset,count));
            provider.SetDecibels(cfg.PreviewGainDb);
            WasapiOut newOutput;
            if(outputChoices.SelectedItem is OutputChoice device && device.Id is string id)
            {
                using var enumerator=new MMDeviceEnumerator();
                pendingDevice=enumerator.GetDevice(id);
                newOutput=new WasapiOut(pendingDevice,AudioClientShareMode.Shared,true,150);
            }
            else newOutput=new WasapiOut(AudioClientShareMode.Shared,true,150);
            pendingOutput=newOutput;
            newOutput.Init(new SampleToWaveProvider(provider));
            newOutput.PlaybackStopped+=(_,args)=>
            {
                if(!IsHandleCreated || IsDisposed)return;
                try {BeginInvoke((Action)(()=>
                {
                    if(!ReferenceEquals(output,newOutput))return;
                    StopPlayback();
                    if(args.Exception!=null)
                    {
                        status.Text="Playback failed: "+args.Exception.Message;
                        Program.LogError(args.Exception,"Peak Capture playback");
                    }
                }));}
                catch(ObjectDisposedException){}
                catch(InvalidOperationException){}
            };
            lock(readerGate)
            {
                reader=newReader;gainProvider=provider;output=newOutput;
                playbackMeter=analyzer;
                playbackEndpoint=pendingDevice;
                pendingReader=null;pendingDevice=null;pendingOutput=null;pendingAnalyzer=null;
                if(requestedSeek>0)
                    reader.CurrentTime=TimeSpan.FromSeconds(reader.TotalTime.TotalSeconds*Math.Clamp(requestedSeek,0,1));
                requestedSeek=0;
            }
            listening=true;
            listeningChanged(true); // measurement and timer pause while previewing
            newOutput.Play();play.Text="Pause";
            status.Text="PLAYBACK mode: meters show the original WAV (not listening gain). Live session is preserved.";
        }
        catch(Exception ex)
        {
            StopPlayback();
            try {pendingOutput?.Dispose();}catch(Exception cleanup){Program.LogError(cleanup,"Cleaning up failed preview output");}
            try {pendingReader?.Dispose();}catch(Exception cleanup){Program.LogError(cleanup,"Cleaning up failed preview WAV");}
            try {pendingAnalyzer?.Dispose();}catch(Exception cleanup){Program.LogError(cleanup,"Cleaning up failed preview analyzer");}
            try {pendingDevice?.Dispose();}catch(Exception cleanup){Program.LogError(cleanup,"Cleaning up failed preview device");}
            status.Text="Playback failed: "+ex.Message;
            Program.LogError(ex,"Starting Peak Capture playback");
            MessageBox.Show(this,ex.Message,"Cannot play Peak Capture",MessageBoxButtons.OK,MessageBoxIcon.Warning);
        }
    }
    private void StopPlayback()
    {
        var oldOutput=output;output=null;
        try {oldOutput?.Stop();}catch(Exception ex){Program.LogError(ex,"Stopping Peak Capture preview");}
        try {oldOutput?.Dispose();}catch(Exception ex){Program.LogError(ex,"Disposing Peak Capture output");}
        try
        {
            lock(readerGate)
            {
                reader?.Dispose();reader=null;gainProvider=null;
                playbackMeter?.Dispose();playbackMeter=null;
            }
        }
        catch(Exception ex){Program.LogError(ex,"Disposing Peak Capture WAV");reader=null;gainProvider=null;
            playbackMeter?.Dispose();playbackMeter=null;}
        try {playbackEndpoint?.Dispose();}
        catch(Exception ex){Program.LogError(ex,"Releasing playback device");}
        playbackEndpoint=null;
        if(listening)
        {
            listening=false;
            try {listeningChanged(false);}catch(Exception ex){Program.LogError(ex,"Resuming measurement after preview");}
        }
        play.Text="Play";
        waveform.PositionSeconds=0;
        if(!IsDisposed && !Disposing)
            status.Text="Ready. Listening gain changes only the preview, not the saved WAV.";
    }
    private void UpdatePosition()
    {
        if(reader==null)return;
        try
        {
            lock(readerGate)
            {
                if(reader==null)return;
                waveform.PositionSeconds=reader.CurrentTime.TotalSeconds;
                timeInfo.Text=$"{FormatTime(reader.CurrentTime)} / {FormatTime(reader.TotalTime)}";
            }
        }
        catch(ObjectDisposedException){}
    }
    private void Seek(double fraction)
    {
        if(reader==null)
        {
            if(currentPath!=null)
            {
                // Seeking when stopped sets a start point for the next play.
                requestedSeek=Math.Clamp(fraction,0,1);
                waveform.PositionSeconds=waveform.DurationSeconds*requestedSeek;
            }
            return;
        }
        lock(readerGate)
        {
            if(reader==null)return;
            reader.CurrentTime=TimeSpan.FromSeconds(Math.Max(0,reader.TotalTime.TotalSeconds*Math.Clamp(fraction,0,1)));
            // Seeking begins a new integrated listening measurement at that
            // position. Never count skipped audio as heard/processed.
            playbackMeter?.Reset();
        }
    }
    private double requestedSeek;
    private void UpdateGain()
    {
        gainInfo.Text=$"{gain.Value:+0;-0;0} dB";
        gainProvider?.SetDecibels(gain.Value);
        if(gain.Value>0 && !listening)status.Text="Positive listening gain can clip the preview. WAV files stay unchanged.";
    }
    private void MoveSelection(int direction)
    {
        int index=clipList.SelectedIndex;
        if(index<0 || clipList.Items.Count==0)return;
        clipList.SelectedIndex=Math.Clamp(index+direction,0,clipList.Items.Count-1);
    }
    private void OpenExternally()
    {
        if(currentPath==null)return;
        try {System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(currentPath){UseShellExecute=true});}
        catch(Exception ex){MessageBox.Show(this,ex.Message,"Cannot open WAV");}
    }
    private void OpenFolder()
    {
        try
        {
            Directory.CreateDirectory(cfg.PeakCaptureFolder);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(cfg.PeakCaptureFolder){UseShellExecute=true});
        }
        catch(Exception ex){MessageBox.Show(this,ex.Message,"Cannot open capture folder");}
    }
    private static string FormatTime(TimeSpan t)=>$"{(int)t.TotalMinutes}:{t.Seconds:00}.{t.Milliseconds/100}";
}

// In-process gain is intentionally applied after reading the saved file.
// Samples are confined to +/-1 to prevent over-range float samples reaching
// the output. The ORIGINAL source WAV is never modified.
internal sealed record PlaybackReadout(Snapshot Snapshot,TimeSpan ClipTime,bool Paused);

internal sealed class PreviewGain : ISampleProvider
{
    private readonly ISampleProvider source;
    private readonly object streamGate;
    private readonly Action<float[],int,int> measureOriginal;
    private float multiplier=1;
    public PreviewGain(ISampleProvider source,object gate,Action<float[],int,int> measure)
    {this.source=source;streamGate=gate;measureOriginal=measure;}
    public WaveFormat WaveFormat=>source.WaveFormat;
    public void SetDecibels(int db)=>Volatile.Write(ref multiplier,(float)Math.Pow(10,Math.Clamp(db,-24,12)/20.0));
    public int Read(float[] buffer,int offset,int count)
    {
        lock(streamGate)
        {
            int n=source.Read(buffer,offset,count);
            if(n>0)measureOriginal(buffer,offset,n); // BEFORE preview gain
            float g=Volatile.Read(ref multiplier);
            // At 0 dB, pass float samples through untouched, including any
            // over-range samples present in the original capture.
            if(g==1f)return n;
            for(int i=offset;i<offset+n;i++)buffer[i]=Math.Clamp(buffer[i]*g,-1f,1f);
            return n;
        }
    }
}

internal sealed class WaveformView : Control
{
    private float[] samples=[];
    private double? firstTrigger;
    public double DurationSeconds {get;private set;}
    private double playhead;
    public double PositionSeconds
    {
        get=>playhead;
        set {playhead=value;Invalidate();}
    }
    public event Action<double>? SeekRequested;
    public WaveformView()
    {
        DoubleBuffered=true;
        SetStyle(ControlStyles.ResizeRedraw|ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);
        Cursor=Cursors.Hand;
    }
    public void SetDuration(double duration){DurationSeconds=Math.Max(0,duration);Invalidate();}
    public void SetSamples(float[] data,double? marker){samples=data;firstTrigger=marker;Invalidate();}
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if(e.Button==MouseButtons.Left && Width>16)SeekRequested?.Invoke(Math.Clamp((double)(e.X-8)/(Width-16),0,1));
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;
        using var bg=new SolidBrush(Color.FromArgb(31,33,38));
        g.FillRectangle(bg,ClientRectangle);
        var r=new Rectangle(8,12,Math.Max(1,Width-16),Math.Max(1,Height-24));
        using var mid=new Pen(Color.FromArgb(65,71,78),1);
        g.DrawLine(mid,r.Left,r.Top+r.Height/2,r.Right,r.Top+r.Height/2);
        if(samples.Length>0)
        {
            using var stroke=new Pen(Color.FromArgb(170,183,195),1);
            int cy=r.Top+r.Height/2;
            for(int x=0;x<r.Width;x++)
            {
                int index=(int)Math.Min(samples.Length-1,(long)x*samples.Length/r.Width);
                int h=(int)Math.Clamp(samples[index]*r.Height/2,1,r.Height/2);
                g.DrawLine(stroke,r.Left+x,cy-h,r.Left+x,cy+h);
            }
        }
        if(firstTrigger is double peak && DurationSeconds>0)
        {
            int x=r.Left+(int)Math.Clamp(peak/DurationSeconds*r.Width,0,r.Width);
            using var marker=new Pen(Color.FromArgb(241,109,100),2){DashStyle=DashStyle.Dash};
            g.DrawLine(marker,x,r.Top,x,r.Bottom);
            using var brush=new SolidBrush(Color.FromArgb(241,109,100));
            g.DrawString("PEAK",Font,brush,Math.Clamp(x+3,r.Left,Math.Max(r.Left,r.Right-45)),r.Top+1);
        }
        if(DurationSeconds>0)
        {
            int x=r.Left+(int)Math.Clamp(playhead/DurationSeconds*r.Width,0,r.Width);
            using var head=new Pen(Color.White,1);
            g.DrawLine(head,x,r.Top,x,r.Bottom);
        }
    }
}
