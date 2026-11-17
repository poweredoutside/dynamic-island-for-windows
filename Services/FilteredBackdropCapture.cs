using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DynamicIslandWin.Services;

// The magnifier has its own window exclusion list. Unlike display affinity,
// it does not change what screenshots or screen sharing can see.
internal sealed class FilteredBackdropCapture : IDisposable
{
    private sealed record Request(Rectangle Bounds, IntPtr[] Excluded, TaskCompletionSource<GlassBackdropService.Frame?> Completion);
    private readonly BlockingCollection<Request> _requests = new();
    private readonly Thread _thread;
    private readonly ScalingCallback _callback;
    private byte[]? _pixels;
    private int _width, _height;
    private bool _capturing;
    private int _requestedWidth, _requestedHeight;
    private bool _disposed;

    internal FilteredBackdropCapture()
    {
        _callback = OnPixels;
        _thread = new Thread(Run) { IsBackground = true, Name = "Island glass backdrop" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    internal Task<GlassBackdropService.Frame?> CaptureAsync(Rectangle bounds, IntPtr[] excluded)
    {
        var completion = new TaskCompletionSource<GlassBackdropService.Frame?>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (_disposed || bounds.Width < 2 || bounds.Height < 2 || (long)bounds.Width * bounds.Height > 4_000_000)
            return Task.FromResult<GlassBackdropService.Frame?>(null);
        try { _requests.Add(new(bounds, excluded, completion)); }
        catch (InvalidOperationException) { completion.TrySetResult(null); }
        return completion.Task;
    }

    private void Run()
    {
        IntPtr host = IntPtr.Zero, magnifier = IntPtr.Zero;
        bool initialized = false;
        try
        {
            if (!(initialized = MagInitialize())) throw new Win32Exception(Marshal.GetLastWin32Error());
            // A hidden host is sufficient: no screen overlay, focus changes or
            // visibility/display-affinity toggles are needed to get the pixels.
            host = CreateWindowEx(0x00080080, "Static", "Island backdrop capture", 0,
                0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            if (host == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            foreach (var request in _requests.GetConsumingEnumerable())
            {
                try
                {
                    _pixels = null;
                    // Never pass an off-desktop source to the native API.
                    // Pixels outside the desktop cannot be displayed anyway.
                    var bounds = Rectangle.Intersect(request.Bounds, System.Windows.Forms.SystemInformation.VirtualScreen);
                    if (bounds.Width < 2 || bounds.Height < 2) { request.Completion.TrySetResult(null); continue; }
                    // Recreate at the new size. Resizing a magnifier while its
                    // previous source has another aspect ratio can corrupt
                    // buffers inside Magnification.dll on current drivers.
                    if (magnifier == IntPtr.Zero || _requestedWidth != bounds.Width || _requestedHeight != bounds.Height)
                    {
                        if (magnifier != IntPtr.Zero) DestroyWindow(magnifier);
                        magnifier = CreateWindowEx(0, "Magnifier", "", 0x50000000,
                            0, 0, bounds.Width, bounds.Height, host, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
                        if (magnifier == IntPtr.Zero || !MagSetImageScalingCallback(magnifier, _callback))
                            throw new Win32Exception(Marshal.GetLastWin32Error());
                    }
                    _requestedWidth = bounds.Width; _requestedHeight = bounds.Height;
                    var watch = Stopwatch.StartNew();
                    if (!MagSetWindowFilterList(magnifier, 0, request.Excluded.Length, request.Excluded))
                        throw new Win32Exception(Marshal.GetLastWin32Error());
                    _capturing = true;
                    bool captured;
                    try { captured = MagSetWindowSource(magnifier, new NativeRect(bounds)); }
                    finally { _capturing = false; }
                    if (!captured || _pixels == null)
                        throw new Win32Exception(Marshal.GetLastWin32Error(), "Filtered backdrop is unavailable");
                    byte[] pixels = _pixels;
                    if (bounds != request.Bounds)
                    {
                        pixels = new byte[request.Bounds.Width * request.Bounds.Height * 4];
                        for (int row = 0; row < _height; row++)
                            Buffer.BlockCopy(_pixels, row * _width * 4, pixels,
                                ((row + bounds.Y - request.Bounds.Y) * request.Bounds.Width + bounds.X - request.Bounds.X) * 4, _width * 4);
                    }
                    var image = BitmapSource.Create(request.Bounds.Width, request.Bounds.Height, 96, 96, PixelFormats.Bgr32, null, pixels, request.Bounds.Width * 4);
                    image.Freeze();
                    request.Completion.TrySetResult(new(image, watch.Elapsed.TotalMilliseconds));
                }
                catch (Exception ex) { request.Completion.TrySetException(ex); }
                finally { _pixels = null; }
            }
        }
        catch (Exception ex)
        {
            // Keep accepting requests so every pending caller completes, even
            // when this graphics driver cannot provide filtered capture.
            foreach (var request in _requests.GetConsumingEnumerable()) request.Completion.TrySetException(ex);
        }
        finally
        {
            if (magnifier != IntPtr.Zero) DestroyWindow(magnifier);
            if (host != IntPtr.Zero) DestroyWindow(host);
            if (initialized) MagUninitialize();
            GC.KeepAlive(_callback);
        }
    }

    private bool OnPixels(IntPtr window, IntPtr source, ImageHeader header, IntPtr destination,
        ImageHeader destinationHeader, NativeRect unclipped, NativeRect clipped, IntPtr dirty)
    {
        // Never let an exception cross the unmanaged callback boundary.
        try
        {
            // Resizing can invoke a callback for the old source. Only accept
            // the synchronous callback for this request, with a full buffer.
            if (!_capturing || source == IntPtr.Zero || header.Width != _requestedWidth || header.Height != _requestedHeight
                || header.Stride != header.Width * 4 || header.Size.ToUInt64() < (ulong)header.Stride * header.Height) return true;
            _width = (int)header.Width; _height = (int)header.Height;
            var pixels = new byte[_width * _height * 4];
            for (int row = 0; row < _height; row++)
                Marshal.Copy(source + checked(row * (int)header.Stride), pixels, row * _width * 4, _width * 4);
            _pixels = pixels;
        }
        catch { _pixels = null; }
        return true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _requests.CompleteAdding();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left, Top, Right, Bottom;
        public NativeRect(Rectangle r) { Left = r.Left; Top = r.Top; Right = r.Right; Bottom = r.Bottom; }
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct ImageHeader
    {
        public uint Width, Height;
        public Guid Format;
        public uint Stride, Offset;
        public UIntPtr Size;
    }
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private delegate bool ScalingCallback(IntPtr window, IntPtr source, ImageHeader header, IntPtr destination,
        ImageHeader destinationHeader, NativeRect unclipped, NativeRect clipped, IntPtr dirty);
    [DllImport("Magnification.dll", SetLastError = true)] private static extern bool MagInitialize();
    [DllImport("Magnification.dll")] private static extern bool MagUninitialize();
    [DllImport("Magnification.dll", SetLastError = true)] private static extern bool MagSetImageScalingCallback(IntPtr window, ScalingCallback callback);
    [DllImport("Magnification.dll", SetLastError = true)] private static extern bool MagSetWindowFilterList(IntPtr window, uint mode, int count, IntPtr[] windows);
    [DllImport("Magnification.dll", SetLastError = true)] private static extern bool MagSetWindowSource(IntPtr window, NativeRect bounds);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr CreateWindowEx(uint extendedStyle,
        string className, string title, uint style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr window);
}
