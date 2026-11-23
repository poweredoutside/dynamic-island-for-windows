using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls.Primitives;
using DynamicIslandWin.Services;
using Button = System.Windows.Controls.Button;
using ContextMenu = System.Windows.Controls.ContextMenu;
using MenuItem = System.Windows.Controls.MenuItem;
using Separator = System.Windows.Controls.Separator;

namespace DynamicIslandWin;

public partial class MainWindow
{
    private ContextMenu? _playerOutputMenu;
    private bool _changingPlayerOutput;

    private void LocalizePlayerOutputButton(Button button)
    {
        string label = L("Выход звука плеера");
        button.ToolTip = label;
        AutomationProperties.SetName(button, label);
    }

    private async void PlayerOutput_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is not Button anchor || !_hasPlayerMedia || _changingPlayerOutput) return;
        if (_playerOutputMenu is { IsOpen: true } opened)
        {
            opened.IsOpen = false;
            return;
        }

        string source = _playerTrack?.Source ?? "";
        var menu = new ContextMenu
        {
            Style = (Style)FindResource("IslandContextMenu"),
            MinWidth = 250,
            PlacementTarget = anchor,
            Placement = PlacementMode.Bottom
        };
        ApplyPopupPalette(menu);
        _playerOutputMenu = menu;
        void CloseOnHide(object _, DependencyPropertyChangedEventArgs change)
        {
            if (!(bool)change.NewValue) menu.IsOpen = false;
        }
        anchor.IsVisibleChanged += CloseOnHide;
        menu.Closed += (_, _) =>
        {
            anchor.IsVisibleChanged -= CloseOnHide;
            if (ReferenceEquals(_playerOutputMenu, menu)) _playerOutputMenu = null;
        };
        menu.Items.Add(new MenuItem
        {
            Header = L("Поиск аудиоустройств…"), IsEnabled = false,
            Style = (Style)FindResource("IslandMenuItem")
        });
        menu.IsOpen = true;

        PlayerAudioService.Outputs outputs;
        try { outputs = await Task.Run(() => PlayerAudioService.ReadOutputs(source)); }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            AppLog.Error("Read player audio outputs", ex);
            outputs = new([], null, false);
        }
        if (!_windowClosed && ReferenceEquals(_playerOutputMenu, menu) && menu.IsOpen)
            FillPlayerOutputMenu(menu, source, outputs);
    }

    private void FillPlayerOutputMenu(ContextMenu menu, string source, PlayerAudioService.Outputs outputs)
    {
        menu.Items.Clear();
        if (outputs.CanRoute)
        {
            Add(L("Выход Windows по умолчанию"), "\uE7F4", outputs.PreferredId == null, () => _ = SelectPlayerOutputAsync(source, null));
            foreach (var output in outputs.Devices)
            {
                bool selected = string.Equals(output.Id, outputs.PreferredId, StringComparison.OrdinalIgnoreCase);
                var item = Add(output.Name, "\uE7F6", selected, () => _ = SelectPlayerOutputAsync(source, output.Id));
                if (output.InUse)
                {
                    item.InputGestureText = L("Сейчас используется");
                    AutomationProperties.SetHelpText(item, item.InputGestureText);
                }
            }
            menu.Items.Add(new Separator { Style = (Style)FindResource("IslandMenuSeparator") });
        }
        else Add(L("Аудиосессия плеера недоступна"), "\uE7BA", false, null);
        Add(L("Микшер громкости Windows"), "\uE767", false, () => OpenWebSettings("ms-settings:apps-volume"));

        MenuItem Add(string text, string glyph, bool selected, Action? click)
        {
            var item = new MenuItem
            {
                Header = text, Tag = glyph, IsChecked = selected, IsEnabled = click != null,
                Style = (Style)FindResource("IslandMenuItem")
            };
            if (click != null) item.Click += (_, _) => click();
            menu.Items.Add(item);
            return item;
        }
    }

    private async Task SelectPlayerOutputAsync(string source, string? endpointId)
    {
        if (_changingPlayerOutput || !_hasPlayerMedia || _playerTrack?.Source != source) return;
        _changingPlayerOutput = true;
        try
        {
            if (_selectedMedia?.Track.Source != source) return;
            string? expected = await Task.Run(() => PlayerAudioService.ChangeOutput(source, endpointId));
            if (expected == null || !_isMediaPlaying || _windowClosed) return;
            await Task.Delay(800);
            if (_windowClosed || _playerTrack?.Source != source) return;
            var current = await Task.Run(() => PlayerAudioService.ReadOutputs(source));
            if (!_windowClosed && _playerTrack?.Source == source && current.Devices.Any(device => device.InUse)
                && !current.Devices.Any(device => device.InUse && string.Equals(device.Id, expected, StringComparison.OrdinalIgnoreCase)))
                ShowSideBubble(L("Выход выбран. Если звук не переключился, возобновите воспроизведение."), ToastCategory.Info, bypassDnd: true);
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            AppLog.Error("Change player audio output", ex);
            if (!_windowClosed && _playerTrack?.Source == source)
                ShowSideBubble(L("Не удалось переключить выход плеера."), ToastCategory.Info, bypassDnd: true);
        }
        finally { _changingPlayerOutput = false; }
    }
}
