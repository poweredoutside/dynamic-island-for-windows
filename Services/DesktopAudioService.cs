using System.Diagnostics;
using System.Management;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace DynamicIslandWin.Services;

internal sealed class DesktopAudioService : IDisposable
{
    internal sealed record AppVolume(string Id, string Name, int ProcessId, float Volume, bool Muted);
    private readonly MMDeviceEnumerator _enumerator = new();
    internal (float Volume, bool Muted) Master()
    {
        using var device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        return (device.AudioEndpointVolume.MasterVolumeLevelScalar, device.AudioEndpointVolume.Mute);
    }
    internal void SetMaster(float volume)
    {
        using var device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        device.AudioEndpointVolume.MasterVolumeLevelScalar = Math.Clamp(volume, 0, 1);
    }
    internal IReadOnlyList<AppVolume> Apps()
    {
        using var device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        var result = new List<AppVolume>();
        var sessions = device.AudioSessionManager.Sessions;
        for (int i = 0; i < sessions.Count; i++)
        {
            using var session = sessions[i];
            if (session.State == AudioSessionState.AudioSessionStateExpired) continue;
            string name = session.DisplayName;
            try { using var process = Process.GetProcessById((int)session.GetProcessID); name = process.ProcessName; } catch { }
            if (string.IsNullOrWhiteSpace(name) || session.IsSystemSoundsSession) name = "Системные звуки";
            result.Add(new(session.GetSessionInstanceIdentifier, name, (int)session.GetProcessID,
                session.SimpleAudioVolume.Volume, session.SimpleAudioVolume.Mute));
        }
        return result;
    }
    internal bool SetApp(string id, float? volume, bool? mute)
    {
        using var device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        var sessions = device.AudioSessionManager.Sessions;
        for (int i = 0; i < sessions.Count; i++)
        {
            using var session = sessions[i];
            if (session.GetSessionInstanceIdentifier != id) continue;
            if (volume.HasValue) session.SimpleAudioVolume.Volume = Math.Clamp(volume.Value, 0, 1);
            if (mute.HasValue) session.SimpleAudioVolume.Mute = mute.Value;
            return true;
        }
        return false;
    }
    internal static int? Brightness()
    {
        try
        {
            using var query = new ManagementObjectSearcher("root\\WMI", "SELECT CurrentBrightness FROM WmiMonitorBrightness WHERE Active=True");
            using var results = query.Get();
            foreach (ManagementObject item in results) using (item) return Convert.ToInt32(item["CurrentBrightness"]);
        }
        catch (ManagementException) { } catch (System.Runtime.InteropServices.COMException) { }
        return null;
    }
    internal static int? GpuUsage()
    {
        try
        {
            using var query = new ManagementObjectSearcher("SELECT Name, UtilizationPercentage FROM Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine");
            using var results = query.Get();
            var engines = new Dictionary<string, double>();
            foreach (ManagementObject item in results)
                using (item)
                {
                    string name = Convert.ToString(item["Name"]) ?? "";
                    int luid = name.IndexOf("luid_", StringComparison.Ordinal);
                    if (luid < 0) continue;
                    string key = name[luid..];
                    engines[key] = engines.GetValueOrDefault(key) + Convert.ToDouble(item["UtilizationPercentage"]);
                }
            return engines.Count == 0 ? null : (int)Math.Clamp(engines.Values.Max(), 0, 100);
        }
        catch (ManagementException) { return null; } catch (System.Runtime.InteropServices.COMException) { return null; }
    }
    internal static void SetBrightness(int value)
    {
        using var query = new ManagementObjectSearcher("root\\WMI", "SELECT * FROM WmiMonitorBrightnessMethods WHERE Active=True");
        using var results = query.Get();
        foreach (ManagementObject item in results) using (item) item.InvokeMethod("WmiSetBrightness", [1u, (byte)Math.Clamp(value, 0, 100)]);
    }
    public void Dispose() => _enumerator.Dispose();
}
