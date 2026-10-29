using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace LufsTaskbarMeter;

// A per-pixel alpha bitmap is composited by Windows itself. Unlike Form.Region,
// the partially transparent edge pixels produce smoothly rounded corners.
internal static class LayeredFrame
{
    private const int ULW_ALPHA=2;
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X,Y; public NativePoint(int x,int y){X=x;Y=y;} }
    [StructLayout(LayoutKind.Sequential)] private struct NativeSize { public int CX,CY; public NativeSize(int w,int h){CX=w;CY=h;} }
    [StructLayout(LayoutKind.Sequential,Pack=1)] private struct Blend { public byte Op,Flags,Alpha,Format; }
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window,IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc,IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("user32.dll",SetLastError=true)] private static extern bool UpdateLayeredWindow(IntPtr hwnd,IntPtr dst,ref NativePoint dstAt,ref NativeSize size,IntPtr source,ref NativePoint srcAt,uint key,ref Blend blend,uint flags);

    internal static GraphicsPath RoundedPath(RectangleF r,float radius)
    {
        float d=Math.Min(radius*2,Math.Min(r.Width,r.Height));
        var p=new GraphicsPath();
        p.AddArc(r.Left,r.Top,d,d,180,90);
        p.AddArc(r.Right-d,r.Top,d,d,270,90);
        p.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);
        p.AddArc(r.Left,r.Bottom-d,d,d,90,90);
        p.CloseFigure();
        return p;
    }

    internal static void Present(Form window, Action<Graphics> draw)
    {
        if(!window.IsHandleCreated || window.Width<2 || window.Height<2)return;
        using var image=new Bitmap(window.Width,window.Height,PixelFormat.Format32bppPArgb);
        using(var graphics=Graphics.FromImage(image))
        {
            graphics.Clear(Color.Transparent);
            graphics.SmoothingMode=SmoothingMode.AntiAlias;
            graphics.PixelOffsetMode=PixelOffsetMode.HighQuality;
            graphics.CompositingMode=CompositingMode.SourceOver;
            draw(graphics);
        }
        IntPtr screen=IntPtr.Zero,memory=IntPtr.Zero,hbitmap=IntPtr.Zero,old=IntPtr.Zero;
        try
        {
            screen=GetDC(IntPtr.Zero);
            if(screen==IntPtr.Zero)throw new Win32Exception("Could not obtain screen DC");
            memory=CreateCompatibleDC(screen);
            if(memory==IntPtr.Zero)throw new Win32Exception("Could not create graphics DC");
            hbitmap=image.GetHbitmap(Color.FromArgb(0,0,0,0));
            old=SelectObject(memory,hbitmap);
            var dest=new NativePoint(window.Left,window.Top);
            var size=new NativeSize(window.Width,window.Height);
            var src=new NativePoint(0,0);
            var blend=new Blend{Op=0,Flags=0,Alpha=255,Format=1}; // AC_SRC_OVER, AC_SRC_ALPHA
            if(!UpdateLayeredWindow(window.Handle,screen,ref dest,ref size,memory,ref src,0,ref blend,ULW_ALPHA))
                throw new Win32Exception(Marshal.GetLastWin32Error(),"UpdateLayeredWindow failed");
        }
        finally
        {
            if(old!=IntPtr.Zero && memory!=IntPtr.Zero)SelectObject(memory,old);
            if(hbitmap!=IntPtr.Zero)DeleteObject(hbitmap);
            if(memory!=IntPtr.Zero)DeleteDC(memory);
            if(screen!=IntPtr.Zero)ReleaseDC(IntPtr.Zero,screen);
        }
    }
}
