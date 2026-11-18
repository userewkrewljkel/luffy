using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace LufsTaskbarMeter;

// Captures the desktop only on actual True Peak limit crossings when enabled.
// No perpetual screenshot buffer or capture thread. At most one image is in flight.
internal sealed class PeakScreenshotRecorder
{
    private readonly object gate=new();
    private Task pending=Task.CompletedTask;
    private DateTime lastCaptureUtc=DateTime.MinValue;
    private bool suspended;

    public bool TryCapture(Settings cfg)
    {
        if(!cfg.PeakScreenshotEnabled)return false;
        lock(gate)
        {
            var now=DateTime.UtcNow;
            if(suspended || !pending.IsCompleted || 
               (now-lastCaptureUtc).TotalSeconds<cfg.ScreenshotMinIntervalSeconds)
                return false;
            Rectangle screens=SystemInformation.VirtualScreen;
            if(screens.Width<=0 || screens.Height<=0)return false;
            string folder=cfg.PeakScreenshotFolder;
            DateTime time=DateTime.Now;
            lastCaptureUtc=now;
            pending=Task.Run(()=>SaveCapture(folder,screens,time));
            return true;
        }
    }

    private static void SaveCapture(string folder,Rectangle screens,DateTime time)
    {
        string file=Path.Combine(folder,
            $"LUFFY_TP_{time:yyyy-MM-dd_HH-mm-ss_fff}_{Guid.NewGuid().ToString("N")[..8]}.png");
        try
        {
            // Capture and PNG encoding are performed only following a peak.
            // This intentionally makes no attempt to buffer past screen images.
            using var bitmap=new Bitmap(screens.Width,screens.Height,PixelFormat.Format32bppArgb);
            using(var graphics=Graphics.FromImage(bitmap))
                graphics.CopyFromScreen(screens.Location,Point.Empty,screens.Size,CopyPixelOperation.SourceCopy);
            Directory.CreateDirectory(folder);
            bitmap.Save(file,ImageFormat.Png);
        }
        catch(Exception ex)
        {
            Program.LogError(ex,"Saving automatic True Peak screenshot");
            // Leave any partial image in its own folder for the user to review
            // or move to the Recycle Bin. No permanent-delete fallback.
        }
    }

    // Stop new captures and drain the current writer before a bulk recycle action.
    public Task SuspendAndWaitAsync()
    {
        lock(gate)
        {
            suspended=true;
            return pending;
        }
    }
    public void ResumeAfterCleanup()
    {
        lock(gate)suspended=false;
    }
}
