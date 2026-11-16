using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DynamicIslandWin;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Contains("--installed-icons")) return CheckInstalledIcons();
        if (args.Contains("--probe-screen-recording")) return ProbeScreenRecordingAsync(args[1]).GetAwaiter().GetResult();
        if (args.Contains("--probe-video-overlay")) return ProbeVideoOverlay(args[1]);
        // Construct and render offscreen: never start the product integrations,
        // change user settings, launch messengers, or send real messages.
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var window = new MainWindow();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        object? Call(string name, params object?[] values) => typeof(MainWindow).GetMethod(name, flags)!.Invoke(window, values);
        void Set(string name, object? value) => typeof(MainWindow).GetField(name, flags)!.SetValue(window, value);
        T Field<T>(string name) => (T)typeof(MainWindow).GetField(name, flags)!.GetValue(window)!;
        T Element<T>(string name) => (T)window.FindName(name);
        int checks = 0;
        void Check(bool ok, string message) { if (!ok) throw new Exception(message); checks++; }
        var settings = Field<MainWindow.AppSettings>("_settings");
        var themes = Field<List<MainWindow.ThemePreset>>("_themes");
        var output = args.Length > 0 ? args[0] : "artifacts/ui-checks";
        System.IO.Directory.CreateDirectory(output);
        var recording = typeof(MainWindow).Assembly.GetType("DynamicIslandWin.Services.ScreenRecordingService")!;
        var request = Guid.NewGuid();
        var captureUri = (Uri)recording.GetMethod("BuildCaptureUri", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, new object[] { request })!;
        Check(captureUri.Host == "capture" && captureUri.AbsolutePath == "/video", "Recording launches video capture rather than Game Bar");
        Check(captureUri.Query.Contains("redirect-uri=") && captureUri.Query.Contains(request.ToString()), "Recording callback and correlation included");
        var parseResponse = recording.GetMethod("TryReadResponse", BindingFlags.Static | BindingFlags.NonPublic)!;
        bool Accept(string value) => (bool)parseResponse.Invoke(null, new object?[] { value, request, 0, null })!;
        string response = $"dynamicisland-capture://response?code=200&file-access-token=test&x-request-correlation-id={request:D}";
        Check(Accept(response), "Matching recording response accepted");
        Check(!Accept(response.Replace(request.ToString(), Guid.NewGuid().ToString())), "Stale or unsolicited callback rejected");
        Check(!Accept(response.Replace("dynamicisland-capture:", "file:")), "Unexpected callback scheme rejected");
        Check(!Accept(response + "&code=500"), "Ambiguous duplicate callback fields rejected");
        Check(!Accept(response.Replace("&file-access-token=test", "")), "Success without file token rejected");
        Check(!Accept("dynamicisland-capture://response?code=200"), "Incomplete callback rejected");
        Check(Accept(response.Replace("code=200", "code=499").Replace("&file-access-token=test", "")), "User cancellation accepted without a file");
        Check(window.FindName("BtnQuickQr") == null && window.FindName("BtnQuickOcr") == null
            && window.FindName("ChkActQr") == null && window.FindName("ChkActOcr") == null, "Removed actions cannot reappear from saved preferences");

        Call("BuildCharactersGrid");
        foreach (var theme in themes)
        {
            Call("ApplyTheme", theme);
            var expected = (SolidColorBrush)window.FindResource("ThemeTextBrush");
            foreach (string name in new[] { "CompactClockText", "LblSettingsTitle", "LblWeatherDashTitle", "SideToastText", "LblNotificationDuration" })
                Check(((SolidColorBrush)Element<TextBlock>(name).Foreground).Color == expected.Color, $"{theme.Key}: {name} theme text");
            var character = (Border)Element<System.Windows.Controls.Primitives.UniformGrid>("CharactersUniformGrid").Children[0];
            var label = (TextBlock)((StackPanel)character.Child).Children[1];
            Check(((SolidColorBrush)label.Foreground).Color == expected.Color, $"{theme.Key}: existing character label updates");
            Check(theme.IsLight ? expected.Color.R < 60 : expected.Color.R > 220, $"{theme.Key}: text contrast direction");
            Check(ReferenceEquals(Element<Border>("IslandBorder").ContextMenu.FindResource("ThemeTextBrush"), expected), $"{theme.Key}: popup uses current palette");
        }
        var slider = Element<Slider>("NotificationDurationSlider");
        Check(slider.Minimum == 3 && slider.Maximum == 10 && slider.IsSnapToTickEnabled, "Duration range");
        Set("_isLoadingSettings", true); // All tests remain unable to save settings.
        settings.NotificationDurationSeconds = 10;
        var category = typeof(MainWindow).GetNestedType("ToastCategory", BindingFlags.NonPublic)!;
        Call("ShowSideBubble", "Keyboard hint", Enum.Parse(category, "Layout"), "RU", false);
        Check(Field<DispatcherTimer>("_toastHideTimer").Interval == TimeSpan.FromSeconds(10), "Duration applied to timer");
        Set("_dndStateKnown", true);
        Element<CheckBox>("TglAllAppNotifs").IsChecked = true;
        var notificationType = typeof(MainWindow).GetNestedType("IslandNotification", BindingFlags.NonPublic)!;
        var activationType = typeof(MainWindow).Assembly.GetType("DynamicIslandWin.Services.NotificationActivationService+Target")!;
        var noActivation = typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(activationType).Invoke(null, new object?[] { null });
        object Notice(string text) => Activator.CreateInstance(notificationType, 42u, text, true, "Discord.Test", "Discord", null, DateTimeOffset.Now, noActivation)!;
        Set("_pendingNotification", Notice("Discord: test notification"));
        Check((bool)Call("ShowNextNotification")!, "Message preempts active keyboard timer");
        Check(Element<TextBlock>("SideToastText").Text == "Discord: test notification", "Message displayed immediately");
        Check(Element<TextBlock>("SideToastBadgeShortText").Text == "✉", "Loading fallback never shows application initials");
        Check(Field<object>("_visibleNotification") != null, "Source preserved for click");
        Call("ShowSideBubble", "Clipboard hint", Enum.Parse(category, "Clipboard"), "", false);
        Check(Element<TextBlock>("SideToastText").Text.StartsWith("Discord:"), "Clipboard cannot replace active message");
        Set("_pendingNotification", Notice("Discord: newer notification"));
        Call("ShowNextNotification");
        Check(Element<TextBlock>("SideToastText").Text.EndsWith("newer notification"), "New messages never wait for duration");
        Set("_isDndActive", true);
        Set("_pendingNotification", Notice("Muted"));
        Check(!(bool)Call("ShowNextNotification")!, "DND blocks pending messages");
        Set("_isDndActive", false);
        Call("HideNotificationBubble");
        Check(typeof(MainWindow).GetField("_visibleNotification", flags)!.GetValue(window) == null, "Hidden notification clears click target");

        var island = Element<Border>("IslandBorder");
        island.RenderTransform = Transform.Identity;
        void SaveVisual(FrameworkElement element, string name, double width, double height, double inset = 0)
        {
            var visual = new DrawingVisual();
            using (var draw = visual.RenderOpen())
            {
                draw.DrawRectangle(new SolidColorBrush(Color.FromRgb(117, 139, 164)), null, new Rect(0, 0, width, height));
                draw.DrawRectangle(new VisualBrush(element) { Stretch = Stretch.None }, null,
                    new Rect(inset, inset, width - 2 * inset, height - 2 * inset));
            }
            var bitmap = new RenderTargetBitmap((int)width, (int)height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
            var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = System.IO.File.Create(System.IO.Path.Combine(output, name + ".png")); png.Save(file);
        }
        void Snapshot(string name, string view, string? page = null)
        {
            foreach (string id in new[] { "CompactView", "HoverView", "ExpandedView", "WeatherDashboardView", "SettingsView", "ClipboardDashboardView" })
                if (window.FindName(id) is FrameworkElement el) el.Visibility = Visibility.Collapsed;
            var content = Element<FrameworkElement>(view);
            content.BeginAnimation(UIElement.OpacityProperty, null); content.Opacity = 1; content.Visibility = Visibility.Visible;
            if (page != null)
            {
                string nav = page == "PageAppearance" ? "NavAppearanceBtn" : "NavBehaviorBtn";
                Call("SwitchSettingsPage", Element<UIElement>(page), Element<Button>(nav));
                var p = Element<FrameworkElement>(page); p.BeginAnimation(UIElement.OpacityProperty, null); p.Opacity = 1;
            }
            island.Width = 740; island.Height = 515;
            ((FrameworkElement)window.Content).Measure(new Size(1360, 700));
            ((FrameworkElement)window.Content).Arrange(new Rect(0, 0, 1360, 700));
            ((FrameworkElement)window.Content).UpdateLayout();
            var visual = new DrawingVisual();
            using (var draw = visual.RenderOpen())
            {
                draw.DrawRectangle(new SolidColorBrush(Color.FromRgb(117, 139, 164)), null, new Rect(0, 0, 740, 515));
                draw.DrawRectangle(new VisualBrush(island), null, new Rect(0, 0, 740, 515));
            }
            var bitmap = new RenderTargetBitmap(740, 515, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
            var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = System.IO.File.Create(System.IO.Path.Combine(output, name + ".png")); png.Save(file);
        }
        foreach (string key in new[] { "Midnight", "Snow" })
        {
            var theme = themes.Single(t => t.Key == key);
            settings.SelectedTheme = key; settings.LiquidGlass = true;
            Call("ApplyTheme", theme); Call("BuildThemesGrid"); Call("UpdateCharacterVectors", settings.SelectedCharacter);
            // Let palette animations complete without creating a desktop window.
            var frame = new DispatcherFrame();
            var settle = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            settle.Tick += (_, _) => { settle.Stop(); frame.Continue = false; }; settle.Start(); Dispatcher.PushFrame(frame);
            Snapshot(key + "-appearance", "SettingsView", "PageAppearance");
            Snapshot(key + "-behavior", "SettingsView", "PageBehavior");
            Snapshot(key + "-home", "ExpandedView");
            Snapshot(key + "-weather", "WeatherDashboardView");
            var menu = island.ContextMenu;
            Set("_isGameModeActive", true);
            Call("IslandContextMenu_Opened", menu, new RoutedEventArgs());
            Check(Element<MenuItem>("CtxGameModeItem").IsChecked, key + ": active game mode marked in menu");
            Set("_isGameModeActive", false);
            menu.ApplyTemplate();
            menu.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            menu.Arrange(new Rect(menu.DesiredSize)); menu.UpdateLayout();
            Check(menu.DesiredSize.Width < 420, key + ": menu stays compact");
            SaveVisual(menu, key + "-menu", Math.Ceiling(menu.ActualWidth), Math.Ceiling(menu.ActualHeight));

            Snapshot(key + "-game-layout", "CompactView");
            Element<FrameworkElement>("CompactStandardPanel").Visibility = Visibility.Collapsed;
            var hud = Element<FrameworkElement>("CompactGameHudPanel");
            hud.Visibility = Visibility.Visible;
            Call("UpdateGameHudValues");
            island.Width = 480; island.Height = 64; island.CornerRadius = new CornerRadius(24);
            Element<Border>("LiquidGlassOverlay").CornerRadius = island.CornerRadius;
            ((FrameworkElement)window.Content).Measure(new Size(1360, 700));
            ((FrameworkElement)window.Content).Arrange(new Rect(0, 0, 1360, 700));
            ((FrameworkElement)window.Content).UpdateLayout();
            Check(hud.DesiredSize.Width <= 456 && hud.DesiredSize.Height <= 64, key + ": game cards fit without clipping");
            SaveVisual(island, key + "-game", 512, 96, 16);
            hud.Visibility = Visibility.Collapsed;
            Element<FrameworkElement>("CompactStandardPanel").Visibility = Visibility.Visible;
            var toast = Element<Border>("SideToastBorder");
            toast.Visibility = Visibility.Visible;
            toast.BeginAnimation(UIElement.OpacityProperty, null); toast.Opacity = 1;
            toast.RenderTransform = Transform.Identity;
            Element<TextBlock>("SideToastText").Text = "Новое уведомление";
            var discord = Element<Border>("DiscordCallBorder"); discord.Visibility = Visibility.Visible;
            ((FrameworkElement)window.Content).Measure(new Size(1360, 700));
            ((FrameworkElement)window.Content).Arrange(new Rect(0, 0, 1360, 700));
            ((FrameworkElement)window.Content).UpdateLayout();
            SaveVisual(toast, key + "-toast-glass", Math.Ceiling(toast.ActualWidth) + 16, 54, 8);
            SaveVisual(discord, key + "-discord-glass", Math.Ceiling(discord.ActualWidth) + 16, 54, 8);
            toast.Visibility = Visibility.Collapsed; discord.Visibility = Visibility.Collapsed;
        }
        settings.LiquidGlass = false; Call("ApplyTheme", themes.Single(t => t.Key == "Snow"));
        Check(Element<Border>("LiquidGlassOverlay").Visibility == Visibility.Collapsed, "Glass off hides reflections");
        Check(island.BorderThickness.Left == 0, "Glass off removes glass edge");
        Check(((SolidColorBrush)window.FindResource("GlassSheenBrush")).Color.A == 0, "Glass off clears reflections on all auxiliary surfaces");
        Check(window.FindResource("ThemeFloatingBrush") is SolidColorBrush, "Glass off restores opaque popup surfaces");
        Field<DispatcherTimer>("_toastHideTimer").Stop();
        Console.WriteLine($"PASS: {checks} WPF checks; offscreen previews in {output}");
        // Not closing the unloaded product window avoids invoking real shutdown integrations.
        app.Shutdown();
        return 0;
    }

    private static async Task<int> ProbeScreenRecordingAsync(string reportPath)
    {
        // No screen capture: discover verifies the installed Snipping Tool, MSIX
        // identity, protocol activation and return pipe without opening the overlay.
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var type = typeof(MainWindow).Assembly.GetType("DynamicIslandWin.Services.ScreenRecordingService")!;
        string pipeName = (string)type.GetProperty("PipeName", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        await using var pipe = new System.IO.Pipes.NamedPipeServerStream(pipeName, System.IO.Pipes.PipeDirection.In, 1,
            System.IO.Pipes.PipeTransmissionMode.Byte, System.IO.Pipes.PipeOptions.Asynchronous | System.IO.Pipes.PipeOptions.CurrentUserOnly);
        var connection = pipe.WaitForConnectionAsync(timeout.Token);
        Guid correlation = Guid.NewGuid();
        bool launched = false, callbackReceived = false, recordingSupported = false;
        string? error = null;
        try
        {
            launched = await Windows.System.Launcher.LaunchUriAsync(new Uri(
                "ms-screenclip://discover?api-version=1.2&user-agent=DynamicIslandWin&redirect-uri=dynamicisland-capture%3A%2F%2Fresponse&x-request-correlation-id=" + correlation)).AsTask(timeout.Token);
            await connection;
            using var reader = new System.IO.StreamReader(pipe);
            string response = (await reader.ReadLineAsync(timeout.Token))!;
            var uri = new Uri(response);
            var query = new Windows.Foundation.WwwFormUrlDecoder(uri.Query);
            callbackReceived = uri.Scheme == "dynamicisland-capture" && uri.Host == "response"
                && query.GetFirstValueByName("x-request-correlation-id") == correlation.ToString();
            using var capabilities = System.Text.Json.JsonDocument.Parse(query.GetFirstValueByName("discover"));
            recordingSupported = capabilities.RootElement.GetProperty("capabilities").EnumerateArray()
                .Any(c => c.GetProperty("path").GetString() == "capture/video");
        }
        catch (Exception ex) { error = ex.GetType().Name; }
        await System.IO.File.WriteAllTextAsync(reportPath, System.Text.Json.JsonSerializer.Serialize(
            new { launched, callbackReceived, recordingSupported, error }));
        if (!launched || !callbackReceived || !recordingSupported) throw new Exception("Installed screen recording protocol probe failed: " + error);
        Console.WriteLine("PASS: installed Snipping Tool supports video; packaged callback and return pipe work.");
        return 0;
    }

    private static int ProbeVideoOverlay(string reportPath)
    {
        // A foreground button reproduces the real activation context; background
        // command-line callers can be denied capture UI by Windows.
        var app = new Application();
        var button = new Button { Content = "Открыть выбор области записи", Margin = new Thickness(24), Padding = new Thickness(16) };
        var fixture = new Window { Title = "Dynamic Island — проверка записи", Width = 400, Height = 150,
            WindowStartupLocation = WindowStartupLocation.CenterScreen, Content = button };
        var type = typeof(MainWindow).Assembly.GetType("DynamicIslandWin.Services.ScreenRecordingService")!;
        using var service = (IDisposable)Activator.CreateInstance(type, nonPublic: true)!;
        button.Click += async (_, _) =>
        {
            button.IsEnabled = false;
            bool cancelled = false;
            string? error = null;
            try
            {
                var task = (Task<string?>)type.GetMethod("RecordAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(service, null)!;
                cancelled = await task.WaitAsync(TimeSpan.FromSeconds(60)) == null;
            }
            catch (Exception ex) { error = ex.GetType().Name; }
            await System.IO.File.WriteAllTextAsync(reportPath, System.Text.Json.JsonSerializer.Serialize(new { cancelled, error }));
            fixture.Close();
        };
        return app.Run(fixture);
    }

    private static int CheckInstalledIcons()
    {
        // Explicit local integration check: resolves icons without launching apps
        // or reading notification content. The PNGs allow checking actual artwork.
        string output = System.IO.Path.GetFullPath("artifacts/icon-checks");
        System.IO.Directory.CreateDirectory(output);
        var service = typeof(MainWindow).Assembly.GetType("DynamicIslandWin.Services.NotificationIconService")!;
        var load = service.GetMethod("LoadAsync", BindingFlags.Static | BindingFlags.NonPublic)!;
        foreach (var (name, id) in new[]
        {
            ("exDesktop", "exDesktop.exDesktop"),
            ("Discord", "com.squirrel.Discord.Discord"),
            ("Telegram", "TelegramMessengerLLP.TelegramDesktop_t4vj0pshhgkwm!Telegram.TelegramDesktop.Store")
        })
        {
            var task = (Task<BitmapSource?>)load.Invoke(null, new object?[] { id, null })!;
            var bitmap = task.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
            if (bitmap == null || !bitmap.IsFrozen || bitmap.PixelWidth < 24 || bitmap.PixelHeight < 24)
                throw new Exception($"Missing or unusable installed icon: {name}");
            var png = new PngBitmapEncoder();
            png.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = System.IO.File.Create(System.IO.Path.Combine(output, name + ".png"));
            png.Save(file);
            Console.WriteLine($"PASS: {name} Shell icon {bitmap.PixelWidth}x{bitmap.PixelHeight}");
        }
        return 0;
    }
}
