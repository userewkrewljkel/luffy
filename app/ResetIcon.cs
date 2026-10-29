using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace LufsTaskbarMeter;

// Fixed 22-pixel clickable reset icon. Drawing it as a vector instead of a
// Button caption prevents the glyph baseline from being clipped at the bottom.
internal sealed class ResetIcon : Control
{
    private bool hovered;

    public ResetIcon()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
        BackColor=Color.Transparent;
        ForeColor=Color.White;
        Cursor=Cursors.Hand;
        TabStop=false;
        Size=new Size(22,22);
        AccessibleRole=AccessibleRole.PushButton;
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e); hovered=true; Invalidate();
    }
    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e); hovered=false; Invalidate();
    }
    internal void DrawAt(Graphics g,Rectangle rect,bool isHovered)
    {
        if(isHovered)
        {
            using var background=new SolidBrush(Color.FromArgb(72,74,81));
            g.FillEllipse(background,rect.X+1.5f,rect.Y+1.5f,19,19);
        }
        using var pen=new Pen(ForeColor,1.65f) {StartCap=LineCap.Round,EndCap=LineCap.Round};
        float x=rect.X,y=rect.Y;
        g.DrawArc(pen,x+4.8f,y+4.8f,12.4f,12.4f,210f,285f);
        g.DrawLine(pen,x+6.2f,y+11.7f,x+6.6f,y+15.4f);
        g.DrawLine(pen,x+6.6f,y+15.4f,x+10.2f,y+14.5f);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g=e.Graphics;
        g.SmoothingMode=SmoothingMode.AntiAlias;
        if(hovered)
        {
            using var brush=new SolidBrush(Color.FromArgb(72,74,81));
            g.FillEllipse(brush,1.5f,1.5f,19,19);
        }
        using var pen=new Pen(ForeColor,1.65f)
        {
            StartCap=LineCap.Round, EndCap=LineCap.Round
        };
        // One clean curved-arrow symbol, centered vertically on the digits.
        g.DrawArc(pen,4.8f,4.8f,12.4f,12.4f,210f,285f);
        g.DrawLine(pen,6.2f,11.7f,6.6f,15.4f);
        g.DrawLine(pen,6.6f,15.4f,10.2f,14.5f);
    }
}
