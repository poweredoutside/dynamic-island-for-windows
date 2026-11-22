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

    // Windows can create several render sessions for one app, including on different outputs.
    // The active session supplies the displayed level. Writes apply to the matched app's sessions.
    internal static Snapshot? ReadOrSet(string source, float? volume = null)
    {
        if (string.IsNullOrWhiteSpace(source)) return null;
        var matches = new Dictionary<uint, bool>();
        Snapshot? snapshot = null;
        bool active = false;
        using var audio = new MMDeviceEnumerator();
        foreach (var endpoint in audio.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        {
            using var device = endpoint;
            try
            {
                var sessions = device.AudioSessionManager.Sessions;
                for (int i = 0; i < sessions.Count; i++)
                {
                    using var session = sessions[i];
                    var state = session.State;
                    if (state == AudioSessionState.AudioSessionStateExpired || session.IsSystemSoundsSession) continue;
                    uint pid = session.GetProcessID;
                    if (!matches.TryGetValue(pid, out bool match)) matches[pid] = match = MatchesProcess(source, pid);
                    if (!match) continue;

                    var audioVolume = session.SimpleAudioVolume;
                    if (volume.HasValue)
                    {
                        audioVolume.Volume = Math.Clamp(volume.Value, 0, 1);
                        if (volume.Value > 0 && audioVolume.Mute) audioVolume.Mute = false;
                    }
                    if (snapshot == null || (!active && state == AudioSessionState.AudioSessionStateActive))
                    {
                        snapshot = new Snapshot(audioVolume.Volume, audioVolume.Mute);
                        active = state == AudioSessionState.AudioSessionStateActive;
                        if (active && !volume.HasValue) return snapshot;
                    }
                }
            }
            catch (COMException) { } // The output may have disconnected during enumeration.
        }
        return snapshot;
    }

    private static bool MatchesProcess(string source, uint pid)
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

            return MatchesDesktopPlayer(source, process.ProcessName);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception) { return false; }
    }

    internal static bool MatchesDesktopPlayer(string source, string processName)
    {
        var appId = source.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? source[..^4] : source;
        if (appId.Equals(processName, StringComparison.OrdinalIgnoreCase)) return true;

        return processName.ToLowerInvariant() switch
        {
            "chrome" => appId.StartsWith("Chrome.", StringComparison.OrdinalIgnoreCase),
            "msedge" => appId.StartsWith("MSEdge.", StringComparison.OrdinalIgnoreCase) || appId.Equals("Microsoft.MicrosoftEdge", StringComparison.OrdinalIgnoreCase),
            "yandexmusic" or "яндекс музыка" => appId.Equals("com.yandex.music", StringComparison.OrdinalIgnoreCase)
                || appId.Equals("ru.yandex.desktop.music", StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int GetApplicationUserModelId(SafeProcessHandle process, ref uint length, [Out] StringBuilder appId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(SafeProcessHandle process, uint flags, [Out] StringBuilder path, ref uint length);
}
