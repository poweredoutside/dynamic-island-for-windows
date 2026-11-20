using System.Globalization;
using System.Text.RegularExpressions;

namespace DynamicIslandWin.Services;

internal static class ReminderTime
{
    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
    private static readonly Regex Relative = new(@"^(?:через\s+|in\s+)?([0-9]{1,5})\s*(?:мин|хв|min|m)\.?$", Options);
    private static readonly Regex Clock = new(@"^(?:(завтра|tomorrow|morgen|mañana)\s+|([0-9]{1,2})\.([0-9]{1,2})\s+)?([0-9]{1,2}):([0-9]{2})$", Options);

    internal static DateTimeOffset? Parse(string text, DateTimeOffset now)
    {
        text = text.Trim();
        var relative = Relative.Match(text);
        if (relative.Success)
        {
            int minutes = Number(relative.Groups[1]);
            return minutes is > 0 and <= 43200 ? now.AddMinutes(minutes) : null;
        }

        var match = Clock.Match(text);
        if (!match.Success) return null;
        int hour = Number(match.Groups[4]);
        int minute = Number(match.Groups[5]);
        if (hour > 23 || minute > 59) return null;
        var localNow = now.ToLocalTime();
        var candidate = localNow.Date.AddHours(hour).AddMinutes(minute);
        if (match.Groups[2].Success)
        {
            int day = Number(match.Groups[2]);
            int month = Number(match.Groups[3]);
            if (day < 1 || month is < 1 or > 12) return null;
            for (int year = localNow.Year; year <= Math.Min(9999, localNow.Year + 8); year++)
            {
                if (day > DateTime.DaysInMonth(year, month)) continue;
                candidate = new DateTime(year, month, day, hour, minute, 0);
                if (candidate > localNow.DateTime) return Local(candidate);
            }
            return null;
        }
        if (match.Groups[1].Success || candidate <= localNow.DateTime) candidate = candidate.AddDays(1);
        return Local(candidate);
    }

    private static DateTimeOffset? Local(DateTime time) =>
        TimeZoneInfo.Local.IsInvalidTime(time) ? null : new DateTimeOffset(time);

    private static int Number(Group group) => int.Parse(group.Value, CultureInfo.InvariantCulture);
}
