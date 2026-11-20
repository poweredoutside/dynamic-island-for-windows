using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using DynamicIslandWin.Services;
using Windows.Devices.Radios;
using Button = System.Windows.Controls.Button;
using MessageBox = System.Windows.MessageBox;
using ProgressBar = System.Windows.Controls.ProgressBar;

namespace DynamicIslandWin;

public partial class MainWindow
{
    private TextBlock? _systemBattery, _systemDnd, _systemPrivacy, _systemDiscord;
    private (TextBlock Text, ProgressBar Bar)[] _systemMeters = [];
    private DateTime _nextPrivacyRead;
    private DateTime _nextConnectivityRead;
    private bool _connectivityReading, _radioChanging;
    private WindowsConnectivity.Snapshot? _connectivity;
    private enum Tile { Wifi, Bluetooth, Airplane, Vpn, Desktop }
    private readonly Dictionary<Tile, (Button Button, TextBlock Status)> _controlTiles = [];
    private (DateTimeOffset Until, string PreviousProfile)? _timedDnd;

    private StackPanel BuildSystemOverview()
    {
        var controls = FeatureCard("Пункт управления");
        _systemBattery = FeatureText("", 11, "ThemeMutedBrush");
        controls.Children.Add(_systemBattery);
        var tiles = new WrapPanel();
        controls.Children.Add(tiles);
        _controlTiles.Clear();
        AddControlTile(Tile.Wifi, "Wi-Fi", "\uE701", "ms-settings:network-wifi", RadioKind.WiFi);
        AddControlTile(Tile.Bluetooth, "Bluetooth", "\uE702", "ms-settings:bluetooth", RadioKind.Bluetooth);
        AddControlTile(Tile.Airplane, "Режим полёта", "\uE709", "ms-settings:network-airplanemode");
        AddControlTile(Tile.Vpn, "VPN", "\uE72E", "ms-settings:network-vpn");
        AddControlTile(Tile.Desktop, "Чистый стол", "\uE7F4");
        _nextConnectivityRead = DateTime.MinValue;

        void AddControlTile(Tile id, string label, string glyph, string? settings = null, RadioKind? radio = null)
        {
            var tile = ControlTile(label, glyph, () =>
            {
                if (radio is { } kind) _ = ToggleSystemRadioAsync(kind, settings!);
                else if (settings != null) OpenWebSettings(settings);
                else if (ReadDesktopIconsHidden() is bool hidden) SetDesktopIconsHidden(!hidden);
            }, settings);
            _controlTiles.Add(id, tile);
            tiles.Children.Add(tile.Button);
        }

        var power = new WrapPanel();
        controls.Children.Add(power);
        power.Children.Add(FeatureButton("Блокировка", () => { if (!SystemOverview.LockWorkStation()) throw new InvalidOperationException("Windows lock failed."); }));
        power.Children.Add(FeatureButton("Сон", () =>
        {
            if (!System.Windows.Forms.Application.SetSuspendState(System.Windows.Forms.PowerState.Suspend, false, false))
                FeatureNotice(L("Не удалось перевести компьютер в спящий режим."), () => OpenFeaturePage("system"));
        }));
        power.Children.Add(FeatureButton("Перезагрузка", () => ConfirmPowerAction(true)));
        power.Children.Add(FeatureButton("Выключение", () => ConfirmPowerAction(false)));

        var mixer = FeatureCard("Громкость приложений");
        var resources = new System.Windows.Controls.Primitives.UniformGrid { Columns = 3 };
        DashboardSectionContent.Children.Add(resources);
        _systemMeters = new[] { "Процессор", "Видеокарта", "Память" }.Select(name =>
        {
            var card = FeatureCard(name, parent: resources);
            if (card.Parent is Border border) border.Margin = new Thickness(0, 0, 6, 10);
            var value = FeatureText("—", 21);
            var bar = new ProgressBar { Minimum = 0, Maximum = 100, Height = 5 };
            bar.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, "ThemeAccentBrush");
            bar.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "ThemeInputBrush");
            card.Children.Add(value);
            card.Children.Add(bar);
            return (value, bar);
        }).ToArray();

        var live = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2 };
        DashboardSectionContent.Children.Add(live);
        var focus = FeatureCard("Не беспокоить", parent: live);
        _systemDnd = FeatureText("", 11, "ThemeMutedBrush");
        focus.Children.Add(_systemDnd);
        var focusActions = new WrapPanel();
        focus.Children.Add(focusActions);
        foreach (var (minutes, label) in new[] { (30, "30 мин"), (60, "1 ч"), (120, "2 ч") })
            focusActions.Children.Add(FeatureButton(label, () => StartTimedDnd(minutes)));
        focusActions.Children.Add(FeatureButton("Переключить", () => SetDoNotDisturbState(!_isDndActive, true)));
        var discord = FeatureCard("Discord", parent: live);
        _systemDiscord = FeatureText("", 11, "ThemeMutedBrush");
        discord.Children.Add(_systemDiscord);
        discord.Children.Add(FeatureButton("Открыть Discord", () => VoiceLauncher.Open("discord")));
        var privacy = FeatureCard("Микрофон и камера");
        _systemPrivacy = FeatureText("", 11, "ThemeMutedBrush");
        privacy.Children.Add(_systemPrivacy);
        var privacyLinks = new WrapPanel();
        privacy.Children.Add(privacyLinks);
        privacyLinks.Children.Add(FeatureButton("Микрофон", () => OpenWebSettings("ms-settings:privacy-microphone")));
        privacyLinks.Children.Add(FeatureButton("Камера", () => OpenWebSettings("ms-settings:privacy-webcam")));
        _nextPrivacyRead = DateTime.MinValue;
        RefreshSystemOverview();
        return mixer;
    }

    private async Task ToggleSystemRadioAsync(RadioKind kind, string settings)
    {
        if (_radioChanging) return;
        _radioChanging = true;
        try
        {
            if (!await WindowsConnectivity.ToggleRadioAsync(kind) && !_windowClosed) OpenWebSettings(settings);
        }
        catch (Exception ex)
        {
            AppLog.Error("Toggle Windows radio", ex);
            if (!_windowClosed)
            {
                try { OpenWebSettings(settings); }
                catch (Exception launchError) { AppLog.Error("Open Windows radio settings", launchError); }
            }
        }
        finally
        {
            _radioChanging = false;
            _nextConnectivityRead = DateTime.MinValue;
            if (!_windowClosed) _ = RefreshConnectivityAsync();
        }
    }

    private (Button Button, TextBlock Status) ControlTile(string label, string icon, Action click, string? settings = null)
    {
        var tile = FeatureButton(label, click);
        tile.MinWidth = 91;
        var content = new StackPanel();
        content.Children.Add(new TextBlock
        {
            Text = icon, FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets"), FontSize = 20,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center, Margin = new Thickness(0, 1, 0, 5)
        });
        content.Children.Add(new TextBlock { Text = L(label), HorizontalAlignment = System.Windows.HorizontalAlignment.Center });
        var status = new TextBlock { FontSize = 10, MaxWidth = 102, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 3, 0, 0) };
        status.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
        content.Children.Add(status);
        tile.Content = content;
        System.Windows.Automation.AutomationProperties.SetName(tile, L(label));
        if (settings != null)
        {
            tile.ContextMenu = new System.Windows.Controls.ContextMenu();
            var item = new System.Windows.Controls.MenuItem { Header = L("Настройки Windows") };
            item.Click += (_, _) => OpenWebSettings(settings);
            tile.ContextMenu.Items.Add(item);
        }
        return (tile, status);
    }

    private bool SystemPageVisible => _currentState == IslandState.Section && _featurePage == "system";

    private async Task RefreshConnectivityAsync()
    {
        if (_connectivityReading || (!SystemPageVisible && !WidgetIsVisible("network"))) return;
        _connectivityReading = true;
        _nextConnectivityRead = DateTime.UtcNow.AddSeconds(2);
        try
        {
            var snapshot = await Task.Run(WindowsConnectivity.ReadAsync);
            if (_windowClosed) return;
            _connectivity = snapshot;
            if (SystemPageVisible) PaintControlTiles();
        }
        catch (Exception ex) { AppLog.Error("Read Windows connectivity", ex); }
        finally { _connectivityReading = false; }
    }

    private void PaintControlTiles()
    {
        if (_controlTiles.Count == 0) return;
        var snapshot = _connectivity;
        Paint(Tile.Wifi, ToSwitch(snapshot?.Wifi), snapshot?.WifiProfile);
        Paint(Tile.Bluetooth, ToSwitch(snapshot?.Bluetooth));
        Paint(Tile.Airplane, snapshot?.Airplane);
        Paint(Tile.Vpn, snapshot?.Vpn.Available == true ? snapshot.Value.Vpn.Name != null : null,
            snapshot?.Vpn.Name ?? (snapshot?.Vpn.Available == true ? L("Не подключено") : null));
        bool? hidden = ReadDesktopIconsHidden();
        Paint(Tile.Desktop, hidden);
        if (hidden.HasValue) TglHideDesktopIcons.IsChecked = hidden.Value;

        void Paint(Tile id, bool? active, string? detail = null)
        {
            var (button, status) = _controlTiles[id];
            string background = active == true ? "ThemeAccentBrush" : "ThemeInputBrush";
            if (!Equals(button.Tag, background))
            {
                button.Tag = background;
                button.SetResourceReference(Button.BackgroundProperty, background);
                button.SetResourceReference(Button.ForegroundProperty, active == true ? "ThemeOnAccentBrush" : "ThemeTextBrush");
                status.SetResourceReference(TextBlock.ForegroundProperty, active == true ? "ThemeOnAccentBrush" : "ThemeMutedBrush");
            }
            status.Text = detail ?? L(active == true ? "Включено" : active == false ? "Выключено" : "Состояние недоступно");
            button.ToolTip = status.Text;
            System.Windows.Automation.AutomationProperties.SetHelpText(button, status.Text);
        }

        static bool? ToSwitch(RadioState? state) => state switch { RadioState.On => true, RadioState.Off or RadioState.Disabled => false, _ => null };
    }

    private void RefreshSystemOverview()
    {
        if (!SystemPageVisible || _systemMeters.Length != 3) return;
        PaintControlTiles();
        if (IsLoaded && DateTime.UtcNow >= _nextConnectivityRead) _ = RefreshConnectivityAsync();
        _systemBattery!.Text = BatteryDisplay();
        _systemDnd!.Text = _timedDnd is { } timed
            ? string.Format(L("До {0}"), timed.Until.ToLocalTime().ToString("HH:mm"))
            : L(!_dndStateKnown ? "Состояние недоступно" : _isDndActive ? "Включено" : "Выключено");
        _systemDiscord!.Text = _discordDetected ? _discordDetectedLabel : L("Голосовой канал не обнаружен");
        if (DateTime.UtcNow >= _nextPrivacyRead)
        {
            _nextPrivacyRead = DateTime.UtcNow.AddSeconds(5);
            _systemPrivacy!.Text = $"{L("Микрофон")} · {PrivacyStatus("microphone")}   {L("Камера")} · {PrivacyStatus("webcam")}";
        }
        SetMeter(0, UsageDisplay(_lastCpuUsage), _lastCpuUsage);
        SetMeter(1, UsageDisplay(_lastGpuUsage), _lastGpuUsage);
        var memory = SystemOverview.Memory();
        SetMeter(2, MemoryDisplay(memory), memory?.Percent ?? -1);

        void SetMeter(int index, string text, int percent)
        {
            var (value, bar) = _systemMeters[index];
            value.Text = text;
            bar.Value = Math.Max(0, percent);
            bar.Visibility = percent < 0 ? Visibility.Hidden : Visibility.Visible;
        }
    }

    private string PrivacyStatus(string capability) => L(SystemOverview.IsPrivacyDeviceActive(capability) switch
    {
        true => "Используется",
        false => "Активность не обнаружена",
        null => "Состояние недоступно"
    });

    private void StartTimedDnd(int minutes)
    {
        if (_dndChanging) return;
        if (!_windowsDnd.TryRead(out var state)) { ShowDndError(null); return; }
        if (_timedDnd is { } timed && state.SelectedProfile == WindowsDndService.Priority)
        {
            _timedDnd = (DateTimeOffset.Now.AddMinutes(minutes), timed.PreviousProfile);
            RefreshSystemOverview();
            return;
        }
        if (state.IsActive || state.SelectedProfile != WindowsDndService.Off)
        {
            _systemDnd!.Text = L("Чтобы задать длительность, выключите текущий режим.");
            return;
        }
        if (!_windowsDnd.TrySetProfile(WindowsDndService.Priority, endAutomaticRule: false, out var error))
        {
            ShowDndError(error);
            return;
        }
        _timedDnd = (DateTimeOffset.Now.AddMinutes(minutes), state.SelectedProfile);
        RefreshSystemOverview();
    }

    private void RestoreTimedDnd()
    {
        if (_timedDnd is not { } timed) return;
        _timedDnd = null;
        if (_windowsDnd.TryRead(out var state) && state.SelectedProfile == WindowsDndService.Priority
            && !_windowsDnd.TrySetProfile(timed.PreviousProfile, endAutomaticRule: false, out var error) && !_windowClosed)
            ShowDndError(error);
    }

    private string BatteryDisplay() => SystemOverview.Battery() is { } battery
        ? string.Format(L(battery.Charging ? "Батарея {0}% · заряжается" : "Батарея {0}%"), battery.Percent) : L("Батарея недоступна");

    private static string UsageDisplay(int value) => value < 0 ? "—" : $"{value}%";

    private string MemoryDisplay((double Used, double Total, int Percent)? memory) => memory is { } usage
        ? string.Format(L("{0:0.0} / {1:0.0} ГБ"), usage.Used, usage.Total) : "—";

    private void ConfirmPowerAction(bool restart)
    {
        string action = L(restart ? "Перезагрузка" : "Выключение");
        if (MessageBox.Show(this, L(restart ? "Перезагрузить компьютер сейчас?" : "Выключить компьютер сейчас?"),
            action, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        Process.Start(new ProcessStartInfo("shutdown.exe", restart ? "/r /t 0" : "/s /t 0") { UseShellExecute = false, CreateNoWindow = true });
    }
}
