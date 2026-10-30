using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;
using NAudio.CoreAudioApi;

namespace LufsTaskbarMeter;

internal sealed class MeterWindow : Form
{
    private readonly LoudnessMeter meter=new();
    private readonly Settings cfg=Settings.Load();
    private readonly Stopwatch elapsed=new();
    private readonly ReadoutStrip strip=new();
    private readonly Label brand=new();
    private readonly StereoLevel stereo=new();
    private readonly PauseIcon pauseIcon=new();
    private readonly ResetIcon reset=new();
    private readonly Panel resetHolder=new();
    private readonly ContextMenuStrip menu=new();
    // One UI timer schedules separate 50 ms numeric and 33 ms stereo updates.
    // A single layered-window composition happens per tick at most.
    private readonly System.Windows.Forms.Timer timer=new(){Interval=16};
    private readonly Stopwatch uiClock=Stopwatch.StartNew();
    private double lastReadoutMs, lastStereoMs;
    private bool resizingForLayout;
    private string? previousTooltip;
    private readonly ToolTip tooltip=new();
    private Font readoutFont = new("Segoe UI",11,FontStyle.Bold,GraphicsUnit.Pixel);

    private bool dragging, programmaticMove;
    private DiagnosticWindow? diagnosticWindow;
    private readonly MultiScreenWarning overlay=new();
    private readonly AmbientBorder ambient=new();
    private readonly PeakScreenshotCapture screenshot=new();
    private double lastAmbientUpdateMs;
    private SettingsWindow? settingsWindow;
    private bool previewPlaying;
    private bool resumeMeasurementAfterPreview;
    private DateTime lastPeakWarning=DateTime.MinValue;
    private Point dragAt;
    private const int HOTKEY_ID=1351;
    private static readonly Color Background=Color.FromArgb(31,33,38);
    private static readonly Color Green=Color.FromArgb(110,221,155), Red=Color.FromArgb(255,110,110);
    private Rectangle pauseBounds, resetBounds, gearBounds;
    private bool hoveredTp, hoveredPause, hoveredReset, hoveredGear;
    private enum HitTarget { None, TruePeak, Pause, Reset, Gear }
    private HitTarget pressed=HitTarget.None;
    private bool layeringFailed;
    private static readonly string StartupKey=@"Software\Microsoft\Windows\CurrentVersion\Run";

    public MeterWindow()
    {
        Text="LUFFY by Andreas Busk – v3.15 PREVIEW";
        Icon=Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
        FormBorderStyle=FormBorderStyle.None;
        StartPosition=FormStartPosition.Manual;
        ShowInTaskbar=true;
        BackColor=Background;
        ForeColor=Color.White;
        DoubleBuffered=true;
        SetStyle(ControlStyles.StandardClick | ControlStyles.StandardDoubleClick,true);
        TopMost=cfg.OnTop;
        Height=40;
        Padding=Padding.Empty;

        brand.Text="LUFFY v3.15 PREVIEW";
        brand.Dock=DockStyle.Top;
        brand.Height=13;
        brand.AutoSize=false;
        brand.TextAlign=ContentAlignment.BottomCenter;
        brand.Font=new Font("Segoe UI",8,FontStyle.Bold,GraphicsUnit.Pixel);
        brand.ForeColor=Color.FromArgb(174,180,191);
        brand.BackColor=Color.Transparent;
        brand.Margin=Padding.Empty;
        // Text, meters and buttons are drawn into one per-pixel-alpha bitmap;
        // no WinForms child HWNDs obscure the transparency or shuffle order.
        strip.Height=22;
        strip.DisplayFont=readoutFont;
        stereo.Size=new Size(8,18);
        pauseIcon.Size=new Size(22,22);
        reset.ForeColor=Color.White;
        reset.Size=new Size(22,22);
        tooltip.SetToolTip(this,"Double-click to open Settings. Right-click for menu.");
        ContextMenuStrip=menu;

        menu.Opening+=(_,_)=>BuildMenu();
        SystemEvents.DisplaySettingsChanged+=DisplayChanged;
        Move+=(_,_)=>{if(!programmaticMove && !cfg.Docked){cfg.X=Left;cfg.Y=Top;cfg.Save();}};
        Shown+=(_,_)=>
        {
            if(cfg.Docked || cfg.X==-99999) PositionByTaskbar();
            else if(Screen.AllScreens.Any(s=>s.WorkingArea.IntersectsWith(new Rectangle(cfg.X,cfg.Y,Width,Height)))) Location=new Point(cfg.X,cfg.Y);
            else PositionByTaskbar();
            SyncStartupExecutableIfEnabled();
            meter.ConfigurePeakCapture(cfg);
            meter.Start(cfg.DeviceId, cfg.UseInput);
            elapsed.Start();
            if(meter.Error!=null) MessageBox.Show(this,meter.Error,"Audio capture error",MessageBoxButtons.OK,MessageBoxIcon.Warning);
            stereo.ResetLevels();
            UpdateDisplay();
        };
        timer.Tick+=(_,_)=>
        {
            try
            {
                double now=uiClock.Elapsed.TotalMilliseconds;
                bool draw=false;
                if(now-lastReadoutMs>=50)
                {
                    lastReadoutMs=now;
                    draw=UpdateDisplay(false);
                    CheckPeakWarning();
                    if(now-lastAmbientUpdateMs>=200)
                    {
                        lastAmbientUpdateMs=now;
                        UpdateAmbient();
                    }
                }
                if(now-lastStereoMs>=33)
                {
                    double dt=Math.Max(0,(now-lastStereoMs)/1000.0);
                    lastStereoMs=now;
                    var playback=settingsWindow?.ReadPlayback();
                    if(cfg.ShowStereoMeter && (playback!=null ? !playback.Paused : !meter.Paused && meter.IsRunning))
                    {
                        var (left,right)=playback!=null
                            ? settingsWindow!.ConsumePlaybackStereo() : meter.ConsumeStereoPeaks();
                        // Don't composite again for an unchanged 3-pixel level bar.
                        draw=stereo.Advance(left,right,dt)||draw;
                    }
                }
                if(draw)RenderLayered();
            }
            catch(Exception ex) { timer.Stop();Program.ReportError(ex,"Updating meter display"); }
        };
        timer.Start();
        SizeChanged+=(_,_)=>{if(!resizingForLayout)RenderLayered();};
        ApplyStyle();
    }
    // A borderless Windows Form normally uses a binary Region (jagged edges).
    // WS_EX_LAYERED + UpdateLayeredWindow allows genuinely antialiased corners.
    protected override CreateParams CreateParams
    {
        get { var p=base.CreateParams; p.ExStyle|=0x00080000; return p; }
    }
    protected override void OnPaintBackground(PaintEventArgs e) { }
    protected override void OnPaint(PaintEventArgs e) { }

    private void ApplyStyle()
    {
        // Do not set TransparencyKey on layered windows; that introduces color
        // key artifacts/black borders. Alpha lives in the composited bitmap.
        TransparencyKey=Color.Empty;
        var text=cfg.TextColor=="Black"?Color.Black:Color.White;
        if(readoutFont.Size!=cfg.FontSize)
        {
            var replacement=new Font("Segoe UI",cfg.FontSize,FontStyle.Bold,GraphicsUnit.Pixel);
            strip.DisplayFont=replacement;
            var obsolete=readoutFont;
            readoutFont=replacement;
            obsolete.Dispose();
        }
        brand.ForeColor=cfg.TextColor=="Black"?Color.FromArgb(70,70,70):Color.FromArgb(174,180,191);
        reset.ForeColor=text;
        UpdateDisplay();
    }
    private void RenderLayered()
    {
        if(layeringFailed || !IsHandleCreated || Width<40 || Height<24 ||
           pauseBounds.Width==0 || resetBounds.Width==0 || gearBounds.Width==0 || IsDisposed || Disposing)return;
        try
        {
            LayeredFrame.Present(this,g=>
            {
                g.SmoothingMode=SmoothingMode.AntiAlias;
                using(var path=LayeredFrame.RoundedPath(new RectangleF(.3f,.3f,Width-.6f,Height-.6f),10f))
                using(var background=new SolidBrush(cfg.Transparent
                    ? Color.FromArgb(1,31,33,38) // visually clear, still draggable
                    : Background))
                    g.FillPath(background,path);
                using(var brush=new SolidBrush(brand.ForeColor))
                using(var sf=new StringFormat {Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center,FormatFlags=StringFormatFlags.NoWrap})
                    g.DrawString(brand.Text,brand.Font,brush,new RectangleF(0,0,Width,14),sf);
                strip.DrawAt(g,14);
                if(cfg.ShowStereoMeter)stereo.DrawAt(g,12,17);
                pauseIcon.DrawAt(g,pauseBounds,hoveredPause);
                reset.DrawAt(g,resetBounds,hoveredReset);
                GearIcon.DrawAt(g,gearBounds,hoveredGear);
            });
        }
        catch(Exception ex)
        {
            layeringFailed=true;
            Program.ReportError(ex,"Rendering rounded meter window");
        }
    }

    private bool UpdateDisplay(bool render=true)
    {
        // The strip is drawn as ONE surface using the same GDI renderer for
        // measuring and painting. In v3.4–3.6 child Labels visibly cropped
        // numerical values and even the final digit of the clock.
        var playback=settingsWindow?.ReadPlayback();
        Snapshot s=playback?.Snapshot ?? meter.Read();
        TimeSpan t=playback?.ClipTime ?? elapsed.Elapsed;
        string desiredBrand=playback!=null ? "LUFFY v3.15 PREVIEW · PLAYBACK" : "LUFFY v3.15 PREVIEW";
        bool brandChanged=brand.Text!=desiredBrand;
        brand.Text=desiredBrand;
        string time=$"{(int)t.TotalMinutes}:{t.Seconds:00}";
        var normal=cfg.TextColor=="Black"?Color.Black:Color.White;
        var iColor=s.Integrated is double i
            ? i>cfg.Target+1?Red:i>=cfg.Target-1?Green:normal
            : normal;
        var tpColor=cfg.PeakWarning && s.TruePeak is double tp && tp>cfg.PeakLimit ? Red : normal;
        var entries=new System.Collections.Generic.List<ReadoutStrip.Entry>();
        if(cfg.ShowI)entries.Add(new ReadoutStrip.Entry("I "+Fmt(s.Integrated),iColor));
        if(cfg.ShowS)entries.Add(new ReadoutStrip.Entry("S "+FmtShort(s.ShortTerm),normal));
        if(cfg.ShowTP)entries.Add(new ReadoutStrip.Entry("TP "+Fmt(s.TruePeak),tpColor));
        if(cfg.ShowTime)entries.Add(new ReadoutStrip.Entry(time,s.Paused?Color.Orange:normal));
        strip.StereoReservedWidth=cfg.ShowStereoMeter?14:0;
        bool colorChanged=stereo.SetLevelColor(iColor);
        bool pauseChanged=pauseIcon.IsPaused!=s.Paused;
        pauseIcon.IsPaused=s.Paused;
        int lastFieldRight=strip.SetEntries(entries);
        bool changed=strip.LastEntriesChanged || colorChanged || pauseChanged || brandChanged;
        const int buttonGap=4, rightInset=8;
        int pauseX=lastFieldRight+buttonGap;
        pauseBounds=new Rectangle(pauseX,14,22,22);
        int resetX=pauseX+22+2;
        resetBounds=new Rectangle(resetX,14,22,22);
        int gearX=resetX+22+3;
        gearBounds=new Rectangle(gearX,16,18,18);
        int required=gearX+18+rightInset;
        // No silent horizontal clipping: always size for complete visible text.
        int newWidth=Math.Max(64,required);
        if(Width!=newWidth)
        {
            changed=true;
            resizingForLayout=true;
            try { Width=newWidth; }
            finally { resizingForLayout=false; }
            if(cfg.Docked && IsHandleCreated)PositionByTaskbar();
        }
        string status = playback!=null ? "PLAYBACK (original WAV) — live LUFS session preserved" : s.Error ?? (!meter.IsRunning
            ? "Audio capture stopped – Settings → Audio → Restart capture"
            : $"I: {Fmt(s.Integrated)} LUFS | S: {FmtShort(s.ShortTerm)} LUFS | TP: {Fmt(s.TruePeak)} dBTP");
        if(status!=previousTooltip)
        {
            previousTooltip=status;
            tooltip.SetToolTip(this,status+" | Double-click to open Settings");
        }
        if(render)RenderLayered();
        return changed;
    }
    // Practical digital silence is shown as an em dash. The underlying
    // short-term measurement and history are not altered.
    private static string FmtShort(double? value) => value is double v && v < -70 ? "—" : Fmt(value);
    private static string Fmt(double? value)=>value is double v && double.IsFinite(v)?v.ToString("0.0",CultureInfo.InvariantCulture):"—";
    private void Reset()
    {
        // In playback mode Reset seeks to the start of the WAV; the live
        // measurement must not be reset or saved accidentally.
        if(previewPlaying)
        {
            settingsWindow?.ResetPlayback();
            stereo.ResetLevels();UpdateDisplay();
            return;
        }
        SaveSession();
        // Reset starts a new measurement; save current session before restarting.
        bool remainPaused=meter.Paused;
        overlay.HideWarning();
        ambient.Hide();
        screenshot.ResetSession();
        // Mark the new capture as paused *before* its first WASAPI callback.
        meter.Start(cfg.DeviceId,cfg.UseInput,remainPaused);
        elapsed.Reset();
        if(!remainPaused)elapsed.Start();
        stereo.ResetLevels();
        meter.ConsumeStereoPeaks();
        UpdateDisplay();
        if(meter.Error is string resetError) MessageBox.Show(this,resetError,"Audio capture could not start",MessageBoxButtons.OK,MessageBoxIcon.Warning);
    }
    private void PauseResume()
    {
        // A clip is being played through Windows audio. Keep the active
        // measurement isolated from our own preview until playback stops.
        if(previewPlaying)
        {
            settingsWindow?.TogglePreviewPlayback();
            UpdateDisplay();
            return;
        }
        bool pause=!meter.Paused;
        meter.SetPaused(pause);
        if(pause)
        {
            elapsed.Stop();
            overlay.HideWarning();
            ambient.Hide();
        }
        else
        {
            elapsed.Start();
            lastStereoMs=uiClock.Elapsed.TotalMilliseconds;
        }
        UpdateDisplay();
    }
    private void SaveSession()
    {
        if(elapsed.Elapsed.TotalSeconds<1)return;
        try
        {
            Directory.CreateDirectory(Settings.Folder);
            string path=Path.Combine(Settings.Folder,"history.csv");
            bool header=!File.Exists(path);
            var s=meter.Read();
            using var writer=new StreamWriter(path,true,new UTF8Encoding(true));
            if(header)writer.WriteLine("Timestamp,Source,Profile,TargetLUFS,Seconds,IntegratedLUFS,ShortTermMaxLUFS,TruePeakMaxdBTP");
            writer.WriteLine(string.Join(",",DateTimeOffset.Now.ToString("o"),Csv(meter.SourceName),Csv(cfg.Profile),cfg.Target.ToString(CultureInfo.InvariantCulture),elapsed.Elapsed.TotalSeconds.ToString("0.0",CultureInfo.InvariantCulture),CsvNum(s.Integrated),CsvNum(s.MaxShortTerm),CsvNum(s.TruePeak)));
        }catch{}
    }
    private static string Csv(string value)=>"\""+value.Replace("\"","\"\"")+"\"";
    private static string CsvNum(double? value)=>value?.ToString("0.00",CultureInfo.InvariantCulture)??"";
    private void BuildMenu()
    {
        menu.Items.Clear();
        menu.Items.Add(new ToolStripMenuItem("Settings…  (double-click / gear)",null,(_,_)=>OpenSettings()));
        menu.Items.Add(new ToolStripSeparator());
        var sourceItem=new ToolStripMenuItem("Audio source (Windows outputs / inputs)");
        using(var enumerator=new MMDeviceEnumerator())
        {
            try
            {
                var def=enumerator.GetDefaultAudioEndpoint(DataFlow.Render,Role.Multimedia);
                var m=new ToolStripMenuItem("Windows default output"){Checked=!cfg.UseInput && cfg.DeviceId==null};
                m.Click+=(_,_)=>SetSource(null, false);
                sourceItem.DropDownItems.Add(m);
                foreach(var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render,DeviceState.Active))
                {
                    string id=device.ID;
                    var item=new ToolStripMenuItem(device.FriendlyName){Checked=!cfg.UseInput && cfg.DeviceId==id};
                    item.Click+=(_,_)=>SetSource(id, false);
                    sourceItem.DropDownItems.Add(item);
                }
            }
            catch(Exception ex){sourceItem.DropDownItems.Add(new ToolStripMenuItem(ex.Message){Enabled=false});}
        }
        sourceItem.DropDownItems.Add(new ToolStripSeparator());
        sourceItem.DropDownItems.Add(new ToolStripMenuItem("Windows input / Focusrite Loopback") { Enabled = false });
        using(var inputEnumerator = new MMDeviceEnumerator())
        {
            try
            {
                var defaultInput = new ToolStripMenuItem("Default Windows recording input")
                    { Checked = cfg.UseInput && cfg.DeviceId == null };
                defaultInput.Click += (_,_) => SetSource(null, true);
                sourceItem.DropDownItems.Add(defaultInput);
                foreach(var device in inputEnumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
                {
                    string id = device.ID;
                    var item = new ToolStripMenuItem("Input: " + device.FriendlyName)
                        { Checked = cfg.UseInput && cfg.DeviceId == id };
                    item.Click += (_,_) => SetSource(id, true);
                    sourceItem.DropDownItems.Add(item);
                }
            }
            catch(Exception ex) { sourceItem.DropDownItems.Add(new ToolStripMenuItem("Inputs unavailable: " + ex.Message){Enabled=false}); }
        }
        sourceItem.DropDownItems.Add(new ToolStripSeparator());
        sourceItem.DropDownItems.Add(new ToolStripMenuItem("Direct ASIO capture is not supported; select Focusrite Loopback input") { Enabled=false });
        menu.Items.Add(sourceItem);
        var targetItem=new ToolStripMenuItem("LUFS target / profile");
        foreach(var (label,value) in new[]{("Game (-23)",-23d),("Music (-16)",-16d),("Streaming (-14)",-14d)})
        {
            double v=value;
            var item=new ToolStripMenuItem(label){Checked=cfg.Target==v};
            item.Click+=(_,_)=>{cfg.Target=v;cfg.Profile=label.Split(' ')[0];cfg.Save();UpdateDisplay();};
            targetItem.DropDownItems.Add(item);
        }
        targetItem.DropDownItems.Add(new ToolStripMenuItem("Custom target…",null,(_,_)=>CustomTarget()));
        targetItem.DropDownItems.Add(new ToolStripMenuItem("True Peak warning threshold…",null,(_,_)=>CustomPeakLimit()));
        menu.Items.Add(targetItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem(meter.Paused?"Resume measurement":"Pause measurement",null,(_,_)=>PauseResume()));
        menu.Items.Add(new ToolStripMenuItem("Reset measurement  (Ctrl+Alt+R)",null,(_,_)=>Reset()));
        menu.Items.Add(new ToolStripMenuItem("Test True Peak warning",null,(_,_)=>ShowPeakWarning(true)));
        menu.Items.Add(new ToolStripSeparator());
        AddToggle(menu,"Always on Top",cfg.OnTop,v=>{cfg.OnTop=v;TopMost=v;});
        AddToggle(menu,"True Peak warning",cfg.PeakWarning,v=>cfg.PeakWarning=v);
        menu.Items.Add(new ToolStripSeparator());
        var tools=new ToolStripMenuItem("Tools & diagnostics");
        tools.DropDownItems.Add(new ToolStripMenuItem("Restart capture (resets measurement)…",null,(_,_)=>RestartCaptureWithWarning()));
        tools.DropDownItems.Add(new ToolStripMenuItem($"Capture: {(meter.Error != null ? meter.Error : meter.IsRunning ? "Running" : "Stopped")} · buffers {meter.CapturedBuffers}"){Enabled=false});
        tools.DropDownItems.Add(new ToolStripMenuItem("True Peak diagnostics…",null,(_,_)=>OpenDiagnostics()));
        tools.DropDownItems.Add(new ToolStripMenuItem("Open measurement history (CSV)",null,(_,_)=>OpenHistory()));
        tools.DropDownItems.Add(new ToolStripMenuItem(hotkeyRegistered?"Reset shortcut: Ctrl+Alt+R":"Reset shortcut: unavailable") {Enabled=false});
        menu.Items.Add(tools);
        menu.Items.Add(new ToolStripMenuItem("About LUFFY",null,(_,_)=>ShowAbout()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Exit",null,(_,_)=>Close()));
    }
    private void UpdateAmbient()
    {
        var playback=settingsWindow?.ReadPlayback();
        bool active=cfg.AmbientEnabled && playback==null && meter.IsRunning && !meter.Paused;
        bool suppressed=(DateTime.UtcNow-lastPeakWarning).TotalSeconds<
            cfg.WarningDurationSeconds+(cfg.WarningFadeOut?0.3:0);
        ambient.Update(active?meter.Read().ShortTerm:null,cfg.AmbientTarget,
            cfg.AmbientRangeLU,active,suppressed);
    }
    private void CheckPeakWarning()
    {
        if((!cfg.PeakWarning && !cfg.PeakScreenshotEnabled) || !meter.IsRunning || meter.Paused)
        {
            meter.ClearPendingPeakEvents();
            return;
        }
        // Process ALL buffer peaks, not just the last of the UI timer interval.
        // Edge-triggered, so 0.0 second cooldown does not keep restarting one
        // held warning while a loud signal stays above the limit.
        double? eventPeak=meter.ConsumePeakCrossingValue(cfg.PeakLimit);
        if(eventPeak is not double peak)return;
        // Capture the screen before the red warning overlay is drawn.
        screenshot.Capture(cfg,peak);
        if(!cfg.PeakWarning)return;
        if((DateTime.UtcNow-lastPeakWarning).TotalSeconds<cfg.WarningCooldownSeconds)return;
        ambient.Hide();
        ShowPeakWarning(false);
    }
    private void ShowPeakWarning(bool test)
    {
        if(!test && !cfg.PeakWarning)return;
        lastPeakWarning=DateTime.UtcNow;
        try { overlay.ShowWarning(cfg.GlowStyle,cfg.WarningDurationSeconds,cfg.WarningFadeOut,cfg.WarningBorderPx); }
        catch(Exception ex) { Program.ReportError(ex,"Displaying True Peak warning"); }
    }
    private void ConfigureWarning()
    {
        using var dialog=new Form { Text="True Peak warning timing",StartPosition=FormStartPosition.CenterParent,FormBorderStyle=FormBorderStyle.FixedDialog,ClientSize=new Size(300,165),MaximizeBox=false,MinimizeBox=false,ShowInTaskbar=false };
        var duration=new NumericUpDown {Minimum=0.2m,Maximum=10,Increment=0.1m,DecimalPlaces=1,Value=(decimal)Math.Clamp(cfg.WarningDurationSeconds,0.2,10),Location=new Point(160,16),Width=112};
        var cooldown=new NumericUpDown {Minimum=0,Maximum=30,Increment=0.5m,DecimalPlaces=1,Value=(decimal)Math.Clamp(cfg.WarningCooldownSeconds,0,30),Location=new Point(160,51),Width=112};
        var fade=new CheckBox {Text="Fade out (no fade in)",Checked=cfg.WarningFadeOut,Location=new Point(14,88),Width=250};
        var ok=new Button {Text="Save",DialogResult=DialogResult.OK,Location=new Point(193,125),Width=79};
        dialog.Controls.AddRange(new Control[]{new Label{Text="Duration (seconds)",Location=new Point(14,19),AutoSize=true},new Label{Text="Cooldown (seconds)",Location=new Point(14,54),AutoSize=true},duration,cooldown,fade,ok});dialog.AcceptButton=ok;
        if(dialog.ShowDialog(this)==DialogResult.OK){cfg.WarningDurationSeconds=(double)duration.Value;cfg.WarningCooldownSeconds=(double)cooldown.Value;cfg.WarningFadeOut=fade.Checked;cfg.Save();}
    }
    private void PreviewListeningChanged(bool active)
    {
        if(active)
        {
            if(previewPlaying)return;
            previewPlaying=true;
            resumeMeasurementAfterPreview=!meter.Paused;
            stereo.ResetLevels();
            if(!meter.Paused)
            {
                meter.SetPaused(true);
                elapsed.Stop();
                overlay.HideWarning();
            }
        }
        else
        {
            if(!previewPlaying)return;
            previewPlaying=false;
            if(resumeMeasurementAfterPreview && meter.Paused)
            {
                meter.SetPaused(false);
                elapsed.Start();
                lastStereoMs=uiClock.Elapsed.TotalMilliseconds;
            }
            resumeMeasurementAfterPreview=false;
            stereo.ResetLevels();
        }
        UpdateDisplay();
    }
    private void OpenSettings()
    {
        if(settingsWindow==null || settingsWindow.IsDisposed)
            settingsWindow=new SettingsWindow(cfg, ()=>{meter.ConfigurePeakCapture(cfg);ApplyStyle();TopMost=cfg.OnTop;},
                ()=>ShowPeakWarning(true), ()=>PositionByTaskbar(), IsStartupEnabled, SetStartup,
                meter,SetSource,RestartCaptureWithWarning,OpenDiagnostics,OpenHistory,ShowAbout,
                () => hotkeyRegistered,PreviewListeningChanged);
        if(!settingsWindow.Visible)settingsWindow.Show();
        if(settingsWindow.WindowState==FormWindowState.Minimized)settingsWindow.WindowState=FormWindowState.Normal;
        settingsWindow.BringToFront();settingsWindow.Activate();
    }
    private void ShowAbout()
    {
        MessageBox.Show(this,"LUFFY v3.15 PREVIEW\nby Andreas Busk\ncontact@andreasbusk.com\n\nIntegrated LUFS · Short-term LUFS · True Peak\nPeak Capture stores local WAV clips when enabled.\n\nBeta software: validate measurements before critical professional use.",
            "About LUFFY",MessageBoxButtons.OK,MessageBoxIcon.Information);
    }
    private void RestartCaptureWithWarning()
    {
        var answer=MessageBox.Show(this,
            "Restarting the audio device starts a new loudness measurement. Save the current session and continue?",
            "Restart audio capture",MessageBoxButtons.YesNo,MessageBoxIcon.Question);
        if(answer==DialogResult.Yes)Reset();
    }
    private void AddToggle(ToolStripMenuItem parent,string text,bool state,Action<bool> apply)
    {var item=new ToolStripMenuItem(text){Checked=state,CheckOnClick=true};item.Click+=(_,_)=>{apply(item.Checked);cfg.Save();ApplyStyle();};parent.DropDownItems.Add(item);}
    private void AddToggle(ContextMenuStrip parent,string text,bool state,Action<bool> apply)
    {var item=new ToolStripMenuItem(text){Checked=state,CheckOnClick=true};item.Click+=(_,_)=>{apply(item.Checked);cfg.Save();ApplyStyle();};parent.Items.Add(item);}
    private void CustomTarget()=>AskNumber("Custom integrated LUFS target",(decimal)cfg.Target,-70,0, v=>cfg.Target=v);
    private void CustomPeakLimit()=>AskNumber("True Peak alert threshold (dBTP)",(decimal)cfg.PeakLimit,-30,6,v=>cfg.PeakLimit=v);
    private void AskNumber(string caption,decimal current,decimal minimum,decimal maximum,Action<double> apply)
    {
        using var dlg=new Form{Text=caption,FormBorderStyle=FormBorderStyle.FixedDialog,StartPosition=FormStartPosition.CenterParent,ClientSize=new Size(270,110),MaximizeBox=false,MinimizeBox=false,ShowInTaskbar=false};
        var value=new NumericUpDown{Minimum=minimum,Maximum=maximum,DecimalPlaces=1,Increment=.5m,Value=Math.Clamp(current,minimum,maximum),Location=new Point(12,12),Width=244};
        var ok=new Button{Text="OK",DialogResult=DialogResult.OK,Location=new Point(90,60),Width=76};
        var cancel=new Button{Text="Cancel",DialogResult=DialogResult.Cancel,Location=new Point(175,60),Width=76};
        dlg.Controls.AddRange(new Control[]{value,ok,cancel});dlg.AcceptButton=ok;dlg.CancelButton=cancel;
        if(dlg.ShowDialog(this)==DialogResult.OK){apply((double)value.Value);cfg.Profile="Custom";cfg.Save();UpdateDisplay();}
    }
    private void SetSource(string? id, bool inputSource)
    {
        if(id==cfg.DeviceId && inputSource==cfg.UseInput)return;
        SaveSession();
        cfg.DeviceId=id;cfg.UseInput=inputSource;cfg.Save();
        bool remainPaused=meter.Paused;
        overlay.HideWarning();
        // Never process an initial buffer if the user was already paused.
        meter.Start(id,inputSource,remainPaused);
        elapsed.Reset();
        if(!remainPaused)elapsed.Start();
        stereo.ResetLevels();
        UpdateDisplay();
        if(meter.Error!=null) MessageBox.Show(this,meter.Error,"Audio capture error",MessageBoxButtons.OK,MessageBoxIcon.Warning);
    }
    private void OpenDiagnostics()
    {
        if(diagnosticWindow == null || diagnosticWindow.IsDisposed)
            diagnosticWindow=new DiagnosticWindow(meter);
        diagnosticWindow.Show();diagnosticWindow.BringToFront();
    }
    private void OpenHistory()
    {
        var path=Path.Combine(Settings.Folder,"history.csv");
        if(!File.Exists(path)){MessageBox.Show(this,"No measurements have been saved yet. Reset after measuring for at least one second.","Measurement history");return;}
        Process.Start(new ProcessStartInfo(path){UseShellExecute=true});
    }
    private static void SyncStartupExecutableIfEnabled()
    {
        try
        {
            using var key=Registry.CurrentUser.OpenSubKey(StartupKey,true);
            if(key?.GetValue("LufsTaskbarMeter") is string)
                key.SetValue("LufsTaskbarMeter","\""+Application.ExecutablePath+"\"");
        }
        catch(Exception ex){Program.LogError(ex,"Updating Start with Windows path");}
    }
    private static bool IsStartupEnabled()
    {try{using var reg=Registry.CurrentUser.OpenSubKey(StartupKey);return reg?.GetValue("LufsTaskbarMeter") is string;}catch{return false;}}
    private void SetStartup(bool enabled)
    {try{using var key=Registry.CurrentUser.OpenSubKey(StartupKey,true);if(enabled)key?.SetValue("LufsTaskbarMeter","\""+Application.ExecutablePath+"\"");else key?.DeleteValue("LufsTaskbarMeter",false);}catch(Exception ex){MessageBox.Show(this,ex.Message,"Autostart failed");}}
    // Place above the taskbar instead of fighting Windows 11's topmost
    // taskbar every second. No z-order toggling or focus stealing.
    private void PositionByTaskbar()
    {
        var screen=Screen.FromPoint(Cursor.Position);
        var work=screen.WorkingArea;
        int y=work.Bottom-Height-4;
        int x=work.Right-Width-12;
        programmaticMove=true;
        try { Location=new Point(Math.Max(work.Left,x),Math.Max(work.Top,y)); }
        finally { programmaticMove=false; }
    }
    private void DisplayChanged(object? sender,EventArgs e)
    {
        if(IsDisposed || Disposing)return;
        if(InvokeRequired)
        {
            if(!IsHandleCreated)return;
            try { BeginInvoke(new Action(()=>DisplayChanged(sender,e))); }
            catch(InvalidOperationException) { } // Includes ObjectDisposedException (a derived exception).
            return;
        }
        overlay.HideWarning();
        ambient.DisplayChanged();
        if(cfg.Docked)PositionByTaskbar();
    }
    private bool OverTruePeak(Point point)
    {
        if(!cfg.ShowTP || previewPlaying)return false;
        var bounds=strip.BoundsForEntryPrefix("TP ");
        if(bounds is not Rectangle entry)return false;
        entry.Offset(0,14);
        return entry.Contains(point);
    }
    private HitTarget Hit(Point point) => OverTruePeak(point)?HitTarget.TruePeak:
        pauseBounds.Contains(point)?HitTarget.Pause:
        resetBounds.Contains(point)?HitTarget.Reset:
        gearBounds.Contains(point)?HitTarget.Gear:HitTarget.None;
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if(e.Button!=MouseButtons.Left)return;
        pressed=Hit(e.Location);
        if(pressed==HitTarget.None) { dragging=true;dragAt=Cursor.Position; }
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        bool tp=OverTruePeak(e.Location),p=pauseBounds.Contains(e.Location),r=resetBounds.Contains(e.Location),gear=gearBounds.Contains(e.Location);
        if(tp!=hoveredTp || p!=hoveredPause || r!=hoveredReset || gear!=hoveredGear)
        {
            hoveredTp=tp;hoveredPause=p;hoveredReset=r;hoveredGear=gear;
            Cursor=tp||p||r||gear?Cursors.Hand:Cursors.Default;
            tooltip.SetToolTip(this,tp?"Click to reset True Peak max":p?(meter.Paused?"Resume measurement":"Pause measurement"):
                r?"Reset measurement":gear?"Open Settings":"Double-click for Settings; right-click for menu");
            RenderLayered();
        }
        if(!dragging || Control.MouseButtons!=MouseButtons.Left)return;
        var pt=Cursor.Position;
        if(pt==dragAt)return;
        cfg.Docked=false;
        programmaticMove=true;
        try {Location=new Point(Left+pt.X-dragAt.X,Top+pt.Y-dragAt.Y);}
        finally {programmaticMove=false;}
        dragAt=pt;cfg.X=Left;cfg.Y=Top;
    }
    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if(e.Button!=MouseButtons.Left)return;
        if(dragging){dragging=false;cfg.Save();}
        else if(pressed!=HitTarget.None && Hit(e.Location)==pressed)
        {
            if(pressed==HitTarget.TruePeak)
            {
                meter.ResetTruePeakMaximum();
                UpdateDisplay();
            }
            else if(pressed==HitTarget.Pause)PauseResume();
            else if(pressed==HitTarget.Reset)Reset();
            else if(pressed==HitTarget.Gear)OpenSettings();
        }
        pressed=HitTarget.None;
    }
    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if(hoveredTp || hoveredPause || hoveredReset || hoveredGear){hoveredTp=false;hoveredPause=false;hoveredReset=false;hoveredGear=false;Cursor=Cursors.Default;RenderLayered();}
    }
    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        if(e.Button==MouseButtons.Left && Hit(e.Location)==HitTarget.None)OpenSettings();
    }
    [DllImport("user32.dll")]private static extern bool RegisterHotKey(IntPtr hWnd,int id,uint modifiers,uint key);
    [DllImport("user32.dll")]private static extern bool UnregisterHotKey(IntPtr hWnd,int id);
    private bool hotkeyRegistered;
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        hotkeyRegistered=RegisterHotKey(Handle,HOTKEY_ID,0x0001|0x0002,(uint)Keys.R);
    }
    protected override void WndProc(ref Message m){if(m.Msg==(int)Program.WakeMessage){Show();WindowState=FormWindowState.Normal;BringToFront();return;}if(m.Msg==0x0312 && m.WParam.ToInt32()==HOTKEY_ID){Reset();return;}base.WndProc(ref m);}
    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        SaveSession();timer.Stop();timer.Dispose();
        settingsWindow?.Close();overlay.Dispose();ambient.Dispose();
        if(hotkeyRegistered && IsHandleCreated)UnregisterHotKey(Handle,HOTKEY_ID);
        SystemEvents.DisplaySettingsChanged-=DisplayChanged;
        meter.Dispose();readoutFont.Dispose();brand.Dispose();strip.Dispose();
        stereo.Dispose();pauseIcon.Dispose();reset.Dispose();resetHolder.Dispose();
        tooltip.Dispose();menu.Dispose();cfg.Save();base.OnFormClosed(e);
    }
}
