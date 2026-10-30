using System.Text.Json;
using System.Drawing;
namespace LufsTaskbarMeter;
internal sealed class Settings
{
    public double Target {get;set;}=-23;
    public double PeakLimit {get;set;}=-1;
    public string Profile {get;set;}="Game";
    public bool OnTop {get;set;}=true;
    public bool Transparent {get;set;}
    public bool Docked {get;set;}=true;
    public bool ShowI {get;set;}=true;
    public bool ShowS {get;set;}=true;
    public bool ShowTP {get;set;}=true;
    public bool ShowTime {get;set;}=true;
    public bool ShowStereoMeter {get;set;}=true;
    public bool PeakWarning {get;set;}=true;
    public string GlowStyle {get;set;}="Studio";
    public int WarningBorderPx {get;set;}=5;
    public double WarningDurationSeconds {get;set;}=0.5;
    public double WarningCooldownSeconds {get;set;}=0.0;
    public bool WarningFadeOut {get;set;}=true;
    // New settings default OFF for existing and new installations.
    public bool AmbientEnabled {get;set;}=false;
    public double AmbientTarget {get;set;}=-23;
    public double AmbientRangeLU {get;set;}=2;
    public bool PeakScreenshotEnabled {get;set;}=false;
    public bool PeakScreenshotWatermark {get;set;}=true;
    public double PeakScreenshotCooldownSeconds {get;set;}=5;
    public int PeakScreenshotMaxPerSession {get;set;}=100;
    public int FontSize {get;set;}=11;
    public int X {get;set;}=-99999;
    public int Y {get;set;}=-99999;
    public string? DeviceId {get;set;}
    public bool UseInput {get;set;} = false;
    public string TextColor {get;set;}="White";
    // Playback preferences only affect listening, never the captured WAV file.
    public string? PreviewOutputDeviceId {get;set;}
    public int PreviewGainDb {get;set;}=0;
    public bool PeakCaptureEnabled {get;set;}=false;
    public int PeakBeforeSeconds {get;set;}=5;
    public int PeakAfterSeconds {get;set;}=5;
    public string PeakCaptureFolder {get;set;}=DefaultPeakFolder;
    public static string DefaultPeakFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),"LUFFY","Peak Captures");
    public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"LufsTaskbarMeter");
    public static string FilePath => Path.Combine(Folder,"settings.json");
    public static Settings Load()
    {
        try
        {
            var cfg = File.Exists(FilePath)
                ? JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings()
                : new Settings();
            cfg.Validate();
            return cfg;
        }
        catch (Exception ex)
        {
            Program.LogError(ex, "Loading settings; using defaults");
            return new Settings();
        }
    }
    private void Validate()
    {
        // An invalid setting must never prevent the meter window from opening.
        if(!double.IsFinite(Target)) Target=-23;
        Target=Math.Clamp(Target,-70,0);
        if(!double.IsFinite(PeakLimit)) PeakLimit=-1;
        PeakLimit=Math.Clamp(PeakLimit,-30,6);
        if(!double.IsFinite(WarningDurationSeconds)) WarningDurationSeconds=0.5;
        WarningDurationSeconds=Math.Clamp(WarningDurationSeconds,0.2,10);
        if(!double.IsFinite(WarningCooldownSeconds)) WarningCooldownSeconds=0;
        WarningCooldownSeconds=Math.Clamp(WarningCooldownSeconds,0,30);
        WarningBorderPx=Math.Clamp(WarningBorderPx,2,10);
        if(!double.IsFinite(AmbientTarget))AmbientTarget=-23;
        AmbientTarget=Math.Clamp(AmbientTarget,-70,0);
        if(!double.IsFinite(AmbientRangeLU))AmbientRangeLU=2;
        AmbientRangeLU=Math.Clamp(AmbientRangeLU,0.5,10);
        if(!double.IsFinite(PeakScreenshotCooldownSeconds))PeakScreenshotCooldownSeconds=5;
        PeakScreenshotCooldownSeconds=Math.Clamp(PeakScreenshotCooldownSeconds,0,60);
        PeakScreenshotMaxPerSession=Math.Clamp(PeakScreenshotMaxPerSession,10,500);
        FontSize=Math.Clamp(FontSize,10,15);
        if(GlowStyle is not ("Soft" or "Studio" or "Alert"))GlowStyle="Studio";
        if(TextColor is not ("Black" or "White"))TextColor="White";
        Profile ??= "Game";
        PreviewGainDb=Math.Clamp(PreviewGainDb,-24,12);
        PeakBeforeSeconds=Math.Clamp(PeakBeforeSeconds,1,30);
        PeakAfterSeconds=Math.Clamp(PeakAfterSeconds,1,30);
        if(string.IsNullOrWhiteSpace(PeakCaptureFolder)) PeakCaptureFolder=DefaultPeakFolder;
    }
    public void Save()
    {
        try
        {
            Validate();
            Directory.CreateDirectory(Folder);
            string pending = FilePath+".tmp";
            File.WriteAllText(pending,JsonSerializer.Serialize(this,new JsonSerializerOptions{WriteIndented=true}));
            File.Move(pending,FilePath,true); // Atomic rename prevents half-written JSON.
        }
        catch(Exception ex){ Program.LogError(ex,"Saving settings"); }
    }
}
