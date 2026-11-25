using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace DynamicIslandWin.Services;

internal sealed class DesktopAudioService : IDisposable
{
    internal sealed record AppVolume(string Id, string EndpointId, string OutputName, string Name, int ProcessId,
        bool Active, float Volume, bool Muted);
    private readonly MMDeviceEnumerator _enumerator = new();
    internal (float Volume, bool Muted) Master()
    {
        using var device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        return (device.AudioEndpointVolume.MasterVolumeLevelScalar, device.AudioEndpointVolume.Mute);
    }
    internal IReadOnlyList<AppVolume> Apps()
    {
        string? defaultId = null;
        try
        {
            using var defaultOutput = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            defaultId = defaultOutput.ID;
        }
        catch (COMException) { }

        var result = new List<AppVolume>();
        foreach (var device in _enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        {
            using (device)
            {
                try
                {
                    string endpointId = device.ID, outputName = device.FriendlyName;
                    var sessions = device.AudioSessionManager.Sessions;
                    for (int i = 0; i < sessions.Count; i++)
                    {
                        try
                        {
                            using var session = sessions[i];
                            var state = session.State;
                            if (state == AudioSessionState.AudioSessionStateExpired) continue;
                            bool systemSounds = session.IsSystemSoundsSession;
                            int pid = systemSounds ? 0 : (int)session.GetProcessID;
                            string name = systemSounds ? "Системные звуки" : SessionName(session, pid);
                            var volume = session.SimpleAudioVolume;
                            result.Add(new(session.GetSessionInstanceIdentifier, endpointId, outputName, name, pid,
                                state == AudioSessionState.AudioSessionStateActive, volume.Volume, volume.Mute));
                        }
                        catch (COMException) { } // A session may close during enumeration.
                    }
                }
                catch (COMException) { } // Ignore an output disconnected during enumeration.
            }
        }

        var activeProcesses = result.Where(session => session.Active).Select(session => session.ProcessId).ToHashSet();
        return result.Where(session => session.Active)
            .Concat(result.Where(session => !session.Active && !activeProcesses.Contains(session.ProcessId))
                .OrderByDescending(session => session.EndpointId == defaultId)
                .DistinctBy(session => session.ProcessId != 0 ? (session.ProcessId, "") : (0, session.Name)))
            .ToArray();
    }

    private static string SessionName(AudioSessionControl session, int pid)
    {
        if (pid != 0)
        {
            try
            {
                using var process = Process.GetProcessById(pid);
                return process.ProcessName;
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { }
        }
        string name = session.DisplayName;
        return string.IsNullOrWhiteSpace(name) ? "Приложение" : name;
    }

    internal (float Volume, bool Muted)? SetApp(AppVolume target, float? volume, bool? mute)
    {
        using var device = _enumerator.GetDevice(target.EndpointId);
        var sessions = device.AudioSessionManager.Sessions;
        for (int i = 0; i < sessions.Count; i++)
        {
            using var session = sessions[i];
            if (session.State == AudioSessionState.AudioSessionStateExpired || session.GetSessionInstanceIdentifier != target.Id) continue;
            var level = session.SimpleAudioVolume;
            if (volume.HasValue) level.Volume = Math.Clamp(volume.Value, 0, 1);
            if (mute.HasValue) level.Mute = mute.Value;
            return (level.Volume, level.Mute);
        }
        return null;
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
