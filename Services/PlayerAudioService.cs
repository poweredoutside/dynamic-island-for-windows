using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace DynamicIslandWin.Services;

internal static class PlayerAudioService
{
    internal readonly record struct Snapshot(float Volume, bool Muted);
    internal sealed record Output(string Id, string Name, bool InUse);
    internal sealed record Outputs(IReadOnlyList<Output> Devices, string? PreferredId, bool CanRoute);
    private sealed record OutputScan(IReadOnlyList<Output> Devices, uint ProcessId, string? DefaultId);

    // Windows can create several render sessions for one app, including on different outputs.
    // The active session supplies the displayed level. Writes apply to the matched app's sessions.
    internal static Snapshot? ReadOrSet(string source, float? volume = null)
    {
        if (string.IsNullOrWhiteSpace(source)) return null;
        // Yandex Music exposes its own volume even when it has no WASAPI mixer session.
        if (YandexMusicVolume.ReadOrSet(source, volume) is { } playerVolume) return playerVolume;
        var matches = new Dictionary<uint, bool>();
        Snapshot? snapshot = null;
        bool active = false;
        using var audio = new MMDeviceEnumerator();
        foreach (var endpoint in audio.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        {
            using var device = endpoint;
            try
            {
                foreach (var (session, _, isActive) in MatchingSessions(device, source, matches))
                {
                    var audioVolume = session.SimpleAudioVolume;
                    if (volume.HasValue)
                    {
                        audioVolume.Volume = Math.Clamp(volume.Value, 0, 1);
                        if (volume.Value > 0 && audioVolume.Mute) audioVolume.Mute = false;
                    }
                    if (snapshot == null || (!active && isActive))
                    {
                        snapshot = new Snapshot(audioVolume.Volume, audioVolume.Mute);
                        active = isActive;
                        if (active && !volume.HasValue) return snapshot;
                    }
                }
            }
            catch (COMException) { } // The output may have disconnected during enumeration.
        }
        return snapshot;
    }

    internal static Outputs ReadOutputs(string source)
    {
        var scan = ScanOutputs(source);
        if (scan.ProcessId == 0 || scan.Devices.Count == 0) return new(scan.Devices, null, false);
        using var routing = new AppAudioRouting();
        return new(scan.Devices, routing.GetOutput(scan.ProcessId), true);
    }

    internal static string? ChangeOutput(string source, string? endpointId)
    {
        var scan = ScanOutputs(source);
        if (!MatchesProcess(source, scan.ProcessId))
            throw new InvalidOperationException("The audio session for the player ended.");
        if (endpointId != null && !scan.Devices.Any(device => string.Equals(device.Id, endpointId, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("The selected player output is unavailable.");
        using var routing = new AppAudioRouting();
        routing.SetOutput(scan.ProcessId, endpointId);
        return endpointId ?? scan.DefaultId;
    }

    private static OutputScan ScanOutputs(string source)
    {
        var matches = new Dictionary<uint, bool>();
        var outputs = new List<Output>();
        uint activePid = 0, pausedPid = 0;
        using var audio = new MMDeviceEnumerator();
        foreach (var endpoint in audio.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        {
            using var device = endpoint;
            try
            {
                bool playingHere = false;
                foreach (var (_, pid, active) in MatchingSessions(device, source, matches))
                {
                    if (pausedPid == 0) pausedPid = pid;
                    if (!active) continue;
                    if (activePid == 0) activePid = pid;
                    playingHere = true;
                    break;
                }
                outputs.Add(new(device.ID, device.FriendlyName, playingHere));
            }
            catch (COMException) { } // The endpoint may have been unplugged.
        }
        string? defaultId = null;
        try
        {
            using var endpoint = audio.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            defaultId = endpoint.ID;
        }
        catch (COMException) { }
        return new(outputs, activePid != 0 ? activePid : pausedPid, defaultId);
    }

    private static IEnumerable<(AudioSessionControl Session, uint ProcessId, bool Active)> MatchingSessions(
        MMDevice endpoint, string source, Dictionary<uint, bool> matches)
    {
        if (string.IsNullOrWhiteSpace(source)) yield break;
        var sessions = endpoint.AudioSessionManager.Sessions;
        for (int i = 0; i < sessions.Count; i++)
        {
            using var session = sessions[i];
            var state = session.State;
            if (state == AudioSessionState.AudioSessionStateExpired || session.IsSystemSoundsSession) continue;
            uint pid = session.GetProcessID;
            if (!matches.TryGetValue(pid, out bool match)) matches[pid] = match = MatchesProcess(source, pid);
            if (match) yield return (session, pid, state == AudioSessionState.AudioSessionStateActive);
        }
    }

    internal static bool MatchesProcess(string source, uint pid)
    {
        if (pid == 0) return false;
        try
        {
            using var process = Process.GetProcessById((int)pid);
            using var handle = OpenProcess(0x1000, false, pid); // PROCESS_QUERY_LIMITED_INFORMATION
            if (handle.IsInvalid) return false;

            var appId = new StringBuilder(256);
            uint capacity = (uint)appId.Capacity;
            if (GetApplicationUserModelId(handle, ref capacity, appId) == 0
                && source.Equals(appId.ToString(), StringComparison.OrdinalIgnoreCase)) return true;
            if (source.Contains('!')) return false; // Packaged apps require an exact app ID.

            if (Path.IsPathFullyQualified(source))
            {
                var executable = new StringBuilder(32768);
                uint length = (uint)executable.Capacity;
                return QueryFullProcessImageName(handle, 0, executable, ref length)
                    && source.Equals(executable.ToString(), StringComparison.OrdinalIgnoreCase);
            }

            return MediaSources.MatchesDesktopPlayer(source, process.ProcessName);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception) { return false; }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int GetApplicationUserModelId(SafeProcessHandle process, ref uint length, [Out] StringBuilder appId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(SafeProcessHandle process, uint flags, [Out] StringBuilder path, ref uint length);
}
