using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Runtime.InteropServices;

namespace LufsTaskbarMeter;

// One on-demand capture at the trigger, with no video or image ring buffer.
// Capture pixels before the coral warning is shown; encode/save off the UI thread.
internal sealed class PeakScreenshotCapture
{
    private DateTime last=DateTime.MinValue;
    private int saved;
    private bool busy;
    [DllImport("user32.dll")]private static extern IntPtr GetForegroundWindow();
    public void ResetSession(){saved=0;last=DateTime.MinValue;}
    public void Capture(Settings cfg,double peak)
    {
        if(!cfg.PeakScreenshotEnabled || busy || saved>=cfg.PeakScreenshotMaxPerSession)return;
        DateTime now=DateTime.Now;
        if((now-last).TotalSeconds<cfg.PeakScreenshotCooldownSeconds)return;
        last=now;busy=true;
        // Capture the foreground monitor, not the meter's own location.
        // A dedicated game window capture can be introduced only after testing.
        Screen screen=Screen.FromHandle(GetForegroundWindow());
        Rectangle bounds=screen.Bounds;
        Bitmap? source=null;
        try
        {
            source=new Bitmap(bounds.Width,bounds.Height,PixelFormat.Format32bppArgb);
            using(var g=Graphics.FromImage(source))
                g.CopyFromScreen(bounds.Location,Point.Empty,bounds.Size,CopyPixelOperation.SourceCopy);
            saved++;
            Bitmap captured=source;
            source=null;
            string folder=Path.Combine(cfg.PeakCaptureFolder,"Screenshots");
            bool watermark=cfg.PeakScreenshotWatermark;
            _=Task.Run(()=>
            {
                try
                {
                    Directory.CreateDirectory(folder);
                    int width=Math.Min(1280,captured.Width);
                    int height=Math.Max(1,(int)Math.Round(captured.Height*(double)width/captured.Width));
                    if(height>720){height=720;width=Math.Max(1,(int)Math.Round(captured.Width*(double)height/captured.Height));}
                    using var scaled=new Bitmap(width,height,PixelFormat.Format24bppRgb);
                    using(var g=Graphics.FromImage(scaled))
                    {
                        g.InterpolationMode=InterpolationMode.HighQualityBilinear;
                        g.DrawImage(captured,new Rectangle(0,0,width,height));
                        if(watermark)
                        {
                            string label=$"TP {peak:+0.0;-0.0;0.0} dBTP  {now:HH:mm:ss}";
                            using var font=new Font("Segoe UI",Math.Max(11,width/95f),FontStyle.Bold,GraphicsUnit.Pixel);
                            SizeF sz=g.MeasureString(label,font);
                            var rect=new RectangleF(width-sz.Width-22,height-sz.Height-16,sz.Width+12,sz.Height+8);
                            using var background=new SolidBrush(Color.FromArgb(225,23,28,33));
                            using var ink=new SolidBrush(Color.FromArgb(255,244,169,161));
                            g.FillRectangle(background,rect);
                            g.DrawString(label,font,ink,rect.Left+6,rect.Top+4);
                        }
                    }
                    string name=$"LUFFY_Peak_{now:yyyy-MM-dd_HH-mm-ss-fff}_TP_{peak.ToString("+0.0;-0.0;0.0",CultureInfo.InvariantCulture)}dBTP.jpg";
                    string path=Path.Combine(folder,name);
                    var encoder=ImageCodecInfo.GetImageEncoders().First(x=>x.MimeType=="image/jpeg");
                    using var parameters=new EncoderParameters(1);
                    parameters.Param[0]=new EncoderParameter(System.Drawing.Imaging.Encoder.Quality,80L);
                    scaled.Save(path,encoder,parameters);
                }
                catch(Exception ex){Program.LogError(ex,"Saving peak screenshot");}
                finally{captured.Dispose();busy=false;}
            });
        }
        catch(Exception ex){source?.Dispose();busy=false;Program.LogError(ex,"Capturing peak screenshot");}
    }
}
