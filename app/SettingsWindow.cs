using System;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.VisualBasic.FileIO;
using System.Windows.Forms;
using NAudio.CoreAudioApi;

namespace LufsTaskbarMeter;

// A single, nonmodal control panel. Changes take effect immediately. No
// menu-closing behavior; all source and diagnostics actions are accessible here.
internal sealed class SettingsWindow : Form
{
    private readonly Settings cfg;
    private readonly LoudnessMeter meter;
    private readonly Label audioStatus=new();
    private readonly Label audioLevels=new();
    private readonly Label audioFormat=new();
    private readonly Label captureError=new();
    private readonly ComboBox audioChoices=new();
    private readonly ListBox clips=new();
    private readonly Label clipDetails=new();
    private readonly System.Windows.Forms.Timer refresh=new(){Interval=1000};
    private readonly TabControl tabs=new(){Dock=DockStyle.Fill};
    private TabPage? capturesTab;
    private PeakCapturePlayerWindow? capturePlayer;
    private readonly Action<bool> previewListeningChanged;
    private readonly Action changed;
    private readonly Action<string?,bool> setSource;
    public PlaybackReadout? ReadPlayback() => capturePlayer?.ReadPlayback();
    public (double Left,double Right) ConsumePlaybackStereo() =>
        capturePlayer?.ConsumePlaybackStereo() ?? (0,0);
    public void TogglePreviewPlayback() => capturePlayer?.ToggleFromMeter();
    public void ResetPlayback() => capturePlayer?.ResetFromMeter();
    private bool choosingSource;
    private bool deletingCaptures;
    private string[] lastClips=[];
    private int refreshTicks;
    private sealed record AudioDevice(string Label,string? Id,bool Input)
    {
        public override string ToString()=>Label;
    }

    public SettingsWindow(Settings options,Action changed,Action preview,Action resetPosition,
        Func<bool> startupEnabled,Action<bool> setStartup,LoudnessMeter meter,
        Action<string?,bool> setSource,Action restart,Action showDiagnostics,
        Action openHistory,Action about,Func<bool> hotkeyAvailable,Action<bool> previewListeningChanged)
    {
        cfg=options;this.changed=changed;this.meter=meter;this.setSource=setSource;
        this.previewListeningChanged=previewListeningChanged;
        Text="LUFFY v3.14 – Settings";
        StartPosition=FormStartPosition.CenterScreen;
        ClientSize=new Size(590,590);
        MinimumSize=new Size(530,480);
        ShowInTaskbar=true;
        FormBorderStyle=FormBorderStyle.Sizable;
        Controls.Add(tabs);

        FlowLayoutPanel Page(string name)
        {
            var page=new TabPage(name){AutoScroll=true};
            tabs.TabPages.Add(page);
            var stack=new FlowLayoutPanel
            {
                Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,
                WrapContents=false,AutoScroll=true,Padding=new Padding(12)
            };
            page.Controls.Add(stack);
            return stack;
        }
        const int contentWidth=505;
        void Add(FlowLayoutPanel stack,Control c,int width=contentWidth)
        {
            c.Width=width;c.Margin=new Padding(3,5,3,5);
            stack.Controls.Add(c);
        }
        void Heading(FlowLayoutPanel stack,string label)
        {
            Add(stack,new Label{Text=label,Height=29,TextAlign=ContentAlignment.MiddleLeft,
                Font=new Font("Segoe UI",11,FontStyle.Bold)});
        }
        CheckBox Toggle(FlowLayoutPanel stack,string label,bool value,Action<bool> apply)
        {
            var cb=new CheckBox{Text=label,Checked=value,Height=28,AutoSize=false};
            cb.CheckedChanged+=(_,_)=>{apply(cb.Checked);cfg.Save();changed();};
            Add(stack,cb);
            return cb;
        }
        NumericUpDown Number(FlowLayoutPanel stack,string label,decimal initial,
            decimal min,decimal max,decimal step,Action<decimal> apply)
        {
            var panel=new Panel{Height=37};
            panel.Controls.Add(new Label{Text=label,Location=new Point(0,10),Width=340});
            var up=new NumericUpDown
            {
                Location=new Point(360,4),Width=115,
                DecimalPlaces=step<1?1:0,Increment=step,
                Minimum=min,Maximum=max,Value=Math.Clamp(initial,min,max)
            };
            up.ValueChanged+=(_,_)=>{apply(up.Value);cfg.Save();changed();};
            panel.Controls.Add(up);Add(stack,panel);return up;
        }
        void Button(FlowLayoutPanel stack,string label,Action action)
        {
            var b=new Button{Text=label,Height=33};b.Click+=(_,_)=>action();Add(stack,b);
        }
        void Note(FlowLayoutPanel stack,string text,int height=46)
        {
            Add(stack,new Label{Text=text,Height=height,ForeColor=Color.DimGray});
        }
        void Separator(FlowLayoutPanel stack)
        {
            Add(stack,new Panel{Height=1,BackColor=Color.FromArgb(210,210,210)});
        }
        void OpenFolder()
        {
            try
            {
                Directory.CreateDirectory(cfg.PeakCaptureFolder);
                Process.Start(new ProcessStartInfo(cfg.PeakCaptureFolder){UseShellExecute=true});
            }
            catch(Exception ex){MessageBox.Show(this,ex.Message,"Could not open folder");}
        }

        var general=Page("General");
        Heading(general,"General");
        Toggle(general,"Always on Top – meter",cfg.OnTop,v=>cfg.OnTop=v);
        Toggle(general,"Start with Windows",startupEnabled(),setStartup);
        Heading(general,"Shortcuts & application");
        Note(general,hotkeyAvailable()?"Reset shortcut: Ctrl + Alt + R":"Reset shortcut: unavailable (used by another app)",32);
        Note(general,"Open Settings: double-click the meter or click its small gear.",32);
        Note(general,"LUFFY v3.14 Beta · Andreas Busk · contact@andreasbusk.com",32);
        Button(general,"About LUFFY",about);
        Button(general,"Check for updates (GitHub Releases)",()=>
            Process.Start(new ProcessStartInfo("https://github.com/userewkrewljkel/luffy/releases"){UseShellExecute=true}));
        Button(general,"Open measurement history (CSV)",openHistory);

        var audio=Page("Audio");
        Heading(audio,"Audio source");
        audioChoices.DropDownStyle=ComboBoxStyle.DropDownList;
        audioChoices.Height=29;Add(audio,audioChoices);
        audioChoices.SelectedIndexChanged+=(_,_)=>
        {
            if(choosingSource || audioChoices.SelectedItem is not AudioDevice device)return;
            setSource(device.Id,device.Input);
            UpdateAudio();
        };
        Button(audio,"Refresh available devices",RefreshDevices);
        audioStatus.Height=26;audioLevels.Height=27;audioFormat.Height=40;
        captureError.Height=32;captureError.ForeColor=Color.Firebrick;
        Add(audio,audioStatus);Add(audio,audioLevels);Add(audio,audioFormat);
        Add(audio,captureError);
        Separator(audio);
        Heading(audio,"Loudness target");
        var target=Number(audio,"Integrated LUFS target",(decimal)cfg.Target,-70,0,0.5m,v=>
        {cfg.Target=(double)v;cfg.Profile="Custom";});
        var presets=new FlowLayoutPanel{Height=40,FlowDirection=FlowDirection.LeftToRight,WrapContents=false};
        foreach(var preset in new[]{("Game −23",-23d,"Game"),("Music −16",-16d,"Music"),("Streaming −14",-14d,"Streaming")})
        {
            var chosen=preset;
            var button=new Button{Text=chosen.Item1,Size=new Size(155,30)};
            button.Click+=(_,_)=>
            {
                target.Value=(decimal)chosen.Item2;
                cfg.Profile=chosen.Item3;cfg.Save();changed();
            };
            presets.Controls.Add(button);
        }
        Add(audio,presets);
        Separator(audio);
        Heading(audio,"Tools & diagnostics");
        Button(audio,"Audio diagnostics…",showDiagnostics);
        Button(audio,"Open measurement history (CSV)",openHistory);
        Button(audio,"Restart audio capture (resets measurement)…",restart);
        Note(audio,"Restart begins a new measurement. Your current session is saved to history after confirmation.",46);
        Note(audio,"ASIO is not captured directly. For compatible devices, select their Windows loopback input.",46);

        var display=Page("Display");
        Heading(display,"Visible parameters");
        Toggle(display,"Show Integrated LUFS",cfg.ShowI,v=>cfg.ShowI=v);
        Toggle(display,"Show Short-term LUFS",cfg.ShowS,v=>cfg.ShowS=v);
        Toggle(display,"Show True Peak",cfg.ShowTP,v=>cfg.ShowTP=v);
        Toggle(display,"Show elapsed time",cfg.ShowTime,v=>cfg.ShowTime=v);
        Toggle(display,"Show stereo peak bars (L/R)",cfg.ShowStereoMeter,v=>cfg.ShowStereoMeter=v);
        Heading(display,"Appearance");
        Toggle(display,"Transparent meter background",cfg.Transparent,v=>cfg.Transparent=v);
        Number(display,"Text size (px)",cfg.FontSize,10,15,1,v=>cfg.FontSize=(int)v);
        var colorRow=new Panel{Height=37};
        colorRow.Controls.Add(new Label{Text="Text color",Location=new Point(0,10),Width=340});
        var textColorChoice=new ComboBox{Location=new Point(360,4),Width=115,DropDownStyle=ComboBoxStyle.DropDownList};
        textColorChoice.Items.AddRange(new object[]{"White","Black"});textColorChoice.SelectedItem=cfg.TextColor;
        textColorChoice.SelectedIndexChanged+=(_,_)=>
        {cfg.TextColor=textColorChoice.SelectedItem!.ToString()!;cfg.Save();changed();};
        colorRow.Controls.Add(textColorChoice);Add(display,colorRow);
        Button(display,"Position meter above the taskbar",()=>
        {cfg.Docked=true;cfg.Save();resetPosition();});

        var warning=Page("True Peak");
        capturesTab=tabs.TabPages[tabs.TabPages.Count-1];
        Heading(warning,"Screen-edge warning");
        Toggle(warning,"Enable True Peak warning",cfg.PeakWarning,v=>cfg.PeakWarning=v);
        var glowRow=new Panel{Height=37};
        glowRow.Controls.Add(new Label{Text="Glow style",Location=new Point(0,10),Width=340});
        var glow=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Location=new Point(360,4),Width=115};
        glow.Items.AddRange(new object[]{"Soft","Studio","Alert"});glow.SelectedItem=cfg.GlowStyle;
        glow.SelectedIndexChanged+=(_,_)=>{cfg.GlowStyle=glow.SelectedItem!.ToString()!;cfg.Save();changed();};
        glowRow.Controls.Add(glow);Add(warning,glowRow);
        Number(warning,"Warning border (px)",cfg.WarningBorderPx,2,10,1,v=>cfg.WarningBorderPx=(int)v);
        Number(warning,"Warning duration (s)",(decimal)cfg.WarningDurationSeconds,0.2m,10,0.1m,v=>cfg.WarningDurationSeconds=(double)v);
        Number(warning,"Cooldown (s)",(decimal)cfg.WarningCooldownSeconds,0,30,0.1m,v=>cfg.WarningCooldownSeconds=(double)v);
        Number(warning,"True Peak limit (dBTP)",(decimal)cfg.PeakLimit,-30,6,0.1m,v=>cfg.PeakLimit=(double)v);
        Toggle(warning,"Fade out only (never fade in)",cfg.WarningFadeOut,v=>cfg.WarningFadeOut=v);
        Button(warning,"Test warning on all screens",preview);
        Separator(warning);
        Heading(warning,"Peak Capture · local WAV clips");
        var recordingToggle=Toggle(warning,"Automatically record peaks (off by default)",cfg.PeakCaptureEnabled,v=>cfg.PeakCaptureEnabled=v);
        Number(warning,"Seconds before peak",cfg.PeakBeforeSeconds,1,30,1,v=>cfg.PeakBeforeSeconds=(int)v);
        Number(warning,"Seconds after peak",cfg.PeakAfterSeconds,1,30,1,v=>cfg.PeakAfterSeconds=(int)v);
        Note(warning,"When enabled, LUFFY keeps a short memory buffer and saves nearby peaks as a single WAV clip. Continued loud sound can extend the clip (maximum 60 s).",62);
        var folderRow=new Panel{Height=67};
        folderRow.Controls.Add(new Label{Text="Save WAV files to:",Location=new Point(0,0),Width=450,Height=18});
        var folderName=new TextBox{Text=cfg.PeakCaptureFolder,Location=new Point(0,24),Width=398,ReadOnly=true};
        var folderPick=new Button{Text="Browse",Location=new Point(401,22),Width=90,Height=28};
        folderPick.Click+=(_,_)=>
        {
            using var browse=new FolderBrowserDialog{Description="Choose the folder for LUFFY Peak Captures",SelectedPath=cfg.PeakCaptureFolder,ShowNewFolderButton=true};
            if(browse.ShowDialog(this)==DialogResult.OK)
            {
                cfg.PeakCaptureFolder=browse.SelectedPath;folderName.Text=browse.SelectedPath;
                cfg.Save();changed();RefreshClips();
            }
        };
        folderRow.Controls.Add(folderName);folderRow.Controls.Add(folderPick);Add(warning,folderRow);
        Button(warning,"Open Peak Captures folder",OpenFolder);
        Heading(warning,"Recorded clips (newest first)");
        clips.Height=135;clips.HorizontalScrollbar=true;Add(warning,clips);
        clipDetails.Height=45;Add(warning,clipDetails);
        clips.SelectedIndexChanged+=(_,_)=>ShowClipDetails();
        clips.DoubleClick+=(_,_)=>OpenSelectedClip();
        Button(warning,"Open Peak Capture Player…",()=>OpenCapturePlayer(null));
        Button(warning,"Listen to selected clip inside LUFFY",OpenSelectedClip);
        Button(warning,"Open selected WAV in external player",OpenSelectedClipExternally);
        Button(warning,"Refresh clip list",RefreshClips);
        Separator(warning);
        Heading(warning,"Manage saved recordings");
        Button(warning,"Delete all Peak Captures (Recycle Bin)…",()=>
        {
            _=DeleteAllCapturesAsync(recordingToggle);
        });
        Note(warning,"Moves only LUFFY_Peak_*.wav files and their matching .wav.txt metadata to the Windows Recycle Bin. Does not erase measurement history or other files.",62);
        Note(warning,"Clips remain on this computer. A matching .txt file records the approximate first trigger position. Pause, Reset and device changes may produce a shorter partial clip.",70);
        Note(warning,"Screen warnings and recording are independent. Exclusive fullscreen apps may prevent visible overlays.",50);

        RefreshDevices();UpdateAudio();RefreshClips();
        refresh.Tick+=(_,_)=>
        {
            if(!Visible || IsDisposed)return;
            UpdateAudio();
            if(++refreshTicks%4==0 && tabs.SelectedTab==capturesTab)RefreshClips();
        };
        refresh.Start();
        FormClosed+=(_,_)=>{refresh.Dispose();capturePlayer?.Close();};
    }

    private static string[] SavedCapturePaths(string folder)
    {
        if(!Directory.Exists(folder))return [];
        // Only accept file names produced by LUFFY's PeakCaptureRecorder.
        return Directory.GetFiles(folder,"LUFFY_Peak_*.wav",System.IO.SearchOption.TopDirectoryOnly)
            .Where(path=>Regex.IsMatch(Path.GetFileName(path),
                @"^LUFFY_Peak_\d{4}-\d{2}-\d{2}_\d{2}-\d{2}-\d{2}_.+\.wav$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            .ToArray();
    }
    private async Task DeleteAllCapturesAsync(CheckBox recordingToggle)
    {
        if(deletingCaptures)return;
        string folder=cfg.PeakCaptureFolder;
        string[] initial;
        try { initial=SavedCapturePaths(folder); }
        catch(Exception ex)
        {
            MessageBox.Show(this,ex.Message,"Cannot list Peak Captures",MessageBoxButtons.OK,MessageBoxIcon.Warning);
            return;
        }
        if(initial.Length==0)
        {
            MessageBox.Show(this,"No saved LUFFY Peak Captures were found in the selected folder.",
                "Delete Peak Captures",MessageBoxButtons.OK,MessageBoxIcon.Information);
            return;
        }
        string recordingNote=recordingToggle.Checked
            ? "\nAutomatic Peak Capture will be switched OFF before deletion."
            : "";
        var confirmation=MessageBox.Show(this,
            $"Move ALL {initial.Length} LUFFY Peak Capture WAV recordings and their associated metadata to the Windows Recycle Bin?\n\n"+
            $"Folder: {folder}\n\nThis does not delete measurement history or unrelated files.{recordingNote}\n\nContinue?",
            "Delete all Peak Captures",MessageBoxButtons.YesNo,MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);
        if(confirmation!=DialogResult.Yes)return;
        deletingCaptures=true;
        try
        {
            // Disable recorder first. This finalizes a pending partial clip;
            // wait for asynchronous writes before collecting files to delete.
            if(recordingToggle.Checked)recordingToggle.Checked=false;
            await meter.WaitForPeakCaptureWritesAsync();
            if(capturePlayer!=null && !capturePlayer.IsDisposed)
            {
                capturePlayer.Close();
                capturePlayer=null;
            }
            var files=SavedCapturePaths(folder);
            int deleted=0,failed=0;
            var errors=new System.Collections.Generic.List<string>();
            foreach(var wav in files)
            {
                try
                {
                    FileSystem.DeleteFile(wav,UIOption.OnlyErrorDialogs,RecycleOption.SendToRecycleBin);
                    deleted++;
                }
                catch(Exception ex){failed++;errors.Add(Path.GetFileName(wav)+": "+ex.Message);}
                // Never delete an unattached .txt file unless its WAV was
                // successfully moved to the Recycle Bin.
                if(!File.Exists(wav) && File.Exists(wav+".txt"))
                {
                    try {FileSystem.DeleteFile(wav+".txt",UIOption.OnlyErrorDialogs,RecycleOption.SendToRecycleBin);}
                    catch(Exception ex){failed++;errors.Add(Path.GetFileName(wav)+".txt: "+ex.Message);}
                }
            }
            lastClips=[];RefreshClips();
            string message=$"Moved {deleted} WAV recording(s) to the Recycle Bin."+
                (failed>0?$"\n{failed} item(s) could not be moved.\n"+string.Join("\n",errors.Take(5)):"")+
                "\n\nAutomatic Peak Capture is now off; enable it again to record new peaks.";
            MessageBox.Show(this,message,"Peak Capture cleanup",
                MessageBoxButtons.OK,failed>0?MessageBoxIcon.Warning:MessageBoxIcon.Information);
        }
        catch(Exception ex)
        {
            Program.LogError(ex,"Deleting saved Peak Captures");
            MessageBox.Show(this,"No permanent deletion fallback will be used. "+ex.Message,
                "Peak Capture cleanup failed",MessageBoxButtons.OK,MessageBoxIcon.Warning);
        }
        finally {deletingCaptures=false;}
    }

    private void RefreshDevices()
    {
        choosingSource=true;
        try
        {
            audioChoices.Items.Clear();
            audioChoices.Items.Add(new AudioDevice("Windows default output",null,false));
            using var endpoints=new MMDeviceEnumerator();
            foreach(var d in endpoints.EnumerateAudioEndPoints(DataFlow.Render,DeviceState.Active))
                audioChoices.Items.Add(new AudioDevice("Output: "+d.FriendlyName,d.ID,false));
            audioChoices.Items.Add(new AudioDevice("Default Windows recording input",null,true));
            foreach(var d in endpoints.EnumerateAudioEndPoints(DataFlow.Capture,DeviceState.Active))
                audioChoices.Items.Add(new AudioDevice("Input: "+d.FriendlyName,d.ID,true));
            for(int i=0;i<audioChoices.Items.Count;i++)
            {
                var d=(AudioDevice)audioChoices.Items[i]!;
                if(d.Id==cfg.DeviceId && d.Input==cfg.UseInput)
                {audioChoices.SelectedIndex=i;break;}
            }
            if(audioChoices.SelectedIndex<0)audioChoices.Text="Source unavailable: choose a device";
        }
        catch(Exception ex){audioChoices.Items.Add(new AudioDevice("Could not list devices: "+ex.Message,null,false));}
        finally{choosingSource=false;}
    }
    private void UpdateAudio()
    {
        var signal=meter.ReadInputSignal();
        double age=signal.LastBufferAtUtc==DateTime.MinValue?double.PositiveInfinity:
            (DateTime.UtcNow-signal.LastBufferAtUtc).TotalSeconds;
        string state=signal.Paused?"Paused":!signal.Running?"Stopped":
            age>2?"Waiting for audio":
            signal.LastPeakDbfs is double db && db> -80?"Receiving audio":"Receiving silence";
        audioStatus.Text=$"Capture status: {state} · Buffers: {signal.Buffers}";
        audioLevels.Text=$"Input peak: {Db(signal.LastPeakDbfs)} dBFS    RMS: {Db(signal.LastRmsDbfs)} dBFS";
        audioFormat.Text=$"Source: {meter.SourceName}\nFormat: {signal.Format}";
        captureError.Text=signal.Error==null?meter.LastPeakCaptureError==null?"":"Peak Capture: "+meter.LastPeakCaptureError:"Audio error: "+signal.Error;
    }
    private static string Db(double? x)=>x.HasValue?x.Value.ToString("0.0",CultureInfo.InvariantCulture):"—";
    private void RefreshClips()
    {
        try
        {
            string[] files=Directory.Exists(cfg.PeakCaptureFolder)?
                Directory.GetFiles(cfg.PeakCaptureFolder,"LUFFY_Peak_*.wav")
                    .OrderByDescending(File.GetLastWriteTimeUtc).Take(100).ToArray():[];
            if(files.SequenceEqual(lastClips))return;
            string? selected=clips.SelectedItem is ClipItem ci?ci.Path:null;
            lastClips=files;
            clips.BeginUpdate();
            clips.Items.Clear();
            foreach(var file in files)clips.Items.Add(new ClipItem(file));
            if(selected!=null)
            {
                for(int i=0;i<clips.Items.Count;i++)
                    if(((ClipItem)clips.Items[i]!).Path==selected){clips.SelectedIndex=i;break;}
            }
            clips.EndUpdate();
            clipDetails.Text=files.Length==0?"No clips yet. Enable Peak Capture and play audio above the threshold.":$"{files.Length} most recent clip(s) shown. Double-click to listen.";
        }
        catch(Exception ex){clipDetails.Text="Could not read capture folder: "+ex.Message;}
    }
    private sealed record ClipItem(string Path)
    {
        public override string ToString()=>System.IO.Path.GetFileName(Path);
    }
    private void ShowClipDetails()
    {
        if(clips.SelectedItem is not ClipItem item)return;
        try
        {
            string metadata=File.Exists(item.Path+".txt")?File.ReadAllText(item.Path+".txt").Split('\n')[0]:"";
            clipDetails.Text=metadata;
        }
        catch{clipDetails.Text="Double-click to listen to this clip.";}
    }
    private void OpenCapturePlayer(string? path)
    {
        if(capturePlayer==null || capturePlayer.IsDisposed)
            capturePlayer=new PeakCapturePlayerWindow(cfg,previewListeningChanged);
        capturePlayer.ShowClip(path);
    }
    private void OpenSelectedClip()
    {
        OpenCapturePlayer((clips.SelectedItem as ClipItem)?.Path);
    }
    private void OpenSelectedClipExternally()
    {
        if(clips.SelectedItem is not ClipItem item)return;
        try {Process.Start(new ProcessStartInfo(item.Path){UseShellExecute=true});}
        catch(Exception ex){MessageBox.Show(this,ex.Message,"Cannot open clip");}
    }
}
