using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
namespace LufsTaskbarMeter;

// Fixed-width owner-drawn pause / play icon, independent of symbol fonts.
internal sealed class PauseIcon : Control
{
    private bool hovered;
    private bool paused;
    public bool IsPaused
    {
        get=>paused;
        set { if(paused!=value){paused=value;Invalidate();} }
    }
    public PauseIcon()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor,true);
        BackColor=Color.Transparent;
        ForeColor=Color.White;
        Cursor=Cursors.Hand;
        TabStop=false;
        Size=new Size(22,22);
        AccessibleRole=AccessibleRole.PushButton;
        AccessibleName="Pause or resume measurement";
    }
    protected override void OnMouseEnter(EventArgs e){base.OnMouseEnter(e);hovered=true;Invalidate();}
    protected override void OnMouseLeave(EventArgs e){base.OnMouseLeave(e);hovered=false;Invalidate();}
    // Option 2: an unmistakable, solid light button that cannot be confused
    // with the two bare vertical stereo level bars.
    internal void DrawAt(Graphics g,Rectangle rect,bool isHovered)
    {
        using var shape=LayeredFrame.RoundedPath(new RectangleF(rect.Left,rect.Top,rect.Width,rect.Height),5f);
        using var background=new SolidBrush(paused?Color.FromArgb(110,221,155):
            isHovered?Color.FromArgb(235,237,239):Color.FromArgb(200,203,209));
        g.FillPath(background,shape);
        using var brush=new SolidBrush(Color.FromArgb(31,33,38));
        float x=rect.Left,y=rect.Top;
        if(paused)
        {
            PointF[] triangle={new(x+7.5f,y+5f),new(x+7.5f,y+17f),new(x+16.5f,y+11f)};
            g.FillPolygon(brush,triangle);
        }
        else
        {
            g.FillRectangle(brush,x+6.5f,y+5.5f,3.5f,11f);
            g.FillRectangle(brush,x+12.5f,y+5.5f,3.5f,11f);
        }
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g=e.Graphics;
        g.SmoothingMode=SmoothingMode.AntiAlias;
        if(hovered)
        {
            using var hoverBrush=new SolidBrush(Color.FromArgb(72,74,81));
            g.FillEllipse(hoverBrush,1.5f,1.5f,19,19);
        }
        using var brush=new SolidBrush(paused?Color.FromArgb(110,221,155):ForeColor);
        if(paused)
        {
            PointF[] triangle={new(8,5.5f),new(8,16.5f),new(16.5f,11)};
            g.FillPolygon(brush,triangle);
        }
        else
        {
            g.FillRectangle(brush,7,6,3,10);
            g.FillRectangle(brush,13,6,3,10);
        }
    }
}
