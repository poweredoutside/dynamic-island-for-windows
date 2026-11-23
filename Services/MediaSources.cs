namespace DynamicIslandWin.Services;

internal static class MediaSources
{
    private sealed record Browser(string Process, string Label, string[] ExactIds, string[] IdPrefixes);

    private static readonly Browser[] Browsers =
    [
        new("chrome", "Google Chrome", ["Chrome", "Google Chrome"], ["Chrome.", "Google.Chrome."]),
        new("msedge", "Microsoft Edge", ["MSEdge", "Microsoft Edge", "Microsoft.MicrosoftEdge"], ["MSEdge.", "Microsoft.MicrosoftEdge_"]),
        new("firefox", "Mozilla Firefox", ["Firefox", "Mozilla Firefox"], ["Firefox-", "Firefox.", "Mozilla.Firefox_"]),
        new("brave", "Brave", ["Brave", "Brave Browser"], ["Brave.", "BraveSoftware.Brave", "BraveSoftware Brave-Browser"]),
        new("vivaldi", "Vivaldi", ["Vivaldi", "Vivaldi Browser"], ["Vivaldi."]),
        new("opera", "Opera", ["Opera", "Opera Browser", "OperaStable", "Opera GX", "OperaGXStable"], ["Opera.", "Opera Stable.", "Opera GX Stable.", "OperaSoftware."]),
        new("browser", "Yandex Browser", ["Browser", "Yandex Browser", "YandexBrowser", "Yandex.Browser", "Yandex.YandexBrowser"], ["YandexBrowser.", "Yandex.Browser."]),
        new("arc", "Arc", ["Arc"], ["TheBrowserCompany.Arc_"]),
        new("waterfox", "Waterfox", ["Waterfox"], ["Waterfox-", "Waterfox."]),
        new("librewolf", "LibreWolf", ["LibreWolf"], ["LibreWolf-", "LibreWolf."])
    ];

    internal static string? BrowserName(string source)
    {
        if (System.IO.Path.IsPathFullyQualified(source)) source = System.IO.Path.GetFileName(source);
        return FindBrowser(WithoutExe(source))?.Label;
    }

    internal static bool MatchesDesktopPlayer(string source, string processName)
    {
        string id = WithoutExe(source);
        if (id.Equals(processName, StringComparison.OrdinalIgnoreCase)) return true;
        if (FindBrowser(id) is { } browser) return browser.Process.Equals(processName, StringComparison.OrdinalIgnoreCase);
        return processName.ToLowerInvariant() switch
        {
            "yandexmusic" or "яндекс музыка" => id.Equals("com.yandex.music", StringComparison.OrdinalIgnoreCase)
                || id.Equals("ru.yandex.desktop.music", StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    private static Browser? FindBrowser(string id) => Browsers.FirstOrDefault(browser =>
        browser.ExactIds.Contains(id, StringComparer.OrdinalIgnoreCase)
        || browser.IdPrefixes.Any(prefix => id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)));

    private static string WithoutExe(string source) => source.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? source[..^4] : source;
}
