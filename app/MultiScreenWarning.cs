using System;
using System.Collections.Generic;
using System.Windows.Forms;
namespace LufsTaskbarMeter;
internal sealed class MultiScreenWarning : IDisposable
{
    private readonly List<PeakWarningOverlay> overlays=new();
    public void ShowWarning(string style,double duration,bool fade,int border)
    {
        HideWarning();
        foreach(var screen in Screen.AllScreens)
        {
            var overlay=new PeakWarningOverlay();
            try
            {
                overlay.ShowWarning(screen,style,duration,fade,border);
                overlays.Add(overlay);
            }
            catch(Exception ex)
            {
                overlay.Dispose();
                Program.LogError(ex,"True Peak overlay on "+screen.DeviceName);
            }
        }
    }
    public void HideWarning()
    {
        foreach(var overlay in overlays){overlay.HideWarning();overlay.Dispose();}
        overlays.Clear();
    }
    public void Dispose()=>HideWarning();
}
