using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace LufsTaskbarMeter;

// One alpha-composited, non-activating, mouse-transparent rim per monitor.
// The middle is REMOVED from the window region; no opaque full-screen form
// covers the user's applications. The outer rim is per-pixel ARGB, not
// TransparencyKey or WinForms Opacity (which caused black outlines).
internal sealed class PeakWarningOverlay : Form
{
    private const int WS_EX_LAYERED = 0x00080000;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WM_NCHITTEST = 0x0084;
    private const int WM_MOUSEACTIVATE = 0x0021;
    private const int HTTRANSPARENT = -1;
    private const int MA_NOACTIVATE = 3;
    private const uint ULW_ALPHA = 0x00000002;
    private const byte AC_SRC_OVER = 0;
    private const byte AC_SRC_ALPHA = 1;

    private readonly System.Windows.Forms.Timer tick = new() { Interval = 33 };
    private DateTime until, fadeEnd;
    private bool fading, fadeOut;
    private string style = "Studio";
    private int thickness = 5;
    private IntPtr screenDc, memoryDc, dib, oldBitmap;
    private Region? hollowRegion;

    protected override CreateParams CreateParams
    {
        get
        {
            var p = base.CreateParams;
            p.ExStyle |= WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
            return p;
        }
    }
    protected override bool ShowWithoutActivation => true;
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_NCHITTEST) { m.Result = new IntPtr(HTTRANSPARENT); return; }
        if (m.Msg == WM_MOUSEACTIVATE) { m.Result = new IntPtr(MA_NOACTIVATE); return; }
        base.WndProc(ref m);
    }

    public PeakWarningOverlay()
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        // No TransparencyKey: anti-aliased pixels are composited using
        // per-pixel alpha via UpdateLayeredWindow instead of a color key.
        tick.Tick += (_, _) => Advance();
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
    }

    protected override void OnPaintBackground(PaintEventArgs e) { }
    protected override void OnPaint(PaintEventArgs e) { }

    public void ShowWarning(Screen screen, string glowStyle, double duration, bool fade, int borderPx)
    {
        style = glowStyle is "Soft" or "Studio" or "Alert" ? glowStyle : "Studio";
        fadeOut = fade;
        thickness = Math.Clamp(borderPx, 2, 10);
        fading = false;
        tick.Stop();
        Bounds = screen.Bounds;
        // Even if layered alpha or window hit testing misbehaves, the central
        // rectangle is not part of this window and cannot intercept clicks.
        int spread = style == "Soft" ? 7 : style == "Alert" ? 18 : 12;
        int rim = Math.Clamp(thickness + spread + 20, 32, 60);
        hollowRegion?.Dispose();
        hollowRegion = new Region(new Rectangle(0, 0, Width, Height));
        if (Width > 2 * rim && Height > 2 * rim)
            hollowRegion.Exclude(new Rectangle(rim, rim, Width - 2 * rim, Height - 2 * rim));
        Region = hollowRegion;
        PrepareAlphaSurface();
        until = DateTime.UtcNow.AddSeconds(Math.Clamp(duration, 0.2, 10));
        if (!Visible) Show();
        DrawAtOpacity(255);  // Immediate full brightness; NO fade-in.
        tick.Start();
    }

    public void HideWarning()
    {
        tick.Stop();
        Hide();
        FreeAlphaSurface();
    }

    private void Advance()
    {
        DateTime now = DateTime.UtcNow;
        if (!fading && now >= until)
        {
            if (!fadeOut) { HideWarning(); return; }
            fading = true;
            fadeEnd = now.AddMilliseconds(280);
        }
        if (fading)
        {
            double fraction = (fadeEnd - now).TotalMilliseconds / 280.0;
            if (fraction <= 0) { HideWarning(); return; }
            DrawAtOpacity((byte)Math.Clamp((int)Math.Round(255 * fraction), 0, 255));
        }
    }

    // Render one border and a very soft gradient on a fully transparent
    // premultiplied-alpha surface. There is no second dark or red outline.
    private Bitmap BuildImage()
    {
        var bmp = new Bitmap(Math.Max(1, Width), Math.Max(1, Height), PixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.Transparent);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.CompositingMode = CompositingMode.SourceOver;
        Color coral = ColorTranslator.FromHtml("#F16D64");
        int spread = style == "Soft" ? 7 : style == "Alert" ? 18 : 12;
        float strength = style == "Soft" ? 0.40f : style == "Alert" ? 1.0f : 0.65f;
        float center = thickness / 2f + 3f;
        // Translucent strokes at closely spaced insets blend into a diffuse
        // inner halo, not another distinct outline.
        for (int i = spread; i >= 1; --i)
        {
            float proximity = 1f - i / (float)(spread + 1);
            int alpha = Math.Clamp((int)Math.Round(strength * (2f + 8f * proximity * proximity)), 1, 15);
            using var haloPen = new Pen(Color.FromArgb(alpha, coral), 3.6f);
            float inset = center + i;
            using var path = RoundedRect(new RectangleF(inset, inset,
                Width - 2 * inset, Height - 2 * inset), 12 + i);
            g.DrawPath(haloPen, path);
        }
        using var pen = new Pen(coral, thickness);
        using var border = RoundedRect(new RectangleF(center, center,
            Width - 2 * center, Height - 2 * center), 12f);
        g.DrawPath(pen, border);
        return bmp;
    }

    private static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        var p = new GraphicsPath();
        float d = Math.Max(1f, Math.Min(radius * 2, Math.Min(r.Width, r.Height)));
        p.AddArc(r.Left, r.Top, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    private void PrepareAlphaSurface()
    {
        FreeAlphaSurface();
        using var image = BuildImage();
        screenDc = GetDC(IntPtr.Zero);
        if (screenDc == IntPtr.Zero) throw new InvalidOperationException("Cannot obtain desktop drawing context.");
        memoryDc = CreateCompatibleDC(screenDc);
        if (memoryDc == IntPtr.Zero) throw new InvalidOperationException("Cannot create overlay drawing context.");
        BITMAPINFO info = new()
        {
            Header = new BITMAPINFOHEADER
            {
                Size = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                Width = image.Width,
                Height = -image.Height, // top-down DIB, BGRA byte order
                Planes = 1,
                BitCount = 32,
                Compression = 0
            }
        };
        dib = CreateDIBSection(screenDc, ref info, 0, out IntPtr pixels, IntPtr.Zero, 0);
        if (dib == IntPtr.Zero || pixels == IntPtr.Zero)
            throw new InvalidOperationException("Cannot create 32-bit overlay bitmap.");
        oldBitmap = SelectObject(memoryDc, dib);
        var area = new Rectangle(0, 0, image.Width, image.Height);
        var data = image.LockBits(area, ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
        try
        {
            int rowBytes = image.Width * 4;
            byte[] row = new byte[rowBytes];
            for (int y = 0; y < image.Height; y++)
            {
                Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), row, 0, rowBytes);
                Marshal.Copy(row, 0, IntPtr.Add(pixels, y * rowBytes), rowBytes);
            }
        }
        finally { image.UnlockBits(data); }
    }

    private void DrawAtOpacity(byte alpha)
    {
        if (memoryDc == IntPtr.Zero) return;
        POINT at = new() { X = Left, Y = Top };
        POINT source = new() { X = 0, Y = 0 };
        SIZE size = new() { Width = Width, Height = Height };
        BLENDFUNCTION blend = new()
        {
            BlendOp = AC_SRC_OVER,
            SourceConstantAlpha = alpha,
            AlphaFormat = AC_SRC_ALPHA
        };
        if (!UpdateLayeredWindow(Handle, screenDc, ref at, ref size,
            memoryDc, ref source, 0, ref blend, ULW_ALPHA))
            Debug.WriteLine($"LUFFY overlay: UpdateLayeredWindow failed ({Marshal.GetLastWin32Error()}).");
    }

    private void FreeAlphaSurface()
    {
        if (memoryDc != IntPtr.Zero)
        {
            if (oldBitmap != IntPtr.Zero) { SelectObject(memoryDc, oldBitmap); oldBitmap = IntPtr.Zero; }
            DeleteDC(memoryDc); memoryDc = IntPtr.Zero;
        }
        if (dib != IntPtr.Zero) { DeleteObject(dib); dib = IntPtr.Zero; }
        if (screenDc != IntPtr.Zero) { ReleaseDC(IntPtr.Zero, screenDc); screenDc = IntPtr.Zero; }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            tick.Dispose();
            FreeAlphaSurface();
            Region = null;
            hollowRegion?.Dispose();
        }
        base.Dispose(disposing);
    }

    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct SIZE { public int Width, Height; }
    [StructLayout(LayoutKind.Sequential, Pack = 1)] private struct BLENDFUNCTION
    {
        public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat;
    }
    [StructLayout(LayoutKind.Sequential)] private struct BITMAPINFOHEADER
    {
        public uint Size;
        public int Width, Height;
        public ushort Planes, BitCount;
        public uint Compression, SizeImage;
        public int XPelsPerMeter, YPelsPerMeter;
        public uint ClrUsed, ClrImportant;
    }
    [StructLayout(LayoutKind.Sequential)] private struct BITMAPINFO
    {
        public BITMAPINFOHEADER Header;
        public uint Colors;
    }
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern IntPtr CreateDIBSection(
        IntPtr dc, ref BITMAPINFO info, uint usage, out IntPtr pixels, IntPtr section, uint offset);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool UpdateLayeredWindow(
        IntPtr hwnd, IntPtr screenDc, ref POINT at, ref SIZE size, IntPtr sourceDc,
        ref POINT source, uint colorKey, ref BLENDFUNCTION blend, uint flags);
}
