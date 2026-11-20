using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using DynamicIslandWin.Services;
using Windows.Devices.Radios;
using Button = System.Windows.Controls.Button;

namespace DynamicIslandWin;

public partial class MainWindow
{
    private readonly DashboardTimer _widgetStopwatch = new(TimeProvider.System);
    private VolumeIslandService.Snapshot? _widgetVolume;
    private string _widgetTrack = "", _widgetArtist = "";

    private void AddWidgetControls(string kind, DockPanel panel, TextBlock body)
    {
        if (kind == "month")
        {
            body.FontFamily = new System.Windows.Media.FontFamily("Consolas");
            body.TextWrapping = TextWrapping.NoWrap;
            return;
        }
        if (kind == "calculator")
        {
            var calculator = new WidgetCalculator();
            var keys = new UniformGrid { Rows = 5, Columns = 4 };
            body.FontSize = 32;
            body.TextAlignment = TextAlignment.Right;
            body.TextWrapping = TextWrapping.NoWrap;
            body.Text = calculator.Display;
            DockPanel.SetDock(keys, Dock.Bottom);
            panel.Children.Add(keys);
            foreach (string key in new[] { "AC", "±", "%", "÷", "7", "8", "9", "×", "4", "5", "6", "−", "1", "2", "3", "+", "0", ".", "⌫", "=" })
            {
                string label = key == "." ? CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator : key;
                var button = WidgetButton(label, () =>
                {
                    calculator.Press(key);
                    body.Text = calculator.HasError ? L("Ошибка") : calculator.Display;
                });
                button.FontSize = 16;
                button.MinHeight = 37;
                if (key is "÷" or "×" or "−" or "+" or "=")
                {
                    button.Background = WidgetBrush("#FFE99625");
                    button.Foreground = WidgetBrush("#FF19100A");
                }
                keys.Children.Add(button);
            }
            return;
        }

        var actions = new WrapPanel();
        switch (kind)
        {
            case "music":
                var albumBadge = new Border
                {
                    Width = 63, Height = 63, CornerRadius = new CornerRadius(32),
                    Margin = new Thickness(0, 3, 13, 0), VerticalAlignment = VerticalAlignment.Top,
                    Background = WidgetBackground("#DC344D74"),
                    Child = new TextBlock
                    {
                        Text = "\uE8D6", FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets"),
                        Foreground = System.Windows.Media.Brushes.White, FontSize = 24,
                        HorizontalAlignment = System.Windows.HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                    }
                };
                DockPanel.SetDock(albumBadge, Dock.Left);
                panel.Children.Add(albumBadge);
                AddIcon("\uE892", "Предыдущий трек", () => PrevTrack_Click(this, new RoutedEventArgs()));
                AddIcon("\uE768", "Воспроизвести / пауза", () => PlayPause_Click(this, new RoutedEventArgs()));
                AddIcon("\uE893", "Следующий трек", () => NextTrack_Click(this, new RoutedEventArgs()));
                break;
            case "stopwatch":
                _widgetStopwatch.Select(DashboardTimer.Mode.Stopwatch);
                AddIcon("\uE768", "Старт / пауза", () => { _widgetStopwatch.Toggle(); Update(); });
                AddIcon("\uE72C", "Сброс", () => { _widgetStopwatch.Reset(); Update(); });
                break;
            case "volume":
                AddIcon("\uE738", "Убавить громкость", () => ChangeVolume(-5));
                AddIcon("\uE74F", "Включить / выключить звук", () => _volumeIsland?.ToggleMute());
                AddIcon("\uE710", "Прибавить громкость", () => ChangeVolume(5));
                break;
            case "quick":
                AddIcon("\uE722", "Полный экран", async () => await CaptureScreenshotAsync(true));
                AddIcon("\uE77F", "Буфер обмена", () => OpenWidgetPage("clipboard"));
                AddIcon("\uE713", "Настройки", () => { ShowIsland(); MorphToState(IslandState.Settings); Activate(); });
                break;
        }
        if (actions.Children.Count == 0) return;
        DockPanel.SetDock(actions, Dock.Bottom);
        panel.Children.Add(actions);

        void AddIcon(string glyph, string label, Action action)
        {
            var button = WidgetButton(glyph, action);
            button.FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets");
            button.FontSize = 15;
            button.MinWidth = 38;
            button.ToolTip = L(label);
            System.Windows.Automation.AutomationProperties.SetName(button, L(label));
            actions.Children.Add(button);
        }
        void Update()
        {
            _nextWidgetReading.Remove(kind);
            RefreshDesktopWidgets();
        }
        void ChangeVolume(int delta)
        {
            if (_volumeIsland == null || _widgetVolume is not { } current) return;
            double target = Math.Clamp(current.Volume * 100 + delta, 0, 100);
            if (_volumeIsland.SetVolume(target)) _widgetVolume = new((float)(target / 100), false);
            Update();
        }
    }

    private Button WidgetButton(string label, Action click)
    {
        var button = FeatureButton(label, click);
        button.Margin = new Thickness(0, 0, 4, 4);
        button.Padding = new Thickness(8, 5, 8, 5);
        button.HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch;
        return button;
    }

    private static string WidgetMonth(DateTime today)
    {
        var culture = CultureInfo.CurrentCulture;
        var format = culture.DateTimeFormat;
        var first = new DateTime(today.Year, today.Month, 1);
        int offset = (7 + (int)first.DayOfWeek - (int)format.FirstDayOfWeek) % 7;
        var result = new StringBuilder(today.ToString("MMMM yyyy", culture)).AppendLine().AppendLine();
        for (int col = 0; col < 7; col++)
            result.Append("  ").Append(format.GetShortestDayName((DayOfWeek)(((int)format.FirstDayOfWeek + col) % 7)).PadRight(2));
        result.AppendLine();
        int last = DateTime.DaysInMonth(today.Year, today.Month);
        for (int day = 1 - offset; day <= last; day++)
        {
            result.Append(day < 1 ? "    " : day == today.Day ? $"[{day,2}]" : $" {day,2} ");
            if ((day + offset) % 7 == 0 && day < last) result.AppendLine();
        }
        return result.ToString();
    }

    private static readonly (string Label, TimeZoneInfo Zone)[] WidgetZones =
    [
        ("UTC", TimeZoneInfo.Utc),
        ("New York", TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time")),
        ("Tokyo", TimeZoneInfo.FindSystemTimeZoneById("Tokyo Standard Time"))
    ];

    private string WidgetWorldTime(DateTimeOffset now) => string.Join('\n', WidgetZones.Select(zone =>
        $"{TimeZoneInfo.ConvertTime(now, zone.Zone):HH:mm}   {L(zone.Label)}"));

    private string WidgetNetworkStatus() => _connectivity is { } value
        ? $"Wi-Fi · {value.WifiProfile ?? L(value.Wifi == RadioState.On ? "Включено" : value.Wifi is RadioState.Off or RadioState.Disabled ? "Выключено" : "Состояние недоступно")}\nVPN · {value.Vpn.Name ?? L(value.Vpn.Available ? "Не подключено" : "Состояние недоступно")}"
        : L("Состояние недоступно");

    private string WidgetUptime()
    {
        var uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);
        return string.Format(L("{0} д · {1} ч · {2} мин"), uptime.Days, uptime.Hours, uptime.Minutes);
    }
}
