using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using DynamicIslandWin.Services;
using ContextMenu = System.Windows.Controls.ContextMenu;
using MenuItem = System.Windows.Controls.MenuItem;
using Separator = System.Windows.Controls.Separator;

namespace DynamicIslandWin;

public partial class MainWindow
{
    private DiscordCallAudio.Microphone? _discordMicrophone;
    private bool _discordMicSwitching;
    private int _discordMicReadVersion;
    private bool _discordOutputBusy;
    private ContextMenu? _discordOutputMenu;

    private void LocalizeDiscordCallControls()
    {
        DiscordOutputButton.ToolTip = L("Выход звука Discord");
        AutomationProperties.SetName(DiscordOutputButton, (string)DiscordOutputButton.ToolTip);
        DiscordTimerButton.ToolTip = L("Открыть Discord");
        AutomationProperties.SetName(DiscordTimerButton, (string)DiscordTimerButton.ToolTip);
        UpdateDiscordMicrophoneVisual();
    }

    private async Task RefreshDiscordMicrophoneAsync(DiscordCallMonitor.Call? call)
    {
        if (!call.HasValue || DiscordCallBorder.Visibility != Visibility.Visible)
        {
            _discordMicrophone = null;
            UpdateDiscordMicrophoneVisual();
            return;
        }
        if (_discordMicSwitching) return;
        int version = _discordMicReadVersion;
        try
        {
            var microphone = await Task.Run(() => DiscordCallAudio.ReadMicrophone(call.Value.ProcessName));
            if (_discordCall == call && version == _discordMicReadVersion) _discordMicrophone = microphone;
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            if (_discordCall == call && version == _discordMicReadVersion) _discordMicrophone = null;
        }
        finally
        {
            if (!_windowClosed) UpdateDiscordMicrophoneVisual();
        }
    }

    private void UpdateDiscordMicrophoneVisual()
    {
        bool available = _discordMicrophone.HasValue && _discordCall.HasValue;
        bool muted = available && _discordMicrophone!.Value.Muted;
        DiscordMicButton.IsEnabled = available;
        DiscordMicButton.Opacity = available ? 1 : .45;
        DiscordMicSlash.Visibility = muted ? Visibility.Visible : Visibility.Collapsed;
        DiscordMicIcon.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, muted ? "ThemeDangerBrush" : "ThemeTextBrush");
        string label = L(!available ? "Микрофон недоступен" : muted ? "Включить микрофон" : "Выключить микрофон");
        DiscordMicButton.ToolTip = available
            ? $"{label} · {_discordMicrophone!.Value.Name}\n{L("Действует на все приложения, использующие этот микрофон.")}"
            : label;
        System.Windows.Controls.ToolTipService.SetShowOnDisabled(DiscordMicButton, true);
        AutomationProperties.SetName(DiscordMicButton, label);
    }

    private async void DiscordMic_Click(object sender, RoutedEventArgs e)
    {
        if (_discordCall is not { } call || _discordMicrophone is not { } current || _discordMicSwitching) return;
        _discordMicSwitching = true;
        _discordMicReadVersion++;
        try
        {
            var updated = await Task.Run(() => DiscordCallAudio.ReadMicrophone(call.ProcessName, current.Id));
            if (!_windowClosed && _discordCall == call)
            {
                _discordMicrophone = updated;
                if (!updated.HasValue) DiscordAudioNotice("Не удалось переключить микрофон.");
            }
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            AppLog.Error("Toggle Discord microphone", ex);
            if (!_windowClosed) DiscordAudioNotice("Не удалось переключить микрофон.");
        }
        finally
        {
            _discordMicSwitching = false;
            if (!_windowClosed) UpdateDiscordMicrophoneVisual();
        }
    }

    private async void DiscordOutput_Click(object sender, RoutedEventArgs e)
    {
        if (_discordCall is not { } call) return;
        if (_discordOutputMenu is { IsOpen: true } opened)
        {
            opened.IsOpen = false;
            return;
        }
        if (_discordOutputBusy) return;

        var menu = new ContextMenu
        {
            Style = (Style)FindResource("IslandContextMenu"),
            MinWidth = 250,
            PlacementTarget = DiscordOutputButton,
            Placement = PlacementMode.Bottom
        };
        ApplyPopupPalette(menu);
        _discordOutputMenu = menu;
        menu.Closed += (_, _) => _discordOutputMenu = null;
        menu.Items.Add(new MenuItem
        {
            Header = L("Поиск аудиоустройств…"),
            IsEnabled = false,
            Style = (Style)FindResource("IslandMenuItem")
        });
        menu.IsOpen = true;

        _discordOutputBusy = true;
        try
        {
            var outputs = await Task.Run(() => DiscordCallAudio.ReadOutputs(call.ProcessName));
            if (_windowClosed || _discordCall != call || !menu.IsOpen) return;
            FillDiscordOutputMenu(menu, call, outputs);
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            AppLog.Error("Read Discord output devices", ex);
            if (!_windowClosed && _discordCall == call && menu.IsOpen) FillDiscordOutputMenu(menu, call, new([], null, 0, false));
        }
        finally { _discordOutputBusy = false; }
    }

    private void FillDiscordOutputMenu(ContextMenu menu, DiscordCallMonitor.Call call, DiscordCallAudio.Outputs outputs)
    {
        menu.Items.Clear();
        if (outputs.CanRoute)
        {
            Add("Выход Windows по умолчанию", "\uE7F4", outputs.PreferredId == null, () => SelectOutput(null));
            foreach (var output in outputs.Devices)
            {
                var item = Add(output.Name, "\uE7F6", string.Equals(output.Id, outputs.PreferredId, StringComparison.OrdinalIgnoreCase),
                    () => SelectOutput(output.Id), translate: false);
                if (output.InUse) item.InputGestureText = L("Сейчас используется");
            }
            menu.Items.Add(new Separator { Style = (Style)FindResource("IslandMenuSeparator") });
        }
        else Add("Выход Discord недоступен", "\uE7BA", false, null);
        Add("Микшер громкости Windows", "\uE767", false, () => OpenWebSettings("ms-settings:apps-volume"));

        MenuItem Add(string text, string icon, bool selected, Action? action, bool translate = true)
        {
            var item = new MenuItem
            {
                Header = translate ? L(text) : text, Tag = icon, IsChecked = selected, IsEnabled = action != null,
                Style = (Style)FindResource("IslandMenuItem")
            };
            if (action != null) item.Click += (_, _) => action();
            menu.Items.Add(item);
            return item;
        }

        async void SelectOutput(string? id)
        {
            if (_discordCall != call || _discordOutputBusy) return;
            _discordOutputBusy = true;
            try
            {
                await Task.Run(() => DiscordCallAudio.ChangeOutput(call.ProcessName, outputs.ProcessId, id));
                // Some voice engines reopen the stream asynchronously or hold an explicit Discord device.
                await Task.Delay(800);
                if (id != null && !_windowClosed && _discordCall == call)
                {
                    var actual = await Task.Run(() => DiscordCallAudio.ReadOutputs(call.ProcessName));
                    if (!_windowClosed && _discordCall == call && actual.Devices.Any(device => device.InUse)
                        && !actual.Devices.Any(device => device.InUse && string.Equals(device.Id, id, StringComparison.OrdinalIgnoreCase)))
                        DiscordAudioNotice("Discord остался на прежнем выходе. Выберите «По умолчанию» в настройках звука Discord и переподключитесь.");
                }
            }
            catch (Exception ex) when (ex is COMException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                AppLog.Error("Change Discord output", ex);
                if (!_windowClosed) DiscordAudioNotice("Не удалось изменить выход Discord.");
            }
            finally { _discordOutputBusy = false; }
        }
    }

    private void DiscordAudioNotice(string message) => ShowSideBubble(L(message), ToastCategory.Info, bypassDnd: true);
}
