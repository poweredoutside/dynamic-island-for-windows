using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace DynamicIslandWin.Services;

// Reads the undo journal written by package 1.1.29.7.
internal sealed class AudioMuteRecovery : IDisposable
{
    internal static string JournalPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DynamicIslandWin", "audio-mute.json");

    internal sealed record MutedSession(string Endpoint, string Session, string Instance);
    private sealed record LiveSession(MutedSession Id, AudioSessionControl Control, AudioSessionState State);
    private readonly string _path;
    private readonly FileStream _lease;
    private HashSet<MutedSession> _muted;

    internal AudioMuteRecovery(string path)
    {
        _path = path;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        _lease = new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        try
        {
            _muted = File.Exists(path)
                ? JsonSerializer.Deserialize<HashSet<MutedSession>>(File.ReadAllText(path)) ?? []
                : [];
        }
        catch
        {
            _lease.Dispose();
            throw;
        }
    }

    internal bool HasPendingRestore => _muted.Count != 0;

    internal void Restore(MMDeviceEnumerator enumerator)
    {
        if (!HasPendingRestore) return;
        foreach (var output in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        {
            try
            {
                using (output) Restore(output);
            }
            catch (COMException) { } // Restore disconnected sessions when they become available again.
        }
    }

    private void Restore(MMDevice device)
    {
        string endpoint = device.ID;
        var previous = _muted.Where(session => session.Endpoint == endpoint).ToHashSet();
        if (previous.Count == 0) return;

        var manager = device.AudioSessionManager;
        manager.RefreshSessions();
        var sessions = manager.Sessions;
        var current = new List<LiveSession>();
        try
        {
            for (int i = 0; i < sessions.Count; i++)
            {
                var session = sessions[i];
                try
                {
                    if (session.IsSystemSoundsSession || session.SimpleAudioVolume == null) continue;
                    var id = new MutedSession(endpoint, session.GetSessionIdentifier, session.GetSessionInstanceIdentifier);
                    current.Add(new(id, session, session.State));
                    session = null;
                }
                catch (COMException) { } // The session ended during enumeration.
                finally { session?.Dispose(); }
            }

            var remaining = new HashSet<MutedSession>(_muted);
            foreach (var group in previous.GroupBy(session => session.Session))
            {
                var matches = current.Where(session => session.Id.Session == group.Key).ToArray();
                foreach (var original in group)
                {
                    var match = matches.FirstOrDefault(session => session.Id == original);
                    // A restarted app can inherit its previous mute. Restore by session only
                    // when there is a single candidate and no ambiguity between instances.
                    if ((match == null || match.State == AudioSessionState.AudioSessionStateExpired) && group.Count() == 1)
                    {
                        var live = matches.Where(session => session.State != AudioSessionState.AudioSessionStateExpired).ToArray();
                        if (live.Length == 1) match = live[0];
                    }
                    if (match != null && TryUnmute(match)) remaining.Remove(original);
                }
            }
            Save(remaining);
        }
        finally
        {
            foreach (var session in current) session.Control.Dispose();
        }
    }

    private static bool TryUnmute(LiveSession session)
    {
        try
        {
            var volume = session.Control.SimpleAudioVolume;
            if (volume.Mute) volume.Mute = false;
            return true;
        }
        catch (COMException) { return false; } // Keep the recovery record if a device disconnected.
    }

    private void Save(HashSet<MutedSession> current)
    {
        if (_muted.SetEquals(current)) return;
        string temporary = _path + ".tmp";
        using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, current);
            stream.Flush(flushToDisk: true);
        }
        File.Move(temporary, _path, overwrite: true);
        _muted = current;
    }

    public void Dispose() => _lease.Dispose();
}
