using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DynamicIslandWin.Services;

internal static class LyricsService
{
    private const int MaxTextLength = 500_000;
    private static readonly Regex Timestamp = new(@"\[(\d{1,3}):([0-5]\d)(?:[.:](\d{1,3}))?\]", RegexOptions.CultureInvariant);
    private static readonly Regex Offset = new(@"^\s*\[offset:([+-]?\d{1,9})\]\s*$", RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant);
    private static readonly Regex Edition = new(
        @"\s*(?:[\[(](?:feat\.?|ft\.?|official (?:music )?(?:video|audio)|lyrics?|remaster(?:ed)?|\d{4} remaster)[^\])]*[\])]|[-–—]\s*(?:official (?:music )?(?:video|audio)|lyrics?|remaster(?:ed)?|\d{4} remaster)\b.*)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex ArtistSeparator = new(@"\s+(?:feat\.?|ft\.?|and|with|&|x|×)\s+|\s*[,;]\s*",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private const string Cyrillic = "абвгдежзийклмнопрстуфхцчшщъыьэюя";
    private static readonly string[] Roman = ["a", "b", "v", "g", "d", "e", "zh", "z", "i", "y", "k", "l", "m", "n", "o", "p", "r", "s", "t", "u", "f", "kh", "ts", "ch", "sh", "shch", "", "y", "", "e", "yu", "ya"];
    private static long _retryAfterTicks;

    internal sealed record Line(double Seconds, string Text);
    internal sealed record Track(string Title, string Artist, string Source);
    internal sealed record Result(Line[] Lines, string? Plain)
    {
        internal static readonly Result Empty = new([], null);
    }

    internal static Line[] Parse(string text)
    {
        if (text.Length > MaxTextLength) throw new InvalidDataException();
        var offset = Offset.Match(text);
        double adjustment = offset.Success ? double.Parse(offset.Groups[1].Value, CultureInfo.InvariantCulture) / 1000 : 0;
        var lines = new List<Line>();
        using var reader = new StringReader(text);
        while (reader.ReadLine() is { } raw)
        {
            var timestamps = Timestamp.Matches(raw);
            if (timestamps.Count == 0) continue;
            string line = Timestamp.Replace(raw, "").Trim();
            foreach (Match stamp in timestamps)
            {
                double seconds = 60 * int.Parse(stamp.Groups[1].Value, CultureInfo.InvariantCulture)
                    + int.Parse(stamp.Groups[2].Value, CultureInfo.InvariantCulture);
                if (stamp.Groups[3].Success) seconds += double.Parse("0." + stamp.Groups[3].Value, CultureInfo.InvariantCulture);
                lines.Add(new Line(Math.Max(0, seconds + adjustment), line));
            }
        }
        return lines.OrderBy(line => line.Seconds).ToArray();
    }

    internal static string LineAt(Line[] lines, double seconds)
    {
        int index = IndexAt(lines, seconds);
        return index < 0 ? "" : lines[index].Text;
    }

    internal static int IndexAt(Line[] lines, double seconds)
    {
        int low = 0, high = lines.Length;
        while (low < high)
        {
            int middle = low + (high - low) / 2;
            if (lines[middle].Seconds <= seconds) low = middle + 1;
            else high = middle;
        }
        return low - 1;
    }

    internal static Uri LookupUri(Track track, string album, double duration)
    {
        string query = "track_name=" + Uri.EscapeDataString(track.Title) + "&artist_name=" + Uri.EscapeDataString(track.Artist);
        if (!string.IsNullOrWhiteSpace(album)) query += "&album_name=" + Uri.EscapeDataString(album);
        if (duration is >= 1 and <= 3600) query += "&duration=" + Math.Round(duration).ToString(CultureInfo.InvariantCulture);
        return new Uri("https://lrclib.net/api/get?" + query);
    }

    internal static async Task<Result> FindAsync(HttpClient client, Track track, string album, double duration, CancellationToken cancellation)
    {
        string artist = track.Artist.Trim();
        string title = track.Title.Trim();
        if (artist.Length == 0)
        {
            string[] parts = title.Split([" - ", " — ", " – "], 2, StringSplitOptions.TrimEntries);
            if (parts.Length == 2 && parts.All(part => part.Length > 0)) (artist, title) = (parts[0], parts[1]);
        }
        string cleaned = CleanTitle(title);
        if (cleaned.Length == 0) return Result.Empty;
        Result fallback = Result.Empty;
        if (artist.Length > 0)
        {
            using var exact = await RequestAsync(client, LookupUri(new(title, artist, ""), album, duration), cancellation).ConfigureAwait(false);
            if (exact?.RootElement.ValueKind == JsonValueKind.Object)
            {
                fallback = ReadResult(exact.RootElement, true);
                if (fallback.Lines.Length > 0) return fallback;
            }
        }

        string leadArtist = ArtistSeparator.Split(artist, 2)[0].Trim();
        string romanTitle = Transliterate(cleaned);
        string romanArtist = Transliterate(leadArtist);
        var searches = new[]
        {
            SearchUri(cleaned, leadArtist),
            SearchUri(romanTitle, romanArtist),
            SearchUri(cleaned, "")
        }.Distinct();
        foreach (var uri in searches)
        {
            try
            {
                await Task.Delay(250, cancellation).ConfigureAwait(false);
                using var search = await RequestAsync(client, uri, cancellation).ConfigureAwait(false);
                if (search?.RootElement.ValueKind != JsonValueKind.Array) continue;
                var match = SelectResult(search.RootElement, title, artist, album, duration);
                if (match.Lines.Length > 0) return match;
                if (fallback.Plain == null) fallback = match;
            }
            catch (HttpRequestException) when (fallback.Plain != null) { break; }
        }
        return fallback;
    }

    private static Uri SearchUri(string title, string artist) => new(
        "https://lrclib.net/api/search?track_name=" + Uri.EscapeDataString(title) +
        (artist.Length > 0 ? "&artist_name=" + Uri.EscapeDataString(artist) : ""));

    private static Result SelectResult(JsonElement results, string title, string artist, string album, double duration)
    {
        var titleKey = MatchKey(title);
        var cleanKey = MatchKey(CleanTitle(title));
        var artists = ArtistSeparator.Split(artist).Select(MatchKey).Where(key => key.Length > 0).ToHashSet();
        var albumKey = MatchKey(album);
        var candidates = new List<(JsonElement Entry, string Artist, double Delta, int Rank)>();
        foreach (var entry in results.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object) continue;
            string candidateTitle = ReadString(entry, "trackName") ?? ReadString(entry, "name") ?? "";
            string candidateArtist = ReadString(entry, "artistName") ?? "";
            string candidateKey = MatchKey(candidateTitle);
            if (candidateKey.Length == 0 || (candidateKey != titleKey && MatchKey(CleanTitle(candidateTitle)) != cleanKey)) continue;
            if (artists.Count > 0 && !ArtistSeparator.Split(candidateArtist).Select(MatchKey).Any(artists.Contains)) continue;
            double delta = duration >= 1 && entry.TryGetProperty("duration", out var length)
                && length.ValueKind == JsonValueKind.Number && length.TryGetDouble(out var seconds) && seconds > 0
                ? Math.Abs(duration - seconds) : double.PositiveInfinity;
            if (artists.Count == 0 && duration >= 1 && delta > 2) continue;
            int rank = (candidateKey == titleKey ? 2 : 0) + (albumKey.Length > 0 && MatchKey(ReadString(entry, "albumName") ?? "") == albumKey ? 1 : 0);
            candidates.Add((entry, MatchKey(candidateArtist), delta, rank));
        }
        if (artists.Count == 0 && candidates.Select(candidate => candidate.Artist).Distinct().Take(2).Count() > 1)
            return Result.Empty;

        Result fallback = Result.Empty;
        double tolerance = Math.Clamp(duration * .03, 5, 10);
        foreach (var candidate in candidates.OrderBy(candidate => candidate.Delta).ThenByDescending(candidate => candidate.Rank))
        {
            var result = ReadResult(candidate.Entry, duration < 1 || candidate.Delta <= tolerance);
            if (result.Lines.Length > 0) return result;
            if (fallback.Plain == null) fallback = result;
        }
        return fallback;
    }

    private static Result ReadResult(JsonElement entry, bool allowTiming)
    {
        var synced = ReadString(entry, "syncedLyrics");
        var lines = synced is { Length: > 0 and <= MaxTextLength } ? Parse(synced) : [];
        string? plain = ReadString(entry, "plainLyrics");
        if (string.IsNullOrWhiteSpace(plain)) plain = null;
        if (allowTiming && lines.Any(line => line.Text.Length > 0)) return new Result(lines, plain);
        if (plain == null && lines.Any(line => line.Text.Length > 0)) plain = string.Join('\n', lines.Select(line => line.Text));
        return new Result([], plain);
    }

    private static string? ReadString(JsonElement entry, string key) =>
        entry.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string CleanTitle(string title) => Edition.Replace(Edition.Replace(title, ""), "").Trim();

    private static string Transliterate(string value)
    {
        var result = new StringBuilder();
        foreach (char c in value.ToLowerInvariant().Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            int index = Cyrillic.IndexOf(c);
            if (index >= 0) result.Append(Roman[index]);
            else result.Append(c);
        }
        return result.ToString();
    }

    private static string MatchKey(string value) => new(Transliterate(value).Where(char.IsLetterOrDigit).ToArray());

    private static async Task<JsonDocument?> RequestAsync(HttpClient client, Uri uri, CancellationToken cancellation)
    {
        if (DateTime.UtcNow.Ticks < Interlocked.Read(ref _retryAfterTicks))
            throw new HttpRequestException("LRCLIB asked the client to wait before retrying.");
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.ParseAdd("OC-DynamicIsland/" + GitHubUpdateService.CurrentVersion.ToString(3));
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        if (response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable)
        {
            var now = DateTimeOffset.UtcNow;
            var retry = response.Headers.RetryAfter;
            var delay = retry?.Delta ?? (response.StatusCode == HttpStatusCode.TooManyRequests
                ? TimeSpan.FromSeconds(30) : TimeSpan.FromSeconds(1));
            Interlocked.Exchange(ref _retryAfterTicks, (retry?.Date ?? now.Add(delay)).UtcTicks);
        }
        response.EnsureSuccessStatusCode();
        using var stream = await response.Content.ReadAsStreamAsync(cancellation).ConfigureAwait(false);
        return JsonDocument.Parse(await ReadBoundedTextAsync(stream, 2_000_000, cancellation).ConfigureAwait(false));
    }

    internal static Task<string> ReadTextAsync(Stream stream, CancellationToken cancellation) =>
        ReadBoundedTextAsync(stream, MaxTextLength, cancellation);

    private static async Task<string> ReadBoundedTextAsync(Stream stream, int limit, CancellationToken cancellation)
    {
        using var reader = new StreamReader(stream, leaveOpen: true);
        var buffer = new char[4096];
        var text = new System.Text.StringBuilder();
        int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), cancellation).ConfigureAwait(false)) > 0)
        {
            if (text.Length + count > limit) throw new InvalidDataException();
            text.Append(buffer, 0, count);
        }
        return text.ToString();
    }
}
