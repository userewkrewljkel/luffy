using System;
using System.Drawing;
using System.Windows.Forms;

namespace LufsTaskbarMeter;

// Two 3 px, 16 px high VU-like peak indicators: front L/R input sample peaks.
// A shared loudness-target color is supplied by MeterWindow; the actual
// bar heights represent *channel peaks*, never per-channel LUFS.
internal sealed class StereoLevel : Control
{
    private double leftDb=-60, rightDb=-60;
    private Color levelColor=Color.White;
    private static readonly Color Track=Color.FromArgb(69,70,75);
    private const double Floor=-60;
    private const double ReleaseDbPerSecond=20;
    public StereoLevel()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
        BackColor=Color.Transparent;
        Size=new Size(8,18);
        TabStop=false;
    }
    public void ResetLevels()
    {
        leftDb=rightDb=Floor;
        Invalidate();
    }
    public bool SetLevelColor(Color color)
    {
        if(levelColor==color)return false;
        levelColor=color;
        Invalidate();
        return true;
    }
    public bool Advance(double left,double right,double seconds)
    {
        int oldL=Pixels(leftDb),oldR=Pixels(rightDb);
        double delta=ReleaseDbPerSecond*Math.Clamp(seconds,0,0.2);
        leftDb=Math.Max(AmplitudeDb(left),Math.Max(Floor,leftDb-delta));
        rightDb=Math.Max(AmplitudeDb(right),Math.Max(Floor,rightDb-delta));
        bool changed=Pixels(leftDb)!=oldL || Pixels(rightDb)!=oldR;
        if(changed)Invalidate();
        return changed;
    }
    private static int Pixels(double db) => (int)Math.Round((db-Floor)/(-Floor)*16);
    private static double AmplitudeDb(double value)
        => value>0 && double.IsFinite(value)?Math.Clamp(20*Math.Log10(value),Floor,0):Floor;
    internal void DrawAt(Graphics g,int x,int y)
    {
        using var background=new SolidBrush(Track);
        using var fill=new SolidBrush(levelColor);
        g.FillRectangle(background,x,y,3,16);
        g.FillRectangle(background,x+5,y,3,16);
        int l=Pixels(leftDb);
        int r=Pixels(rightDb);
        if(l>0)g.FillRectangle(fill,x,y+16-l,3,l);
        if(r>0)g.FillRectangle(fill,x+5,y+16-r,3,r);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g=e.Graphics;
        using var background=new SolidBrush(Track);
        using var fill=new SolidBrush(levelColor);
        int y=Math.Max(0,(Height-16)/2);
        g.FillRectangle(background,0,y,3,16);
        g.FillRectangle(background,5,y,3,16);
        int l=Pixels(leftDb);
        int r=Pixels(rightDb);
        if(l>0)g.FillRectangle(fill,0,y+16-l,3,l);
        if(r>0)g.FillRectangle(fill,5,y+16-r,3,r);
    }
}
