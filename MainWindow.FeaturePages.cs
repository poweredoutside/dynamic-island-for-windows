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
    private static readonly Dictionary<string, string> FeatureNames = new()
    {
        ["home"] = "⌂  Главная", ["system"] = "◉  Система", ["calendar"] = "▦  Календарь", ["timers"] = "◷  Таймеры",
        ["music"] = "♫  Музыка", ["chat"] = "✧  Чат", ["layout"] = "▤  Раскладка", ["themes"] = "◈  Своя тема",
        ["diagnostics"] = "✓  Диагностика", ["updates"] = "↓  Обновления", ["news"] = "↗  Что нового"
    };
    private TextBlock FeatureText(string text, double size = 12, string color = "ThemeTextBrush")
    {
        var block = new TextBlock { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
        block.SetResourceReference(TextBlock.ForegroundProperty, color); return block;
    }
    private Button FeatureButton(string text, Action action)
    {
        var button = new Button { Content = text, Style = (Style)FindResource("GlassPillBtn"), HorizontalAlignment = System.Windows.HorizontalAlignment.Left, Margin = new Thickness(0, 0, 8, 8), Padding = new Thickness(12, 8, 12, 8) };
        button.Click += (_, e) => { e.Handled = true; try { action(); } catch (Exception ex) { AppLog.Error("Feature action", ex); FeatureNotice("Не удалось выполнить действие. Подробности — в диагностике.", () => OpenFeaturePage("diagnostics")); } };
        return button;
    }
    private StackPanel FeatureCard(string title, string? subtitle = null)
    {
        var content = new StackPanel();
        var heading = FeatureText(title, 16); heading.FontWeight = FontWeights.SemiBold; content.Children.Add(heading);
        if (subtitle != null) content.Children.Add(FeatureText(subtitle, 11, "ThemeMutedBrush"));
        FeatureContent.Children.Add(new Border { Style = (Style)FindResource("GlassSectionCard"), Child = content }); return content;
    }
    private TextBox FeatureInput(Panel parent, string label, string value)
    {
        parent.Children.Add(FeatureText(label, 11, "ThemeMutedBrush"));
        var input = new TextBox { Text = value, FontSize = 13, BorderThickness = new Thickness(0), Padding = new Thickness(10), Margin = new Thickness(0, 0, 0, 12), MinWidth = 100 };
        input.SetResourceReference(Control.ForegroundProperty, "ThemeTextBrush"); input.SetResourceReference(Control.BackgroundProperty, "ThemeInputBrush");
        input.SetResourceReference(TextBox.CaretBrushProperty, "ThemeTextBrush"); parent.Children.Add(input); return input;
    }
    private void FeatureToggle(Panel parent, string text, bool value, Action<bool> changed)
    {
        var toggle = new CheckBox { Content = text, IsChecked = value, Margin = new Thickness(0, 3, 0, 12), FontSize = 12 };
        toggle.SetResourceReference(Control.ForegroundProperty, "ThemeTextBrush");
        toggle.Click += (_, _) => { changed(toggle.IsChecked == true); ApplyFeatureAppearance(); SaveSettings(); };
        parent.Children.Add(toggle);
    }
    private void FeatureSlider(Panel parent, string text, double min, double max, double value, Action<double> changed, bool integer = false)
    {
        var label = FeatureText($"{text} · {value:0.##}", 12); parent.Children.Add(label);
        var slider = new Slider { Minimum = min, Maximum = max, Value = Math.Clamp(value, min, max), TickFrequency = 1, IsSnapToTickEnabled = integer, Margin = new Thickness(0, 0, 0, 12) };
        slider.ValueChanged += (_, _) => { label.Text = $"{text} · {slider.Value:0.##}"; changed(slider.Value); ApplyFeatureAppearance(); SaveSettings(); };
        parent.Children.Add(slider);
    }
    private void FeatureChoice(Panel parent, string label, (string Key, string Label)[] options, string current, Action<string> changed)
    {
        parent.Children.Add(FeatureText(label, 11, "ThemeMutedBrush"));
        var combo = new ComboBox { Style = (Style)FindResource("FeatureCombo"), ItemsSource = options.Select(o => o.Label), SelectedIndex = Math.Max(0, Array.FindIndex(options, o => o.Key == current)), Margin = new Thickness(0, 0, 0, 14), Padding = new Thickness(6), FontSize = 12, MinHeight = 36 };
        combo.SelectionChanged += (_, _) => { if (combo.SelectedIndex < 0) return; changed(options[combo.SelectedIndex].Key); ApplyFeatureAppearance(); SaveSettings(); };
        parent.Children.Add(combo);
    }
    private void OpenFeatureHub_Click(object sender, RoutedEventArgs e) { e.Handled = true; OpenFeaturePage(_featurePage); }
    private void OpenLayout_Click(object sender, RoutedEventArgs e) { e.Handled = true; OpenFeaturePage("layout"); }
    private void OpenDiagnostics_Click(object sender, RoutedEventArgs e) { e.Handled = true; OpenFeaturePage("diagnostics"); }
    private void OpenThemes_Click(object sender, RoutedEventArgs e) { e.Handled = true; OpenFeaturePage("themes"); }
    private void BuildFeatureNavigation()
    {
        FeatureNavigation.Children.Clear();
        var order = _settings.TabOrder.Concat(FeatureNames.Keys).Distinct().Where(FeatureNames.ContainsKey);
        foreach (string key in order.Where(k => !_settings.HiddenTabs.Contains(k) || k is "layout" or "diagnostics"))
        {
            var button = FeatureButton(FeatureNames[key], () => { if (key == "home") MorphToState(IslandState.Expanded); else OpenFeaturePage(key); });
            button.HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch;
            button.HorizontalContentAlignment = System.Windows.HorizontalAlignment.Left;
            if (key == _featurePage) button.SetResourceReference(Control.BackgroundProperty, "ThemeSelectionBrush");
            FeatureNavigation.Children.Add(button);
        }
    }
    private void OpenFeaturePage(string page)
    {
        _featurePage = page;
        _manualResize = false; ResizeHint.Visibility = ResizeWidthThumb.Visibility = ResizeHeightThumb.Visibility = Visibility.Collapsed;
        MorphToState(IslandState.Features); BuildFeatureNavigation(); FeatureContent.Children.Clear(); FeatureScroll.ScrollToTop();
        switch (page)
        {
            case "layout": BuildLayoutPage(); break;
            case "themes": BuildUserThemesPage(); break;
            case "system": _ = BuildSystemPageAsync(); break;
            case "calendar": BuildCalendarPage(); break;
            case "timers": BuildTimersPage(); break;
            case "music": BuildMusicPage(); break;
            case "chat": BuildChatPage(); break;
            case "diagnostics": BuildDiagnosticsPage(); break;
            case "welcome": BuildWelcomePage(); break;
            case "updates": BuildUpdatesPage(); break;
            default: BuildNewsPage(); break;
        }
    }

    private void BuildLayoutPage()
    {
        var pill = FeatureCard("Размер и форма", "Для ручной настройки потяните правый или нижний край свёрнутого островка.");
        pill.Children.Add(FeatureButton("Изменить мышью", () => StartManualResize(this, new RoutedEventArgs())));
        FeatureSlider(pill, "Ширина", 160, 650, _settings.CompactWidth, v => _settings.CompactWidth = v, true);
        FeatureSlider(pill, "Высота", 32, 100, _settings.CompactHeight, v => _settings.CompactHeight = v, true);
        FeatureSlider(pill, "Скругление", 0, 1, _settings.CornerRounding, v => _settings.CornerRounding = v);
        FeatureSlider(pill, "Непрозрачность в покое", .15, 1, _settings.IdleOpacity, v => _settings.IdleOpacity = v);
        FeatureToggle(pill, "Тень", _settings.IslandShadow, v => _settings.IslandShadow = v);
        pill.Children.Add(FeatureButton("Сбросить размер", () => { _settings.CompactWidth = 210; _settings.CompactHeight = 38; _settings.CornerRounding = 1; ApplyFeatureAppearance(); SaveSettings(); OpenFeaturePage("layout"); }));
        var content = FeatureCard("Что показывать в покое");
        FeatureChoice(content, "По центру", [("time", "Время"), ("both", "Время и дата"), ("date", "Дата")], _settings.IdleCenter, v => _settings.IdleCenter = v);
        FeatureChoice(content, "Справа", [("weather", "Погода"), ("battery", "Батарея"), ("none", "Ничего")], _settings.IdleRight, v => _settings.IdleRight = v);
        FeatureChoice(content, "Шрифт", [("Segoe UI", "Segoe UI"), ("Consolas", "Consolas"), ("Calibri", "Calibri"), ("Verdana", "Verdana")], _settings.IslandFont, v => _settings.IslandFont = v);
        FeatureToggle(content, "12-часовой формат", _settings.Clock12Hour, v => _settings.Clock12Hour = v);
        FeatureToggle(content, "Показывать секунды", _settings.ClockSeconds, v => _settings.ClockSeconds = v);
        FeatureSlider(content, "Задержка раскрытия, мс", 0, 2000, _settings.HoverDelayMs, v => _settings.HoverDelayMs = (int)v, true);
        FeatureToggle(content, "Звук таймеров и встреч", _settings.ReminderSound, v => _settings.ReminderSound = v);
        var behavior = FeatureCard("Поведение");
        FeatureToggle(behavior, "Показ громкости в динамическом острове", _settings.VolumeIslandHud ?? _settings.LevelHud, SetVolumeIslandHud);
        behavior.Children.Add(FeatureText("Заменяет индикатор Windows при нажатии клавиш громкости. Выключите, чтобы вернуть системный показ.", 11));
        FeatureToggle(behavior, "Показывать уровень яркости", _settings.LevelHud, v => _settings.LevelHud = v);
        FeatureToggle(behavior, "Следовать за курсором на другой монитор", _settings.FollowCursorMonitor, v => _settings.FollowCursorMonitor = v);
        FeatureToggle(behavior, "Счётчик непрочитанных без доступа к уведомлениям", _settings.TitleUnreadFallback, v => _settings.TitleUnreadFallback = v);
        FeatureToggle(behavior, "Прогресс других программ", _settings.ExternalProgress, v => _settings.ExternalProgress = v);
        var tabs = FeatureCard("Разделы", "Перетащите строку для изменения порядка. Галочка показывает или скрывает раздел.");
        BuildReorderList(tabs, _settings.TabOrder, FeatureNames.Keys.Where(k => k is not "layout" and not "diagnostics" and not "themes" and not "news"),
            key => FeatureNames.GetValueOrDefault(key, key), key => !_settings.HiddenTabs.Contains(key),
            (key, visible) => { if (visible) _settings.HiddenTabs.Remove(key); else if (!_settings.HiddenTabs.Contains(key)) _settings.HiddenTabs.Add(key); BuildFeatureNavigation(); });
        var actions = FeatureCard("Кнопки главной", "Перетаскивайте строки. Скрытые кнопки можно вернуть в настройках главной.");
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
                var check = new CheckBox { Content = "⠿   " + label(key), IsChecked = shown(key), Tag = key, Padding = new Thickness(8), Margin = new Thickness(2) };
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
            order.Remove(source); order.Insert(order.IndexOf(target), source); ApplyFeatureAppearance(); BuildFeatureNavigation(); SaveSettings(); Fill(); e.Handled = true;
        };
        Fill(); parent.Children.Add(list);
    }
    private static T? FindAncestor<T>(DependencyObject? node) where T : DependencyObject
    { while (node != null) { if (node is T found) return found; node = VisualTreeHelper.GetParent(node); } return null; }

    private void BuildUserThemesPage()
    {
        var card = FeatureCard("Ваша палитра", "Цвета задаются как #RRGGBB. Стекло и все дополнительные панели используют выбранную палитру.");
        var name = FeatureInput(card, "Название", "Моя тема");
        var island = FeatureInput(card, "Островок", "#142D38");
        var scene = FeatureInput(card, "Второй цвет", "#294653");
        var accent = FeatureInput(card, "Акцент", "#6EE7D5");
        bool light = false; FeatureToggle(card, "Светлая палитра", false, v => light = v);
        var status = FeatureText("", 11, "ThemeMutedBrush");
        void Add(UserTheme theme)
        {
            if (!ThemeCode.Valid(theme)) { status.Text = "Проверьте название и цвета #RRGGBB."; return; }
            _settings.CustomThemes.RemoveAll(t => t.Name == theme.Name);
            _settings.CustomThemes.Add(theme); _settings.CustomThemes = _settings.CustomThemes.TakeLast(32).ToList();
            string key = "User: " + theme.Name;
            _themes.RemoveAll(t => t.Key == key); var preset = new ThemePreset(key, theme.Island, theme.Scene, theme.Accent, theme.Light); _themes.Add(preset);
            _settings.SelectedTheme = key; _settings.CustomPhotoPath = ""; CustomBgBrush.ImageSource = null;
            ApplyTheme(preset); BuildThemesGrid(); SaveSettings(); OpenFeaturePage("themes");
        }
        card.Children.Add(FeatureButton("Сохранить и применить", () => Add(new(name.Text.Trim(), island.Text.Trim(), scene.Text.Trim(), accent.Text.Trim(), light))));
        var import = FeatureInput(card, "Код темы", "");
        card.Children.Add(FeatureButton("Импортировать", () => { try { Add(ThemeCode.Import(import.Text.Trim())); } catch (FormatException ex) { status.Text = ex.Message; } })); card.Children.Add(status);
        var saved = FeatureCard("Сохранённые темы");
        if (_settings.CustomThemes.Count == 0) saved.Children.Add(FeatureText("Создайте первую тему выше.", 12, "ThemeMutedBrush"));
        foreach (var theme in _settings.CustomThemes.ToArray())
        {
            var row = new WrapPanel();
            var select = FeatureButton(theme.Name, () => { _settings.SelectedTheme = "User: " + theme.Name; ApplyTheme(_themes.Single(t => t.Key == _settings.SelectedTheme)); BuildThemesGrid(); SaveSettings(); });
            select.ContextMenu = CreateThemeCopyMenu(theme);
            row.Children.Add(select); row.Children.Add(FeatureButton("Код", () => Clipboard.SetText(ThemeCode.Export(theme))));
            row.Children.Add(FeatureButton("Удалить", () => { _settings.CustomThemes.Remove(theme); _themes.RemoveAll(t => t.Key == "User: " + theme.Name); if (_settings.SelectedTheme == "User: " + theme.Name) { _settings.SelectedTheme = "Midnight"; ApplyTheme(_themes[0]); } BuildThemesGrid(); SaveSettings(); OpenFeaturePage("themes"); })); saved.Children.Add(row);
        }
    }

    private ContextMenu CreateThemeCopyMenu(UserTheme theme)
    {
        var menu = new ContextMenu { Style = (Style)FindResource("IslandContextMenu"), ItemContainerStyle = (Style)FindResource("IslandMenuItem") };
        var copy = new MenuItem { Header = "Копировать код темы" }; copy.Click += (_, _) => Clipboard.SetText(ThemeCode.Export(theme)); menu.Items.Add(copy);
        menu.Opened += (_, _) => { foreach (string key in Resources.Keys.OfType<string>()) if ((key.StartsWith("Theme") || key.StartsWith("Glass")) && Resources[key] is Brush brush) menu.Resources[key] = brush; };
        return menu;
    }
    private async Task BuildSystemPageAsync()
    {
        var game = FeatureCard("Игровой центр", _gameFps?.Status ?? "Для измерения FPS игры включите встроенный счётчик. Windows запросит права администратора для ETW. Дополнительных программ не требуется.");
        FeatureToggle(game, "Автоматический HUD в полноэкранной игре", _settings.AutoGameHud, v => _settings.AutoGameHud = v);
        game.Children.Add(FeatureButton(_gameFps == null ? "Включить FPS игры" : "Остановить счётчик", () =>
        {
            if (_gameFps != null) { _gameFps.Dispose(); _gameFps = null; OpenFeaturePage("system"); return; }
            _gameFps = new FpsService(); _ = _gameFps.StartAsync(); OpenFeaturePage("system");
        }));
        game.Children.Add(FeatureText("Измеряются кадры, отправленные через DirectX Present. Для игр OpenGL / Vulkan без событий DXGI счётчик может быть недоступен.", 11, "ThemeMutedBrush"));
        var card = FeatureCard("Громкость приложений", "Регуляторы управляют настоящими звуковыми сессиями Windows.");
        var placeholder = FeatureText("Читаю аудиоустройства…", 12, "ThemeMutedBrush"); card.Children.Add(placeholder);
        try
        {
            var result = await Task.Run(() => { using var audio = new DesktopAudioService(); return (Apps: audio.Apps(), Master: audio.Master(), Brightness: DesktopAudioService.Brightness()); });
            if (_featurePage != "system") return; card.Children.Remove(placeholder);
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
        catch (Exception ex) { AppLog.Error("Read audio mixer", ex); placeholder.Text = "Нет доступного устройства вывода. Откройте настройки звука Windows."; card.Children.Add(FeatureButton("Настройки звука", () => OpenWebSettings("ms-settings:sound"))); }
    }
    private void BuildCalendarPage()
    {
        var card = FeatureCard("Ближайшие встречи", "Импортируйте календарь .ics. Повторы и часовые пояса учитываются. Ссылка на звонок появится за 10 минут и в момент начала.");
        card.Children.Add(FeatureButton("Импортировать .ics", async () =>
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Календарь iCalendar|*.ics", CheckFileExists = true };
            if (dialog.ShowDialog(this) != true) return;
            try
            {
                if (new FileInfo(dialog.FileName).Length > 4_000_000) throw new FormatException("Календарь больше 4 МБ.");
                string text = await File.ReadAllTextAsync(dialog.FileName);
                _meetings = await Task.Run(() => CalendarReader.Read(text, DateTimeOffset.Now));
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(CalendarPath)!); await File.WriteAllTextAsync(CalendarPath, text); OpenFeaturePage("calendar");
            }
            catch (Exception ex) { AppLog.Error("Import calendar", ex); card.Children.Add(FeatureText("Не удалось прочитать календарь. Проверьте файл .ics.", 11, "ThemeDangerBrush")); }
        }));
        card.Children.Add(FeatureButton("Удалить календарь", () => { if (File.Exists(CalendarPath)) File.Delete(CalendarPath); _meetings = []; OpenFeaturePage("calendar"); }));
        if (_meetings.Count == 0) card.Children.Add(FeatureText("На ближайшие 30 дней встреч нет.", 12, "ThemeMutedBrush"));
        foreach (var meeting in _meetings.Take(60))
        {
            var row = FeatureCard(meeting.Title, meeting.Start.ToLocalTime().ToString("ddd, dd MMM · HH:mm") + " — " + meeting.End.ToLocalTime().ToString("HH:mm"));
            if (meeting.Join != null) row.Children.Add(FeatureButton("Присоединиться · " + meeting.Join.Host, () => OpenWeb(meeting.Join)));
        }
    }
    private void BuildTimersPage()
    {
        var card = FeatureCard("Таймеры и напоминания", "Продолжают отсчитывать время после перезапуска приложения.");
        var label = FeatureInput(card, "Название", "Пора сделать перерыв");
        var minutes = FeatureInput(card, "Через сколько минут", "25");
        var error = FeatureText("", 11, "ThemeDangerBrush");
        card.Children.Add(FeatureButton("Запустить таймер", () =>
        {
            if (!double.TryParse(minutes.Text.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double value) || !double.IsFinite(value) || value < .05 || value > 525600)
            { error.Text = "Введите от 0,05 до 525600 минут."; return; }
            _settings.Timers.Add(new(Guid.NewGuid(), string.IsNullOrWhiteSpace(label.Text) ? "Таймер" : label.Text[..Math.Min(label.Text.Length, 120)], DateTimeOffset.Now.AddMinutes(value))); SaveSettings(); OpenFeaturePage("timers");
        })); card.Children.Add(error);
        foreach (var timer in _settings.Timers.OrderBy(t => t.Due).ToArray())
        {
            var row = FeatureCard(timer.Label, "Сработает " + timer.Due.ToLocalTime().ToString("dd MMM HH:mm:ss"));
            row.Children.Add(FeatureButton("Отменить", () => { _settings.Timers.Remove(timer); SaveSettings(); OpenFeaturePage("timers"); }));
        }
    }
    private void BuildMusicPage()
    {
        var card = FeatureCard("Караоке", "Строка следует позиции системного медиаплеера. Для локального LRC запустите соответствующую песню перед импортом.");
        card.Children.Add(FeatureText(string.IsNullOrWhiteSpace(_currentMediaTitle) ? "Нет активного медиа" : _currentMediaTitle + " · " + _currentMediaArtist, 14));
        FeatureToggle(card, "Показывать текст в островке", _settings.Karaoke, v => _settings.Karaoke = v);
        FeatureToggle(card, "Искать текст в LRCLIB (передаётся название и исполнитель)", _settings.OnlineLyrics, v => { _settings.OnlineLyrics = v; _lyricsTrack = ""; });
        card.Children.Add(FeatureButton("Импортировать LRC", async () =>
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Синхронизированный текст|*.lrc", CheckFileExists = true };
            if (dialog.ShowDialog(this) != true) return;
            try { _lyrics = LyricsReader.Parse(await File.ReadAllTextAsync(dialog.FileName)); _lyricsTrack = _currentMediaTitle + "\n" + _currentMediaArtist; _lyricIndex = -1; _lyricsStatus = $"Загружено строк: {_lyrics.Count}"; _settings.Karaoke = true; SaveSettings(); OpenFeaturePage("music"); }
            catch (Exception ex) { AppLog.Error("Read lyrics", ex); }
        }));
        card.Children.Add(FeatureText(_lyricsStatus, 11, "ThemeMutedBrush"));
        var buttons = new WrapPanel(); buttons.Children.Add(FeatureButton("⏮", () => PrevTrack_Click(this, new RoutedEventArgs()))); buttons.Children.Add(FeatureButton("⏯", () => PlayPause_Click(this, new RoutedEventArgs()))); buttons.Children.Add(FeatureButton("⏭", () => NextTrack_Click(this, new RoutedEventArgs()))); card.Children.Add(buttons);
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
            voice.Status += message => Dispatcher.BeginInvoke(() => { if (ReferenceEquals(_voice, voice) && _voiceStatusText != null) _voiceStatusText.Text = message; });
            voice.Transcript += text => Dispatcher.BeginInvoke(() => { if (!ReferenceEquals(_voice, voice) || _voiceTranscript == null) return; _voiceTranscript.Text = (_voiceTranscript.Text + "\n" + text).Trim(); if (_voiceTranscript.Text.Length > 8000) _voiceTranscript.Text = _voiceTranscript.Text[^8000..]; });
            OpenFeaturePage("chat");
            try { await voice.StartAsync(GeminiKeyStore.Read(), _settings.GeminiModel, _settings.GeminiVoice); }
            catch (Exception ex) { AppLog.Error("Start Gemini voice", ex); bool current = ReferenceEquals(_voice, voice); if (current) _voice = null; await voice.DisposeAsync(); if (current && _voiceStatusText != null) _voiceStatusText.Text = "Не удалось начать разговор. Проверьте ключ, модель, сеть и разрешение микрофона."; }
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
        ("FPS", "Интерфейс отрисовывается по необходимости. " + (_gameFps?.Status ?? "Счётчик игры можно включить на странице «Система»."), null),
        ("Стекло", _settings.LiquidGlass ? "Включено. Видимость при захвате настраивается отдельно." : "Выключено в настройках внешнего вида.", null),
        ("Автозапуск", _settings.RunOnStartup ? "Запрошен в настройках; Windows может ограничить запуск." : "Выключен.", "ms-settings:startupapps"),
        ("Обновления", string.IsNullOrWhiteSpace(_settings.UpdateRepository) ? "Укажите репозиторий GitHub в разделе обновлений." : "Источник задан. Доступность новой версии проверяется в разделе обновлений.", null),
        ("Календарь", File.Exists(CalendarPath) ? $"Подключён .ics, событий на 30 дней: {_meetings.Count}. Обновление — повторный импорт." : "Импортируйте файл .ics на странице календаря.", null),
        ("Яркость", _lastBrightness.HasValue ? "Экран поддерживает системную регулировку." : "Экран не публикует яркость через Windows WMI.", null),
        ("Gemini", string.IsNullOrWhiteSpace(GeminiKeyStore.Read()) ? "Нужен ваш API-ключ." : _voice?.Connected == true ? "Голосовой сеанс активен." : "Ключ найден; соединение проверяется при запуске голоса.", "ms-settings:privacy-microphone")
    ];
    private void BuildDiagnosticsPage()
    {
        var header = FeatureCard("Проверка системы", "Отчёт содержит технические статусы, без сообщений, имён собеседников и API-ключей.");
        header.Children.Add(FeatureButton("Копировать отчёт", () => Clipboard.SetText("OC - Dynamic Island 1.1.5\nWindows " + Environment.OSVersion.Version + "\n" + string.Join("\n", DiagnosticRows().Select(r => r.Name + ": " + r.Status)))));
        header.Children.Add(FeatureButton("Повторить проверку", () => OpenFeaturePage("diagnostics")));
        foreach (var row in DiagnosticRows())
        {
            var card = FeatureCard(row.Name, row.Status);
            if (row.Fix != null) card.Children.Add(FeatureButton("Открыть настройки Windows", () => OpenWebSettings(row.Fix)));
            else if (row.Name is "Календарь" or "Gemini") card.Children.Add(FeatureButton("Настроить", () => OpenFeaturePage(row.Name == "Календарь" ? "calendar" : "chat")));
            else if (row.Name == "Погода") card.Children.Add(FeatureButton("Указать город", () => { MorphToState(IslandState.Settings); SwitchSettingsPage(PageWeather, NavWeatherBtn); }));
            else if (row.Name == "Музыка") card.Children.Add(FeatureButton("Переподключить", () => _ = InitializeMediaManagerAsync()));
            else if (row.Name == "FPS") card.Children.Add(FeatureButton("Открыть игровой центр", () => OpenFeaturePage("system")));
            else if (row.Name == "Обновления") card.Children.Add(FeatureButton("Настроить", () => OpenFeaturePage("updates")));
        }
    }
    private static void OpenWebSettings(string uri) => Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
    private GitHubUpdateService.Release? _availableUpdate;
    private string? _downloadedUpdate;
    private bool _updateBusy;
    private string _updateStatus = "Версия приложения · 1.1.5";
    private CancellationTokenSource? _updateCancellation;
    private void BuildUpdatesPage()
    {
        var card = FeatureCard("Обновления через GitHub", "Обновления OC - Dynamic Island загружаются из GitHub. Дополнительные программы не нужны. Проверяются контрольная сумма и подпись установщика ocdynamicisland.exe.");
        var repo = FeatureInput(card, "Репозиторий owner/repository", _settings.UpdateRepository);
        var status = FeatureText(_updateStatus, 12, "ThemeMutedBrush"); card.Children.Add(status);
        var actions = new WrapPanel();
        actions.Children.Add(FeatureButton("Проверить", async () =>
        {
            if (_updateBusy) return;
            try
            {
                _settings.UpdateRepository = GitHubUpdateService.Repository(repo.Text); SaveSettings();
                _updateBusy = true; _updateCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30)); status.Text = "Проверяю опубликованные релизы…";
                var release = await GitHubUpdateService.CheckAsync(_settings.UpdateRepository, _updateCancellation.Token);
                _availableUpdate = release.Version > new Version(1, 1, 5) ? release : null; _downloadedUpdate = null;
                _updateStatus = _availableUpdate != null ? "Доступна версия " + release.Version : "Установлена актуальная версия.";
            }
            catch (FormatException ex) { _updateStatus = ex.Message; }
            catch (Exception ex) { AppLog.Error("Check GitHub update", ex); _updateStatus = "Не удалось проверить релиз. Проверьте адрес публичного репозитория и интернет."; }
            finally { _updateBusy = false; _updateCancellation?.Dispose(); _updateCancellation = null; if (_featurePage == "updates") OpenFeaturePage("updates"); }
        }));
        actions.Children.Add(FeatureButton("Что нового", () => OpenFeaturePage("news"))); card.Children.Add(actions);
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
                    var progress = new Progress<double>(value => { _updateStatus = $"Загрузка · {value:0}%"; status.Text = _updateStatus; });
                    _downloadedUpdate = await GitHubUpdateService.DownloadAsync(release, progress, _updateCancellation.Token);
                    _updateStatus = "Установщик загружен; SHA-256 и подпись издателя проверены.";
                }
                catch (System.Security.Cryptography.CryptographicException ex) { _updateStatus = ex.Message; }
                catch (FormatException ex) { _updateStatus = ex.Message; }
                catch (OperationCanceledException) { _updateStatus = "Загрузка отменена."; }
                catch (Exception ex) { AppLog.Error("Download update", ex); _updateStatus = "Не удалось загрузить и проверить обновление."; }
                finally { _updateBusy = false; _updateCancellation?.Dispose(); _updateCancellation = null; if (_featurePage == "updates") OpenFeaturePage("updates"); }
            }));
            else card.Children.Add(FeatureButton("Установить обновление", () =>
            {
                GitHubUpdateService.RejectLinks(System.IO.Path.GetDirectoryName(_downloadedUpdate)!);
                Process.Start(new ProcessStartInfo(_downloadedUpdate) { UseShellExecute = true });
            }));
        }
        if (_updateBusy) card.Children.Add(FeatureButton("Отменить", () => _updateCancellation?.Cancel()));
    }
    private void BuildNewsPage()
    {
        var card = FeatureCard("OC - Dynamic Island 1.1.5", "Новые возможности островка");
        card.Children.Add(FeatureText("• Источник обновлений GitHub подключён по умолчанию.\n• Установщик переименован в ocdynamicisland.exe.\n• Все исправления уведомлений, громкости и стабильности включены.", 13));
        card.Children.Add(FeatureText("• Громкость по событиям CoreAudio, включая отключение звука.\n• Отдельный переключатель замены индикатора Windows.\n• Исправлены потерянные клики при наведении и перетаскивании.\n• Облегчены перерисовка, тени и фоновая синхронизация.", 13));
        card.Children.Add(FeatureText("• Своя раскладка, ручной размер, шрифт, часы и прозрачность.\n• Микшер приложений и индикаторы звука / яркости.\n• Календарь с переходом на звонок и таймеры.\n• Караоке из LRC и LRCLIB.\n• Голосовой Gemini с вашим ключом.\n• Новые персонажи и собственные палитры.\n• Прогресс окон, счётчик непрочитанных и диагностика.", 13));
        card.Children.Add(FeatureButton("Канал проекта", () => OpenWeb(new Uri("https://t.me/outsidecompany"))));
    }
    private void BuildWelcomePage()
    {
        var card = FeatureCard($"Добро пожаловать · шаг {_welcomeStep + 1} из 3", _welcomeStep switch { 0 => "Выберите характер островка.", 1 => "Решите, что показывать рядом с часами.", _ => "Наведите курсор для предпросмотра. Нажмите для раскрытия. Правой кнопкой откройте меню. Ctrl+Alt+O — показать островок." });
        if (_welcomeStep == 0)
        {
            FeatureToggle(card, "Liquid Glass", _settings.LiquidGlass, v => { _settings.LiquidGlass = v; TglLiquidGlass.IsChecked = v; ApplyTheme(_themes.First(t => t.Key == _settings.SelectedTheme)); });
            FeatureChoice(card, "Персонаж", [("Eyes", "Глазки"), ("Puppy", "Щенок"), ("Fox", "Лиса"), ("None", "Без персонажа")], _settings.SelectedCharacter, v => { _settings.SelectedCharacter = v; UpdateCharacterVectors(v); });
        }
        if (_welcomeStep == 1) FeatureChoice(card, "Справа от часов", [("weather", "Погода"), ("battery", "Батарея"), ("none", "Ничего")], _settings.IdleRight, v => _settings.IdleRight = v);
        card.Children.Add(FeatureButton(_welcomeStep < 2 ? "Далее" : "Начать", () =>
        {
            if (_welcomeStep < 2) { _welcomeStep++; OpenFeaturePage("welcome"); }
            else { _settings.OnboardingComplete = true; _settings.LastSeenVersion = "1.1.5"; SaveSettings(); MorphToState(IslandState.Compact); }
        }));
    }
}
