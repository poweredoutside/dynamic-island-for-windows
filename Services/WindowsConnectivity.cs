using System.Runtime.InteropServices;
using Microsoft.Win32;
using Windows.Devices.Radios;
using Windows.Networking.Connectivity;

namespace DynamicIslandWin.Services;

internal static class WindowsConnectivity
{
    internal readonly record struct VpnStatus(bool Available, string? Name);
    internal readonly record struct Snapshot(RadioState? Wifi, RadioState? Bluetooth, string? WifiProfile, bool? Airplane, VpnStatus Vpn);

    internal static async Task<Snapshot> ReadAsync()
    {
        IReadOnlyList<Radio> radios;
        try { radios = await Radio.GetRadiosAsync(); }
        catch (Exception ex) when (ex is COMException or UnauthorizedAccessException or InvalidOperationException) { radios = []; }

        RadioState? wifi = State(RadioKind.WiFi);
        string? wifiProfile = wifi is null or RadioState.On ? WifiProfile() : null;
        if (wifiProfile != null && wifi == null) wifi = RadioState.On;
        return new(wifi, State(RadioKind.Bluetooth), wifiProfile, AirplaneMode(), ReadVpn());

        RadioState? State(RadioKind kind)
        {
            var states = radios.Where(radio => radio.Kind == kind).Select(radio => radio.State).ToArray();
            if (states.Contains(RadioState.On)) return RadioState.On;
            if (states.Contains(RadioState.Off)) return RadioState.Off;
            return states.Length > 0 ? states[0] : null;
        }
    }

    internal static async Task<bool> ToggleRadioAsync(RadioKind kind)
    {
        if (await Radio.RequestAccessAsync() != RadioAccessStatus.Allowed) return false;
        var radios = (await Radio.GetRadiosAsync())
            .Where(radio => radio.Kind == kind && radio.State is RadioState.On or RadioState.Off).ToArray();
        if (radios.Length == 0) return false;
        var target = radios.Any(radio => radio.State == RadioState.On) ? RadioState.Off : RadioState.On;
        bool accepted = true;
        foreach (var radio in radios)
            if (radio.State != target && await radio.SetStateAsync(target) != RadioAccessStatus.Allowed) accepted = false;
        return accepted;
    }

    private static string? WifiProfile()
    {
        try
        {
            return NetworkInformation.GetConnectionProfiles().FirstOrDefault(profile => profile.IsWlanConnectionProfile
                && profile.GetNetworkConnectivityLevel() != NetworkConnectivityLevel.None)?.ProfileName;
        }
        catch (Exception ex) when (ex is COMException or UnauthorizedAccessException) { return null; }
    }

    private static bool? AirplaneMode()
    {
        // Read the global Windows switch. Individual radios can be on in airplane mode.
        const string key = @"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\RadioManagement\SystemRadioState";
        try { return Registry.GetValue(key, "", null) switch { 0 => false, 1 => true, _ => null }; }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or System.IO.IOException) { return null; }
    }

    private static VpnStatus ReadVpn()
    {
        const uint BufferTooSmall = 603;
        int entrySize = Marshal.SizeOf<RasConnection>();
        uint bytes = (uint)entrySize;
        for (int attempt = 0; attempt < 2; attempt++)
        {
            IntPtr buffer = Marshal.AllocHGlobal(checked((int)bytes));
            try
            {
                Marshal.WriteInt32(buffer, entrySize);
                uint result = RasEnumConnectionsW(buffer, ref bytes, out uint count);
                if (result == BufferTooSmall && bytes > entrySize && bytes <= 1024 * 1024) continue;
                if (result != 0 || count > bytes / entrySize) return default;
                for (int index = 0; index < count; index++)
                {
                    var entry = Marshal.PtrToStructure<RasConnection>(buffer + index * entrySize);
                    if (string.Equals(entry.DeviceType, "vpn", StringComparison.OrdinalIgnoreCase))
                        return new(true, entry.EntryName);
                }
                return new(true, null);
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        return default;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode, Pack = 4)]
    private struct RasConnection
    {
        public uint Size;
        public IntPtr Handle;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 257)] public string EntryName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 17)] public string DeviceType;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 129)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string Phonebook;
        public uint SubEntry;
        public Guid EntryId;
        public uint Flags;
        public uint LuidLow;
        public int LuidHigh;
        public Guid CorrelationId;
    }

    [DllImport("rasapi32.dll", ExactSpelling = true)]
    private static extern uint RasEnumConnectionsW(IntPtr connections, ref uint bytes, out uint count);
}
