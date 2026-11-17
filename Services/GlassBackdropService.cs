using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DynamicIslandWin.Services;

// Unfiltered screenshots for integration diagnostics. The product lens uses
// FilteredBackdropCapture so it never captures its own windows.
internal static class GlassBackdropService
{
    internal sealed record Frame(BitmapSource Image, double Milliseconds);
    internal static Frame? Capture(Rectangle bounds, bool includeLayered = true, long maxPixels = 4_000_000)
    {
        if (bounds.Width < 2 || bounds.Height < 2 || (long)bounds.Width * bounds.Height > maxPixels
            || !System.Windows.Forms.SystemInformation.VirtualScreen.Contains(bounds)) return null;
        var clock = Stopwatch.StartNew();
        using var bitmap = new Bitmap(bounds.Width, bounds.Height, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            IntPtr source = GetDC(IntPtr.Zero);
            if (source == IntPtr.Zero) throw new System.ComponentModel.Win32Exception();
            IntPtr target = IntPtr.Zero;
            try
            {
                target = graphics.GetHdc();
                if (!BitBlt(target, 0, 0, bounds.Width, bounds.Height, source, bounds.X, bounds.Y, includeLayered ? 0x40CC0020u : 0x00CC0020u))
                    throw new System.ComponentModel.Win32Exception();
            }
            finally { if (target != IntPtr.Zero) graphics.ReleaseHdc(target); ReleaseDC(IntPtr.Zero, source); }
        }
        var data = bitmap.LockBits(new Rectangle(Point.Empty, bounds.Size), ImageLockMode.ReadOnly,
            System.Drawing.Imaging.PixelFormat.Format32bppRgb);
        try
        {
            int stride = bounds.Width * 4;
            var pixels = new byte[stride * bounds.Height];
            for (int row = 0; row < bounds.Height; row++)
                Marshal.Copy(data.Scan0 + row * data.Stride, pixels, row * stride, stride);
            var image = BitmapSource.Create(bounds.Width, bounds.Height, 96, 96, PixelFormats.Bgr32, null, pixels, stride);
            image.Freeze();
            return new(image, clock.Elapsed.TotalMilliseconds);
        }
        finally { bitmap.UnlockBits(data); }
    }
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window, IntPtr dc);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern bool BitBlt(IntPtr target, int x, int y, int width, int height,
        IntPtr source, int sourceX, int sourceY, uint operation);
}
