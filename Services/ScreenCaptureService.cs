using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;

namespace DynamicIslandWin.Services;

internal static class ScreenCaptureService
{
    internal static Rectangle DesktopBounds => new(GetSystemMetrics(76), GetSystemMetrics(77), GetSystemMetrics(78), GetSystemMetrics(79));

    internal static Task<BitmapSource> CaptureMonitorAsync(IntPtr owner) => Task.Run(() =>
    {
        var previous = SetThreadDpiAwarenessContext(new IntPtr(-4));
        try
        {
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (!GetMonitorInfo(MonitorFromWindow(owner, 2), ref info)) throw new Win32Exception(Marshal.GetLastWin32Error());
            return Capture(info.Monitor.Bounds);
        }
        finally { if (previous != IntPtr.Zero) SetThreadDpiAwarenessContext(previous); }
    });

    internal static Task<(BitmapSource Image, Rectangle Bounds)> CaptureDesktopAsync() => Task.Run(() =>
    {
        var previous = SetThreadDpiAwarenessContext(new IntPtr(-4));
        try
        {
            var bounds = DesktopBounds;
            return (Capture(bounds), bounds);
        }
        finally { if (previous != IntPtr.Zero) SetThreadDpiAwarenessContext(previous); }
    });

    private static BitmapSource Capture(Rectangle bounds) =>
        GlassBackdropService.Capture(bounds, maxPixels: 100_000_000)?.Image
        ?? throw new InvalidOperationException("Screen bounds are unavailable or exceed the screenshot limit.");

    internal static Rectangle SelectionBounds(Point start, Point end, Rectangle desktop)
    {
        int left = Math.Clamp(Math.Min(start.X, end.X), desktop.Left, desktop.Right);
        int top = Math.Clamp(Math.Min(start.Y, end.Y), desktop.Top, desktop.Bottom);
        int right = Math.Clamp(Math.Max(start.X, end.X), desktop.Left, desktop.Right);
        int bottom = Math.Clamp(Math.Max(start.Y, end.Y), desktop.Top, desktop.Bottom);
        return Rectangle.FromLTRB(left, top, right, bottom);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left, Top, Right, Bottom;
        public readonly Rectangle Bounds => Rectangle.FromLTRB(Left, Top, Right, Bottom);
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor, Work;
        public uint Flags;
    }
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
}
