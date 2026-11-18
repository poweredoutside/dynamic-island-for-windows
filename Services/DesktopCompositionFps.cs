using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DynamicIslandWin.Services;

internal sealed class DesktopCompositionFps
{
    private ulong? _previousFrame;
    private long? _sampleTime;
    internal double? Rate { get; private set; }

    internal void Poll()
    {
        long now = Stopwatch.GetTimestamp();
        if (_sampleTime is { } previous && Stopwatch.GetElapsedTime(previous, now) < TimeSpan.FromSeconds(1)) return;
        var timing = new CompositionTiming { Size = (uint)Marshal.SizeOf<CompositionTiming>() };
        Sample(DwmGetCompositionTimingInfo(IntPtr.Zero, ref timing) == 0 ? timing.ComposedFrame : null, now);
    }

    internal void Sample(ulong? frame, long timestamp)
    {
        Rate = null;
        if (frame is { } composed && _previousFrame is { } previous && composed >= previous && _sampleTime is { } start && timestamp > start)
        {
            double fps = (composed - previous) / Stopwatch.GetElapsedTime(start, timestamp).TotalSeconds;
            if (fps < 10000) Rate = fps;
        }
        _previousFrame = frame;
        _sampleTime = timestamp;
    }

    internal void Reset()
    {
        _previousFrame = null;
        _sampleTime = null;
        Rate = null;
    }

    [StructLayout(LayoutKind.Explicit, Size = 292)]
    private struct CompositionTiming
    {
        [FieldOffset(0)] internal uint Size;
        [FieldOffset(56)] internal ulong ComposedFrame;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetCompositionTimingInfo(IntPtr hwnd, ref CompositionTiming timing);
}
