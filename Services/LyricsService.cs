using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DynamicIslandWin.Services;

internal static class LyricsService
{
    private const int MaxTextLength = 500_000;
    private static readonly Regex Timestamp = new(@"\[(\d{1,3}):([0-5]\d)(?:[.:](\d{1,3}))?\]", RegexOptions.CultureInvariant);
    private static readonly Regex Offset = new(@"^\s*\[offset:([+-]?\d{1,9})\]\s*$", RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant);

    internal sealed record Line(double Seconds, string Text);
    internal sealed record Track(string Title, string Artist, string Source);

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
        int low = 0, high = lines.Length;
        while (low < high)
        {
            int middle = low + (high - low) / 2;
            if (lines[middle].Seconds <= seconds) low = middle + 1;
            else high = middle;
        }
        return low == 0 ? "" : lines[low - 1].Text;
    }

    internal static Uri LookupUri(Track track, string album, double duration)
    {
        string query = "track_name=" + Uri.EscapeDataString(track.Title) + "&artist_name=" + Uri.EscapeDataString(track.Artist);
        if (!string.IsNullOrWhiteSpace(album)) query += "&album_name=" + Uri.EscapeDataString(album);
        if (duration is >= 1 and <= 3600) query += "&duration=" + Math.Round(duration).ToString(CultureInfo.InvariantCulture);
        return new Uri("https://lrclib.net/api/get?" + query);
    }

    internal static async Task<Line[]> FindAsync(HttpClient client, Track track, string album, double duration, CancellationToken cancellation)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, LookupUri(track, album, duration));
        request.Headers.UserAgent.ParseAdd("OC-DynamicIsland/" + GitHubUpdateService.CurrentVersion.ToString(3));
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound) return [];
        response.EnsureSuccessStatusCode();
        using var stream = await response.Content.ReadAsStreamAsync(cancellation).ConfigureAwait(false);
        using var document = JsonDocument.Parse(await ReadTextAsync(stream, cancellation).ConfigureAwait(false));
        return document.RootElement.TryGetProperty("syncedLyrics", out var lyrics) && lyrics.ValueKind == JsonValueKind.String
            ? Parse(lyrics.GetString()!) : [];
    }

    internal static async Task<string> ReadTextAsync(Stream stream, CancellationToken cancellation)
    {
        using var reader = new StreamReader(stream, leaveOpen: true);
        var buffer = new char[4096];
        var text = new System.Text.StringBuilder();
        int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), cancellation).ConfigureAwait(false)) > 0)
        {
            if (text.Length + count > MaxTextLength) throw new InvalidDataException();
            text.Append(buffer, 0, count);
        }
        return text.ToString();
    }
}
