namespace DynamicIslandWin.Services;

internal readonly record struct ForegroundIslandContext(string? Tab, string? BrowserProcess = null)
{
    internal static ForegroundIslandContext ForWindow(string processName, string windowClass)
    {
        string process = processName.ToLowerInvariant();
        if (MediaSources.BrowserName(process) != null) return new("clipboard", process);
        if ((windowClass is "GLFW30" or "LWJGL") && (process is "java" or "javaw")) return new("system");

        return process switch
        {
            "spotify" or "yandexmusic" or "яндекс музыка" or "vlc" or "mpc-hc" or "mpc-hc64" or "mpv"
                or "wmplayer" or "microsoft.media.player" or "music.ui" or "applemusic" or "itunes" or "aimp"
                => new("music"),
            "winword" or "excel" or "powerpnt" or "onenote" or "notepad" or "notepad++" or "code"
                or "code - insiders" or "cursor" or "devenv" or "idea64" or "pycharm64" or "rider64"
                or "sublime_text" or "obsidian" or "libreoffice" or "soffice.bin" => new("notes"),
            "zoom" or "teams" or "ms-teams" or "outlook" or "olk" => new("calendar"),
            "robloxplayerbeta" or "minecraft.windows" or "fortniteclient-win64-shipping" or "cs2"
                or "valorant-win64-shipping" or "league of legends" or "dota2" or "overwatch" => new("system"),
            "explorer" when windowClass is "CabinetWClass" or "ExploreWClass" => new("clipboard"),
            _ when NotificationPolicy.IsMessenger(process, "") => new("clipboard"),
            _ => default
        };
    }

    internal string? GetTab(string? playingSource) =>
        BrowserProcess != null && playingSource is { Length: > 0 } source &&
        MediaSources.MatchesDesktopPlayer(source, BrowserProcess) ? "music" : Tab;
}
