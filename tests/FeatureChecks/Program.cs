using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DynamicIslandWin;
using DynamicIslandWin.Services;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        int checks = 0;
        void Check(bool result, string reason) { if (!result) throw new InvalidOperationException(reason); checks++; }
        if (args.Contains("--volume-events")) return CheckVolumeEvents();
        if (args.Contains("--verify-release")) return CheckPublishedRelease();
        var keys = new VolumeKeyRouter();
        var commands = new List<int>();
        bool Queue(int key) { commands.Add(key); return true; }
        Check(!keys.Handle(0x41, true, false, true, true, Queue) && commands.Count == 0, "Ordinary keys never enter the volume queue");
        Check(!keys.Handle(0xAF, true, false, false, true, Queue), "Disabled mode passes volume to Windows");
        Check(!keys.Handle(0xAF, true, false, true, false, Queue), "Missing endpoint fails open");
        Check(!keys.Handle(0xAF, true, false, true, true, _ => false), "Busy worker fails open without swallowing input");
        Check(keys.Handle(0xAF, true, false, true, true, Queue) && keys.Handle(0xAF, true, false, true, true, Queue) && commands.Count == 2, "Held volume keys preserve autorepeat");
        Check(keys.Handle(0xAF, false, true, false, false, Queue), "Matched release is consumed after disabling");
        Check(!keys.Handle(0xAF, false, true, true, true, Queue), "Unmatched release passes through");
        Check(keys.Handle(0xAD, true, false, true, true, Queue) && commands.Last() == 0xAD, "Mute uses the same independent path");
        Check(keys.Handle(0xAE, true, false, true, true, Queue) && commands.Last() == 0xAE, "Volume down uses the same independent path");
        var theme = new UserTheme("Морская волна 🌊", "#123456", "#234567", "#AbCdEf", false);
        Check(ThemeCode.Import(ThemeCode.Export(theme)) == theme, "Theme travels without loss of Unicode or colors");
        foreach (string bad in new[] { "", "DI2:AAAA", "DI1:***", "DI1:" + Convert.ToBase64String("{}"u8.ToArray()), new string('a', 3000) })
        { try { ThemeCode.Import(bad); throw new Exception("Invalid theme accepted"); } catch (FormatException) { checks++; } }
        Check(!ThemeCode.Valid(theme with { Accent = "red" }), "Imported colors must be explicit RGB");
        string calendar = "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//Island tests//EN\r\nBEGIN:VEVENT\r\nUID:test-recurring\r\nDTSTART;TZID=Europe/Moscow:20261115T120000\r\nDTEND;TZID=Europe/Moscow:20261115T123000\r\nRRULE:FREQ=DAILY;COUNT=4\r\nEXDATE;TZID=Europe/Moscow:20261116T120000\r\nSUMMARY:Test meeting\r\nDESCRIPTION:Join https://meet.google.com/abc-defg-hij\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n";
        var meetings = CalendarReader.Read(calendar, DateTimeOffset.Parse("2026-11-15T08:00:00Z"));
        Check(meetings.Count == 3, "Calendar recurrence excludes EXDATE");
        Check(meetings[0].Start == DateTimeOffset.Parse("2026-11-15T09:00:00Z"), "Calendar honors Moscow timezone");
        Check(meetings[0].Join?.Host == "meet.google.com", "Meeting link extracted");
        Check(CalendarReader.FindCall("https://zoom.us.attacker.example/j/123") == null, "Call URL lookalike rejected");
        Check(CalendarReader.FindCall("https://name@zoom.us/j/123") == null, "Call URL userinfo rejected");
        Check(CalendarReader.FindCall("https://us02web.zoom.us/j/123") != null, "Legitimate Zoom regional host accepted");
        Check(CalendarReader.Read(calendar, DateTimeOffset.Parse("2026-12-30T00:00:00Z")).Count == 0, "Expired meetings omitted");
        var lyrics = LyricsReader.Parse("[offset:-500]\n[00:01.00][00:03.500]Первая строка\n[00:05.20]Вторая\n[00:99.10]bad");
        Check(lyrics.Count == 3 && Math.Abs(lyrics[0].Seconds - .5) < .001, "LRC handles multiple timestamps and offset");
        Check(LyricsReader.At(lyrics, .4) == -1 && LyricsReader.At(lyrics, .5) == 0 && LyricsReader.At(lyrics, 4) == 1, "Lyrics seek boundaries");
        Check(WindowSignals.Progress("Copying (42%)") == 42 && WindowSignals.Progress("archive — 100 %") == 100, "Window job progress");
        Check(WindowSignals.Progress("101%") == null && WindowSignals.Progress("no progress") == null, "Invalid job percentage rejected");
        Check(WindowSignals.UnreadCount("(12) Telegram") == 12 && WindowSignals.UnreadCount("exDesktop [3]") == 3 && WindowSignals.UnreadCount("Telegram 2026") == 0, "Unread counter avoids ordinary numbers");
        Check(WindowSignals.UnreadCount("\u200eTelegram – (42)") == 42 && WindowSignals.UnreadCount("\u200e(7) Telegram") == 7, "Telegram trailing counts and direction markers supported");
        foreach (string bad in new[] { "cmd.exe", "file:///C:/Windows", "javascript:alert(1)", "https://localhost/", "https://127.0.0.1/", "https://user:secret@example.com/", "steam://install/123" })
            Check(VoiceLauncher.Resolve(bad) == null, "Voice launch rejects shell, local, credentialed or unknown protocol targets");
        Check(VoiceLauncher.Resolve("telegram") == "tg://" && VoiceLauncher.Resolve("https://example.com/path") != null, "Voice opens supported targets");
        Check(FpsService.IsPresent(new("CA11C036-0102-4A2D-A6AD-F03CFED5D3C9"), 42) && !FpsService.IsPresent(new("CA11C036-0102-4A2D-A6AD-F03CFED5D3C9"), 43), "FPS counts starts, not both start and stop");
        Check(GitHubUpdateService.Repository("https://github.com/example/island/") == "example/island", "Update repository normalization");
        string releaseJson = "{\"tag_name\":\"v1.2.0\",\"name\":\"1.2\",\"assets\":[{\"name\":\"Setup.exe\",\"browser_download_url\":\"https://github.com/example/island/releases/download/v1.2.0/Setup.exe\"},{\"name\":\"SHA256SUMS.txt\",\"browser_download_url\":\"https://github.com/example/island/releases/download/v1.2.0/SHA256SUMS.txt\"}]}";
        Check(GitHubUpdateService.Parse("example/island", releaseJson).Version == new Version(1, 2, 0), "Update resolves exact same-repository installer");
        string brandedRelease = releaseJson.Replace("Setup.exe", "ocdynamicisland.exe");
        Check(GitHubUpdateService.Parse("example/island", brandedRelease).InstallerName == "ocdynamicisland.exe", "Branded installer is supported");
        string bothInstallers = releaseJson.Replace("\"assets\":[", "\"assets\":[{\"name\":\"ocdynamicisland.exe\",\"browser_download_url\":\"https://github.com/example/island/releases/download/v1.2.0/ocdynamicisland.exe\"},");
        Check(GitHubUpdateService.Parse("example/island", bothInstallers).InstallerName == "ocdynamicisland.exe", "Branded installer takes priority over legacy Setup.exe");
        try { GitHubUpdateService.Parse("example/island", brandedRelease.Replace("https://github.com/example/island/releases/download/v1.2.0/ocdynamicisland.exe", "https://attacker.example/ocdynamicisland.exe")); throw new Exception("External installer accepted"); }
        catch (FormatException) { checks++; }
        foreach (string bad in new[] { releaseJson.Replace("SHA256SUMS.txt", "other.txt"), releaseJson.Replace("https://github.com/example/island/releases/download/v1.2.0/Setup.exe", "https://attacker.example/Setup.exe") })
        { try { GitHubUpdateService.Parse("example/island", bad); throw new Exception("Unsafe release accepted"); } catch (FormatException) { checks++; } }
        string digest = new string('a', 64);
        Check(GitHubUpdateService.Digest(digest + "  Setup.exe", "Setup.exe") == digest, "Mandatory exact-file checksum read");
        foreach (string bad in new[] { "", digest + "  another.exe", digest + "  Setup.exe\n" + digest + "  Setup.exe" })
        { try { GitHubUpdateService.Digest(bad, "Setup.exe"); throw new Exception("Missing or ambiguous digest accepted"); } catch (FormatException) { checks++; } }
        var migrated = JsonSerializer.Deserialize<MainWindow.AppSettings>("{\"NotificationDurationSeconds\":7,\"SelectedTheme\":\"Ocean\"}")!;
        Check(migrated.CompactWidth == 210 && migrated.NotificationDurationSeconds == 7 && migrated.SelectedTheme == "Ocean" && !migrated.FollowCursorMonitor, "Settings migration preserves values and safe defaults");
        Check(migrated.UpdateRepository == "poweredoutside/dynamic-island-for-windows", "New and absent settings use the production update repository");
        var due = DateTimeOffset.Now.AddMinutes(12);
        migrated.Timers.Add(new(Guid.NewGuid(), "Test", due));
        Check(JsonSerializer.Deserialize<MainWindow.AppSettings>(JsonSerializer.Serialize(migrated))!.Timers.Single().Due == due, "Timer deadlines survive serialization");

        using (var audio = new DesktopAudioService())
        {
            try { var master = audio.Master(); var apps = audio.Apps(); Check(master.Volume is >= 0 and <= 1 && apps.All(a => a.Volume is >= 0 and <= 1), "Read-only CoreAudio runtime probe"); Console.WriteLine($"Audio available; session count={apps.Count}"); }
            catch (System.Runtime.InteropServices.COMException) { Console.WriteLine("Audio device unavailable; UI handles this case."); }
        }
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var window = new MainWindow();
        Check(window.Title == "OC - Dynamic Island", "Product window uses the new display name");
        Check(typeof(MainWindow).Assembly.GetCustomAttribute<AssemblyProductAttribute>()?.Product == "OC - Dynamic Island", "Windows file metadata uses the new product name");
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        object? Call(string name, params object?[] values) => typeof(MainWindow).GetMethod(name, flags)!.Invoke(window, values);
        T Element<T>(string name) => (T)window.FindName(name);
        var settings = (MainWindow.AppSettings)typeof(MainWindow).GetField("_settings", flags)!.GetValue(window)!;
        settings.UpdateRepository = ""; Call("LoadFeatureThemes");
        Check(settings.UpdateRepository == GitHubUpdateService.DefaultRepository, "Existing empty update repository migrates to the default");
        settings.UpdateRepository = "example/custom"; Call("LoadFeatureThemes");
        Check(settings.UpdateRepository == "example/custom", "An explicitly selected update repository is preserved");
        settings.UpdateRepository = GitHubUpdateService.DefaultRepository;
        Check((JsonSerializer.Deserialize<MainWindow.AppSettings>("{\"LevelHud\":false}")! is { VolumeIslandHud: null, LevelHud: false }), "Migration retains a previously disabled HUD");
        settings.VolumeIslandHud = true;
        for (int i = 0; i < 1000; i++) Call("VolumeChanged", new VolumeIslandService.Snapshot(i / 999f, i == 999));
        Check((bool)typeof(MainWindow).GetField("_volumeDispatchPending", flags)!.GetValue(window)!, "Rapid changes schedule a single UI delivery");
        var drain = new DispatcherFrame(); Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => drain.Continue = false)); Dispatcher.PushFrame(drain);
        Check(Element<TextBlock>("LevelHudText").Text == "Звук выключен", "Latest mute-only snapshot wins, without a system scan");
        Call("SetVolumeIslandHud", false); Call("VolumeChanged", new VolumeIslandService.Snapshot(.5f, false));
        drain = new DispatcherFrame(); Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => drain.Continue = false)); Dispatcher.PushFrame(drain);
        Check(Element<Border>("LevelHudBorder").Visibility == Visibility.Collapsed, "Disabled HUD ignores already queued callbacks");
        Check((bool)typeof(MainWindow).GetMethod("IsInteractiveIslandChild", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [Element<System.Windows.Controls.Button>("BtnQuickDnd")])!, "Action buttons are excluded from body drag capture");
        Check((bool)typeof(MainWindow).GetMethod("IsInteractiveIslandChild", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [Element<Border>("CompactProgressPanel")])!, "Progress panels keep their own click target");
        var islandBody = Element<Border>("IslandBorder");
        var hoverTimer = (DispatcherTimer)typeof(MainWindow).GetField("_hoverDelay", flags)!.GetValue(window)!;
        hoverTimer.Start();
        Call("BeginBodyGesture", new Point(100, 20));
        Check((bool)typeof(MainWindow).GetField("_isClickCandidate", flags)!.GetValue(window)!, "Body press stays a click candidate");
        Check(!hoverTimer.IsEnabled, "Press cancels pending hover morphology");
        var up = new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 1, System.Windows.Input.MouseButton.Left)
            { RoutedEvent = UIElement.MouseLeftButtonUpEvent, Source = islandBody };
        Call("IslandBorder_MouseLeftButtonUp", islandBody, up);
        Check(typeof(MainWindow).GetField("_currentState", flags)!.GetValue(window)!.ToString() == "Expanded", "Matching release expands the island");
        Check(!(bool)typeof(MainWindow).GetField("_isClickCandidate", flags)!.GetValue(window)!, "Release clears the gesture before changing geometry");
        // Unloaded product window: no integrations, microphone, UAC, settings saves or messages.
        settings.CustomThemes.Add(theme); Call("LoadFeatureThemes");
        settings.SelectedTheme = "User: " + theme.Name; Call("ApplyCurrentSettingsToUi");
        Check(settings.SelectedTheme == "User: " + theme.Name, "Custom theme restored before UI initialization");
        var root = (FrameworkElement)window.Content;
        string directory = System.IO.Path.GetFullPath(args.FirstOrDefault() ?? "artifacts/feature-checks-1.1.5"); System.IO.Directory.CreateDirectory(directory);
        void Render(string name)
        {
            var island = Element<Border>("IslandBorder"); island.Width = 740; island.Height = 540; island.RenderTransform = Transform.Identity;
            Element<FrameworkElement>("FeaturesView").BeginAnimation(UIElement.OpacityProperty, null); Element<FrameworkElement>("FeaturesView").Opacity = 1;
            root.Measure(new Size(1360, 700)); root.Arrange(new Rect(0, 0, 1360, 700)); root.UpdateLayout();
            var visual = new DrawingVisual(); using (var drawing = visual.RenderOpen()) drawing.DrawRectangle(new VisualBrush(island), null, new Rect(0, 0, 740, 540));
            var bitmap = new RenderTargetBitmap(740, 540, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var file = System.IO.File.Create(System.IO.Path.Combine(directory, name + ".png")); encoder.Save(file);
            Check(Element<StackPanel>("FeatureContent").ActualWidth is > 400 and <= 542, name + " content fits the viewport");
        }
        foreach (string palette in new[] { "Midnight", "Snow" })
        {
            var themes = (List<MainWindow.ThemePreset>)typeof(MainWindow).GetField("_themes", flags)!.GetValue(window)!;
            settings.SelectedTheme = palette; Call("ApplyTheme", themes.Single(t => t.Key == palette));
            var frame = new DispatcherFrame(); var settle = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            settle.Tick += (_, _) => { settle.Stop(); frame.Continue = false; }; settle.Start(); Dispatcher.PushFrame(frame);
            foreach (string page in new[] { "layout", "themes", "calendar", "timers", "music", "chat", "diagnostics", "updates", "news", "welcome" })
            { Call("OpenFeaturePage", page); Render(palette + "-" + page); }
        }
        settings.CompactWidth = 300; settings.CompactHeight = 70; settings.CornerRounding = 0;
        Call("ApplyFeatureAppearance"); Call("UpdateIdleContent");
        Check(settings.CompactWidth == 300, "Custom dimensions retained");
        Call("BuildCharactersGrid");
        Check(Element<System.Windows.Controls.Primitives.UniformGrid>("CharactersUniformGrid").Children.Count == 44, "Original mascots plus 22 and None");
        foreach (string character in new[] { "Puppy", "Fox", "Panda", "Bear", "Frog", "Penguin", "Chick", "Piggy", "Koala", "Mouse", "Bee", "Octopus", "Mushroom", "Cactus", "Alien", "Bat", "Moon", "Sun", "Flame", "Droplet", "Heart", "Pumpkin" })
        {
            var view = (Viewbox)Call("CreateCharacterVector", character, 36d)!;
            Check(((Canvas)view.Child).Children.Count >= 2, character + " has drawn body and animated eyes");
        }
        // A reminder cannot hijack the click of a currently visible messenger toast.
        var noticeType = typeof(MainWindow).GetNestedType("IslandNotification", BindingFlags.NonPublic)!;
        var notice = Activator.CreateInstance(noticeType, 123u, "Synthetic notification", true, "test.source", "test", null, DateTimeOffset.Now, Task.FromResult<NotificationActivationService.Target?>(null));
        typeof(MainWindow).GetField("_visibleNotification", flags)!.SetValue(window, notice);
        Call("FeatureNotice", "Synthetic reminder", new Action(() => { }));
        Check(typeof(MainWindow).GetField("_featureNoticeAction", flags)!.GetValue(window) == null, "Feature reminder never replaces messenger click target");
        Call("DisposeFeatures"); app.Shutdown();
        Console.WriteLine($"PASS: {checks} feature checks; offscreen previews: {directory}"); return 0;
    }

    private static int CheckVolumeEvents()
    {
        using var service = new VolumeIslandService(false);
        if (!SpinWait.SpinUntil(() => service.Available, 5000)) throw new Exception("CoreAudio endpoint/hook not ready");
        using var enumerator = new NAudio.CoreAudioApi.MMDeviceEnumerator();
        using var device = enumerator.GetDefaultAudioEndpoint(NAudio.CoreAudioApi.DataFlow.Render, NAudio.CoreAudioApi.Role.Multimedia);
        var endpoint = device.AudioEndpointVolume;
        float original = endpoint.MasterVolumeLevelScalar;
        bool originalMute = endpoint.Mute;
        using var received = new AutoResetEvent(false);
        VolumeIslandService.Snapshot latest = default;
        service.Changed += snapshot => { latest = snapshot; received.Set(); };
        bool WaitFor(Func<VolumeIslandService.Snapshot, bool> match)
        {
            var deadline = System.Diagnostics.Stopwatch.StartNew();
            while (deadline.ElapsedMilliseconds < 1500)
            {
                if (!received.WaitOne((int)Math.Max(1, 1500 - deadline.ElapsedMilliseconds))) return false;
                if (match(latest)) return true;
            }
            return false;
        }
        try
        {
            endpoint.MasterVolumeLevelScalar = original < .98f ? original + .01f : original - .01f;
            if (received.WaitOne(150)) throw new Exception("Disabled service published a HUD event");
            service.Enabled = true;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            endpoint.MasterVolumeLevelScalar = original;
            if (!WaitFor(snapshot => Math.Abs(snapshot.Volume - original) < .001)) throw new Exception("Endpoint callback did not arrive");
            double latency = watch.Elapsed.TotalMilliseconds;
            if (Math.Abs(latest.Volume - original) > .001) throw new Exception("Wrong endpoint snapshot");
            endpoint.Mute = !originalMute;
            if (!WaitFor(snapshot => snapshot.Muted != originalMute)) throw new Exception("Mute-only callback did not arrive");
            service.Enabled = false;
            received.Reset();
            endpoint.Mute = originalMute;
            if (received.WaitOne(150)) throw new Exception("Disabling did not restore passthrough mode");
            Console.WriteLine($"PASS: real endpoint callbacks, mute-only changes, disabled mode, hook ready; callback {latency:0.0} ms; original sound restored.");
            return 0;
        }
        finally { service.Enabled = false; endpoint.MasterVolumeLevelScalar = original; endpoint.Mute = originalMute; }
    }

    private static int CheckPublishedRelease()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        var release = GitHubUpdateService.CheckAsync(GitHubUpdateService.DefaultRepository, timeout.Token).GetAwaiter().GetResult();
        if (release.Version != new Version(1, 1, 5) || release.InstallerName != GitHubUpdateService.InstallerFileName)
            throw new Exception("Unexpected published release");
        string path = GitHubUpdateService.DownloadAsync(release, new Progress<double>(), timeout.Token).GetAwaiter().GetResult();
        Console.WriteLine($"PASS: public latest release {release.Version}, installer {release.InstallerName}, real download, SHA-256 and publisher signature verified; {new System.IO.FileInfo(path).Length} bytes.");
        return 0;
    }
}
