using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace DynamicIslandWin.Services;

// CoreAudio and the keyboard hook have independent threads. Neither waits for WPF,
// WMI, a device driver, or the other thread in a low-level keyboard callback.
internal sealed class VolumeIslandService : IDisposable, IMMNotificationClient
{
    internal readonly record struct Snapshot(float Volume, bool Muted);
    private readonly BlockingCollection<int> _commands = new(128);
    private readonly Thread _audioThread, _hookThread;
    private readonly HookProc _hookProc;
    private readonly VolumeKeyRouter _keys = new();
    private volatile bool _enabled, _available, _disposed, _observeVolume;
    private uint _hookThreadId;
    private int _rebindPending;
    private int _setPending, _requestedPercent;
    private MMDevice? _device;
    private IntPtr _hook;
    internal event Action<Snapshot>? Changed;
    internal bool Available => _available && _hook != IntPtr.Zero;
    internal bool Enabled { get => _enabled; set => _enabled = value; }
    internal bool ObserveVolume { get => _observeVolume; set => _observeVolume = value; }

    internal bool RequestSnapshot() => !_disposed && _commands.TryAdd(2);
    internal bool ToggleMute() => !_disposed && _commands.TryAdd(3);
    internal bool SetVolume(double percent)
    {
        if (_disposed || !double.IsFinite(percent)) return false;
        Interlocked.Exchange(ref _requestedPercent, (int)Math.Round(Math.Clamp(percent, 0, 100)));
        if (Interlocked.Exchange(ref _setPending, 1) != 0) return true;
        if (_commands.TryAdd(1)) return true;
        Interlocked.Exchange(ref _setPending, 0);
        return false;
    }

    internal VolumeIslandService(bool enabled)
    {
        _enabled = enabled;
        _hookProc = KeyboardHook;
        _audioThread = new Thread(AudioLoop) { IsBackground = true, Name = "Island CoreAudio" };
        _hookThread = new Thread(HookLoop) { IsBackground = true, Name = "Island volume keys" };
        _audioThread.Start(); _hookThread.Start();
    }

    private void HookLoop()
    {
        _hookThreadId = GetCurrentThreadId();
        PeekMessage(out _, IntPtr.Zero, 0, 0, 0); // Create the queue before publishing the hook.
        if (_disposed) return;
        _hook = SetWindowsHookEx(13, _hookProc, GetModuleHandle(null), 0);
        if (_hook == IntPtr.Zero) { AppLog.Error("Volume hook unavailable", new System.ComponentModel.Win32Exception()); return; }
        try
        {
            while (!_disposed && GetMessage(out var message, IntPtr.Zero, 0, 0) > 0)
            { TranslateMessage(ref message); DispatchMessage(ref message); }
        }
        finally { UnhookWindowsHookEx(_hook); _hook = IntPtr.Zero; }
    }

    private IntPtr KeyboardHook(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0)
        {
            int key = Marshal.ReadInt32(data);
            long kind = message.ToInt64();
            if (_keys.Handle(key, kind is 0x100 or 0x104, kind is 0x101 or 0x105,
                _enabled && !_disposed, _available, command => _commands.TryAdd(command))) return (IntPtr)1;
        }
        return CallNextHookEx(IntPtr.Zero, code, message, data);
    }

    private void AudioLoop()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            enumerator.RegisterEndpointNotificationCallback(this);
            try
            {
                Rebind(enumerator);
                while (!_disposed)
                {
                    if (!_commands.TryTake(out int command, 2000))
                    { if (!_available) Rebind(enumerator); continue; }
                    if (command == 0) { Interlocked.Exchange(ref _rebindPending, 0); Rebind(enumerator); continue; }
                    if (command == 1) Interlocked.Exchange(ref _setPending, 0);
                    bool manual = command is 1 or 2 or 3;
                    if (manual && !_available) Rebind(enumerator, publish: false);
                    if ((!_enabled && !manual) || _device == null) continue;
                    try
                    {
                        var volume = _device.AudioEndpointVolume;
                        if (command is 0xAD or 3) volume.Mute = !volume.Mute;
                        else if (command != 2)
                        {
                            volume.MasterVolumeLevelScalar = command == 1
                                ? Volatile.Read(ref _requestedPercent) / 100f
                                : Math.Clamp(volume.MasterVolumeLevelScalar + (command == 0xAF ? .02f : -.02f), 0, 1);
                            if (volume.Mute) volume.Mute = false;
                        }
                        // Also acknowledge a key at 0/100%, where CoreAudio may emit no event.
                        Publish(new(volume.MasterVolumeLevelScalar, volume.Mute), manual);
                    }
                    catch (Exception ex) { _available = false; AppLog.Error("Change endpoint volume", ex); }
                }
            }
            finally { _available = false; Detach(); enumerator.UnregisterEndpointNotificationCallback(this); }
        }
        catch (Exception ex) { _available = false; AppLog.Error("Volume event service", ex); }
    }

    private void Rebind(MMDeviceEnumerator enumerator, bool publish = true)
    {
        _available = false; Detach();
        if (_disposed) return;
        try
        {
            _device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            _device.AudioEndpointVolume.OnVolumeNotification += OnVolume;
            _ = _device.AudioEndpointVolume.MasterVolumeLevelScalar;
            _available = true;
            if (publish && _observeVolume) Publish(new(_device.AudioEndpointVolume.MasterVolumeLevelScalar, _device.AudioEndpointVolume.Mute));
        }
        catch { Detach(); } // No endpoint is normal while unplugging a headset.
    }
    private void OnVolume(AudioVolumeNotificationData data) => Publish(new(data.MasterVolume, data.Muted));
    private void Publish(Snapshot snapshot, bool manual = false) { if ((_enabled || _observeVolume || manual) && !_disposed) Changed?.Invoke(snapshot); }
    private void Detach()
    {
        if (_device == null) return;
        try { _device.AudioEndpointVolume.OnVolumeNotification -= OnVolume; _device.Dispose(); } catch { }
        _device = null;
    }
    private void RequestRebind()
    {
        _available = false;
        if (!_disposed && Interlocked.Exchange(ref _rebindPending, 1) == 0 && !_commands.TryAdd(0))
            Interlocked.Exchange(ref _rebindPending, 0);
    }
    public void OnDefaultDeviceChanged(DataFlow flow, Role role, string id) { if (flow == DataFlow.Render && role == Role.Multimedia) RequestRebind(); }
    public void OnDeviceStateChanged(string id, DeviceState state) => RequestRebind();
    public void OnDeviceAdded(string id) => RequestRebind();
    public void OnDeviceRemoved(string id) => RequestRebind();
    public void OnPropertyValueChanged(string id, PropertyKey key) { }
    public void Dispose()
    {
        _enabled = false; _disposed = true; _available = false;
        if (_hookThreadId != 0) PostThreadMessage(_hookThreadId, 0x12, IntPtr.Zero, IntPtr.Zero);
        _commands.TryAdd(0);
        // Do not block the UI waiting on a faulty audio driver. Threads own cleanup.
    }

    private delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);
    [StructLayout(LayoutKind.Sequential)] private struct NativeMessage { public IntPtr Hwnd; public uint Message; public UIntPtr WParam; public IntPtr LParam; public uint Time; public int X, Y; public uint Private; }
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int id, HookProc callback, IntPtr module, uint thread);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
    [DllImport("user32.dll")] private static extern int GetMessage(out NativeMessage message, IntPtr window, uint min, uint max);
    [DllImport("user32.dll")] private static extern bool PeekMessage(out NativeMessage message, IntPtr window, uint min, uint max, uint remove);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref NativeMessage message);
    [DllImport("user32.dll")] private static extern IntPtr DispatchMessage(ref NativeMessage message);
    [DllImport("user32.dll")] private static extern bool PostThreadMessage(uint thread, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
}

internal sealed class VolumeKeyRouter
{
    private readonly HashSet<int> _pressed = [];
    internal bool Handle(int key, bool down, bool up, bool enabled, bool available, Func<int, bool> enqueue)
    {
        if (key is not (0xAD or 0xAE or 0xAF)) return false;
        if (up && _pressed.Remove(key)) return true;
        // A matched key-up is consumed even if the setting changed during the press.
        // Other inputs and a full/unavailable worker always pass back to Windows.
        if (down && enabled && available && enqueue(key)) { _pressed.Add(key); return true; }
        return false;
    }
}
