namespace DynamicIslandWin.Services;

internal sealed class NotificationPolicy
{
    private readonly Dictionary<uint, (DateTimeOffset Created, string Fingerprint)> _seen = new();
    private static readonly string[] MessengerNames =
    [
        "telegram", "exdesktop", "ayugram", "64gram", "kotatogram", "unigram", "discord", "vesktop",
        "vencord", "viber", "whatsapp", "skype", "signal", "slack", "teams", "icq", "vk", "вконтакте"
    ];

    public static bool IsMessenger(string name, string appId)
    {
        // Match tokens/prefixes, not arbitrary substrings (e.g. 'Teams' vs 'Steam').
        string[] tokens = System.Text.RegularExpressions.Regex.Split($"{name} {appId}".ToLowerInvariant(), @"[^\p{L}\p{N}]+");
        return tokens.Any(token => MessengerNames.Any(m => token.StartsWith(m, StringComparison.Ordinal)));
    }

    public static bool ShouldDisplay(bool messenger, bool wantMessengers, bool wantAllApps, bool suppressed)
        => !suppressed && (wantAllApps || (wantMessengers && messenger));

    public static int ClampDuration(double seconds)
        => double.IsFinite(seconds) ? (int)Math.Clamp(Math.Round(seconds), 3, 10) : 5;

    public bool Observe(uint id, DateTimeOffset created, string fingerprint = "")
    {
        var current = (created, fingerprint);
        bool fresh = !_seen.TryGetValue(id, out var previous) || previous != current;
        _seen[id] = current;
        return fresh;
    }

    public void Retain(IEnumerable<uint> currentIds)
    {
        var current = currentIds.ToHashSet();
        foreach (uint id in _seen.Keys.Where(id => !current.Contains(id)).ToArray()) _seen.Remove(id);
    }

    public void Clear() => _seen.Clear();
}
