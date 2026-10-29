using System;
using System.Drawing;
using System.Drawing.Drawing2D;
namespace LufsTaskbarMeter;
internal static class GearIcon
{
    internal static void DrawAt(Graphics g,Rectangle bounds,bool hover)
    {
        g.SmoothingMode=SmoothingMode.AntiAlias;
        float cx=bounds.X+bounds.Width/2f,cy=bounds.Y+bounds.Height/2f;
        using var pen=new Pen(hover?Color.White:Color.FromArgb(181,186,194),1.35f)
        {StartCap=LineCap.Round,EndCap=LineCap.Round,LineJoin=LineJoin.Round};
        // Eight short spokes + two concentric circles: readable at ~13 pixels.
        g.DrawEllipse(pen,cx-5,cy-5,10,10);
        g.DrawEllipse(pen,cx-1.7f,cy-1.7f,3.4f,3.4f);
        for(int i=0;i<8;i++)
        {
            double a=i*Math.PI/4;
            float dx=(float)Math.Cos(a),dy=(float)Math.Sin(a);
            g.DrawLine(pen,cx+dx*5,cy+dy*5,cx+dx*7,cy+dy*7);
        }
    }
}
