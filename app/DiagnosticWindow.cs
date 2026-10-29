using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace LufsTaskbarMeter;

// Optional diagnostics: deliberately not added to the compact taskbar strip.
internal sealed class DiagnosticWindow : Form
{
    private readonly LoudnessMeter meter;
    private readonly Label status = new();
    private readonly Label sample = new();
    private readonly Label truePeak = new();
    private readonly Label delta = new();
    private readonly Label input = new();
    private readonly Label readout = new();
    private readonly Label capture = new();
    private readonly TextBox events = new();
    private readonly System.Windows.Forms.Timer refresh = new(){Interval=250};
    public DiagnosticWindow(LoudnessMeter meter)
    {
        this.meter=meter;
        Text="LUFFY v3.14 – Audio diagnostics";
        StartPosition=FormStartPosition.CenterScreen;
        ClientSize=new Size(630,395);
        MinimumSize=new Size(580,340);
        var panel=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=8,Padding=new Padding(12)};
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute,28));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute,34));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute,34));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute,26));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute,26));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute,27));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute,30));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        foreach(var label in new[]{status,input,capture,sample,truePeak,delta,readout})
        {
            label.Dock=DockStyle.Fill;label.TextAlign=ContentAlignment.MiddleLeft;
            label.Font=new Font("Segoe UI",10,FontStyle.Regular);
            panel.Controls.Add(label);
        }
        events.Multiline=true;events.ReadOnly=true;events.ScrollBars=ScrollBars.Vertical;
        events.Dock=DockStyle.Fill;events.Font=new Font("Consolas",9);
        panel.Controls.Add(events);
        Controls.Add(panel);
        refresh.Tick+=(_,_)=>RefreshData();
        refresh.Start();RefreshData();
        FormClosed+=(_,_)=>refresh.Dispose();
    }
    private static string Number(double? x)=>x.HasValue?x.Value.ToString("+0.00;-0.00;0.00",CultureInfo.InvariantCulture):"—";
    private static string Time(double? x)=>x.HasValue?x.Value.ToString("0.000",CultureInfo.InvariantCulture)+"s":"—";
    private void RefreshData()
    {
        var d=meter.ReadDiagnostics();
        var signal=meter.ReadInputSignal();
        status.Text=$"Source: {meter.SourceName} | {signal.Format}";
        string live=signal.LastPeakDbfs.HasValue ? $"{signal.LastPeakDbfs.Value:0.0} dBFS" : "SILENCE / no sample";
        string rms=signal.LastRmsDbfs.HasValue ? $"{signal.LastRmsDbfs.Value:0.0} dBFS" : "—";
        input.Text=$"Input peak (last buffer): {live} | RMS: {rms}";
        string age=signal.LastBufferAtUtc==DateTime.MinValue ? "never" : $"{(DateTime.UtcNow-signal.LastBufferAtUtc).TotalSeconds:0.0}s ago";
        capture.Text=$"Running: {signal.Running} | Paused: {signal.Paused} | Buffers: {signal.Buffers} | Frames: {signal.Frames} | Last: {age}";
        if(signal.Error!=null)capture.Text="ERROR: "+signal.Error;
        sample.Text=$"Sample Peak:  {Number(d.SamplePeak)} dBFS    at {Time(d.SampleTime)}    channel {d.SampleChannel}";
        truePeak.Text=$"True Peak:     {Number(d.TruePeak)} dBTP    at {Time(d.TrueTime)}    channel {d.TrueChannel}";
        delta.Text=d.TruePeak.HasValue && d.SamplePeak.HasValue?
            $"TP − Sample Peak: {(d.TruePeak.Value-d.SamplePeak.Value):+0.00;-0.00;0.00} dB":"Waiting for audio…";
        var snapshot=meter.Read();
        string shortText=snapshot.ShortTerm is double shortValue && shortValue < -70
            ? "—" : Number(snapshot.ShortTerm);
        readout.Text=$"Meter Snapshot → I: {Number(snapshot.Integrated)} LUFS | S: {shortText} LUFS | TP: {Number(snapshot.TruePeak)} dBTP";
        events.Lines=meter.GetPeakEvents();
        events.SelectionStart=events.TextLength;events.ScrollToCaret();
    }
}
