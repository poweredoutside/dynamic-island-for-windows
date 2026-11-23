using DynamicIslandWin.Services;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--activation-roundtrip")
        {
            bool passed = ActivationRoundtrip.Run(args[1]);
            Console.WriteLine(passed ? "PASS: real cross-process callback receives the exact source/account/chat/message arguments, once, without user input." : "FAIL: cross-process notification activation.");
            return passed ? 0 : 1;
        }
        if (args.Contains("--probe-exdesktop"))
        {
            var clsid = NotificationActivationService.ResolveActivator("exDesktop.exDesktop");
            var probe = new NotificationActivationService.Target("exDesktop.exDesktop", "", clsid);
            Console.WriteLine($"CallbackAvailable={NotificationActivationService.ProbeCallbackAsync(probe).GetAwaiter().GetResult()}");
            return 0;
        }
        int checks = 0;
        void Check(bool passed, string message)
        {
            if (!passed) throw new InvalidOperationException(message);
            checks++;
        }
        foreach (string name in new[] { "Telegram Desktop", "Discord", "Viber", "WhatsApp", "Microsoft Teams", "Unigram", "ВКонтакте" })
            Check(NotificationPolicy.IsMessenger(name, ""), $"Messenger not recognized: {name}");
        Check(NotificationPolicy.IsMessenger("Unknown", "org.telegram.desktop"), "Use app identity for classification");
        Check(!NotificationPolicy.IsMessenger("Steam", "Valve.Steam"), "Unrelated app incorrectly classified");
        (bool messenger, bool wantMessenger, bool all, bool dnd, bool expected)[] scenarios =
        [
            (false, false, false, false, false), (true, false, false, false, false),
            (false, true, false, false, false), (true, true, false, false, true),
            (false, false, true, false, true), (true, false, true, false, true),
            (false, true, true, false, true), (true, true, true, false, true),
            (false, false, false, true, false), (true, false, false, true, false),
            (false, true, false, true, false), (true, true, false, true, false),
            (false, false, true, true, false), (true, false, true, true, false),
            (false, true, true, true, false), (true, true, true, true, false)
        ];
        foreach (var scenario in scenarios)
        {
            Check(NotificationPolicy.ShouldDisplay(scenario.messenger, scenario.wantMessenger, scenario.all, scenario.dnd) ==
                scenario.expected, $"Filter scenario {scenario}");
        }
        var policy = new NotificationPolicy();
        var time = DateTimeOffset.UtcNow;
        Check(policy.Observe(1, time), "First observation");
        Check(!policy.Observe(1, time), "Duplicate notification");
        Check(policy.Observe(1, time.AddSeconds(1)), "Reused Windows ID");
        policy.Retain([]);
        Check(policy.Observe(1, time.AddSeconds(1)), "Removed ID must be forgotten");
        policy.Observe(2, time); // Muted notifications are observed, not replayed.
        Check(!policy.Observe(2, time), "DND backlog must not replay");
        Check(NotificationPolicy.IsMessenger("Telegram Desktop", "TelegramMessengerLLP.TelegramDesktop_t4vj0pshhgkwm!Telegram.TelegramDesktop.Store"), "Telegram Store identity");
        Check(policy.Observe(3, time, "first"), "Initial notification content");
        Check(!policy.Observe(3, time, "first"), "Identical content must not replay");
        Check(policy.Observe(3, time, "updated"), "Changed content under the same Windows ID must appear");
        Check(!policy.Observe(3, time, "updated"), "Changed content must appear only once");
        Check(NotificationPolicy.IsMessenger("exDesktop", ""), "exDesktop display name");
        Check(NotificationPolicy.IsMessenger("Unknown", @"C:\Users\test\AppData\Roaming\exDesktop\Telegram.exe"), "exDesktop source path");
        Check(NotificationPolicy.ClampDuration(1) == 3, "Minimum duration");
        Check(NotificationPolicy.ClampDuration(20) == 10, "Maximum duration");
        Check(NotificationPolicy.ClampDuration(double.NaN) == 5, "Invalid duration fallback");
        Check(NotificationPolicy.ClampDuration(7) == 7, "Selected duration preserved");
        Console.WriteLine($"PASS: {checks} notification policy checks.");

        int beforeActivationChecks = checks;
        string body = "Alice · Hello & goodbye";
        string hash = NotificationActivationService.Fingerprint(body);
        byte[] Payload(string attributes, string content = "Alice</text><text>Hello &amp; goodbye") =>
            System.Text.Encoding.UTF8.GetBytes("<toast " + attributes + "><visual><binding template=\"ToastGeneric\"><text>" +
                content + "</text></binding></visual><actions><action content=\"Reply\" arguments=\"action=reply\"/></actions></toast>");
        long arrival = time.ToFileTime();
        string argsToOpen = "action=open&session=123&peer=456&msg=789";
        string encodedArgs = "launch=\"action=open&amp;session=123&amp;peer=456&amp;msg=789\"";
        Check(NotificationActivationService.ParseArguments(Payload(encodedArgs), arrival, time, hash) == argsToOpen,
            "Forward exact root arguments including account/chat/message, never action buttons");
        Check(NotificationActivationService.ParseArguments(Payload(encodedArgs + " activationType=\"foreground\""), arrival, time, hash) == argsToOpen,
            "Explicit foreground activation");
        Check(NotificationActivationService.ParseArguments(Payload(encodedArgs), arrival, time.AddMinutes(1), hash) == null,
            "Reject reused notification ID with another creation time");
        Check(NotificationActivationService.ParseArguments(Payload(encodedArgs), arrival, time, NotificationActivationService.Fingerprint("Bob")) == null,
            "Reject updated notification content");
        foreach (string kind in new[] { "background", "protocol", "system" })
            Check(NotificationActivationService.ParseArguments(Payload(encodedArgs + " activationType=\"" + kind + "\""), arrival, time, hash) == null,
                "Do not reinterpret non-foreground activation");
        Check(NotificationActivationService.ParseArguments(Payload(""), arrival, time, hash) == null, "No fabricated launch context");
        Check(NotificationActivationService.ParseArguments(Payload("launch=\"\""), arrival, time, hash) == null, "Empty arguments fallback");
        string discordTag = "c948cbe2-7422-4538-8ee3-b2969062c701";
        Check(NotificationActivationService.ParseArguments(Payload(""), arrival, time, hash, "com.squirrel.Discord.Discord", discordTag)
            == "type=click&tag=" + discordTag, "Discord body click routes to exact native notification object");
        Check(NotificationActivationService.ParseArguments(Payload(""), arrival, time, hash, "Other.App", discordTag) == null,
            "Never synthesize Electron actions for an unrelated source");
        Check(NotificationActivationService.ParseArguments(Payload(""), arrival, time, hash, "com.squirrel.Discord.Discord", "tag&type=reply") == null,
            "Reject notification tag argument injection");
        Check(NotificationActivationService.ParseArguments(Payload(""), arrival, time, hash, "com.squirrel.Discord.Discord", null) == null,
            "Discord context requires a Windows notification tag");
        Check(NotificationActivationService.ParseArguments(Payload("activationType=\"protocol\""), arrival, time, hash, "com.squirrel.Discord.Discord", discordTag) == null,
            "Discord routing cannot reinterpret a protocol activation");
        Check(NotificationActivationService.ParseArguments(Payload(encodedArgs, "Mallory"), arrival, time, hash) == null, "Wrong sender body rejected");
        Check(NotificationActivationService.ParseArguments(new byte[131073], arrival, time, hash) == null, "Bound payload size");
        Check(NotificationActivationService.ParseArguments(System.Text.Encoding.UTF8.GetBytes("<!DOCTYPE toast [<!ENTITY x SYSTEM 'file:///C:/secret'>]><toast>&x;</toast>"), arrival, time, hash) == null,
            "No DTD or external entity resolution");
        Check(NotificationActivationService.ParseArguments(System.Text.Encoding.UTF8.GetBytes("<toast>"), arrival, time, hash) == null, "Malformed XML fallback");
        Check(NotificationActivationService.ParseArguments(Payload(encodedArgs), long.MaxValue, time, hash) == null, "Invalid timestamp fallback");
        Check(new NotificationActivationService.Target("App", argsToOpen, Guid.NewGuid()).ToString() == "Target", "Context omitted from diagnostic formatting");
        Check(NotificationActivationService.CaptureAsync(uint.MaxValue, "DynamicIsland.Tests.Missing", time, hash).GetAwaiter().GetResult() == null,
            "Exact ID and source required by real read-only store lookup");
        var activatorId = Guid.NewGuid();
        byte[] manifest = System.Text.Encoding.UTF8.GetBytes($"<Package xmlns='urn:test'><Applications><Application Id='Other'><Extensions><Extension Category='windows.toastNotificationActivation'><ToastNotificationActivation ToastActivatorCLSID='{Guid.NewGuid()}'/></Extension></Extensions></Application><Application Id='Chat'><Extensions><Extension Category='windows.toastNotificationActivation'><ToastNotificationActivation ToastActivatorCLSID='{activatorId}'/></Extension></Extensions></Application></Applications></Package>");
        Check(NotificationActivationService.ParseManifestActivator(manifest, "Family!Chat") == activatorId, "Packaged callback belongs to exact application");
        Check(NotificationActivationService.ParseManifestActivator(manifest, "Family!Missing") == Guid.Empty, "Other manifest application cannot substitute sender");
        Check(NotificationActivationService.ParseManifestActivator(manifest, "Unpackaged") == Guid.Empty, "Package lookup requires packaged identity");
        Check(NotificationActivationService.ParseManifestActivator(new byte[262145], "Family!Chat") == Guid.Empty, "Bound manifest size");
        Console.WriteLine($"PASS: {checks - beforeActivationChecks} notification activation checks.");

        string testId = Guid.NewGuid().ToString("N");
        string testRoot = @"Software\DynamicIslandWin\Tests\" + testId;
        string journalPath = Path.Combine(Path.GetTempPath(), "dynamic-island-banners-" + testId + ".json");
        int beforeBannerChecks = checks;
        try
        {
            using var telegram = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(testRoot + @"\Telegram.Test");
            using var discord = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(testRoot + @"\Discord.Test");
            using var other = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(testRoot + @"\Other.Test");
            discord.SetValue("ShowBanner", 1); other.SetValue("ShowBanner", 0);
            telegram.SetValue("Enabled", 1); telegram.SetValue("ShowInActionCenter", 1);
            using (var banners = new NotificationBannerService(testRoot, journalPath))
            {
                banners.Sync(true, false);
                Check((int?)telegram.GetValue("ShowBanner") == 0, "Messenger banners muted");
                Check((int?)discord.GetValue("ShowBanner") == 0, "Discord banners muted");
                Check((int?)other.GetValue("ShowBanner") == 0, "Other app left as configured");
                Check((int?)telegram.GetValue("Enabled") == 1 && (int?)telegram.GetValue("ShowInActionCenter") == 1, "Listener delivery and history stay enabled");
                var saved = System.Text.Json.JsonSerializer.Deserialize<NotificationBannerService.Journal>(File.ReadAllText(journalPath))!;
                Check(saved.Originals["Telegram.Test"] == null && saved.Originals["Discord.Test"] == 1, "Original values journaled before muting");
                File.SetLastWriteTimeUtc(journalPath, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
                var journalWrite = File.GetLastWriteTimeUtc(journalPath);
                banners.Sync(true, false);
                Check(File.GetLastWriteTimeUtc(journalPath) == journalWrite, "An unchanged banner policy does not write the journal");
                banners.Restore("another-owner");
                Check((int?)discord.GetValue("ShowBanner") == 0, "Old watchdog cannot restore a newer owner's settings");
                banners.Sync(false, true);
                Check((int?)other.GetValue("ShowBanner") == 0, "All-app filter includes existing muted app");
                banners.Sync(false, false);
                Check(telegram.GetValue("ShowBanner") == null, "Absent original value restored");
                Check((int?)discord.GetValue("ShowBanner") == 1, "Original banner value restored");
                Check((int?)other.GetValue("ShowBanner") == 0, "Originally muted app remains muted");
                banners.Sync(false, true);
                other.SetValue("ShowBanner", 1); // Simulate a newer user choice just before exit.
            }
            Check((int?)other.GetValue("ShowBanner") == 1, "Exit preserves a newer manual Windows choice");
            // Simulate crash recovery by a fresh service instance using the saved journal.
            using (var crashed = new NotificationBannerService(testRoot, journalPath))
            {
                crashed.Sync(true, false);
                using var recovery = new NotificationBannerService(testRoot, journalPath);
                recovery.Restore();
                Check((int?)discord.GetValue("ShowBanner") == 1, "Fresh process recovers original values from disk");
            }
            File.WriteAllBytes(journalPath, new byte[64]);
            using (var repaired = new NotificationBannerService(testRoot, journalPath))
            {
                repaired.Sync(false, false);
                Check(File.Exists(journalPath), "Corrupted banner journal is replaced with a valid journal");
                Check(Directory.GetFiles(Path.GetDirectoryName(journalPath)!, Path.GetFileName(journalPath) + ".corrupt-*").Length == 1,
                    "Corrupted banner journal is preserved for diagnostics");
            }
            Console.WriteLine($"PASS: {checks - beforeBannerChecks} isolated registry/banner recovery checks.");
        }
        finally
        {
            Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(testRoot, throwOnMissingSubKey: false);
            if (File.Exists(journalPath)) File.Delete(journalPath);
            foreach (string damaged in Directory.GetFiles(Path.GetDirectoryName(journalPath)!, Path.GetFileName(journalPath) + ".corrupt-*"))
                File.Delete(damaged);
        }

        if (args.Contains("--read-dnd") || args.Contains("--toggle-dnd"))
        {
            using var service = new WindowsDndService();
            if (!service.TryRead(out var original)) throw new Exception("Cannot read Windows DND via Shell COM.");
            Console.WriteLine($"Windows DND: selected={original.SelectedProfile}, active={original.ActiveProfile}");
            if (args.Contains("--toggle-dnd"))
            {
                try
                {
                    foreach (string profile in new[] { WindowsDndService.Priority, WindowsDndService.Alarms, WindowsDndService.Off })
                    {
                        if (!service.TrySetProfile(profile, endAutomaticRule: false, out var error)) throw new Exception(error);
                        bool applied = false;
                        for (int i = 0; i < 20; i++)
                        {
                            Thread.Sleep(100);
                            if (service.TryRead(out var state) && state.SelectedProfile == profile)
                            {
                                Console.WriteLine($"PASS: Windows selected={state.SelectedProfile}, effective={state.ActiveProfile}");
                                applied = true;
                                break;
                            }
                        }
                        if (!applied) throw new Exception($"Windows did not apply {profile}");
                    }
                }
                finally
                {
                    if (!service.TrySetProfile(original.SelectedProfile, false, out var error)) throw new Exception($"Restore failed: {error}");
                    Console.WriteLine($"Restored original Windows profile: {original.SelectedProfile}");
                }
            }
        }
        return 0;
    }
}
