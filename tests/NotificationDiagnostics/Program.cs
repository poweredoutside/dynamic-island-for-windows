using System.Text.Json;
using Windows.ApplicationModel;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;
using DynamicIslandWin.Services;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--notification-activation-broker")
        {
            NotificationActivationBroker.ServeAsync(args[1]).GetAwaiter().GetResult();
            return;
        }
        if (args.Length is < 1 or > 3) return;
        object report;
        try
        {
            var package = Package.Current.Id.FullName;
            if (args.Length == 3 && args[2] == "--messenger-probe")
            {
                string executable = NotificationActivationBroker.PrepareHost(Path.Combine(Package.Current.InstalledLocation.Path,
                    "Activation", "NotificationActivationHost.exe"));
                var probes = new List<object>();
                foreach (string id in new[] { "exDesktop.exDesktop", "com.squirrel.Discord.Discord",
                    "TelegramMessengerLLP.TelegramDesktop_t4vj0pshhgkwm!Telegram.TelegramDesktop.Store" })
                {
                    string? manifest = null;
                    try { manifest = Path.Combine(AppInfo.GetFromAppUserModelId(id).Package.InstalledLocation.Path, "AppxManifest.xml"); }
                    catch { }
                    var target = new NotificationActivationService.Target(id, "probe-only",
                        NotificationActivationService.ResolveActivator(id, manifest));
                    probes.Add(new { AppId = id, SourceRegistered = target.Activator != Guid.Empty,
                        ScopedCallbackAvailable = target.Activator != Guid.Empty &&
                            NotificationActivationBroker.RunAsync(target, probe: true, executable: executable).GetAwaiter().GetResult() });
                }
                File.WriteAllText(args[0], JsonSerializer.Serialize(new { Package = package, Sources = probes },
                    new JsonSerializerOptions { WriteIndented = true }));
                return;
            }
            if (args.Length == 3 && args[2] is "--broker-probe" or "--broker-source-probe")
            {
                var target = new NotificationActivationService.Target("exDesktop.exDesktop", "probe-only",
                    NotificationActivationService.ResolveActivator("exDesktop.exDesktop"));
                string executable = NotificationActivationBroker.PrepareHost(args[2] == "--broker-source-probe" ? args[1] :
                    Path.Combine(Package.Current.InstalledLocation.Path, "Activation", "NotificationActivationHost.exe"));
                var probeReport = new
                {
                    At = DateTimeOffset.Now, Package = package, SourceRegistered = target.Activator != Guid.Empty,
                    DirectCallbackAvailable = NotificationActivationService.ProbeCallbackAsync(target).GetAwaiter().GetResult(),
                    InstalledBrokerCallbackAvailable = NotificationActivationBroker.RunAsync(target, probe: true, executable: executable).GetAwaiter().GetResult()
                };
                File.WriteAllText(args[0], JsonSerializer.Serialize(probeReport, new JsonSerializerOptions { WriteIndented = true }));
                return;
            }
            var listener = UserNotificationListener.Current;
            var access = listener.GetAccessStatus();
            var items = new List<object>();
            var seen = new HashSet<string>();
            int watchSeconds = args.Length >= 2 ? Math.Clamp(int.Parse(args[1]), 0, 180) : 0;
            var until = DateTimeOffset.Now.AddSeconds(watchSeconds);
            int samples = 0;
            if (args.Length == 3 && args[2] is "--send-test" or "--send-burst" or "--send-banner-test")
            {
                int testCount = args[2] == "--send-burst" ? 3 : 1;
                for (int test = 1; test <= testCount; test++)
                {
                var xml = new Windows.Data.Xml.Dom.XmlDocument();
                xml.LoadXml($"<toast><visual><binding template=\"ToastGeneric\"><text>Dynamic Island</text><text>Проверка доставки уведомлений на островок ({test}/{testCount})</text></binding></visual></toast>");
                var toast = new ToastNotification(xml)
                {
                    Tag = "IslandCheck" + test, Group = "Diagnostics", SuppressPopup = args[2] != "--send-banner-test",
                    ExpirationTime = DateTimeOffset.Now.AddSeconds(30)
                };
                ToastNotificationManager.CreateToastNotifier().Show(toast);
                if (test < testCount) Thread.Sleep(400);
                }
            }
            do
            {
            if (access == UserNotificationListenerAccessStatus.Allowed)
            {
                var operation = listener.GetNotificationsAsync(NotificationKinds.Toast).AsTask();
                if (!operation.Wait(TimeSpan.FromSeconds(12))) throw new TimeoutException("Notification enumeration timed out.");
                foreach (var notification in operation.Result)
                {
                    if (!seen.Add($"{notification.Id}:{notification.CreationTime:O}")) continue;
                    try
                    {
                        bool? activationContext = null;
                        bool? nativeCallback = null;
                        bool? brokerCallback = null;
                        if (args.Length == 3 && args[2] is "--activation-check" or "--activation-installed-broker-check")
                        {
                            var binding = notification.Notification?.Visual?.GetBinding(KnownNotificationBindings.ToastGeneric)
                                ?? notification.Notification?.Visual?.Bindings.FirstOrDefault(b => b.GetTextElements().Count > 0);
                            string text = binding == null ? "" : string.Join(" · ", binding.GetTextElements()
                                .Select(t => t.Text).Where(t => !string.IsNullOrWhiteSpace(t)));
                            string? manifest = null;
                            try
                            {
                                string? path = notification.AppInfo?.Package?.InstalledLocation?.Path;
                                if (!string.IsNullOrEmpty(path)) manifest = Path.Combine(path, "AppxManifest.xml");
                            }
                            catch { }
                            var target = NotificationActivationService.CaptureAsync(notification.Id,
                                notification.AppInfo?.AppUserModelId ?? "", notification.CreationTime,
                                NotificationActivationService.Fingerprint(text), manifest).GetAwaiter().GetResult();
                            activationContext = target != null;
                            if (target != null && target.AppId == "exDesktop.exDesktop" &&
                                System.Diagnostics.Process.GetProcessesByName("Telegram").Length > 0)
                            {
                                nativeCallback = NotificationActivationService.ProbeCallbackAsync(target).GetAwaiter().GetResult();
                                string? brokerExe = args[2] == "--activation-installed-broker-check"
                                    ? NotificationActivationBroker.PrepareHost(Path.Combine(Package.Current.InstalledLocation.Path,
                                        "Activation", "NotificationActivationHost.exe")) : Environment.ProcessPath;
                                brokerCallback = NotificationActivationBroker.RunAsync(target, probe: true, executable: brokerExe).GetAwaiter().GetResult();
                            }
                        }
                        items.Add(new
                        {
                            notification.Id,
                            notification.CreationTime,
                            App = notification.AppInfo?.DisplayInfo?.DisplayName,
                            AppId = notification.AppInfo?.AppUserModelId,
                            ActivationContextAvailable = activationContext,
                            NativeCallbackAvailable = nativeCallback,
                            BrokerCallbackAvailable = brokerCallback,
                            Bindings = notification.Notification?.Visual?.Bindings.Select(binding => new
                            {
                                binding.Template,
                                TextElementCount = binding.GetTextElements().Count
                            }).ToArray()
                        });
                    }
                    catch (Exception ex) { items.Add(new { Error = ex.GetType().Name, ex.HResult }); }
                }
            }
            report = new { At = DateTimeOffset.Now, Package = package, Access = access.ToString(), Running = DateTimeOffset.Now < until, Samples = ++samples, Count = items.Count, Notifications = items };
            System.IO.File.WriteAllText(args[0], JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            if (DateTimeOffset.Now < until) Thread.Sleep(250);
            }
            while (DateTimeOffset.Now < until);
            report = new { At = DateTimeOffset.Now, Package = package, Access = access.ToString(), Running = false, Samples = samples, Count = items.Count, Notifications = items };
        }
        catch (Exception ex) { report = new { At = DateTimeOffset.Now, Error = ex.GetType().Name, ex.HResult, ex.Message }; }
        // Metadata only. The optional activation check reads contents in memory for matching;
        // text, sender/account IDs and activation arguments are never persisted.
        System.IO.File.WriteAllText(args[0], JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    }
}
