using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DynamicIslandWin.Services;
using Microsoft.Win32;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using TextBox = System.Windows.Controls.TextBox;
using ComboBox = System.Windows.Controls.ComboBox;
using ListBox = System.Windows.Controls.ListBox;
using Clipboard = System.Windows.Clipboard;
using Orientation = System.Windows.Controls.Orientation;
using Brush = System.Windows.Media.Brush;
using Point = System.Windows.Point;
using Panel = System.Windows.Controls.Panel;
using Control = System.Windows.Controls.Control;
using DataObject = System.Windows.DataObject;

namespace DynamicIslandWin;

public partial class MainWindow
{
    private static readonly Dictionary<string, (string Label, string Icon)> DashboardSections = new()
    {
        ["home"] = ("Главная", "M1.5,7 L8,1.5 L14.5,7 M3,6 V14 H6 V10 H10 V14 H13 V6"),
        ["music"] = ("Музыка", "M6,12 V3 L14,1.5 V10 M6,12 A2.2,1.8 0 1 1 1.6,12 A2.2,1.8 0 1 1 6,12 M14,10 A2.2,1.8 0 1 1 9.6,10 A2.2,1.8 0 1 1 14,10 M6,5.5 L14,4"),
        ["weather"] = ("Погода", "M8,1 V2.5 M8,13.5 V15 M1,8 H2.5 M13.5,8 H15 M3,3 L4,4 M12,12 L13,13 M13,3 L12,4 M4,12 L3,13 M11,8 A3,3 0 1 1 5,8 A3,3 0 1 1 11,8"),
        ["chat"] = ("Чат", "M2,4 Q2,2 4,2 H12 Q14,2 14,4 V10 Q14,12 12,12 H6 L2,14 Z M5,6 H11 M5,8.5 H9"),
        ["timers"] = ("Таймеры", "M8,4 A5.5,5.5 0 1 1 8,15 A5.5,5.5 0 1 1 8,4 M6,1 H10 M8,1 V4 M8,7 V10 L10,11"),
        ["clipboard"] = ("Буфер обмена", "M5,3 H3 V14 H13 V3 H11 M5,2 H7 L8,1 L9,2 H11 V5 H5 Z M5.5,8 H10.5 M5.5,11 H10.5"),
        ["notes"] = ("Заметки", "M3,2 H13 V14 H3 Z M5,5 H11 M5,8 H11 M5,11 H9"),
        ["calendar"] = ("Календарь", "M2,3 H14 V14 H2 Z M5,1 V5 M11,1 V5 M2,7 H14 M5,10 H6 M8,10 H9 M11,10 H12"),
        ["system"] = ("Система", "M6,5 H10 C13,5 14,7 14.5,10 C15,13 13,14 11,12 L9.5,11 H6.5 L5,12 C3,14 1,13 1.5,10 C2,7 3,5 6,5 Z M4,8 H8 M6,6 V10 M11,8 H12"),
        ["widgets"] = ("Виджеты", "M2,2 H6 V6 H2 Z M8,2 H14 V6 H8 Z M2,8 H6 V14 H2 Z M8,8 H14 V14 H8 Z")
    };
    private string? _settingsFeaturePage;
    private TextBlock FeatureText(string text, double size = 12, string color = "ThemeTextBrush")
    {
        var block = new TextBlock { Text = L(text), FontSize = size, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
        block.SetResourceReference(TextBlock.ForegroundProperty, color); return block;
    }
    private Button FeatureButton(string text, Action action)
    {
        var button = new Button { Content = L(text), Style = (Style)FindResource("GlassPillBtn"), HorizontalAlignment = System.Windows.HorizontalAlignment.Left, Margin = new Thickness(0, 0, 8, 8), Padding = new Thickness(12, 8, 12, 8) };
        button.Click += (_, e) => { e.Handled = true; try { action(); } catch (Exception ex) { AppLog.Error("Feature action", ex); FeatureNotice("Не удалось выполнить действие. Подробности — в диагностике.", () => OpenSettingsFeaturePage("diagnostics")); } };
        return button;
    }
    private StackPanel FeatureCard(string title, string? subtitle = null, Panel? parent = null)
    {
        var content = new StackPanel();
        if (title.Length > 0)
        {
            var heading = FeatureText(title, 16);
            heading.FontWeight = FontWeights.SemiBold;
            content.Children.Add(heading);
        }
        if (subtitle != null) content.Children.Add(FeatureText(subtitle, 11, "ThemeMutedBrush"));
        (parent ?? DashboardSectionContent).Children.Add(new Border { Style = (Style)FindResource("GlassSectionCard"), Child = content }); return content;
    }
    private TextBox FeatureInput(Panel parent, string label, string value)
    {
        parent.Children.Add(FeatureText(label, 11, "ThemeMutedBrush"));
        var input = new TextBox { Text = L(value), FontSize = 13, BorderThickness = new Thickness(0), Padding = new Thickness(10), Margin = new Thickness(0, 0, 0, 12), MinWidth = 100 };
        input.SetResourceReference(Control.ForegroundProperty, "ThemeTextBrush"); input.SetResourceReference(Control.BackgroundProperty, "ThemeInputBrush");
        input.SetResourceReference(TextBox.CaretBrushProperty, "ThemeTextBrush"); parent.Children.Add(input); return input;
    }
    private CheckBox FeatureToggle(Panel parent, string text, bool value, Action<bool> changed)
    {
        var toggle = new CheckBox { Content = L(text), IsChecked = value, Margin = new Thickness(0, 3, 0, 12), FontSize = 12 };
        toggle.SetResourceReference(Control.ForegroundProperty, "ThemeTextBrush");
        toggle.Click += (_, _) => { changed(toggle.IsChecked == true); ApplyFeatureAppearance(); SaveSettings(); };
        parent.Children.Add(toggle);
        return toggle;
    }
    private void FeatureSlider(Panel parent, string text, double min, double max, double value, Action<double> changed, bool integer = false)
    {
        text = L(text);
        var label = FeatureText($"{text} · {value:0.##}", 12); parent.Children.Add(label);
        var slider = new Slider { Minimum = min, Maximum = max, Value = Math.Clamp(value, min, max), TickFrequency = 1, IsSnapToTickEnabled = integer, Margin = new Thickness(0, 0, 0, 12) };
        slider.ValueChanged += (_, _) => { label.Text = $"{text} · {slider.Value:0.##}"; changed(slider.Value); ApplyFeatureAppearance(); SaveSettings(); };
        parent.Children.Add(slider);
    }
    private void FeatureChoice(Panel parent, string label, (string Key, string Label)[] options, string current, Action<string> changed)
    {
        parent.Children.Add(FeatureText(label, 11, "ThemeMutedBrush"));
        var combo = new ComboBox { Style = (Style)FindResource("FeatureCombo"), ItemsSource = options.Select(o => L(o.Label)), SelectedIndex = Math.Max(0, Array.FindIndex(options, o => o.Key == current)), Margin = new Thickness(0, 0, 0, 14), Padding = new Thickness(6), FontSize = 12, MinHeight = 36 };
        combo.SelectionChanged += (_, _) => { if (combo.SelectedIndex < 0) return; changed(options[combo.SelectedIndex].Key); ApplyFeatureAppearance(); SaveSettings(); };
        parent.Children.Add(combo);
    }
    private void OpenLayout_Click(object sender, RoutedEventArgs e) { e.Handled = true; OpenSettingsFeaturePage("layout"); }
    private void OpenDiagnostics_Click(object sender, RoutedEventArgs e) { e.Handled = true; OpenSettingsFeaturePage("diagnostics"); }
    private void OpenThemes_Click(object sender, RoutedEventArgs e) { e.Handled = true; OpenSettingsFeaturePage("themes"); }
    private void OpenUpdates_Click(object sender, RoutedEventArgs e) { e.Handled = true; OpenSettingsFeaturePage("updates"); }

    private void OpenSettingsFeaturePage(string page)
    {
        _settingsFeaturePage = page;
        _manualResize = false; ResizeHint.Visibility = ResizeWidthThumb.Visibility = ResizeHeightThumb.Visibility = Visibility.Collapsed;
        MorphToState(IslandState.Settings);
        BuildSettingsFeaturePage(page);
        SwitchSettingsPage(SettingsFeatureScroll, page switch
        {
            "layout" => BtnSettingsLayout,
            "themes" => BtnSettingsThemes,
            "diagnostics" => BtnSettingsDiagnostics,
            _ => BtnSettingsUpdates
        });
        SettingsFeatureScroll.ScrollToTop();
    }

    private void BuildSettingsFeaturePage(string page)
    {
        SettingsFeatureContent.Children.Clear();
        switch (page)
        {
            case "layout": BuildLayoutPage(); break;
            case "themes": BuildUserThemesPage(); break;
            case "diagnostics": BuildDiagnosticsPage(); break;
            case "updates": BuildUpdatesPage(); break;
        }
    }

    private void RefreshUpdatesPage()
    {
        if (!_windowClosed && _currentState == IslandState.Settings && _settingsFeaturePage == "updates")
            BuildSettingsFeaturePage("updates");
    }

    private void BuildDashboardNavigation()
    {
        string active = _currentState switch
        {
            IslandState.Weather => "weather",
            IslandState.Clipboard => "clipboard",
            IslandState.Section => _featurePage,
            _ => "home"
        };
        string? focused = DashboardNavigation.Children.OfType<Button>().FirstOrDefault(button => button.IsKeyboardFocused)?.Tag as string;
        DashboardNavigation.Children.Clear();
        var order = DashboardSectionOrder();
        foreach (string key in order.Where(key => key == "home" || key == active || !_settings.HiddenTabs.Contains(key)))
        {
            var section = DashboardSections[key];
            string label = L(section.Label);
            var button = new Button
            {
                Tag = key, Style = (Style)FindResource("GlassPillBtn"), Background = System.Windows.Media.Brushes.Transparent,
                Padding = new Thickness(6, 0, 6, 0), MinWidth = 30, Height = 32, Margin = new Thickness(0, 0, 2, 0),
                ToolTip = label
            };
            System.Windows.Automation.AutomationProperties.SetName(button, label);
            var icon = new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse(section.Icon), Width = 16, Height = 16, StrokeThickness = 1.35,
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round,
                VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false
            };
            icon.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, key == active ? "ThemeTextBrush" : "ThemeSecondaryBrush");
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(icon);
            if (key == active)
            {
                button.SetResourceReference(Control.BackgroundProperty, "ThemeSelectionBrush");
                content.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) });
            }
            button.Content = content;
            button.Click += (_, e) =>
            {
                e.Handled = true;
                switch (key)
                {
                    case "home": MorphToState(IslandState.Expanded); break;
                    case "weather": MorphToState(IslandState.Weather); break;
                    case "clipboard": MorphToState(IslandState.Clipboard); break;
                    default:
                        if (_currentState != IslandState.Section || _featurePage != key) OpenFeaturePage(key);
                        break;
                }
            };
            DashboardNavigation.Children.Add(button);
            if (key == focused) button.Focus();
        }
    }

    private IEnumerable<string> DashboardSectionOrder() =>
        new[] { "home" }.Concat(_settings.TabOrder).Concat(DashboardSections.Keys).Distinct().Where(DashboardSections.ContainsKey);
    private void OpenFeaturePage(string page)
    {
        _featurePage = page;
        _manualResize = false; ResizeHint.Visibility = ResizeWidthThumb.Visibility = ResizeHeightThumb.Visibility = Visibility.Collapsed;
        bool changingView = _currentState != IslandState.Section;
        MorphToState(IslandState.Section);
        if (!changingView)
        {
            _targetH = DashboardPageHeight;
            StartIslandFrames();
            BuildDashboardNavigation();
        }
        DashboardSectionContent.Children.Clear(); DashboardSectionView.ScrollToTop();
        switch (page)
        {
            case "system": _ = BuildSystemPageAsync(); break;
            case "calendar": BuildCalendarPage(); break;
            case "timers": BuildTimersPage(); break;
            case "music": BuildMusicPage(); break;
            case "chat": BuildChatPage(); break;
            case "notes": BuildNotesPage(); break;
            case "widgets": BuildWidgetsPage(); break;
            case "welcome": BuildWelcomePage(); break;
        }
    }

    private double DashboardPageHeight => _featurePage switch
    {
        "timers" => 350,
        "notes" when _notes.Count == 0 => 235,
        "calendar" => 490,
        "system" or "widgets" => 555,
        _ => 485
    };

    private void BuildLayoutPage()
    {
        var pill = FeatureCard("Размер и форма", "Для ручной настройки потяните правый или нижний край свёрнутого островка.", parent: SettingsFeatureContent);
        pill.Children.Add(FeatureButton("Изменить мышью", () => StartManualResize(this, new RoutedEventArgs())));
        FeatureSlider(pill, "Ширина", 160, 650, _settings.CompactWidth, v => _settings.CompactWidth = v, true);
        FeatureSlider(pill, "Высота", 32, 100, _settings.CompactHeight, v => _settings.CompactHeight = v, true);
        FeatureSlider(pill, "Скругление", 0, 1, _settings.CornerRounding, v => _settings.CornerRounding = v);
        FeatureSlider(pill, "Непрозрачность в покое", .15, 1, _settings.IdleOpacity, v => _settings.IdleOpacity = v);
        FeatureToggle(pill, "Тень", _settings.IslandShadow, v => _settings.IslandShadow = v);
        pill.Children.Add(FeatureButton("Сбросить размер", () => { _settings.CompactWidth = 210; _settings.CompactHeight = 38; _settings.CornerRounding = 1; ApplyFeatureAppearance(); SaveSettings(); OpenSettingsFeaturePage("layout"); }));
        var content = FeatureCard("Что показывать в покое", parent: SettingsFeatureContent);
        FeatureChoice(content, "По центру", [("time", "Время"), ("both", "Время и дата"), ("date", "Дата")], _settings.IdleCenter, v => _settings.IdleCenter = v);
        FeatureChoice(content, "Справа", [("weather", "Погода"), ("battery", "Батарея"), ("none", "Ничего")], _settings.IdleRight, v => _settings.IdleRight = v);
        FeatureChoice(content, "Шрифт", [("Segoe UI", "Segoe UI"), ("Consolas", "Consolas"), ("Calibri", "Calibri"), ("Verdana", "Verdana")], _settings.IslandFont, v => _settings.IslandFont = v);
        FeatureToggle(content, "12-часовой формат", _settings.Clock12Hour, v => _settings.Clock12Hour = v);
        FeatureToggle(content, "Показывать секунды", _settings.ClockSeconds, v => _settings.ClockSeconds = v);
        FeatureSlider(content, "Задержка раскрытия, мс", 0, 2000, _settings.HoverDelayMs, v => _settings.HoverDelayMs = (int)v, true);
        FeatureToggle(content, "Звук таймеров и встреч", _settings.ReminderSound, v => _settings.ReminderSound = v);
        var behavior = FeatureCard("Поведение", parent: SettingsFeatureContent);
        FeatureToggle(behavior, "Показ громкости в динамическом острове", _settings.VolumeIslandHud ?? _settings.LevelHud, SetVolumeIslandHud);
        behavior.Children.Add(FeatureText("Заменяет индикатор Windows при нажатии клавиш громкости. Выключите, чтобы вернуть системный показ.", 11));
        FeatureToggle(behavior, "Показывать уровень яркости", _settings.LevelHud, v => _settings.LevelHud = v);
        FeatureToggle(behavior, "Следовать за курсором на другой монитор", _settings.FollowCursorMonitor, v => _settings.FollowCursorMonitor = v);
        FeatureToggle(behavior, "Счётчик непрочитанных без доступа к уведомлениям", _settings.TitleUnreadFallback, v => _settings.TitleUnreadFallback = v);
        FeatureToggle(behavior, "Прогресс других программ", _settings.ExternalProgress, SetExternalProgress);
        var tabs = FeatureCard("Разделы", "Перетащите строку для изменения порядка. Галочка показывает или скрывает раздел.", parent: SettingsFeatureContent);
        _settings.TabOrder = DashboardSectionOrder().ToList();
        BuildReorderList(tabs, _settings.TabOrder, DashboardSections.Keys.Where(key => key != "home"),
            key => DashboardSections[key].Label, key => !_settings.HiddenTabs.Contains(key),
            (key, visible) => { if (visible) _settings.HiddenTabs.Remove(key); else if (!_settings.HiddenTabs.Contains(key)) _settings.HiddenTabs.Add(key); BuildDashboardNavigation(); });
        var actions = FeatureCard("Кнопки главной", "Перетаскивайте строки. Скрытые кнопки можно вернуть в настройках главной.", parent: SettingsFeatureContent);
        BuildReorderList(actions, _settings.ActionOrder, new[] { "Region", "Full", "Translate", "Record", "Command", "Dnd" },
            key => key switch { "Region" => "Область экрана", "Full" => "Скриншот", "Translate" => "Перевод", "Record" => "Запись", "Command" => "Команда", _ => "Не беспокоить" },
            key => FindName("ChkAct" + key) is System.Windows.Controls.Primitives.ToggleButton b && b.IsChecked == true,
            (key, visible) => { if (FindName("ChkAct" + key) is System.Windows.Controls.Primitives.ToggleButton b) b.IsChecked = visible; ApplyHomeWidgetVisibility(); });
    }
    private void BuildReorderList(Panel parent, List<string> order, IEnumerable<string> known, Func<string, string> label, Func<string, bool> shown, Action<string, bool> toggle)
    {
        foreach (string key in known) if (!order.Contains(key)) order.Add(key);
        var list = new ListBox { Background = System.Windows.Media.Brushes.Transparent, BorderThickness = new Thickness(0), AllowDrop = true, MinHeight = 50 };
        void Fill()
        {
            list.Items.Clear();
            foreach (string key in order.Where(k => known.Contains(k)))
            {
                var check = new CheckBox { Content = "⠿   " + L(label(key)), IsChecked = shown(key), Tag = key, Padding = new Thickness(8), Margin = new Thickness(2) };
                check.SetResourceReference(Control.ForegroundProperty, "ThemeTextBrush");
                check.Click += (_, _) => { toggle(key, check.IsChecked == true); SaveSettings(); };
                list.Items.Add(new ListBoxItem { Content = check, Tag = key, HorizontalContentAlignment = System.Windows.HorizontalAlignment.Stretch });
            }
        }
        Point start = default;
        list.PreviewMouseLeftButtonDown += (_, e) => start = e.GetPosition(list);
        list.PreviewMouseMove += (_, e) =>
        {
            if (e.LeftButton != MouseButtonState.Pressed || (e.GetPosition(list) - start).Length < 8) return;
            var item = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);
            if (item?.Tag is string key) { e.Handled = true; System.Windows.DragDrop.DoDragDrop(item, new DataObject("IslandLayoutItem", key), System.Windows.DragDropEffects.Move); }
        };
        list.Drop += (_, e) =>
        {
            if (e.Data.GetData("IslandLayoutItem") is not string source || !order.Contains(source)) return;
            var target = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject)?.Tag as string;
            if (target == null || target == source || !order.Contains(target)) return;
            order.Remove(source); order.Insert(order.IndexOf(target), source); ApplyFeatureAppearance(); BuildDashboardNavigation(); SaveSettings(); Fill(); e.Handled = true;
        };
        Fill(); parent.Children.Add(list);
    }
    private static T? FindAncestor<T>(DependencyObject? node) where T : DependencyObject
    { while (node != null) { if (node is T found) return found; node = VisualTreeHelper.GetParent(node); } return null; }

    private void BuildUserThemesPage()
    {
        var card = FeatureCard("Ваша палитра", "Цвета задаются как #RRGGBB. Стекло и все дополнительные панели используют выбранную палитру.", parent: SettingsFeatureContent);
        var name = FeatureInput(card, "Название", "Моя тема");
        var island = FeatureInput(card, "Островок", "#142D38");
        var scene = FeatureInput(card, "Второй цвет", "#294653");
        var accent = FeatureInput(card, "Акцент", "#6EE7D5");
        bool light = false; FeatureToggle(card, "Светлая палитра", false, v => light = v);
        var status = FeatureText("", 11, "ThemeMutedBrush");
        void Add(UserTheme theme)
        {
            if (!ThemeCode.Valid(theme)) { status.Text = L("Проверьте название и цвета #RRGGBB."); return; }
            _settings.CustomThemes.RemoveAll(t => t.Name == theme.Name);
            _settings.CustomThemes.Add(theme); _settings.CustomThemes = _settings.CustomThemes.TakeLast(32).ToList();
            string key = "User: " + theme.Name;
            _themes.RemoveAll(t => t.Key == key); var preset = new ThemePreset(key, theme.Island, theme.Scene, theme.Accent, theme.Light); _themes.Add(preset);
            _settings.SelectedTheme = key; _settings.CustomPhotoPath = ""; CustomBgBrush.ImageSource = null;
            ApplyTheme(preset); BuildThemesGrid(); SaveSettings(); OpenSettingsFeaturePage("themes");
        }
        card.Children.Add(FeatureButton("Сохранить и применить", () => Add(new(name.Text.Trim(), island.Text.Trim(), scene.Text.Trim(), accent.Text.Trim(), light))));
        var import = FeatureInput(card, "Код темы", "");
        card.Children.Add(FeatureButton("Импортировать", () => { try { Add(ThemeCode.Import(import.Text.Trim())); } catch (FormatException ex) { status.Text = L(ex.Message); } })); card.Children.Add(status);
        var saved = FeatureCard("Сохранённые темы", parent: SettingsFeatureContent);
        if (_settings.CustomThemes.Count == 0) saved.Children.Add(FeatureText("Создайте первую тему выше.", 12, "ThemeMutedBrush"));
        foreach (var theme in _settings.CustomThemes.ToArray())
        {
            var row = new WrapPanel();
            var select = FeatureButton(theme.Name, () => { _settings.SelectedTheme = "User: " + theme.Name; ApplyTheme(_themes.Single(t => t.Key == _settings.SelectedTheme)); BuildThemesGrid(); SaveSettings(); });
            select.ContextMenu = CreateThemeCopyMenu(theme);
            row.Children.Add(select); row.Children.Add(FeatureButton("Код", () => Clipboard.SetText(ThemeCode.Export(theme))));
            row.Children.Add(FeatureButton("Удалить", () => { _settings.CustomThemes.Remove(theme); _themes.RemoveAll(t => t.Key == "User: " + theme.Name); if (_settings.SelectedTheme == "User: " + theme.Name) { _settings.SelectedTheme = "Midnight"; ApplyTheme(_themes[0]); } BuildThemesGrid(); SaveSettings(); OpenSettingsFeaturePage("themes"); })); saved.Children.Add(row);
        }
    }

    private ContextMenu CreateThemeCopyMenu(UserTheme theme)
    {
        var menu = new ContextMenu { Style = (Style)FindResource("IslandContextMenu"), ItemContainerStyle = (Style)FindResource("IslandMenuItem") };
        var copy = new MenuItem { Header = L("Копировать код темы") }; copy.Click += (_, _) => Clipboard.SetText(ThemeCode.Export(theme)); menu.Items.Add(copy);
        menu.Opened += (_, _) => { foreach (string key in Resources.Keys.OfType<string>()) if ((key.StartsWith("Theme") || key.StartsWith("Glass")) && Resources[key] is Brush brush) menu.Resources[key] = brush; };
        return menu;
    }
    private TextBlock? _gameMetricsStatus;
    private TextBlock? _gameTemperatureStatus;
    private Button? _gameModeButton;
    private bool? _temperatureDriverInstalled;

    private string GameTemperatureStatus()
    {
        if (_gameFps?.CpuTemperatureC is >= 0) return $"CPU: {_gameFps.CpuTemperatureC}°C";
        if ((_temperatureDriverInstalled ??= CpuTemperatureService.IsDriverInstalled) == false)
            return "Температура CPU недоступна: установите PawnIO в игровом центре.";
        if (_gameFps?.HasSample == true) return "Датчик CPU пока не вернул температуру.";
        return _gameFps?.IsRunning == true ? "Ожидаю показания датчика CPU." : "Включите игровой режим, чтобы измерить температуру CPU.";
    }

    private void RefreshGameMetricsControls()
    {
        if (_gameMetricsStatus != null)
            _gameMetricsStatus.Text = GameFpsStatus();
        if (_gameTemperatureStatus != null) _gameTemperatureStatus.Text = L(GameTemperatureStatus());
        if (_gameModeButton != null)
            _gameModeButton.Content = L(_isGameModeActive ? "Выключить игровой режим" : "Включить игровой режим");
    }

    private async Task BuildSystemPageAsync()
    {
        var card = BuildSystemOverview();
        var game = FeatureCard("Игровой центр");
        _gameMetricsStatus = FeatureText("", 11, "ThemeMutedBrush"); game.Children.Add(_gameMetricsStatus);
        _gameTemperatureStatus = FeatureText("", 11, "ThemeMutedBrush"); game.Children.Add(_gameTemperatureStatus);
        _gameModeButton = FeatureButton("Включить игровой режим", () => SetGameModeEnabled(!_isGameModeActive));
        game.Children.Add(_gameModeButton);
        FeatureToggle(game, "Автоматический HUD в полноэкранной игре", _settings.AutoGameHud, enabled =>
        {
            _settings.AutoGameHud = enabled;
            _pauseAutoGameHud = false;
        });
        _temperatureDriverInstalled = CpuTemperatureService.IsDriverInstalled;
        if (_temperatureDriverInstalled == false)
        {
            game.Children.Add(FeatureText("Для температуры CPU нужен драйвер PawnIO. Он включён в установщик; Windows запросит права администратора.", 11, "ThemeMutedBrush"));
            game.Children.Add(FeatureButton("Установить драйвер температуры CPU", async () =>
            {
                try
                {
                    await CpuTemperatureService.InstallDriverAsync();
                    if (_windowClosed) return;
                    _temperatureDriverInstalled = true;
                    if (_isGameModeActive) { StopGameMetrics(); StartGameMetrics(); }
                    if (_currentState == IslandState.Section && _featurePage == "system") OpenFeaturePage("system");
                }
                catch (Exception ex) { AppLog.Error("Install CPU temperature driver", ex); FeatureNotice("Не удалось установить драйвер температуры CPU.", () => OpenFeaturePage("system")); }
            }));
        }
        RefreshGameMetricsControls();
        game.Children.Add(FeatureText("FPS измеряется по событиям отправки кадров активной игры. Если игра не передаёт такие события, показание недоступно.", 11, "ThemeMutedBrush"));
        var placeholder = FeatureText("Читаю аудиоустройства…", 12, "ThemeMutedBrush"); card.Children.Add(placeholder);
        try
        {
            var result = await Task.Run(() => { using var audio = new DesktopAudioService(); return (Apps: audio.Apps(), Master: audio.Master(), Brightness: DesktopAudioService.Brightness()); });
            if (_currentState != IslandState.Section || _featurePage != "system" || !DashboardSectionContent.Children.Contains(card.Parent as UIElement)) return;
            card.Children.Remove(placeholder);
            FeatureSlider(card, "Общая громкость, %", 0, 100, result.Master.Volume * 100, v => { using var audio = new DesktopAudioService(); audio.SetMaster((float)v / 100); }, true);
            if (result.Brightness is { } brightness) FeatureSlider(card, "Яркость, %", 0, 100, brightness, v => _ = Task.Run(() => { try { DesktopAudioService.SetBrightness((int)v); } catch (Exception ex) { AppLog.Error("Set brightness", ex); } }), true);
            foreach (var session in result.Apps)
            {
                var row = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
                row.Children.Add(FeatureText(session.Name, 13));
                var mute = FeatureButton(session.Muted ? "Включить звук" : "Выключить звук", () => { using var audio = new DesktopAudioService(); audio.SetApp(session.Id, null, !session.Muted); OpenFeaturePage("system"); }); row.Children.Add(mute);
                FeatureSlider(row, "Громкость, %", 0, 100, session.Volume * 100, v => { using var audio = new DesktopAudioService(); audio.SetApp(session.Id, (float)v / 100, null); }, true);
                card.Children.Add(row);
            }
            if (result.Apps.Count == 0) card.Children.Add(FeatureText("Запустите воспроизведение в приложении, затем обновите список.", 11, "ThemeMutedBrush"));
            card.Children.Add(FeatureButton("Обновить список", () => OpenFeaturePage("system")));
        }
        catch (Exception ex) { AppLog.Error("Read audio mixer", ex); placeholder.Text = L("Нет доступного устройства вывода. Откройте настройки звука Windows."); card.Children.Add(FeatureButton("Настройки звука", () => OpenWebSettings("ms-settings:sound"))); }
    }
    private void BuildTimersPage() => BuildTimerPageControls();

    private void BuildMusicPage()
    {
        var card = FeatureCard("Музыка");
        foreach (var source in new[] { MediaTitleText, MediaArtistText })
        {
            var label = FeatureText("", source.FontSize);
            label.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(TextBlock.Text)) { Source = source });
            card.Children.Add(label);
        }
        var buttons = new WrapPanel();
        buttons.Children.Add(FeatureButton("⏮", () => PrevTrack_Click(this, new RoutedEventArgs(Button.ClickEvent))));
        buttons.Children.Add(FeatureButton("⏯", () => PlayPause_Click(this, new RoutedEventArgs(Button.ClickEvent))));
        buttons.Children.Add(FeatureButton("⏭", () => NextTrack_Click(this, new RoutedEventArgs(Button.ClickEvent))));
        card.Children.Add(buttons);

        var lyrics = FeatureCard("Текст песни");
        FeatureToggle(lyrics, "Показывать текст при включении музыки", _settings.Karaoke, enabled => _settings.Karaoke = enabled);
        FeatureToggle(lyrics, "Искать текст", _settings.OnlineLyrics, SetOnlineLyrics);
        lyrics.Children.Add(FeatureText("При включённом поиске LRCLIB получает название, исполнителя, альбом и длительность трека, если они доступны.", 11, "ThemeMutedBrush"));
        lyrics.Children.Add(FeatureButton("Импортировать LRC", async () => await ImportLyricsAsync()));
        lyrics.Children.Add(FeatureText("Импортированный файл применяется к текущему треку до закрытия приложения и имеет приоритет над поиском.", 11, "ThemeMutedBrush"));
        _lyricsStatusText = FeatureText("", 12, "ThemeMutedBrush");
        lyrics.Children.Add(_lyricsStatusText);
        RefreshLyricsStatus();
    }
    private void BuildChatPage()
    {
        var card = FeatureCard("Gemini Live", "Микрофон передаётся Google только после нажатия «Голос». Остановите сеанс той же кнопкой. Используется ваш ключ и тариф Gemini API.");
        var key = new PasswordBox { Padding = new Thickness(10), Margin = new Thickness(0, 0, 0, 10) };
        key.SetResourceReference(Control.BackgroundProperty, "ThemeInputBrush"); key.SetResourceReference(Control.ForegroundProperty, "ThemeTextBrush"); card.Children.Add(key);
        card.Children.Add(FeatureText(string.IsNullOrWhiteSpace(GeminiKeyStore.Read()) ? "Ключ не задан. Введите API-ключ выше или используйте GEMINI_API_KEY." : "Ключ сохранён для текущего пользователя Windows.", 11, "ThemeMutedBrush"));
        var keyButtons = new WrapPanel(); keyButtons.Children.Add(FeatureButton("Сохранить ключ", () => { GeminiKeyStore.Save(key.Password); key.Clear(); OpenFeaturePage("chat"); })); keyButtons.Children.Add(FeatureButton("Удалить ключ", () => { GeminiKeyStore.Save(""); OpenFeaturePage("chat"); })); card.Children.Add(keyButtons);
        var model = FeatureInput(card, "Модель Live API", _settings.GeminiModel);
        model.LostKeyboardFocus += (_, _) => { _settings.GeminiModel = model.Text.Trim(); SaveSettings(); };
        FeatureChoice(card, "Голос", [("Kore", "Kore"), ("Puck", "Puck"), ("Charon", "Charon"), ("Fenrir", "Fenrir"), ("Aoede", "Aoede")], _settings.GeminiVoice, v => _settings.GeminiVoice = v);
        FeatureSlider(card, "Громкость голоса", 0, 1, _settings.GeminiGain, v => { _settings.GeminiGain = v; if (_voice != null) _voice.Gain = (float)v; });
        _voiceStatusText = FeatureText(_voice?.Connected == true ? "Микрофон включён" : "Микрофон выключен", 12, "ThemeAccentBrush"); card.Children.Add(_voiceStatusText);
        card.Children.Add(FeatureButton(_voice == null ? "● Голос" : "■ Остановить голос", async () =>
        {
            if (_voice != null) { var previous = _voice; _voice = null; await previous.DisposeAsync(); OpenFeaturePage("chat"); return; }
            _settings.GeminiModel = model.Text.Trim(); SaveSettings();
            var voice = new GeminiVoiceService { Gain = (float)_settings.GeminiGain }; _voice = voice;
            voice.Status += message => Dispatcher.BeginInvoke(() => { if (ReferenceEquals(_voice, voice) && _voiceStatusText != null) _voiceStatusText.Text = L(message); });
            voice.Transcript += text => Dispatcher.BeginInvoke(() => { if (!ReferenceEquals(_voice, voice) || _voiceTranscript == null) return; _voiceTranscript.Text = (_voiceTranscript.Text + "\n" + text).Trim(); if (_voiceTranscript.Text.Length > 8000) _voiceTranscript.Text = _voiceTranscript.Text[^8000..]; });
            OpenFeaturePage("chat");
            try { await voice.StartAsync(GeminiKeyStore.Read(), _settings.GeminiModel, _settings.GeminiVoice); }
            catch (Exception ex) { AppLog.Error("Start Gemini voice", ex); bool current = ReferenceEquals(_voice, voice); if (current) _voice = null; await voice.DisposeAsync(); if (current && _voiceStatusText != null) _voiceStatusText.Text = L("Не удалось начать разговор. Проверьте ключ, модель, сеть и разрешение микрофона."); }
        }));
        var message = FeatureInput(card, "Сообщение во время голосового сеанса", "");
        card.Children.Add(FeatureButton("Отправить в сеанс", async () => { if (_voice?.Connected == true && !string.IsNullOrWhiteSpace(message.Text)) { try { await _voice.SendTextAsync(message.Text); message.Clear(); } catch (Exception ex) { AppLog.Error("Send Gemini text", ex); } } }));
        _voiceTranscript = FeatureText("", 12, "ThemeSecondaryBrush"); card.Children.Add(_voiceTranscript);
    }

    private List<(string Name, string Status, string? Fix)> DiagnosticRows() =>
    [
        ("Уведомления", _notifAccessGranted ? "Доступ Windows разрешён; исходные обработчики открывают чаты." : "Нет доступа Windows. Доступен счётчик заголовка, если мессенджер его публикует.", "ms-settings:privacy-notifications"),
        ("Музыка", _mediaManager != null ? "Системный канал медиа подключён." : "Нет подключения к системному каналу медиа.", null),
        ("Погода", _hasWeatherData ? "Данные получены." : string.IsNullOrWhiteSpace(_settings.WeatherCity) ? "Укажите город в настройках погоды." : "Данные ещё не получены. Проверьте интернет и город.", null),
        ("FPS", L("Интерфейс отрисовывается по необходимости.") + " " + L(_gameFps?.Status ?? "Игровой режим включает HUD, FPS и температуру CPU. Windows может запросить права администратора."), null),
        ("Стекло", _settings.LiquidGlass ? "Включено. Видимость при захвате настраивается отдельно." : "Выключено в настройках внешнего вида.", null),
        ("Автозапуск", _settings.RunOnStartup ? "Запрошен в настройках; Windows может ограничить запуск." : "Выключен.", "ms-settings:startupapps"),
        ("Обновления", string.IsNullOrWhiteSpace(_settings.UpdateRepository) ? "Укажите репозиторий GitHub в разделе обновлений." : "Источник задан. Доступность новой версии проверяется в разделе обновлений.", null),
        ("Календарь", File.Exists(CalendarPath) ? string.Format(L("Подключён .ics, событий на 30 дней: {0}."), _meetings.Count) : "Импортируйте файл .ics на странице календаря.", null),
        ("Яркость", _lastBrightness.HasValue ? "Экран поддерживает системную регулировку." : "Экран не публикует яркость через Windows WMI.", null),
        ("Gemini", string.IsNullOrWhiteSpace(GeminiKeyStore.Read()) ? "Нужен ваш API-ключ." : _voice?.Connected == true ? "Голосовой сеанс активен." : "Ключ найден; соединение проверяется при запуске голоса.", "ms-settings:privacy-microphone")
    ];
    private void BuildDiagnosticsPage()
    {
        var header = FeatureCard("Проверка системы", "Отчёт содержит технические статусы, без сообщений, имён собеседников и API-ключей.", parent: SettingsFeatureContent);
        header.Children.Add(FeatureButton("Копировать отчёт", () => Clipboard.SetText($"OC - Dynamic Island {ProductVersion}\nWindows " + Environment.OSVersion.Version + "\n" + string.Join("\n", DiagnosticRows().Select(r => L(r.Name) + ": " + L(r.Status))))));
        header.Children.Add(FeatureButton("Повторить проверку", () => OpenSettingsFeaturePage("diagnostics")));
        foreach (var row in DiagnosticRows())
        {
            var card = FeatureCard(row.Name, row.Status, parent: SettingsFeatureContent);
            if (row.Fix != null) card.Children.Add(FeatureButton("Открыть настройки Windows", () => OpenWebSettings(row.Fix)));
            else if (row.Name is "Календарь" or "Gemini") card.Children.Add(FeatureButton("Настроить", () => OpenFeaturePage(row.Name == "Календарь" ? "calendar" : "chat")));
            else if (row.Name == "Погода") card.Children.Add(FeatureButton("Указать город", () => { MorphToState(IslandState.Settings); SwitchSettingsPage(PageWeather, NavWeatherBtn); }));
            else if (row.Name == "Музыка") card.Children.Add(FeatureButton("Переподключить", () => _ = InitializeMediaManagerAsync()));
            else if (row.Name == "FPS") card.Children.Add(FeatureButton("Открыть игровой центр", () => OpenFeaturePage("system")));
            else if (row.Name == "Обновления") card.Children.Add(FeatureButton("Настроить", () => OpenSettingsFeaturePage("updates")));
        }
    }
    private static void OpenWebSettings(string uri) => Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
    private GitHubUpdateService.Release? _availableUpdate;
    private string? _downloadedUpdate;
    private bool _updateBusy;
    private bool _checkingUpdates;
    private string _updateStatus = "Версия приложения · " + GitHubUpdateService.CurrentVersion.ToString(3);
    private CancellationTokenSource? _updateCancellation;
    private DateTime _nextUpdateCheck = DateTime.MaxValue;
    private readonly HashSet<(Uri Installer, Version Version)> _notifiedUpdates = [];

    private async Task CheckForUpdatesAsync(string repository, bool automatic)
    {
        if (_updateBusy || _windowClosed) return;
        _updateBusy = _checkingUpdates = true;
        _nextUpdateCheck = DateTime.UtcNow.AddMinutes(30);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        _updateCancellation = cancellation;
        try
        {
            if (!automatic) RefreshUpdatesPage();
            string normalized = GitHubUpdateService.Repository(repository);
            if (!automatic && !string.Equals(normalized, _settings.UpdateRepository, StringComparison.OrdinalIgnoreCase))
            {
                _settings.UpdateRepository = normalized;
                _availableUpdate = null;
                _downloadedUpdate = null;
                SaveSettings();
            }
            var release = await GitHubUpdateService.CheckAsync(normalized, cancellation.Token);
            if (_windowClosed || automatic && !_settings.AutoCheckUpdates) return;
            _nextUpdateCheck = DateTime.UtcNow.AddHours(6);
            var available = GitHubUpdateService.IsNewerVersion(release.Version, GitHubUpdateService.CurrentVersion) ? release : null;
            if (_availableUpdate != available) _downloadedUpdate = null;
            _availableUpdate = available;
            _updateStatus = _downloadedUpdate != null ? "Установщик загружен; SHA-256 и подпись издателя проверены."
                : available != null ? "Доступна версия " + available.Version : "Установлена актуальная версия.";
            if (available != null && _notifiedUpdates.Add((available.Installer, available.Version)) && automatic)
                FeatureNotice(_updateStatus, () => OpenSettingsFeaturePage("updates"));
        }
        catch (FormatException ex) { if (!automatic) _updateStatus = ex.Message; }
        catch (Exception ex)
        {
            if (!_windowClosed && !cancellation.IsCancellationRequested) AppLog.Error("Check GitHub update", ex);
            if (!automatic) _updateStatus = "Не удалось проверить релиз. Проверьте адрес публичного репозитория и интернет.";
        }
        finally
        {
            _updateCancellation = null;
            _updateBusy = _checkingUpdates = false;
            RefreshUpdatesPage();
        }
    }

    private void BuildUpdatesPage()
    {
        var card = FeatureCard("Обновления через GitHub", "Обновления OC - Dynamic Island загружаются из GitHub. Дополнительные программы не нужны. Проверяются контрольная сумма и подпись установщика ocdynamicisland.exe.", parent: SettingsFeatureContent);
        var repo = FeatureInput(card, "Репозиторий owner/repository", _settings.UpdateRepository);
        FeatureToggle(card, "Проверять обновления автоматически", _settings.AutoCheckUpdates, enabled =>
        {
            _settings.AutoCheckUpdates = enabled;
            if (enabled) _nextUpdateCheck = DateTime.UtcNow;
        });
        card.Children.Add(FeatureText("Проверка выполняется при запуске и периодически. Загрузка и установка — только вручную.", 11, "ThemeMutedBrush"));
        var status = FeatureText(_checkingUpdates ? "Проверяю опубликованные релизы…" : _updateStatus, 12, "ThemeMutedBrush"); card.Children.Add(status);
        var actions = new WrapPanel();
        actions.Children.Add(FeatureButton("Проверить", async () => await CheckForUpdatesAsync(repo.Text, automatic: false)));
        card.Children.Add(actions);
        if (_availableUpdate is { } release)
        {
            card.Children.Add(FeatureText(release.Name, 14));
            card.Children.Add(FeatureButton("Открыть релиз", () => OpenWeb(release.Page)));
            if (_downloadedUpdate == null) card.Children.Add(FeatureButton("Загрузить и проверить", async () =>
            {
                if (_updateBusy) return;
                _updateBusy = true; _updateCancellation = new CancellationTokenSource();
                try
                {
                    var progress = new Progress<double>(value => { _updateStatus = $"Загрузка · {value:0}%"; status.Text = L(_updateStatus); });
                    _downloadedUpdate = await GitHubUpdateService.DownloadAsync(release, progress, _updateCancellation.Token);
                    _updateStatus = "Установщик загружен; SHA-256 и подпись издателя проверены.";
                }
                catch (System.Security.Cryptography.CryptographicException ex) { _updateStatus = ex.Message; }
                catch (FormatException ex) { _updateStatus = ex.Message; }
                catch (OperationCanceledException) { _updateStatus = "Загрузка отменена."; }
                catch (Exception ex) { AppLog.Error("Download update", ex); _updateStatus = "Не удалось загрузить и проверить обновление."; }
                finally { _updateBusy = false; _updateCancellation?.Dispose(); _updateCancellation = null; RefreshUpdatesPage(); }
            }));
            else card.Children.Add(FeatureButton("Установить обновление", async () =>
            {
                if (_updateBusy) return;
                _updateBusy = true;
                try
                {
                    await GitHubUpdateService.StartInstallerAsync(_downloadedUpdate, CancellationToken.None);
                    _updateStatus = "Установщик запущен. Приложение закроется на время обновления.";
                }
                catch (Exception ex) { AppLog.Error("Start update installer", ex); _updateStatus = "Не удалось запустить установщик обновления. Повторите попытку или откройте релиз."; }
                finally { _updateBusy = false; RefreshUpdatesPage(); }
            }));
        }
        if (_updateBusy) card.Children.Add(FeatureButton("Отменить", () => _updateCancellation?.Cancel()));
        BuildWhatsNewSection();
    }
    private void BuildWhatsNewSection()
    {
        var card = FeatureCard("Что нового", parent: SettingsFeatureContent);
        foreach (string line in new[]
        {
            "Голосовой звонок определяется даже при свёрнутом Discord.",
            "Таймер показывает время с момента подключения. Нажмите на него, чтобы открыть Discord.",
            "Кнопка наушников выбирает выход Discord, а кнопка микрофона переключает устройство записи в Windows."
        })
            card.Children.Add(FeatureText("• " + L(line), 13));
        card.Children.Add(FeatureButton("Канал проекта", () => OpenWeb(new Uri("https://t.me/outsidecompany"))));
    }
    private void BuildWelcomePage()
    {
        var card = FeatureCard(string.Format(L("Добро пожаловать · шаг {0} из 3"), _welcomeStep + 1), _welcomeStep switch { 0 => "Выберите характер островка.", 1 => "Решите, что показывать рядом с часами.", _ => "Наведите курсор для предпросмотра. Нажмите для раскрытия. Правой кнопкой откройте меню. Ctrl+Alt+O — показать островок." });
        if (_welcomeStep == 0)
        {
            FeatureToggle(card, "Liquid Glass", _settings.LiquidGlass, v => { _settings.LiquidGlass = v; TglLiquidGlass.IsChecked = v; ApplyTheme(_themes.First(t => t.Key == _settings.SelectedTheme)); });
            FeatureChoice(card, "Персонаж", [("Eyes", "Глазки"), ("Puppy", "Щенок"), ("Fox", "Лиса"), ("None", "Без персонажа")], _settings.SelectedCharacter, v => { _settings.SelectedCharacter = v; UpdateCharacterVectors(v); });
        }
        if (_welcomeStep == 1) FeatureChoice(card, "Справа от часов", [("weather", "Погода"), ("battery", "Батарея"), ("none", "Ничего")], _settings.IdleRight, v => _settings.IdleRight = v);
        card.Children.Add(FeatureButton(_welcomeStep < 2 ? "Далее" : "Начать", () =>
        {
            if (_welcomeStep < 2) { _welcomeStep++; OpenFeaturePage("welcome"); }
            else { _settings.OnboardingComplete = true; _settings.LastSeenVersion = ProductVersion; SaveSettings(); MorphToState(IslandState.Compact); }
        }));
    }
}
