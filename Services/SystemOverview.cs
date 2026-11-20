using System.Runtime.InteropServices;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace DynamicIslandWin.Services;

internal static class SystemOverview
{
    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        internal uint Size, Load;
        internal ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile, TotalVirtual, AvailableVirtual, ExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool LockWorkStation();

    internal static (double Used, double Total, int Percent)? Memory()
    {
        var status = new MemoryStatus { Size = (uint)Marshal.SizeOf<MemoryStatus>() };
        const double gib = 1024 * 1024 * 1024;
        if (!GlobalMemoryStatusEx(ref status) || status.TotalPhysical == 0) return null;
        return ((status.TotalPhysical - status.AvailablePhysical) / gib, status.TotalPhysical / gib, (int)status.Load);
    }

    internal static (int Percent, bool Charging)? Battery()
    {
        var power = Forms.SystemInformation.PowerStatus;
        if (power.BatteryChargeStatus == Forms.BatteryChargeStatus.Unknown
            || power.BatteryChargeStatus.HasFlag(Forms.BatteryChargeStatus.NoSystemBattery)
            || power.BatteryLifePercent is < 0 or > 1) return null;
        return ((int)Math.Round(power.BatteryLifePercent * 100), power.BatteryChargeStatus.HasFlag(Forms.BatteryChargeStatus.Charging));
    }

    internal static bool? IsPrivacyDeviceActive(string capability)
    {
        const string prefix = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\";
        try
        {
            using var devices = Registry.CurrentUser.OpenSubKey(prefix + capability);
            if (devices == null) return null;
            return ActiveChildren(devices);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.IO.IOException or System.Security.SecurityException) { return null; }

        static bool ActiveChildren(RegistryKey root)
        {
            foreach (string name in root.GetSubKeyNames())
            {
                using var entry = root.OpenSubKey(name);
                if (entry == null) continue;
                if (entry.GetValue("LastUsedTimeStart") is long start && start > 0 && entry.GetValue("LastUsedTimeStop") is long stop && stop == 0)
                    return true;
                if (name == "NonPackaged" && ActiveChildren(entry)) return true;
            }
            return false;
        }
    }
}
