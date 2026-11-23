using System.Runtime.InteropServices;
using Windows.Media.Control;
using Session = Windows.Media.Control.GlobalSystemMediaTransportControlsSession;
using Playback = Windows.Media.Control.GlobalSystemMediaTransportControlsSessionPlaybackInfo;
using PlaybackStatus = Windows.Media.Control.GlobalSystemMediaTransportControlsSessionPlaybackStatus;

namespace DynamicIslandWin.Services;

internal static class MediaSessions
{
    internal sealed record Selection(Session Session, Playback Playback, LyricsService.Track Track, string Album);
    private sealed record Candidate(Session Session, Playback Playback, int Priority);

    internal static async Task<Selection?> ReadAsync(GlobalSystemMediaTransportControlsSessionManager manager, bool includePaused)
    {
        Session? current = null;
        try { current = manager.GetCurrentSession(); }
        catch (Exception ex) when (ex is COMException or InvalidOperationException) { }
        var preferred = ReadCandidate(current, includePaused);
        if (preferred is { Priority: 3 } && await ReadTrackAsync(preferred) is { } playing) return playing;

        var candidates = new List<Candidate>();
        if (preferred is { Priority: < 3 }) candidates.Add(preferred);
        IReadOnlyList<Session> sessions;
        try { sessions = manager.GetSessions(); }
        catch (Exception ex) when (ex is COMException or InvalidOperationException) { sessions = []; }
        foreach (var session in sessions)
        {
            if (!Equals(session, current) && ReadCandidate(session, includePaused) is { } candidate)
                candidates.Add(candidate);
        }
        foreach (var candidate in candidates.OrderByDescending(candidate => candidate.Priority))
            if (await ReadTrackAsync(candidate) is { } selected) return selected;
        return null;
    }

    internal static int Priority(PlaybackStatus status, bool includePaused) => status switch
    {
        PlaybackStatus.Playing => 3,
        PlaybackStatus.Paused when includePaused => 2,
        PlaybackStatus.Opened or PlaybackStatus.Changing when includePaused => 1,
        _ => 0
    };

    private static Candidate? ReadCandidate(Session? session, bool includePaused)
    {
        if (session == null) return null;
        try
        {
            var playback = session.GetPlaybackInfo();
            int rank = playback == null ? 0 : Priority(playback.PlaybackStatus, includePaused);
            return rank == 0 ? null : new(session, playback!, rank);
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException) { return null; }
    }

    private static async Task<Selection?> ReadTrackAsync(Candidate candidate)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(650));
            var session = candidate.Session;
            var props = await session.TryGetMediaPropertiesAsync().AsTask(timeout.Token).ConfigureAwait(false);
            string source = session.SourceAppUserModelId ?? "";
            if (props == null || string.IsNullOrWhiteSpace(props.Title)) return null;
            var track = ReadTrack(props, source);
            return new(session, candidate.Playback, track, props.AlbumTitle?.Trim() ?? "");
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or OperationCanceledException) { return null; }
    }

    internal static LyricsService.Track ReadTrack(GlobalSystemMediaTransportControlsSessionMediaProperties props, string source)
    {
        string artist = string.IsNullOrWhiteSpace(props.Artist) ? props.AlbumArtist?.Trim() ?? "" : props.Artist.Trim();
        if (artist.EndsWith(" - Topic", StringComparison.OrdinalIgnoreCase) && MediaSources.BrowserName(source) != null)
            artist = artist[..^8].TrimEnd();
        return new(props.Title.Trim(), artist, source);
    }
}
