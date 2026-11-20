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
        string Translate(string text) => (string)Call("L", text)!;
        void Set(string name, object? value) => typeof(MainWindow).GetField(name, flags)!.SetValue(window, value);
        T Field<T>(string name) => (T)typeof(MainWindow).GetField(name, flags)!.GetValue(window)!;
        T Element<T>(string name) => (T)window.FindName(name);
        int checks = 0;
        void Check(bool ok, string message) { if (!ok) throw new Exception(message); checks++; }
        var settings = Field<MainWindow.AppSettings>("_settings");
        settings.Language = "English";
        Check(!settings.LensGlass, "Experimental lens is opt-in for existing installations");
        settings.LensGlass = true;
        Call("ConfigureLensGlass");
        Check(Element<System.Windows.Controls.Image>("LiquidGlassBackdrop").Source == null, "Unloaded window never captures a backdrop");
        settings.ExcludeFromCapture = false; Call("ConfigureLensGlass");
        Check(Element<System.Windows.Controls.Image>("LiquidGlassBackdrop").Visibility == Visibility.Collapsed, "Unloaded window does not capture in screenshot-inclusive mode either");
        Check(System.Text.Json.JsonSerializer.Deserialize<MainWindow.AppSettings>(System.Text.Json.JsonSerializer.Serialize(settings))!.LensGlass,
            "Experimental glass choice survives settings serialization");
        settings.LensGlass = false; settings.ExcludeFromCapture = true; Call("ConfigureLensGlass");
        var track = Element<TextBlock>("CompactTrackText");
        var logo = Element<ContentControl>("CompactCharacterHost");
        settings.SelectedCharacter = "Fox";
        Call("UpdateCharacterVectors", "Fox");
        var selectedLogo = logo.Content;
        settings.IdleRight = "weather";
        string longTrack = "Kick A Door, я роняю двери без стука (Gucci) — " + new string('X', 100);
        Call("ApplyMediaState", true, longTrack, "Artist", "player");
        Check(track.Text == longTrack && track.TextTrimming == TextTrimming.CharacterEllipsis
            && track.Visibility == Visibility.Visible && logo.Visibility == Visibility.Visible && ReferenceEquals(logo.Content, selectedLogo)
            && Element<TextBlock>("CompactClockText").Visibility == Visibility.Collapsed
            && Element<StackPanel>("CompactWeatherPanel").Visibility == Visibility.Collapsed && Field<double>("_targetW") >= 360,
            "Playing a track preserves the selected character and gives the title its own space without overlapping the clock or weather");
        Call("UpdateIdleContent");
        Check(Element<TextBlock>("CompactClockText").Visibility == Visibility.Collapsed
            && Element<StackPanel>("CompactWeatherPanel").Visibility == Visibility.Collapsed,
            "Clock and weather refresh does not reveal them over a playing track");
        Call("ApplyMediaState", true, "Next track", "Artist", "player");
        Check(track.Text == "Next track", "Compact title updates on a track change");
        var lyrics = Element<TextBlock>("CompactLyricsText");
        var lyricsService = typeof(MainWindow).Assembly.GetType("DynamicIslandWin.Services.LyricsService")!;
        var currentTrack = Activator.CreateInstance(lyricsService.GetNestedType("Track", BindingFlags.NonPublic)!, "Next track", "Artist", "player")!;
        Call("UpdateLyricsTrack", currentTrack, 2d, "", 180d);
        Set("_importedLyricsTrack", currentTrack);
        Set("_importedLyrics", lyricsService.GetMethod("Parse", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, ["[00:00]First line\n[00:01.50]Current line\n[00:10]Later"]));
        Call("UpdateCompactContent");
        Check(lyrics.Visibility == Visibility.Collapsed && !settings.OnlineLyrics, "Lyric display and online search are both opt-in");
        settings.Karaoke = true;
        Call("UpdateCompactContent");
        Check(lyrics.Visibility == Visibility.Visible && lyrics.Text == "Current line" && track.Text == "Next track"
            && ReferenceEquals(logo.Content, selectedLogo) && Field<double>("_targetH") >= 50,
            "A local LRC can show the synchronized line below the title beside the chosen character, without online search");
        Call("UpdateLyricsTrack", currentTrack, 0d, "", 180d);
        Call("UpdateCompactLyrics");
        Check(lyrics.Text == "First line", "Skipping backward restores the earlier lyric");
        var otherTrack = Activator.CreateInstance(lyricsService.GetNestedType("Track", BindingFlags.NonPublic)!, "Other track", "Artist", "player")!;
        Call("UpdateLyricsTrack", otherTrack, 2d, "", 180d);
        Call("ApplyMediaState", true, "Other track", "Artist", "player");
        Check(lyrics.Visibility == Visibility.Collapsed && lyrics.Text == "" && track.Text == "Other track",
            "A different track never displays the previous track's imported lyrics");
        Call("UpdateLyricsTrack", currentTrack, 2d, "", 180d);
        Call("ApplyMediaState", true, "Next track", "Artist", "player");
        Check(lyrics.Text == "Current line", "Returning to the imported track restores its lyrics");
        Set("_isGameModeActive", true);
        Call("UpdateCompactContent");
        Check(track.Visibility == Visibility.Collapsed && lyrics.Visibility == Visibility.Collapsed, "Game HUD takes priority over the compact track and lyrics");
        Set("_isGameModeActive", false);
        Call("ApplyMediaState", false, "", "", "");
        Check(lyrics.Visibility == Visibility.Collapsed && lyrics.Text == "", "Pausing hides and clears the visible lyric");
        settings.Karaoke = false;
        Check(track.Visibility == Visibility.Collapsed && track.Text == ""
            && Element<TextBlock>("CompactClockText").Visibility == Visibility.Visible
            && Element<StackPanel>("CompactWeatherPanel").Visibility == Visibility.Visible && Field<double>("_targetW") == settings.CompactWidth,
            "Pause restores the normal compact size, clock and weather");
        settings.SelectedCharacter = "None";
        settings.IdleRight = "battery";
        Call("ApplyMediaState", true, "Track", "", "player");
        Check(logo.Visibility == Visibility.Collapsed && Element<TextBlock>("CompactBatteryText").Visibility == Visibility.Collapsed,
            "Music respects the no-character preference and hides the alternate battery display");
        Call("ApplyMediaState", false, "", "", "");
        Check(Element<TextBlock>("CompactBatteryText").Visibility == Visibility.Visible, "Pause restores the configured battery display");
        settings.SelectedCharacter = "Eyes";
        settings.IdleRight = "weather";
        Call("UpdateCharacterVectors", "Eyes");
        Call("UpdateCompactContent");
        var fpsType = typeof(MainWindow).Assembly.GetType("DynamicIslandWin.Services.FpsService")!;
        var metrics = Activator.CreateInstance(fpsType, true)!;
        fpsType.GetField("Fps", flags)!.SetValue(metrics, (double?)144);
        fpsType.GetField("CpuTemperatureC", flags)!.SetValue(metrics, 72);
        Set("_gameFps", metrics);
        Call("UpdateFpsTarget");
        fpsType.GetField("Fps", flags)!.SetValue(metrics, (double?)144);
        Set("_nextFeatureScan", DateTime.MaxValue);
        Set("_isGameModeActive", true);
        Call("FeatureTick");
        Check(Element<TextBlock>("GameHudFpsText").Text == "144" && Element<TextBlock>("GameHudCpuTempText").Text == "72°C", "Active game HUD refreshes FPS and helper CPU temperature without rebuilding its page");
        Check(Element<TextBlock>("HoverSysText").Text == "144 FPS (72°C)"
            && !Element<TextBlock>("HoverSysText").Text.Contains("CPU") && !Element<TextBlock>("HoverSysText").Text.Contains("GPU"),
            "The hover summary shows FPS and temperature without CPU/GPU load");
        var weatherContent = (StackPanel)Element<Border>("HomeWeatherWidget").Child;
        Check(weatherContent.Children.Count == 2 && weatherContent.Children.Contains(Element<TextBlock>("HomeWeatherDetailsText")),
            "Home weather shows only location and weather details with game mode enabled");
        string[] fpsHeaders = ["DashboardFpsText", "SettingsFpsText"];
        Check(fpsHeaders.All(name => Element<TextBlock>(name).Visibility == Visibility.Visible && Element<TextBlock>(name).Text == "144 FPS"),
            "FPS stays visible in all expanded page headers during game mode");
        var desktopFps = Field<object>("_desktopFps");
        var sampleDesktop = desktopFps.GetType().GetMethod("Sample", flags)!;
        long second = System.Diagnostics.Stopwatch.Frequency;
        sampleDesktop.Invoke(desktopFps, new object[] { 100UL, second });
        sampleDesktop.Invoke(desktopFps, new object[] { 150UL, 2 * second });
        Call("UpdateGameHudValues");
        Check(Element<TextBlock>("GameHudFpsText").Text == "144", "Foreground app FPS takes priority over desktop FPS");
        fpsType.GetField("Fps", flags)!.SetValue(metrics, null);
        Call("UpdateGameHudValues");
        Check(Element<TextBlock>("GameHudFpsText").Text == "50" && Element<TextBlock>("GameHudFpsLabel").Text == "FPS · DWM"
            && fpsHeaders.All(name => Element<TextBlock>(name).Text == "50 FPS DWM"),
            "Real desktop fallback is visibly labeled in every FPS display");
        Set("_temperatureDriverInstalled", false);
        Set("_windowClosed", true);
        Element<CheckBox>("TglGameModeHud").IsChecked = true;
        Element<MenuItem>("CtxGameModeItem").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Check(!Field<bool>("_isGameModeActive") && Field<object?>("_gameFps") == null && Element<CheckBox>("TglGameModeHud").IsChecked == false,
            "One menu click turns off the game HUD and its metrics collector together");
        Set("_windowClosed", false);
        Check(!Element<TextBlock>("HoverSysText").Text.Contains("FPS") && !Element<TextBlock>("HoverSysText").Text.Contains("°C"), "Stopping the helper clears both readings from the hover summary");
        Check(fpsHeaders.All(name => Element<TextBlock>(name).Visibility == Visibility.Collapsed), "FPS headers hide when game mode is off");
        Check(((string)Call("GameTemperatureStatus")!).Contains("PawnIO"), "Missing driver is diagnosed independently of FPS");
        var hideItem = (MenuItem)Element<Border>("IslandBorder").ContextMenu.Items[0];
        var hiddenHandle = Element<Button>("HiddenIslandHandle");
        var hostVisibility = window.Visibility;
        Check(hiddenHandle.Visibility == Visibility.Collapsed, "The reveal handle is hidden by default");
        hideItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Check(Element<Border>("IslandBorder").Visibility == Visibility.Collapsed && hiddenHandle.Visibility == Visibility.Visible
            && hiddenHandle.Width == 74 && window.Visibility == hostVisibility, "Manual hide replaces the capsule with a narrow reveal handle");
        hiddenHandle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(hiddenHandle.Visibility == Visibility.Collapsed && Element<Border>("IslandBorder").Visibility == Visibility.Visible
            && Element<Grid>("CompactView").Visibility == Visibility.Visible, "Clicking the handle restores the compact island");
        ((IDisposable)metrics).Dispose();
        Set("_isGameModeActive", false);
        Call("ApplyLocalization");
        var islandMenu = Element<Border>("IslandBorder").ContextMenu;
        var menuItems = islandMenu.Items.OfType<MenuItem>().ToArray();
        Check(menuItems.All(item => !Equals(item.Header, "Change theme")), "Theme cycling is not in the island context menu");
        Check(menuItems.All(item => !Equals(item.Header, Translate("Новые возможности"))), "The old hub entry is removed");
        var updateItem = menuItems.Single(item => item.Name == "CtxCheckUpdates");
        Check(Equals(updateItem.Header, "Check for updates"), "Check for updates is localized in the island menu");
        string savedRepo = settings.UpdateRepository;
        settings.UpdateRepository = "?";
        updateItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Check(Field<string>("_settingsFeaturePage") == "updates" && Element<Grid>("SettingsView").Visibility == Visibility.Visible
            && Field<string>("_updateStatus").StartsWith("Укажите owner/repository"),
            "Context menu opens updates in Settings and runs the manual check immediately");
        settings.UpdateRepository = savedRepo;
        var dashboardNavigation = Element<StackPanel>("DashboardNavigation");
        string[] dashboardKeys = ["home", "music", "weather", "chat", "timers", "clipboard", "calendar", "system"];
        settings.TabOrder.AddRange(["layout", "themes", "updates", "diagnostics", "news"]);
        Call("BuildDashboardNavigation");
        Check(dashboardNavigation.Children.OfType<Button>().Select(button => (string)button.Tag).SequenceEqual(dashboardKeys),
            "Legacy tab order cannot put Settings pages or release notes into the dashboard");
        Button DashboardTab(string key) => dashboardNavigation.Children.OfType<Button>().Single(button => Equals(button.Tag, key));
        void SelectDashboardTab(string key) => DashboardTab(key).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(dashboardNavigation.Children.OfType<Button>().All(button => button.ToolTip is string { Length: > 0 }
            && ((StackPanel)button.Content).Children[0] is System.Windows.Shapes.Path { Data: not null }),
            "Every dashboard tab has a vector icon and tooltip");
        settings.HiddenTabs.AddRange(["home", "chat"]);
        Call("BuildDashboardNavigation");
        Check(dashboardNavigation.Children.OfType<Button>().Any(button => Equals(button.Tag, "home"))
            && dashboardNavigation.Children.OfType<Button>().All(button => !Equals(button.Tag, "chat")),
            "Tabs honor visibility preferences without hiding Home");
        settings.HiddenTabs.Clear();
        Call("BuildDashboardNavigation");
        foreach (string key in dashboardKeys)
        {
            SelectDashboardTab(key);
            string expectedView = key switch
            {
                "home" => "ExpandedView", "weather" => "WeatherDashboardView", "clipboard" => "ClipboardDashboardView",
                _ => "DashboardSectionView"
            };
            Check(Element<FrameworkElement>(expectedView).Visibility == Visibility.Visible && Element<Grid>("DashboardHeader").Visibility == Visibility.Visible
                && ((StackPanel)DashboardTab(key).Content).Children.OfType<TextBlock>().Single().Text == (string)DashboardTab(key).ToolTip
                && (expectedView != "DashboardSectionView" || Field<string>("_featurePage") == key),
                key + ": opens directly and shows the selected label");
        }
        SelectDashboardTab("home");
        var dashboardHeader = Element<Grid>("DashboardHeader");
        var dashboardFps = Element<TextBlock>("DashboardFpsText");
        dashboardFps.Visibility = Visibility.Visible;
        dashboardFps.Text = "144 FPS DWM";
        foreach (string language in new[] { "English", "Русский", "Українська", "Deutsch", "Español" })
        {
            settings.Language = language;
            Call("BuildDashboardNavigation");
            foreach (string key in dashboardKeys)
            {
                SelectDashboardTab(key);
                double width = Field<double>("_targetW") - dashboardHeader.Margin.Left - dashboardHeader.Margin.Right;
                dashboardHeader.Measure(new Size(width, 32));
                dashboardHeader.Arrange(new Rect(0, 0, width, 32));
                Check(dashboardNavigation.DesiredSize.Width <= dashboardHeader.ColumnDefinitions[0].ActualWidth + .5,
                    language + ": navigation and FPS fit in " + key);
            }
        }
        settings.Language = "English";
        SelectDashboardTab("home");
        Call("ApplyLocalization");
        Call("UpdateTelemetrySummaries");
        var settingsContent = Element<StackPanel>("SettingsFeatureContent");
        var movedPages = new[]
        {
            (Button: "BtnSettingsLayout", Page: "layout", Title: "Размер и форма"),
            (Button: "BtnSettingsThemes", Page: "themes", Title: "Ваша палитра"),
            (Button: "BtnSettingsDiagnostics", Page: "diagnostics", Title: "Проверка системы"),
            (Button: "BtnSettingsUpdates", Page: "updates", Title: "Обновления через GitHub")
        };
        TextBlock CardTitle(Border card) => (TextBlock)((StackPanel)card.Child).Children[0];
        foreach (var page in movedPages)
        {
            var button = Element<Button>(page.Button);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(Field<string>("_settingsFeaturePage") == page.Page && Field<Button>("_activeSettingsNav") == button
                && Element<Grid>("SettingsView").Visibility == Visibility.Visible && Element<Grid>("DashboardHeader").Visibility == Visibility.Collapsed
                && CardTitle((Border)settingsContent.Children[0]).Text == Translate(page.Title), page.Page + " opens inside Settings");
        }
        var updateCards = settingsContent.Children.OfType<Border>().ToArray();
        Check(updateCards.Length == 2 && CardTitle(updateCards[1]).Text == Translate("Что нового")
            && !((StackPanel)updateCards[0].Child).Children.OfType<WrapPanel>()
                .SelectMany(row => row.Children.OfType<Button>()).Any(button => Equals(button.Content, Translate("Что нового"))),
            "What's new is an inline section below the updates card, not a button");
        Element<Button>("NavAppearanceBtn").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Call("RefreshUpdatesPage");
        Check(Element<ScrollViewer>("SettingsFeatureScroll").Visibility == Visibility.Collapsed
            && Element<ScrollViewer>("PageAppearance").Visibility == Visibility.Visible,
            "Update refresh does not interrupt another settings page");
        Set("_nextUpdateCheck", DateTime.MaxValue);
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
            foreach (string name in new[] { "CompactClockText", "LblSettingsTitle", "ExpandedClockText", "SideToastText", "LblNotificationDuration" })
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
            foreach (string id in new[] { "CompactView", "HoverView", "ExpandedView", "WeatherDashboardView", "SettingsView", "ClipboardDashboardView", "DashboardSectionView" })
                if (window.FindName(id) is FrameworkElement el) el.Visibility = Visibility.Collapsed;
            Element<Grid>("DashboardHeader").Visibility = view is "ExpandedView" or "WeatherDashboardView" or "ClipboardDashboardView" or "DashboardSectionView"
                ? Visibility.Visible : Visibility.Collapsed;
            var content = Element<FrameworkElement>(view);
            content.BeginAnimation(UIElement.OpacityProperty, null); content.Opacity = 1; content.Visibility = Visibility.Visible;
            if (page != null)
            {
                string nav = page == "PageAppearance" ? "NavAppearanceBtn" : "NavBehaviorBtn";
                Call("SwitchSettingsPage", Element<UIElement>(page), Element<Button>(nav));
                var p = Element<FrameworkElement>(page); p.BeginAnimation(UIElement.OpacityProperty, null); p.Opacity = 1;
            }
            island.Width = 740; island.Height = 515;
            island.RenderTransform = Transform.Identity;
            Call("UpdateGlassShadow");
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

        settings.BackgroundTransparency = 60;
        Call("ApplyTheme", themes.Single(t => t.Key == "Snow"));
        Check(Math.Abs(island.Background.Opacity - .4) < .001 && island.Opacity == 1, "Transparency fades background without fading the island content");
        Check(Math.Abs(((Brush)window.FindResource("ThemeCapsuleBrush")).Opacity - .4) < .001, "Volume and notification capsules share transparency");
        Check(((SolidColorBrush)window.FindResource("ThemeTextBrush")).Opacity == 1, "Theme text remains fully opaque");
        settings.LiquidGlass = true;
        Call("ApplyTheme", themes.Single(t => t.Key == "Snow"));
        Check(Math.Abs(((Brush)window.FindResource("GlassSheenBrush")).Opacity - .4) < .001 && Math.Abs(Element<Border>("CustomBgBorder").Opacity - .128) < .001, "Reflections and photo follow transparency");
        var settingsJson = System.Text.Json.JsonSerializer.Serialize(settings);
        Check(System.Text.Json.JsonSerializer.Deserialize<MainWindow.AppSettings>(settingsJson)!.BackgroundTransparency == 60, "Transparency survives settings serialization");
        settings.BackgroundTransparency = 0;
        Call("ApplyTheme", themes.Single(t => t.Key == "Midnight"));
        Check(island.Background.Opacity == 1, "Zero transparency restores original glass appearance");
        var volumeSnapshotType = typeof(MainWindow).Assembly.GetType("DynamicIslandWin.Services.VolumeIslandService+Snapshot")!;
        Call("ShowVolumeSnapshot", Activator.CreateInstance(volumeSnapshotType, .45f, false));
        Check(Element<Slider>("VolumeHudSlider").Visibility == Visibility.Visible && Math.Abs(Element<Slider>("VolumeHudSlider").Value - 45) < .001, "Volume notice exposes an interactive slider at the actual endpoint value");
        Check(Element<ProgressBar>("LevelHudBar").Visibility == Visibility.Collapsed, "Volume replaces the read-only progress bar");
        var wheel = new System.Windows.Input.MouseWheelEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0, 120)
            { RoutedEvent = UIElement.PreviewMouseWheelEvent };
        Call("VolumeHud_MouseWheel", Element<Border>("LevelHudBorder"), wheel);
        Check(wheel.Handled && Math.Abs(Element<Slider>("VolumeHudSlider").Value - 47) < .001, "Wheel adjusts volume by two percent without scrolling the island");
        var volumeHud = Element<Border>("LevelHudBorder");
        volumeHud.Measure(new Size(220, 80)); volumeHud.Arrange(new Rect(0, 0, 200, 65)); volumeHud.UpdateLayout();
        SaveVisual(volumeHud, "interactive-volume", 220, 80, 10);
        Call("ShowVolumeSnapshot", Activator.CreateInstance(volumeSnapshotType, .45f, true));
        Check(Math.Abs(Element<Slider>("VolumeHudSlider").Value - 45) < .001, "Mute preserves the level for unmuting");
        Call("LevelNotice", "Brightness", 70d, false);
        Check(Element<Slider>("VolumeHudSlider").Visibility == Visibility.Collapsed && Element<Button>("VolumeMuteButton").Visibility == Visibility.Collapsed, "Brightness cannot change audio by mistake");
        settings.LiquidGlass = false;

        Set("_isLoadingSettings", true);
        Call("RefreshIslandPlacementControls");
        var pinItem = island.ContextMenu.Items.OfType<MenuItem>().Single(item => item.Name == "CtxPinIsland");
        var dockItem = island.ContextMenu.Items.OfType<MenuItem>().Single(item => item.Name == "CtxDockIsland");
        var togglePin = Element<CheckBox>("TglPinPosition");
        var toggleDock = Element<CheckBox>("TglDockIsland");
        bool CanDrag() => (bool)typeof(MainWindow).GetProperty("CanDragIsland", flags)!.GetValue(window)!;
        pinItem.IsChecked = true; Call("PinIsland_Click", pinItem, new RoutedEventArgs());
        Check(settings.PinPosition && togglePin.IsChecked == true && !CanDrag(), "Context menu locks the current placement and updates settings");
        togglePin.IsChecked = false; Call("PinIsland_Click", togglePin, new RoutedEventArgs());
        Check(!pinItem.IsChecked && !settings.PinPosition && CanDrag(), "Unlocking from settings resynchronizes the menu and enables dragging");
        dockItem.IsChecked = true; Call("DockIsland_Click", dockItem, new RoutedEventArgs());
        Check(settings.DockIslandAtTop && toggleDock.IsChecked == true && island.Margin.Top == 0 && !CanDrag(), "macOS mode docks to the top and cannot be dragged off center");
        Check(island.CornerRadius.TopLeft == 0 && island.CornerRadius.BottomLeft > 0, "Docked notch is square at the screen edge and rounded below");
        var dockedSettings = System.Text.Json.JsonSerializer.Deserialize<MainWindow.AppSettings>(System.Text.Json.JsonSerializer.Serialize(settings))!;
        Check(dockedSettings.DockIslandAtTop && !dockedSettings.PinPosition, "Docked style survives serialization without changing the lock preference");
        toggleDock.IsChecked = false; Call("DockIsland_Click", toggleDock, new RoutedEventArgs());
        Check(!dockItem.IsChecked && island.Margin.Top == 10 && island.CornerRadius.TopLeft > 0 && CanDrag(), "Floating style restores its gap, rounded corners and dragging");

        // Language changes must reach detached popup menus and generated pages.
        var catalog = (Dictionary<string, string[]>)typeof(MainWindow).GetField("AdditionalTranslations", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        Check(catalog.Values.All(words => words.Length == 5 && words.All(w => !string.IsNullOrWhiteSpace(w))), "Every new label has all five translations");
        foreach (string language in new[] { "English", "Русский", "Українська", "Deutsch", "Español", "English" })
        {
            settings.Language = language; Call("ApplyLocalization");
            Check(((MenuItem)island.ContextMenu.Items[0]).Header.Equals(Translate("Спрятать островок")), language + ": popup language refreshed");
            Check(Equals(pinItem.Header, Translate("Закрепить на месте")) && Equals(dockItem.Header, Translate("У верхнего края (macOS)")), language + ": pin and dock menu labels translated");
            Check(Element<TextBlock>("LblExternalProgress").Text == Translate("Прогресс других программ справа"), language + ": progress toggle language");
            Check(Equals(DashboardTab("home").ToolTip, Translate("Главная")) && Equals(DashboardTab("system").ToolTip, Translate("Система")),
                language + ": dashboard navigation is localized");
            Call("BuildCharactersGrid");
            Check((string)Call("GetLocalizedCharacterName", "Puppy")! == Translate("Щенок"), language + ": added character names translated");
            Check(Element<TextBlock>("SettingsLayoutText").Text == Translate("▦  Раскладка")
                && Element<TextBlock>("SettingsThemesText").Text == Translate("◈  Своя тема")
                && Element<TextBlock>("SettingsUpdatesText").Text == Translate("↓  Обновления")
                && Element<TextBlock>("SettingsDiagnosticsText").Text == Translate("✓  Диагностика"), language + ": Settings navigation is localized");
            foreach (var (page, host) in new (string, StackPanel)[]
            {
                ("BuildLayoutPage", settingsContent), ("BuildUserThemesPage", settingsContent), ("BuildUpdatesPage", settingsContent),
                ("BuildTimersPage", Element<StackPanel>("DashboardSectionContent")), ("BuildMusicPage", Element<StackPanel>("DashboardSectionContent")),
                ("BuildWelcomePage", Element<StackPanel>("DashboardSectionContent"))
            })
            {
                host.Children.Clear(); Call(page);
                IEnumerable<string> Labels(DependencyObject node)
                {
                    if (node is TextBlock text) yield return text.Text;
                    if (node is ContentControl control && control.Content is string content) yield return content;
                    foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>())
                        foreach (string value in Labels(child)) yield return value;
                }
                if (language is "English" or "Deutsch" or "Español")
                    Check(!Labels(host).Any(label => System.Text.RegularExpressions.Regex.IsMatch(label, "[А-Яа-яЁё]")), language + ": " + page + " contains no untranslated Russian");
            }
        }

        // Progress stays in the right column, leaving all compact content intact.
        var progress = Element<Border>("CompactProgressPanel");
        Check(Grid.GetColumn((UIElement)progress.Parent) == 2 && progress.Parent != Element<Grid>("CompactView"), "Progress capsule is outside the clock and weather");
        progress.Visibility = Visibility.Visible; Call("SetExternalProgress", false);
        Check(progress.Visibility == Visibility.Collapsed && !settings.ExternalProgress && Element<CheckBox>("TglExternalProgress").IsChecked == false, "Disabling progress hides it immediately");
        Call("SetExternalProgress", true);
        Check(Element<CheckBox>("TglExternalProgress").IsChecked == true, "Progress control synchronizes both settings pages");

        // Both legacy and added characters use an independent, movable eye layer.
        foreach (string key in new[] { "Eyes", "Cat", "Claude Code", "Robot", "Ghost", "Fish", "Bunny", "Owl", "Dino", "Puppy", "Fox" })
        {
            var mascot = (Viewbox)Call("CreateCharacterVector", key, 36d)!;
            var canvas = (Canvas)mascot.Child;
            var eyes = canvas.Children.OfType<Canvas>().Single(c => Equals(c.Tag, "eyes"));
            Check(eyes.Children.Count > 0 && eyes.RenderTransform is TranslateTransform, key + ": has gaze layer");
        }
        var gazeMethod = typeof(MainWindow).GetMethod("MascotGaze", BindingFlags.Static | BindingFlags.NonPublic)!;
        var leftGaze = (Point)gazeMethod.Invoke(null, [new Point(-1000, -1000)])!;
        var rightGaze = (Point)gazeMethod.Invoke(null, [new Point(1000, 1000)])!;
        Check(leftGaze.X < 0 && leftGaze.Y < 0 && rightGaze.X > 0 && rightGaze.Y > 0 && rightGaze.X <= 2.2, "Gaze follows pointer direction with bounded travel");

        // Check disk persistence without reading or modifying the real clipboard.
        string clipboardPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(output, "clipboard-data", "history.json"));
        Set("_clipboardHistoryPath", clipboardPath);
        var history = Field<List<MainWindow.ClipboardHistoryItem>>("_clipboardHistory"); history.Clear();
        string exactText = "  first line\r\nsecond line  ";
        history.Add(new(exactText, DateTime.Now)); Call("SaveClipboardHistory"); history.Clear(); Call("LoadClipboardHistory");
        history = Field<List<MainWindow.ClipboardHistoryItem>>("_clipboardHistory");
        Check(history.Single().Text == exactText, "Clipboard history preserves whitespace and line breaks");
        var bitmap = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null, new byte[] { 1, 2, 3, 255, 4, 5, 6, 255, 7, 8, 9, 255, 10, 11, 12, 255 }, 8);
        bitmap.Freeze();
        var encode = typeof(MainWindow).GetMethod("EncodeClipboardImage", BindingFlags.Static | BindingFlags.NonPublic)!;
        byte[] encoded = (byte[])encode.Invoke(null, [bitmap])!;
        string images = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(clipboardPath)!, "clipboard-images"); System.IO.Directory.CreateDirectory(images);
        string imageFile = Guid.NewGuid().ToString("N") + ".png"; System.IO.File.WriteAllBytes(System.IO.Path.Combine(images, imageFile), encoded);
        var screenshot = new MainWindow.ClipboardHistoryItem("image:sample", DateTime.Now, imageFile); history.Insert(0, screenshot);
        Call("SaveClipboardHistory"); Call("LoadClipboardHistory");
        history = Field<List<MainWindow.ClipboardHistoryItem>>("_clipboardHistory");
        var restoredImage = (BitmapSource)Call("LoadClipboardImage", history[0], 0)!;
        Check(restoredImage.PixelWidth == 2 && restoredImage.PixelHeight == 2, "Screenshot survives a history reload at original resolution");
        var historyRows = Element<StackPanel>("ClipboardHistoryListStack");
        Check(historyRows.Children.Count == 0 && Element<TextBlock>("HomeClipboardPreview").Text == "Screenshot",
            "Home summarizes clipboard history without building any preview rows");
        SelectDashboardTab("clipboard");
        Check(historyRows.Children.Count == 2, "Opening clipboard history builds mixed text and image rows");
        Check(Call("ClipboardImagePath", screenshot with { ImageFile = "../../escape.png" }) == null, "History cannot read images outside its directory");
        settings.SelectedTheme = "Midnight"; settings.LiquidGlass = true;
        Call("ApplyTheme", themes.Single(t => t.Key == "Midnight"));
        foreach (string stop in new[] { "GradStop1", "GradStop2", "GradStop3" })
        {
            var gradient = Element<GradientStop>(stop);
            gradient.BeginAnimation(GradientStop.ColorProperty, null);
            gradient.Color = stop == "GradStop2" ? Color.FromArgb(156, 27, 30, 46) : Color.FromArgb(178, 17, 19, 28);
        }
        Snapshot("clipboard-text-and-screenshots", "ClipboardDashboardView");
        SelectDashboardTab("home");
        Check(historyRows.Children.Count == 0, "Leaving clipboard history releases its preview rows");
        string photoPath = System.IO.Path.Combine(output, "background-decode-probe.png");
        var photo = BitmapSource.Create(1024, 2048, 96, 96, PixelFormats.Bgr24, null, new byte[1024 * 2048 * 3], 1024 * 3);
        System.IO.File.WriteAllBytes(photoPath, (byte[])encode.Invoke(null, [photo])!);
        try
        {
            var loadPhoto = typeof(MainWindow).GetMethod("LoadIslandPhoto", BindingFlags.Static | BindingFlags.NonPublic)!;
            var decoded = (BitmapSource)loadPhoto.Invoke(null, [photoPath])!;
            Check(decoded.IsFrozen && decoded.PixelWidth == 768 && decoded.PixelHeight == 1536,
                "Background photo is decoded at a bounded size without changing its aspect ratio");
            var decode = typeof(MainWindow).GetMethod("DecodeLocalImage", BindingFlags.Static | BindingFlags.NonPublic)!;
            var thumbnail = (BitmapSource)decode.Invoke(null, [photoPath, 480])!;
            Check(thumbnail.PixelWidth == 240 && thumbnail.PixelHeight == 480, "Tall clipboard previews are bounded on both axes");
        }
        finally { System.IO.File.Delete(photoPath); }
        history[0] = screenshot with { CopiedAt = DateTime.Now.AddHours(-25) }; Call("PruneExpiredClipboardItems", true);
        Check(history.Count == 1 && !System.IO.File.Exists(System.IO.Path.Combine(images, imageFile)), "Expired screenshot removes both entry and image file");

        settings.LiquidGlass = true; settings.IslandShadow = true;
        Call("ApplyTheme", themes.Single(t => t.Key == "Midnight")); Call("UpdateGlassShadow");
        var shadow = Element<System.Windows.Controls.Image>("IslandShadow");
        var shadowBitmap = new RenderTargetBitmap((int)shadow.Width, (int)shadow.Height, 96, 96, PixelFormats.Pbgra32);
        var shadowVisual = new DrawingVisual(); using (var draw = shadowVisual.RenderOpen()) draw.DrawImage(shadow.Source, new Rect(0, 0, shadow.Width, shadow.Height));
        shadowBitmap.Render(shadowVisual); var pixel = new byte[4]; shadowBitmap.CopyPixels(new Int32Rect((int)shadow.Width / 2, (int)shadow.Height / 2, 1, 1), pixel, 4, 0);
        Check(pixel[3] == 0, "Glass shadow leaves capsule interior fully transparent");
        progress.Visibility = Visibility.Visible; Element<TextBlock>("CompactProgressLabel").Text = "explorer · 14%"; Call("UpdateProgressRing", 14);
        Snapshot("progress-main-island", "CompactView");
        island.Width = 210; island.Height = 38; island.CornerRadius = new CornerRadius(19);
        Element<Border>("LiquidGlassOverlay").CornerRadius = island.CornerRadius;
        Element<FrameworkElement>("CompactStandardPanel").Visibility = Visibility.Visible;
        Element<FrameworkElement>("CompactGameHudPanel").Visibility = Visibility.Collapsed;
        Call("UpdateGlassShadow");
        ((FrameworkElement)window.Content).Measure(new Size(1360, 700));
        ((FrameworkElement)window.Content).Arrange(new Rect(0, 0, 1360, 700));
        ((FrameworkElement)window.Content).UpdateLayout();
        SaveVisual((FrameworkElement)window.Content, "progress-right-capsule", 1360, 700);
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
                var task = (Task<string?>)type.GetMethod("RecordAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(service, [new System.Windows.Interop.WindowInteropHelper(fixture).Handle])!;
                cancelled = await task.WaitAsync(TimeSpan.FromMinutes(3)) == null;
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
