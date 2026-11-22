using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DynamicIslandWin.Services;
using Button = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;
using Brushes = System.Windows.Media.Brushes;

namespace DynamicIslandWin;

public partial class MainWindow
{
    public sealed class WidgetPlacement
    {
        public string Kind { get; set; } = "";
        public double? X { get; set; }
        public double? Y { get; set; }
    }

    public partial class AppSettings
    {
        public List<WidgetPlacement> DesktopWidgets { get; set; } = [];
        public string WidgetTheme { get; set; } = "glass";
        public bool WidgetsHidden { get; set; }
        public bool WidgetsTopmost { get; set; }
    }

    private sealed record WidgetWindow(Window Window, Border Card, TextBlock Title, TextBlock Body);
    private readonly Dictionary<string, WidgetWindow> _widgetWindows = [];
    private readonly DispatcherTimer _desktopWidgetsTimer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
    private readonly Dictionary<string, DateTimeOffset> _nextWidgetReading = [];
    private static readonly (string Key, string Name, string Glyph, string Page)[] WidgetTypes =
    [
        ("clock", "Часы", "\uE823", "timers"), ("weather", "Погода", "\uE9BD", "weather"),
        ("music", "Музыка", "\uE8D6", "music"), ("calendar", "Календарь", "\uE787", "calendar"),
        ("reminders", "Напоминания", "\uE7E7", "calendar"), ("notes", "Заметки", "\uE70B", "notes"),
        ("timer", "Таймер", "\uE916", "timers"), ("focus", "Не беспокоить", "\uE708", "system"),
        ("system", "Система", "\uE770", "system"), ("cpu", "Процессор", "\uE964", "system"),
        ("gpu", "Видеокарта", "\uE7F4", "system"), ("ram", "Память", "\uE950", "system"),
        ("disk", "Диск", "\uEDA2", "system"), ("battery", "Батарея", "\uE850", "system"),
        ("clipboard", "Буфер обмена", "\uE77F", "clipboard"),
        ("month", "Месяц", "\uE787", "calendar"), ("world", "Мировое время", "\uE909", "timers"),
        ("stopwatch", "Секундомер", "\uE916", "timers"), ("calculator", "Калькулятор", "\uE8EF", ""),
        ("volume", "Громкость", "\uE767", "system"), ("network", "Сеть", "\uE701", "system"),
        ("quick", "Быстрые действия", "\uE945", "home"), ("uptime", "Время работы ПК", "\uE7F4", "system")
    ];
    private static readonly (string Key, string Name, string Background, string Text)[] WidgetThemes =
    [
        ("glass", "Матовое стекло", "#E529354A", "#FFFFFFFF"),
        ("dark", "Тёмная", "#F0191A20", "#FFFFFFFF"),
        ("light", "Светлая", "#F5F2F4F8", "#FF161B24"),
        ("ocean", "Океан", "#F0134C63", "#FFFFFFFF"),
        ("forest", "Лес", "#F028482B", "#FFFFFFFF"),
        ("plum", "Слива", "#F0532A62", "#FFFFFFFF"),
        ("sunset", "Закат", "#F075304C", "#FFFFFFFF"),
        ("indigo", "Индиго", "#F03C3B75", "#FFFFFFFF")
    ];

    private bool DesktopSystemWidgetVisible => WidgetIsVisible("gpu");
    private bool WidgetIsVisible(string key) => !_settings.WidgetsHidden && _widgetWindows.TryGetValue(key, out var widget)
        && widget.Window.IsVisible && widget.Window.WindowState != WindowState.Minimized;

    private void InitializeNotesWidgets()
    {
        LoadNotes();
        _settings.DesktopWidgets = (_settings.DesktopWidgets ?? []).Where(p => p != null && WidgetTypes.Any(t => t.Key == p.Kind))
            .DistinctBy(p => p.Kind).ToList();
        _desktopWidgetsTimer.Tick += (_, _) => RefreshDesktopWidgets();
        SyncDesktopWidgets();
    }

    private void DisposeDesktopWidgets()
    {
        _desktopWidgetsTimer.Stop();
        var windows = _widgetWindows.Values.ToArray();
        _widgetWindows.Clear();
        foreach (var widget in windows) widget.Window.Close();
    }

    private void BuildWidgetsPage()
    {
        var appearance = FeatureCard("Тема виджетов");
        var themes = new WrapPanel();
        appearance.Children.Add(themes);
        foreach (var theme in WidgetThemes)
        {
            var button = FeatureButton(theme.Name, () =>
            {
                _settings.WidgetTheme = theme.Key;
                SaveSettings();
                SyncDesktopWidgets();
                OpenFeaturePage("widgets");
            });
            button.Width = 35;
            button.Height = 35;
            button.Padding = new Thickness(0);
            button.Background = WidgetBackground(theme.Background);
            button.Foreground = WidgetBrush(theme.Text);
            button.ToolTip = L(theme.Name);
            System.Windows.Automation.AutomationProperties.SetName(button, L(theme.Name));
            button.Content = _settings.WidgetTheme == theme.Key ? "✓" : "";
            themes.Children.Add(button);
        }
        var toggles = new WrapPanel();
        appearance.Children.Add(toggles);
        var hideToggle = FeatureToggle(toggles, "Скрыть с рабочего стола", _settings.WidgetsHidden, value => { _settings.WidgetsHidden = value; SyncDesktopWidgets(); });
        hideToggle.Margin = new Thickness(0, 3, 18, 8);
        FeatureToggle(toggles, "Поверх всех окон", _settings.WidgetsTopmost, value => { _settings.WidgetsTopmost = value; SyncDesktopWidgets(); });
        var catalog = FeatureCard("На рабочем столе", string.Format(L("Добавлено: {0}"), _settings.DesktopWidgets.Count));
        var buttons = new WrapPanel();
        catalog.Children.Add(buttons);
        foreach (var type in WidgetTypes)
        {
            bool enabled = _settings.DesktopWidgets.Any(p => p.Kind == type.Key);
            var button = FeatureButton(type.Name, () =>
            {
                if (enabled) RemoveDesktopWidget(type.Key);
                else AddWidgetSet(type.Key);
            });
            var row = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
            row.Children.Add(new TextBlock { Text = type.Glyph, FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets"), Margin = new Thickness(0, 0, 7, 0) });
            row.Children.Add(new TextBlock { Text = L(type.Name) + (enabled ? " ✓" : "") });
            button.Content = row;
            System.Windows.Automation.AutomationProperties.SetName(button, L(type.Name));
            System.Windows.Automation.AutomationProperties.SetHelpText(button, L(enabled ? "Удалить виджет" : "Добавить виджет"));
            if (enabled)
            {
                button.SetResourceReference(Button.BackgroundProperty, "ThemeAccentBrush");
                button.SetResourceReference(Button.ForegroundProperty, "ThemeOnAccentBrush");
            }
            buttons.Children.Add(button);
        }
        var sets = FeatureCard("Готовые наборы");
        var setButtons = new WrapPanel();
        sets.Children.Add(setButtons);
        var day = FeatureButton("Мой день", () => AddWidgetSet("clock", "weather", "month", "music"));
        day.ToolTip = L("Часы, погода, календарь и музыка");
        setButtons.Children.Add(day);
        var work = FeatureButton("Работа", () => AddWidgetSet("calendar", "reminders", "notes", "timer", "focus"));
        work.ToolTip = L("Календарь, напоминания, заметки, таймер и фокус");
        setButtons.Children.Add(work);
    }

    private void AddWidgetSet(params string[] keys)
    {
        foreach (string key in keys)
            if (!_settings.DesktopWidgets.Any(p => p.Kind == key))
                _settings.DesktopWidgets.Add(new WidgetPlacement { Kind = key });
        _settings.WidgetsHidden = false;
        DesktopWidgetsChanged();
    }

    private void RemoveDesktopWidget(string key)
    {
        _settings.DesktopWidgets.RemoveAll(p => p.Kind == key);
        DesktopWidgetsChanged();
    }

    private void DesktopWidgetsChanged()
    {
        SaveSettings();
        SyncDesktopWidgets();
        if (_currentState == IslandState.Section && _featurePage == "widgets")
        {
            double offset = DashboardSectionView.VerticalOffset;
            DashboardSectionContent.Children.Clear();
            BuildWidgetsPage();
            DashboardSectionView.ScrollToVerticalOffset(offset);
        }
    }

    private void SyncDesktopWidgets()
    {
        var active = _settings.DesktopWidgets.Select(p => p.Kind).ToHashSet();
        foreach (string removed in _widgetWindows.Keys.Where(key => !active.Contains(key)).ToArray())
        {
            _widgetWindows.Remove(removed, out var widget);
            _nextWidgetReading.Remove(removed);
            widget!.Window.Close();
        }
        if (_settings.WidgetsHidden)
        {
            foreach (var widget in _widgetWindows.Values) widget.Window.Hide();
            UpdateVolumeObservation();
            _desktopWidgetsTimer.Stop();
            return;
        }
        var theme = WidgetThemes.FirstOrDefault(t => t.Key == _settings.WidgetTheme, WidgetThemes[0]);
        var background = WidgetBackground(theme.Background);
        var foreground = WidgetBrush(theme.Text);
        var edge = WidgetBrush(theme.Key == "light" ? "#28263249" : "#52D3E1FF");
        var buttonBrush = WidgetBrush(theme.Key == "light" ? "#162C3763" : "#32FFFFFF");
        for (int index = 0; index < _settings.DesktopWidgets.Count; index++)
        {
            var placement = _settings.DesktopWidgets[index];
            if (!_widgetWindows.TryGetValue(placement.Kind, out var widget))
                _widgetWindows[placement.Kind] = widget = CreateWidgetWindow(placement, index);
            widget.Card.Background = background;
            widget.Card.BorderBrush = edge;
            widget.Title.Foreground = foreground;
            widget.Body.Foreground = foreground;
            widget.Window.Resources["ThemeTextBrush"] = foreground;
            widget.Window.Resources["ThemeInputBrush"] = buttonBrush;
            widget.Window.Topmost = _settings.WidgetsTopmost;
            if (!widget.Window.IsVisible) widget.Window.Show();
        }
        _nextWidgetReading.Clear();
        UpdateVolumeObservation();
        RefreshDesktopWidgets();
    }

    private WidgetWindow CreateWidgetWindow(WidgetPlacement placement, int index)
    {
        var type = WidgetTypes.First(t => t.Key == placement.Kind);
        var (width, height) = placement.Kind switch
        {
            "calculator" => (276d, 322d), "month" => (248d, 232d), "music" => (296d, 144d),
            "world" or "quick" => (230d, 145d), "stopwatch" or "volume" => (210d, 136d), _ => (202d, 115d)
        };
        var work = SystemParameters.WorkArea;
        var left = placement.X ?? double.NaN;
        var top = placement.Y ?? double.NaN;
        var desktop = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        if (!double.IsFinite(left) || !double.IsFinite(top) || !desktop.Contains(new System.Windows.Point(left + width / 2, top + height / 2)))
            (left, top) = FindWidgetPosition(work, width, height, index);
        var window = new Window
        {
            Title = L(type.Name), WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent,
            ShowInTaskbar = false, ShowActivated = false, ResizeMode = ResizeMode.NoResize, Width = width, Height = height,
            Left = left, Top = top, WindowStartupLocation = WindowStartupLocation.Manual
        };
        var card = new Border { CornerRadius = new CornerRadius(23), BorderThickness = new Thickness(1), Padding = new Thickness(14), SnapsToDevicePixels = true };
        var title = new TextBlock { Text = L(type.Name), Opacity = .88, FontSize = 12, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) };
        var body = new TextBlock { FontSize = placement.Kind is "clock" or "timer" or "stopwatch" or "volume" ? 26 : 14, TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.Wrap, ClipToBounds = true };
        var content = new DockPanel();
        DockPanel.SetDock(title, Dock.Top);
        content.Children.Add(title);
        AddWidgetControls(placement.Kind, content, body);
        content.Children.Add(body);
        card.Child = content;
        window.Content = card;
        window.StateChanged += (_, _) =>
        {
            _nextWidgetReading.Remove(placement.Kind);
            RefreshDesktopWidgets();
        };
        card.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2 && type.Page.Length > 0) { OpenWidgetPage(type.Page); return; }
            try { window.DragMove(); }
            catch (InvalidOperationException) { return; }
            if (placement.X == window.Left && placement.Y == window.Top) return;
            placement.X = window.Left;
            placement.Y = window.Top;
            SaveSettings();
        };
        var menu = new System.Windows.Controls.ContextMenu { Style = (Style)FindResource("IslandContextMenu"), MinWidth = 245 };
        ApplyPopupPalette(menu);
        menu.Opened += (_, _) => ApplyPopupPalette(menu);
        if (type.Page.Length > 0) AddMenu("Открыть раздел", "\uE8A7", () => OpenWidgetPage(type.Page));
        if (placement.Kind == "music")
        {
            menu.Items.Add(new System.Windows.Controls.Separator { Style = (Style)FindResource("IslandMenuSeparator") });
            AddMenu("Предыдущий трек", "\uE892", () => PrevTrack_Click(this, new RoutedEventArgs()));
            AddMenu("Воспроизвести / пауза", "\uE768", () => PlayPause_Click(this, new RoutedEventArgs()));
            AddMenu("Следующий трек", "\uE893", () => NextTrack_Click(this, new RoutedEventArgs()));
        }
        if (placement.Kind == "timer")
            AddMenu("Старт / пауза", "\uE768", () => { _dashboardTimer.Toggle(); RefreshDesktopWidgets(); });
        if (menu.Items.Count > 0) menu.Items.Add(new System.Windows.Controls.Separator { Style = (Style)FindResource("IslandMenuSeparator") });
        AddMenu("Удалить виджет", "\uE74D", () => RemoveDesktopWidget(placement.Kind))
            .SetResourceReference(System.Windows.Controls.MenuItem.ForegroundProperty, "ThemeDangerBrush");
        card.ContextMenu = menu;
        window.Closing += (_, e) =>
        {
            if (_windowClosed || !_widgetWindows.ContainsKey(placement.Kind)) return;
            e.Cancel = true;
            Dispatcher.BeginInvoke(() => RemoveDesktopWidget(placement.Kind));
        };
        return new(window, card, title, body);

        System.Windows.Controls.MenuItem AddMenu(string text, string glyph, Action select)
        {
            var item = new System.Windows.Controls.MenuItem { Header = L(text), Tag = glyph, Style = (Style)FindResource("IslandMenuItem") };
            menu.Opened += (_, _) => item.Header = L(text);
            item.Click += (_, _) => select();
            menu.Items.Add(item);
            return item;
        }
    }

    private (double Left, double Top) FindWidgetPosition(Rect workArea, double width, double height, int index)
    {
        const double gap = 12;
        var occupied = _widgetWindows.Values.Select(widget =>
            new Rect(widget.Window.Left, widget.Window.Top, widget.Window.Width + gap, widget.Window.Height + gap)).ToArray();
        for (double left = workArea.Right - width - gap; left >= workArea.Left + gap; left -= 310)
        {
            for (double top = workArea.Top + gap; top + height + gap <= workArea.Bottom; top += 24)
            {
                var bounds = new Rect(left, top, width + gap, height + gap);
                if (!occupied.Any(bounds.IntersectsWith)) return (left, top);
            }
        }
        return (Math.Max(workArea.Left, workArea.Right - width - gap - index % 7 * 17),
            Math.Max(workArea.Top, Math.Min(workArea.Bottom - height, workArea.Top + gap + index % 7 * 20)));
    }

    private void OpenWidgetPage(string page)
    {
        ShowIsland();
        if (page == "weather") MorphToState(IslandState.Weather);
        else if (page == "clipboard") MorphToState(IslandState.Clipboard);
        else if (page == "home") MorphToState(IslandState.Expanded);
        else OpenFeaturePage(page);
        Activate();
    }

    private void RefreshDesktopWidgets()
    {
        if (_settings.WidgetsHidden || _widgetWindows.Count == 0)
        {
            _desktopWidgetsTimer.Stop();
            return;
        }
        var now = DateTimeOffset.Now;
        DateTimeOffset? nextTick = null;
        foreach (var (key, widget) in _widgetWindows)
        {
            if (!WidgetIsVisible(key) || key is "calculator" or "quick") continue;
            TimeSpan? interval = key switch
            {
                "volume" or "notes" => null,
                "stopwatch" when !_widgetStopwatch.Running => null,
                "clock" or "world" => TimeSpan.FromMilliseconds(60000 - now.Second * 1000 - now.Millisecond),
                "disk" or "month" => TimeSpan.FromSeconds(30),
                "battery" or "weather" or "calendar" or "uptime" => TimeSpan.FromSeconds(10),
                "network" => TimeSpan.FromSeconds(2),
                _ => TimeSpan.FromSeconds(1)
            };
            if (interval.HasValue && _nextWidgetReading.TryGetValue(key, out var next) && now < next)
            {
                Schedule(next);
                continue;
            }
            if (interval.HasValue)
            {
                var due = now.Add(interval.Value);
                _nextWidgetReading[key] = due;
                Schedule(due);
            }
            string value = key switch
            {
                "clock" => now.ToString("HH:mm\nddd, dd MMM"),
                "weather" => _hasWeatherData ? $"{FormatTempShort(_lastTempC)}\n{_resolvedCityName}" : L("Погода недоступна"),
                "music" => _widgetTrack.Length == 0 ? L("Нет активного трека") : $"{_widgetTrack}\n{_widgetArtist}",
                "calendar" => _meetings.FirstOrDefault(m => m.End >= now) is { } meeting ? $"{meeting.Start.ToLocalTime():ddd HH:mm}\n{meeting.Title}" : now.ToString("dddd\ndd MMMM"),
                "reminders" => _settings.Timers.Where(t => t.Due > now).MinBy(t => t.Due) is { } timer ? $"{timer.Due.ToLocalTime():ddd HH:mm}\n{timer.Label}" : L("Нет напоминаний"),
                "notes" => _notes.Count > 0 ? _notes[0].Text : L("Нет заметок"),
                "timer" => _dashboardTimer.Display,
                "focus" => L(!_dndStateKnown ? "Состояние недоступно" : _isDndActive ? "Включено" : "Выключено"),
                "system" => $"{L("Процессор")}  {UsageDisplay(_lastCpuUsage)}\n{L("Память")}  {MemoryDisplay(SystemOverview.Memory())}",
                "cpu" => UsageDisplay(_lastCpuUsage),
                "gpu" => UsageDisplay(_lastGpuUsage),
                "ram" => MemoryDisplay(SystemOverview.Memory()),
                "disk" => SystemDriveDisplay(),
                "battery" => BatteryDisplay(),
                "clipboard" => string.Format(L("Элементов в истории: {0}"), _clipboardHistory.Count),
                "month" => WidgetMonth(now.LocalDateTime),
                "world" => WidgetWorldTime(now),
                "stopwatch" => _widgetStopwatch.Display,
                "volume" => _widgetVolume is { } volume ? volume.Muted ? L("Звук выключен") : $"{volume.Volume:P0}" : "—",
                "network" => WidgetNetworkStatus(),
                "uptime" => WidgetUptime(),
                _ => ""
            };
            if (widget.Body.Text != value) widget.Body.Text = value;
        }
        if (WidgetIsVisible("network") && DateTime.UtcNow >= _nextConnectivityRead) _ = RefreshConnectivityAsync();
        if (nextTick is { } tick)
        {
            _desktopWidgetsTimer.Interval = TimeSpan.FromMilliseconds(Math.Clamp((tick - now).TotalMilliseconds, 80, 60000));
            _desktopWidgetsTimer.Start();
        }
        else _desktopWidgetsTimer.Stop();

        void Schedule(DateTimeOffset due)
        {
            if (!nextTick.HasValue || due < nextTick.Value) nextTick = due;
        }
    }

    private static LinearGradientBrush WidgetBackground(string value)
    {
        var color = (Color)System.Windows.Media.ColorConverter.ConvertFromString(value);
        var brush = new LinearGradientBrush(Blend(color, Colors.White, .17), Blend(color, Colors.Black, .23), 65);
        brush.GradientStops[0].Color = WithAlpha(brush.GradientStops[0].Color, color.A);
        brush.GradientStops[1].Color = WithAlpha(brush.GradientStops[1].Color, color.A);
        brush.Freeze();
        return brush;
    }

    private string SystemDriveDisplay()
    {
        try
        {
            var drive = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory)!);
            double free = drive.AvailableFreeSpace / (1024d * 1024 * 1024);
            return string.Format(L("{0:0.0} ГБ свободно"), free);
        }
        catch (IOException) { return "—"; }
        catch (UnauthorizedAccessException) { return "—"; }
    }

    private static SolidColorBrush WidgetBrush(string value)
    {
        var brush = new SolidColorBrush((Color)System.Windows.Media.ColorConverter.ConvertFromString(value));
        brush.Freeze();
        return brush;
    }
}
