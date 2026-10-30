using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace LufsTaskbarMeter;

// Four thin, mouse-transparent windows per monitor. No full-screen bitmap,
// image buffer, or animation loop: update only when the loudness changes.
internal sealed class AmbientBorder : IDisposable
{
    private const int EX_TRANSPARENT=0x20, EX_TOOLWINDOW=0x80, EX_NOACTIVATE=0x08000000;
    private sealed class Edge : Form
    {
        protected override CreateParams CreateParams
        {
            get { var p=base.CreateParams; p.ExStyle|=EX_TRANSPARENT|EX_TOOLWINDOW|EX_NOACTIVATE; return p; }
        }
        protected override bool ShowWithoutActivation=>true;
        protected override void WndProc(ref Message m)
        {
            if(m.Msg==0x84){m.Result=new IntPtr(-1);return;}
            if(m.Msg==0x21){m.Result=new IntPtr(3);return;}
            base.WndProc(ref m);
        }
        public Edge(Rectangle bounds)
        {
            FormBorderStyle=FormBorderStyle.None;
            ShowInTaskbar=false;
            StartPosition=FormStartPosition.Manual;
            TopMost=true;
            Bounds=bounds;
            BackColor=Color.FromArgb(75,235,153);
            Opacity=0;
        }
    }
    private readonly List<Edge> edges=new();
    private double currentOpacity;
    private DateTime lastChange=DateTime.UtcNow;
    private bool visible;
    private double lastTarget=-1;
    public void Update(double? shortTerm,double target,double range,bool enabled,bool suppress)
    {
        double distance=shortTerm is double x && double.IsFinite(x)?Math.Abs(x-target):double.PositiveInfinity;
        // Identical on either side of the target. Fade out near the +/-2 LU boundary.
        double goal=enabled && !suppress && distance<range
            ? 0.58*Math.Pow(Math.Max(0,1-distance/range),0.75) : 0;
        DateTime now=DateTime.UtcNow;
        double elapsed=Math.Clamp((now-lastChange).TotalSeconds,0,1);
        lastChange=now;
        // No fade-in; only gently fade out when falling away from target.
        currentOpacity=goal>=currentOpacity?goal:Math.Max(goal,currentOpacity-elapsed/0.5*0.58);
        currentOpacity=Math.Round(currentOpacity*30)/30;
        if(currentOpacity<0.02){if(visible)Hide();lastTarget=0;return;}
        if(!visible)EnsureWindows();
        if(!visible)return;
        if(Math.Abs(lastTarget-currentOpacity)<0.015)return;
        lastTarget=currentOpacity;
        foreach(var edge in edges)edge.Opacity=currentOpacity;
    }
    private void EnsureWindows()
    {
        if(visible)return;
        if(edges.Count==0)
        {
            foreach(var screen in Screen.AllScreens)
            {
                var r=screen.Bounds;
                const int thickness=5;
                if(r.Width<=2*thickness || r.Height<=2*thickness)continue;
                edges.Add(new Edge(new Rectangle(r.Left,r.Top,r.Width,thickness)));
                edges.Add(new Edge(new Rectangle(r.Left,r.Bottom-thickness,r.Width,thickness)));
                edges.Add(new Edge(new Rectangle(r.Left,r.Top+thickness,thickness,r.Height-2*thickness)));
                edges.Add(new Edge(new Rectangle(r.Right-thickness,r.Top+thickness,thickness,r.Height-2*thickness)));
            }
        }
        foreach(var edge in edges)edge.Show();
        visible=true;
    }
    public void Hide()
    {
        foreach(var edge in edges)edge.Hide();
        visible=false;
    }
    public void DisplayChanged()
    {
        foreach(var edge in edges)edge.Dispose();
        edges.Clear();visible=false;lastTarget=-1;currentOpacity=0;
    }
    public void Dispose()=>DisplayChanged();
}
