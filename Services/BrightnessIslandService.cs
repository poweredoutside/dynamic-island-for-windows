using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Threading;

namespace DynamicIslandWin.Services;

internal sealed class BrightnessIslandService(Dispatcher dispatcher, Func<int, bool> showBrightness) : IDisposable
{
    private const uint ObjectShow = 0x8002, ObjectLocationChange = 0x800B;
    private const int MatchWindowMs = 600, KeyWindowMs = 350;
    private readonly StringBuilder _className = new(64);
    private WinEventProc? _callback;
    private IntPtr _showHook, _locationHook, _candidate;
    private uint _explorerPid;
    private long _nextHookCheck, _candidateAt, _brightnessAt, _volumeAt, _lastSampleAt;
    private int? _brightness;
    private int _learnedKey;
    private long _lastUnmappedKey, _lastVolumeKey;
    private bool _disposed, _volumeEnabled;
    private CancellationTokenSource? _subscription;

    internal void SetEnabled(bool enabled)
    {
        if (_disposed || enabled == (_subscription != null)) return;
        if (!enabled)
        {
            _subscription!.Cancel();
            _subscription.Dispose();
            _subscription = null;
            _candidate = IntPtr.Zero;
            _brightness = null;
            _brightnessAt = 0;
            if (!_volumeEnabled) Unhook();
            return;
        }

        _subscription = new CancellationTokenSource();
        var token = _subscription.Token;
        _lastSampleAt = Environment.TickCount64;
        _nextHookCheck = 0;
        RefreshShellHook();
        _ = Task.Run(() => WatchBrightnessAsync(token));
    }

    internal void SetVolumeEnabled(bool enabled)
    {
        if (_disposed || _volumeEnabled == enabled) return;
        _volumeEnabled = enabled;
        _volumeAt = 0;
        if (!enabled && _subscription == null) Unhook();
        if (!enabled) return;
        _nextHookCheck = 0;
        RefreshShellHook();
    }

    internal void VolumeHudShown()
    {
        if (!_volumeEnabled) return;
        long now = Environment.TickCount64;
        long keyAt = Interlocked.Read(ref _lastVolumeKey);
        if (!IsRecent(now, keyAt, KeyWindowMs)) return;
        _volumeAt = keyAt;
        if (_candidate != IntPtr.Zero && IsRecent(now, _candidateAt, MatchWindowMs))
            TryHideFlyout(_candidate, now);
    }

    private async Task WatchBrightnessAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                using var watcher = new ManagementEventWatcher(@"root\WMI",
                    "SELECT * FROM WmiMonitorBrightnessEvent WHERE Active=True");
                var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                watcher.Stopped += (_, _) => stopped.TrySetResult();
                watcher.EventArrived += (_, args) =>
                {
                    using var change = args.NewEvent;
                    if (change["Brightness"] is not byte value || token.IsCancellationRequested) return;
                    long now = Environment.TickCount64;
                    dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
                    {
                        if (!token.IsCancellationRequested) UpdateBrightness(value, now, fromEvent: true);
                    }));
                };
                watcher.Start();
                try { await stopped.Task.WaitAsync(token).ConfigureAwait(false); }
                finally { watcher.Stop(); }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
            catch (Exception ex)
            {
                if (token.IsCancellationRequested) return;
                AppLog.Error("Watch display brightness", ex);
            }
            // The WMI provider can disappear across sleep and display changes.
            try { await Task.Delay(TimeSpan.FromSeconds(15), token).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
        }
    }

    internal void UpdateBrightness(int value, long at, bool fromEvent = false)
    {
        if (_subscription == null || value is < 0 or > 100 || at < _lastSampleAt) return;
        _lastSampleAt = at;
        int? previous = _brightness;
        _brightness = value;
        if (previous == value || (!fromEvent && previous == null)) return;
        long now = Environment.TickCount64;
        if (fromEvent && IsRecent(now, at, MatchWindowMs))
        {
            long key = Interlocked.Read(ref _lastUnmappedKey);
            if (IsRecent(at, key >> 16, KeyWindowMs)) _learnedKey = (int)(key & 0xffff);
        }
        _brightnessAt = showBrightness(value) ? at : 0;
        if (_candidate != IntPtr.Zero && IsRecent(now, _candidateAt, MatchWindowMs))
            TryHideFlyout(_candidate, now);
    }

    // Called by the existing low-level hook. Never intercept brightness keys.
    internal void ObserveKey(int virtualKey, int scanCode, int flags)
    {
        if ((flags & 0x10) != 0) return; // Injected input
        long now = Environment.TickCount64;
        if (virtualKey is 0xAD or 0xAE or 0xAF) Interlocked.Exchange(ref _lastVolumeKey, now);
        else if (virtualKey == 0xFF && scanCode is > 0 and <= 0xff)
            Interlocked.Exchange(ref _lastUnmappedKey, (now << 16) | (uint)(scanCode | ((flags & 1) << 8)));
    }

    internal void RefreshShellHook()
    {
        long now = Environment.TickCount64;
        if ((_subscription == null && !_volumeEnabled) || now < _nextHookCheck) return;
        _nextHookCheck = now + 5000;
        GetWindowThreadProcessId(FindWindow("Shell_TrayWnd", null), out uint pid);
        if (_explorerPid == pid && _showHook != IntPtr.Zero && _locationHook != IntPtr.Zero) return;
        Unhook();
        if (pid == 0) return;
        _explorerPid = pid;
        _callback ??= OnWindowEvent;
        // Newer Explorer versions keep the flyout host visible at zero size between uses.
        _showHook = SetWinEventHook(ObjectShow, ObjectShow, IntPtr.Zero, _callback, pid, 0, 0);
        _locationHook = SetWinEventHook(ObjectLocationChange, ObjectLocationChange, IntPtr.Zero, _callback, pid, 0, 0);
    }

    private void OnWindowEvent(IntPtr hook, uint eventType, IntPtr hwnd, int objectId, int childId, uint thread, uint eventTime)
    {
        long now = Environment.TickCount64;
        if ((_subscription == null && !_volumeEnabled) || (hook != _showHook && hook != _locationHook) || objectId != 0 || childId != 0 ||
            unchecked((uint)now - eventTime) > MatchWindowMs || !IsFlyoutHost(hwnd)) return;
        _candidate = hwnd;
        _candidateAt = now;
        if (_subscription != null && !IsRecent(now, _brightnessAt, MatchWindowMs) && _brightness is 0 or 100 && _learnedKey != 0)
        {
            long key = Interlocked.Read(ref _lastUnmappedKey);
            if ((key & 0xffff) == _learnedKey && IsRecent(now, key >> 16, KeyWindowMs) &&
                !IsRecent(now, Interlocked.Read(ref _lastVolumeKey), KeyWindowMs) && showBrightness(_brightness.Value))
                _brightnessAt = now;
        }
        TryHideFlyout(hwnd, now);
    }

    private void TryHideFlyout(IntPtr hwnd, long now)
    {
        long keyAt = Interlocked.Read(ref _lastVolumeKey);
        bool volume = _volumeEnabled && _volumeAt == keyAt && IsRecent(now, keyAt, KeyWindowMs);
        bool brightness = _subscription != null && CanHideFlyout(now, _brightnessAt, keyAt);
        if (!(volume || brightness) || !IsFlyoutHost(hwnd)) return;
        ShowWindowAsync(hwnd, 0);
        _candidate = IntPtr.Zero;
    }

    private bool IsFlyoutHost(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return false;
        _className.Clear();
        if (GetClassName(hwnd, _className, _className.Capacity) == 0) return false;
        string name = _className.ToString();
        if (name is not ("XamlExplorerHostIslandWindow" or "XamlExplorerHostIslandWindow_WASDK" or "NativeHWNDHost")) return false;
        if (!IsWindowVisible(hwnd) || GetAncestor(hwnd, 2) != hwnd || GetWindowTextLength(hwnd) != 0) return false;
        if (name == "NativeHWNDHost" && FindWindowEx(hwnd, IntPtr.Zero, "DirectUIHWND", null) == IntPtr.Zero) return false;
        GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid != _explorerPid || !GetWindowRect(hwnd, out var bounds)) return false;
        uint dpi = Math.Max(96u, GetDpiForWindow(hwnd));
        return IsFlyoutSize(name, (bounds.Right - bounds.Left) * 96d / dpi, (bounds.Bottom - bounds.Top) * 96d / dpi);
    }

    internal static bool IsFlyoutSize(string windowClass, double width, double height)
        => windowClass switch
        {
            "NativeHWNDHost" => width is >= 35 and <= 145 && height is >= 40 and <= 260,
            "XamlExplorerHostIslandWindow" or "XamlExplorerHostIslandWindow_WASDK" =>
                width is >= 120 and <= 480 && height is >= 22 and <= 112,
            _ => false
        };

    internal static bool CanHideFlyout(long now, long brightnessAt, long volumeKeyAt)
        => IsRecent(now, brightnessAt, MatchWindowMs) && !IsRecent(now, volumeKeyAt, KeyWindowMs);

    private static bool IsRecent(long now, long eventTime, int milliseconds)
        => eventTime > 0 && now - eventTime is >= 0 && now - eventTime <= milliseconds;

    private void Unhook()
    {
        if (_showHook != IntPtr.Zero) UnhookWinEvent(_showHook);
        if (_locationHook != IntPtr.Zero) UnhookWinEvent(_locationHook);
        _showHook = _locationHook = IntPtr.Zero;
        _explorerPid = 0;
        _candidate = IntPtr.Zero;
    }

    public void Dispose()
    {
        SetVolumeEnabled(false);
        SetEnabled(false);
        _disposed = true;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { internal int Left, Top, Right, Bottom; }
    private delegate void WinEventProc(IntPtr hook, uint eventType, IntPtr hwnd, int objectId, int childId, uint thread, uint time);
    [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr module,
        WinEventProc callback, uint process, uint thread, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string className, string? title);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string className, string? title);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder name, int capacity);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextLength(IntPtr window);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr window, uint flags);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out Rect bounds);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern bool ShowWindowAsync(IntPtr window, int command);
}
