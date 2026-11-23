using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace DynamicIslandWin.Services;

// ShowBanner is a per-user Shell setting, not a public notification API.
// Keep an undo journal before every change and touch no Enabled/history values.
internal sealed class NotificationBannerService : IDisposable
{
    internal const string SettingsRoot = @"Software\Microsoft\Windows\CurrentVersion\Notifications\Settings";
    internal static string JournalPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DynamicIslandWin", "notification-banners.json");
    private static readonly string[] KnownMessengers =
    ["exDesktop.exDesktop", "Telegram.Desktop", "com.squirrel.Discord.Discord",
     "TelegramMessengerLLP.TelegramDesktop_t4vj0pshhgkwm!Telegram.TelegramDesktop.Store"];
    private readonly string _root;
    private readonly string _path;
    private readonly string _owner = $"{Environment.ProcessId}:{Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks}";
    private bool _messengers, _all;
    private readonly Mutex _gate;

    internal sealed record Journal(string Owner, Dictionary<string, int?> Originals);

    public NotificationBannerService(string? root = null, string? journalPath = null)
    {
        _root = root ?? SettingsRoot;
        _path = journalPath ?? JournalPath;
        var digest = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(_path.ToUpperInvariant())));
        _gate = new Mutex(false, @"Local\DynamicIslandWin.Banners." + digest);
    }

    private Journal Read()
    {
        if (!File.Exists(_path)) return EmptyJournal();
        try
        {
            var stored = JsonSerializer.Deserialize<Journal>(File.ReadAllText(_path));
            if (stored?.Originals == null) throw new JsonException("Invalid banner journal");
            return stored with { Originals = new Dictionary<string, int?>(stored.Originals, StringComparer.OrdinalIgnoreCase) };
        }
        catch (JsonException)
        {
            // Preserve the damaged journal before recording any new originals.
            File.Move(_path, _path + ".corrupt-" + Guid.NewGuid().ToString("N"));
            return EmptyJournal();
        }

        static Journal EmptyJournal() => new("", new(StringComparer.OrdinalIgnoreCase));
    }

    private Journal ReadForCurrentOwner()
    {
        var journal = Read();
        if (journal.Owner == _owner) return journal;
        // Adopt a crash-recovery journal before the old watchdog can undo it.
        journal = journal with { Owner = _owner };
        Save(journal);
        return journal;
    }

    private void Save(Journal journal)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
        string temporary = _path + ".tmp";
        using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, journal);
            stream.Flush(flushToDisk: true);
        }
        File.Move(temporary, _path, overwrite: true);
    }

    private void Locked(Action action)
    {
        bool owned = false;
        try
        {
            try { owned = _gate.WaitOne(TimeSpan.FromSeconds(2)); }
            catch (AbandonedMutexException) { owned = true; }
            if (!owned) throw new TimeoutException("Banner state is busy");
            action();
        }
        finally { if (owned) _gate.ReleaseMutex(); }
    }

    public void Sync(bool messengers, bool all)
    {
        Locked(() =>
        {
            _messengers = messengers; _all = all;
            var journal = ReadForCurrentOwner();
            using var root = Registry.CurrentUser.OpenSubKey(_root);
            var ids = (root?.GetSubKeyNames() ?? []).Concat(KnownMessengers).Distinct(StringComparer.OrdinalIgnoreCase);
            var wanted = ids.Where(id => IsAppSource(id) && (all || (messengers && NotificationPolicy.IsMessenger(id, id))))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (string id in journal.Originals.Keys.Where(id => !wanted.Contains(id)).ToArray())
                RestoreOne(journal, id);
            foreach (string id in wanted) Suppress(journal, id);
        });
    }

    public void ObserveSource(string appName, string appId)
    {
        if (!IsAppSource(appId) || !(_all || (_messengers && NotificationPolicy.IsMessenger(appName, appId)))) return;
        Locked(() => Suppress(ReadForCurrentOwner(), appId));
    }

    private static bool IsAppSource(string id) => !string.IsNullOrWhiteSpace(id) && !id.Contains('\\') && !id.Contains('/')
        && !id.StartsWith("Windows.", StringComparison.OrdinalIgnoreCase)
        && !id.StartsWith("windows.immersivecontrolpanel", StringComparison.OrdinalIgnoreCase);

    private void Suppress(Journal journal, string id)
    {
        using var key = Registry.CurrentUser.CreateSubKey(_root + "\\" + id, writable: true);
        object? current = key.GetValue("ShowBanner");
        if (current is not null and not int) return;
        if (!journal.Originals.ContainsKey(id))
        {
            journal.Originals[id] = (int?)current;
            Save(journal); // Do not mute anything if the undo journal cannot be saved.
        }
        // Reapply even when the merged MSIX view still contains a value written
        // by an earlier virtualized package; Windows must see the real HKCU value.
        key.SetValue("ShowBanner", 0, RegistryValueKind.DWord);
        if (current is not int value || value != 0)
        {
            AppLog.Notification($"banner suppressed source={id}");
        }
    }

    private void RestoreOne(Journal journal, string id)
    {
        using var key = Registry.CurrentUser.OpenSubKey(_root + "\\" + id, writable: true);
        // Respect a later manual change to the Windows setting.
        if (key?.GetValue("ShowBanner") is int current && current == 0)
        {
            if (journal.Originals[id] is int original) key.SetValue("ShowBanner", original, RegistryValueKind.DWord);
            else key.DeleteValue("ShowBanner", throwOnMissingValue: false);
        }
        journal.Originals.Remove(id);
        Save(journal);
    }

    public void Restore(string? expectedOwner = null)
    {
        Locked(() =>
        {
            var journal = Read();
            if (expectedOwner != null && journal.Owner != expectedOwner) return;
            foreach (string id in journal.Originals.Keys.ToArray()) RestoreOne(journal, id);
        });
    }

    public void StartWatchdog()
    {
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
        start.ArgumentList.Add("--restore-banners-after-exit");
        start.ArgumentList.Add(Environment.ProcessId.ToString());
        start.ArgumentList.Add(_owner);
        Process.Start(start)?.Dispose();
    }

    public void Dispose()
    {
        try { Restore(_owner); }
        catch (Exception ex) { AppLog.Error("Restore Windows notification banners", ex); }
        _gate.Dispose();
    }
}
