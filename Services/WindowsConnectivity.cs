using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Windows.Devices.Radios;
using Windows.Networking.Connectivity;

namespace DynamicIslandWin.Services;

internal static class WindowsConnectivity
{
    internal readonly record struct VpnStatus(bool Available, string? Name, bool ProxyConfigured = false);
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
        var ras = ReadRasVpn();
        if (ras.Name != null) return ras;

        try
        {
            foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (adapter.OperationalStatus != OperationalStatus.Up || !IsVpnAdapter(adapter.Description, adapter.Name, adapter.NetworkInterfaceType)) continue;
                try
                {
                    if (adapter.GetIPProperties().UnicastAddresses.Any(address => IsVpnAddress(address.Address)))
                        return new(true, string.IsNullOrWhiteSpace(adapter.Name) ? adapter.Description : adapter.Name);
                }
                catch (NetworkInformationException) { } // The tunnel can disappear during enumeration.
            }
        }
        catch (NetworkInformationException) { }

        return ras with { ProxyConfigured = HasSystemProxy() };
    }

    private static readonly string[] VpnAdapterDescriptions =
    [
        "WireGuard", "AmneziaWG", "Wintun", "TAP-Windows", "OpenVPN", "ovpn-dco", "Tailscale", "NordLynx",
        "AnyConnect", "Cisco Secure Client Virtual", "PANGP Virtual", "Cloudflare WARP", "sing-box Tunnel",
        "VPN Adapter", "VPN Virtual", "VPN Tunnel", "sing-tun", "mihomo", "Clash Tunnel"
    ];

    internal static bool IsVpnAdapter(string description, string name = "", NetworkInterfaceType type = NetworkInterfaceType.Unknown)
    {
        if (VpnAdapterDescriptions.Any(marker => description.Contains(marker, StringComparison.OrdinalIgnoreCase))) return true;
        if (type != NetworkInterfaceType.Tunnel) return false;
        return !new[] { "Teredo", "ISATAP", "6to4", "WAN Miniport" }.Any(marker =>
            description.Contains(marker, StringComparison.OrdinalIgnoreCase) || name.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    internal static bool HasSystemProxy()
    {
        bool available = WinHttpGetIEProxyConfigForCurrentUser(out var settings);
        try { return available && (settings.Proxy != IntPtr.Zero || settings.AutoConfigUrl != IntPtr.Zero); }
        finally
        {
            if (settings.Proxy != IntPtr.Zero) GlobalFree(settings.Proxy);
            if (settings.ProxyBypass != IntPtr.Zero) GlobalFree(settings.ProxyBypass);
            if (settings.AutoConfigUrl != IntPtr.Zero) GlobalFree(settings.AutoConfigUrl);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProxyConfig
    {
        public int AutoDetect;
        public IntPtr AutoConfigUrl;
        public IntPtr Proxy;
        public IntPtr ProxyBypass;
    }

    [DllImport("winhttp.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WinHttpGetIEProxyConfigForCurrentUser(out ProxyConfig settings);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern IntPtr GlobalFree(IntPtr memory);

    internal static bool IsVpnAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any)
            || address.IsIPv6LinkLocal) return false;
        if (address.AddressFamily != AddressFamily.InterNetwork) return true;
        Span<byte> bytes = stackalloc byte[4];
        address.TryWriteBytes(bytes, out _);
        return bytes[0] != 169 || bytes[1] != 254;
    }

    private static VpnStatus ReadRasVpn()
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
                    if (string.Equals(entry.DeviceType, "vpn", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(entry.EntryName))
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
