using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace LufsTaskbarMeter;

// Owner-drawn row: avoids the child-Label clipping that hid the LUFS and TP
// values even when ReadDiagnostics() proved the meter had real audio samples.
internal sealed class ReadoutStrip : Panel
{
    internal readonly record struct Entry(string Text, Color Foreground);
    private readonly List<(Entry entry, Rectangle bounds)> placements = new();
    // Managed by MeterWindow. Never depend on WinForms Control.Font setter
    // equality/caching, which can retain a subsequently disposed Font.
    private Font displayFont=SystemFonts.DefaultFont;
    private bool layoutDirty=true;
    internal bool LastEntriesChanged {get;private set;}
    internal Font DisplayFont
    {
        get=>displayFont;
        set { if(!ReferenceEquals(displayFont,value)) { displayFont=value; layoutDirty=true; } }
    }
    private const int LeftInset=12;
    private int stereoReservedWidth;
    internal int StereoReservedWidth
    {
        get=>stereoReservedWidth;
        set { if(stereoReservedWidth!=value) { stereoReservedWidth=value;layoutDirty=true; } }
    }
    private const int FieldGap=5;
    private const int ExtraWidth=7; // safety for actual GDI glyph metrics / DPI
    private const TextFormatFlags Flags = TextFormatFlags.NoPadding |
        TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter |
        TextFormatFlags.NoPrefix | TextFormatFlags.Left;

    internal ReadoutStrip()
    {
        DoubleBuffered=true;
        ResizeRedraw=true;
    }

    /// <returns>Right edge of the final visible measurement.</returns>
    internal int SetEntries(IReadOnlyList<Entry> entries)
    {
        if(!layoutDirty && entries.Count==placements.Count)
        {
            bool identical=true;
            for(int i=0;i<entries.Count;i++)
            {
                if(entries[i]!=placements[i].entry) { identical=false; break; }
            }
            if(identical) {LastEntriesChanged=false;return LastRightEdge;}
        }
        LastEntriesChanged=true;
        layoutDirty=false;
        placements.Clear();
        int x=LeftInset+StereoReservedWidth;
        foreach(var entry in entries)
        {
            if(placements.Count>0)x+=FieldGap;
            // GDI measurement and GDI painting use EXACTLY the same font,
            // flags, and text. Never set narrow child Label bounds.
            int measured=TextRenderer.MeasureText(entry.Text, DisplayFont,
                new Size(32767,Math.Max(22,Height)),Flags).Width;
            int width=Math.Max(18,measured+ExtraWidth);
            placements.Add((entry,new Rectangle(x,0,width,Height)));
            x+=width;
        }
        Invalidate();
        return x;
    }
    // Paint the exact precomputed text positions into the alpha-composited
    // window. No Label controls or layout engines can clip/reorder values.
    internal void DrawAt(Graphics graphics,int top)
    {
        foreach(var (entry,bounds) in placements)
        {
            var rect=new Rectangle(bounds.X,bounds.Y+top,bounds.Width,bounds.Height);
            using var brush=new SolidBrush(entry.Foreground);
            using var fmt=new StringFormat(StringFormat.GenericTypographic)
            {
                Alignment=StringAlignment.Near,
                LineAlignment=StringAlignment.Center,
                FormatFlags=StringFormatFlags.NoWrap,
                Trimming=StringTrimming.None
            };
            graphics.DrawString(entry.Text,DisplayFont,brush,
                new RectangleF(rect.X,rect.Y,rect.Width,rect.Height),fmt);
        }
    }
    internal int LastRightEdge => placements.Count==0 ? LeftInset+StereoReservedWidth : placements[^1].bounds.Right;
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        foreach(var (entry,bounds) in placements)
            TextRenderer.DrawText(e.Graphics,entry.Text,DisplayFont,bounds,
                entry.Foreground,Flags);
    }
}
