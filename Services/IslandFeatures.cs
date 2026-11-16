using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Ical.Net;
using Ical.Net.DataTypes;

namespace DynamicIslandWin.Services;

public sealed record UserTheme(string Name, string Island, string Scene, string Accent, bool Light);

public static class ThemeCode
{
    private static readonly Regex Hex = new("^#[0-9a-fA-F]{6}$", RegexOptions.CultureInvariant);
    public static bool Valid(UserTheme? theme) => theme != null && !string.IsNullOrWhiteSpace(theme.Name)
        && theme.Name.Length <= 40 && Hex.IsMatch(theme.Island ?? "") && Hex.IsMatch(theme.Scene ?? "") && Hex.IsMatch(theme.Accent ?? "");
    public static string Export(UserTheme theme) => Valid(theme)
        ? "DI1:" + Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(theme)))
        : throw new FormatException("Проверьте название и цвета #RRGGBB.");
    public static UserTheme Import(string code)
    {
        if (code.Length > 2048 || !code.StartsWith("DI1:", StringComparison.Ordinal)) throw new FormatException("Неизвестный код темы.");
        try
        {
            var theme = JsonSerializer.Deserialize<UserTheme>(Convert.FromBase64String(code[4..]));
            return Valid(theme) ? theme! : throw new FormatException("Недопустимые цвета темы.");
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException) { throw new FormatException("Повреждённый код темы."); }
    }
}

public sealed record ScheduledTimer(Guid Id, string Label, DateTimeOffset Due);
public sealed record CalendarMeeting(string Id, string Title, DateTimeOffset Start, DateTimeOffset End, Uri? Join);

public static class CalendarReader
{
    private static readonly Regex Url = new(@"https?://[^\s<>""\\]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly string[] CallHosts = ["zoom.us", "zoom.com", "meet.google.com", "teams.microsoft.com", "teams.live.com", "telemost.yandex.ru", "telemost.yandex.com", "webex.com", "meet.jit.si"];
    public static Uri? FindCall(string value)
    {
        foreach (Match match in Url.Matches(value))
            if (Uri.TryCreate(match.Value.TrimEnd('.', ',', ')', ';'), UriKind.Absolute, out var uri)
                && string.IsNullOrEmpty(uri.UserInfo) && CallHosts.Any(h => uri.IdnHost.Equals(h, StringComparison.OrdinalIgnoreCase)
                    || uri.IdnHost.EndsWith("." + h, StringComparison.OrdinalIgnoreCase))) return uri;
        return null;
    }

    public static IReadOnlyList<CalendarMeeting> Read(string ics, DateTimeOffset now)
    {
        if (ics.Length > 4_000_000) throw new FormatException("Календарь больше 4 МБ.");
        var calendar = Ical.Net.Calendar.Load(ics) ?? throw new FormatException("Нет календаря iCalendar.");
        if (calendar.Events.Count > 5000 || Regex.IsMatch(ics, @"(?im)^RRULE:[^\r\n]*\bFREQ=(?:SECONDLY|MINUTELY)\b"))
            throw new FormatException("Календарь содержит слишком частые повторения. Импортируйте календарь встреч без поминутных повторов.");
        var begin = new CalDateTime(now.AddDays(-1).UtcDateTime);
        var end = new CalDateTime(now.AddDays(31).UtcDateTime);
        // A bounded recurrence expansion also handles EXDATE and changed instances.
        return calendar.GetOccurrences(begin, end).Take(2000).Select(o =>
        {
            var ev = (Ical.Net.CalendarComponents.CalendarEvent)o.Source;
            var start = new DateTimeOffset(DateTime.SpecifyKind(o.Period.StartTime.AsUtc, DateTimeKind.Utc));
            var finish = o.Period.EndTime == null ? start.AddHours(1)
                : new DateTimeOffset(DateTime.SpecifyKind(o.Period.EndTime.AsUtc, DateTimeKind.Utc));
            return new CalendarMeeting((ev.Uid ?? "") + ":" + start.ToUnixTimeSeconds(), ev.Summary ?? "Событие", start, finish,
                FindCall($"{ev.Url} {ev.Location} {ev.Description}"));
        }).Where(m => m.End >= now).OrderBy(m => m.Start).ToArray();
    }
}

public sealed record LyricLine(double Seconds, string Text);
public static class LyricsReader
{
    private static readonly Regex Timestamp = new(@"\[(\d{1,3}):(\d{2})(?:[.:](\d{1,3}))?\]", RegexOptions.CultureInvariant);
    public static IReadOnlyList<LyricLine> Parse(string lrc)
    {
        if (lrc.Length > 500_000) throw new FormatException("Текст слишком большой.");
        double offset = 0;
        var offsetMatch = Regex.Match(lrc, @"\[offset:([+-]?\d+)\]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (offsetMatch.Success) offset = double.Parse(offsetMatch.Groups[1].Value, CultureInfo.InvariantCulture) / 1000;
        var result = new List<LyricLine>();
        foreach (string line in lrc.Split('\n'))
        {
            var matches = Timestamp.Matches(line);
            if (matches.Count == 0) continue;
            string text = Timestamp.Replace(line, "").Trim();
            foreach (Match m in matches)
            {
                int seconds = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
                if (seconds >= 60) continue;
                double fraction = m.Groups[3].Success ? double.Parse("0." + m.Groups[3].Value, CultureInfo.InvariantCulture) : 0;
                result.Add(new LyricLine(Math.Max(0, int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) * 60 + seconds + fraction + offset), text));
            }
        }
        return result.OrderBy(l => l.Seconds).ToArray();
    }
    public static int At(IReadOnlyList<LyricLine> lines, double seconds)
    {
        int low = 0, high = lines.Count - 1, found = -1;
        while (low <= high) { int mid = (low + high) / 2; if (lines[mid].Seconds <= seconds) { found = mid; low = mid + 1; } else high = mid - 1; }
        return found;
    }
}

public static class WindowSignals
{
    private static readonly Regex Percentage = new(@"(?<!\d)(100|[1-9]?\d)(?:[.,]\d+)?\s*%", RegexOptions.CultureInvariant);
    private static readonly Regex Unread = new(@"(?:^[\s\p{Cf}]*\((\d{1,6})\)|\[(\d{1,6})\]|\b(\d{1,6})\s+(?:unread|непрочитан)|\((\d{1,6})\)[\s\p{Cf}]*$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    public static int? Progress(string title) => Percentage.Match(title) is { Success: true } m ? int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : null;
    public static int UnreadCount(string title)
    {
        var match = Unread.Match(title);
        return !match.Success ? 0 : int.Parse(match.Groups.Cast<Group>().Skip(1).First(g => g.Success).Value, CultureInfo.InvariantCulture);
    }
}
