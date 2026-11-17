using LibreHardwareMonitor.Hardware;
using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;

namespace DynamicIslandWin.Services;

// Own this on one polling thread. CPU sensors may require the elevated game helper.
internal sealed class CpuTemperatureService : IDisposable
{
    private Computer? _computer;
    private bool _initialized;

    internal static bool IsDriverInstalled
    {
        get
        {
            using var registry = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var driver = registry.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO");
            return Version.TryParse(driver?.GetValue("DisplayVersion") as string, out var version) && version >= new Version(2, 0);
        }
    }

    internal static async Task InstallDriverAsync()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Hardware", "PawnIO_setup.exe");
        await using (var file = File.OpenRead(path))
        {
            string hash = Convert.ToHexString(await SHA256.HashDataAsync(file));
            if (hash != "1F519A22E47187F70A1379A48CA604981C4FCF694F4E65B734AAA74A9FBA3032")
                throw new IOException("PawnIO installer SHA-256 mismatch.");
        }
        using var process = Process.Start(new ProcessStartInfo(path) { Verb = "runas", UseShellExecute = true, WindowStyle = ProcessWindowStyle.Hidden, Arguments = "-install -silent" })
            ?? throw new IOException("PawnIO installer did not start.");
        await process.WaitForExitAsync();
        if (process.ExitCode != 0 || !IsDriverInstalled) throw new IOException("PawnIO installation failed.");
    }

    internal int ReadCelsius()
    {
        try
        {
            if (!IsDriverInstalled) return -1;
            if (!_initialized)
            {
                _initialized = true;
                _computer = new Computer { IsCpuEnabled = true };
                _computer.Open();
            }
            var readings = new List<(string Name, float? Value)>();
            foreach (var cpu in _computer!.Hardware.Where(h => h.HardwareType == HardwareType.Cpu))
                ReadHardware(cpu, readings);
            return SelectCelsius(readings);
        }
        catch { return -1; }
    }

    private static void ReadHardware(IHardware hardware, List<(string Name, float? Value)> readings)
    {
        hardware.Update();
        readings.AddRange(hardware.Sensors.Where(s => s.SensorType == SensorType.Temperature).Select(s => (s.Name, s.Value)));
        foreach (var child in hardware.SubHardware) ReadHardware(child, readings);
    }

    internal static int SelectCelsius(IEnumerable<(string Name, float? Value)> readings)
    {
        var valid = readings.Where(s => s.Value is >= 5 and <= 125 && !s.Name.Contains("Distance", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (valid.Length == 0) return -1;
        static int Priority(string name) => name.Contains("Package", StringComparison.OrdinalIgnoreCase) ? 0
            : name.Contains("Tctl", StringComparison.OrdinalIgnoreCase) ? 1
            : name.Contains("Core", StringComparison.OrdinalIgnoreCase) ? 2 : 3;
        int best = valid.Min(s => Priority(s.Name));
        return (int)Math.Round(valid.Where(s => Priority(s.Name) == best).Max(s => s.Value!.Value));
    }

    public void Dispose()
    {
        try { _computer?.Close(); } catch { }
        _computer = null;
    }
}
