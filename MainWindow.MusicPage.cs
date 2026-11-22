using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using DynamicIslandWin.Services;
using Button = System.Windows.Controls.Button;
using Orientation = System.Windows.Controls.Orientation;
using Panel = System.Windows.Controls.Panel;
using Path = System.Windows.Shapes.Path;

namespace DynamicIslandWin;

public partial class MainWindow
{
    private Slider? _musicSeek, _musicVolume, _musicMaster;
    private TextBlock? _musicElapsed, _musicDuration;
    private Path? _musicPlayPause;
    private bool _updatingMusicMaster;

    private void BuildMusicPlayerControls(Panel card)
    {
        var timeline = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        _musicElapsed = TimeLabel(Dock.Left);
        _musicDuration = TimeLabel(Dock.Right);
        _musicSeek = PlayerSlider("Перемотка трека", 1);
        _musicSeek.Margin = new Thickness(8, 0, 8, 0);
        _musicSeek.ValueChanged += HomePlayerSeek_ValueChanged;
        _musicSeek.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler(HomePlayerSeek_DragCompleted));
        _musicSeek.IsVisibleChanged += (_, e) =>
        {
            if ((bool)e.NewValue) _ = RefreshMediaSessionStateAsync();
            else HomeMusicWidget_IsVisibleChanged(this, e);
        };
        timeline.Children.Add(_musicSeek);
        card.Children.Add(timeline);

        var transport = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Center };
        transport.Children.Add(TransportButton("Предыдущий трек", "M6,6H8V18H6M9.5,12L18,18V6", PrevTrack_Click));
        var play = TransportButton("Воспроизвести / пауза", "M8,5V19L19,12Z", PlayPause_Click);
        play.SetResourceReference(Button.BackgroundProperty, "ThemeAccentBrush");
        _musicPlayPause = (Path)play.Content;
        _musicPlayPause.SetResourceReference(Path.FillProperty, "ThemeOnAccentBrush");
        UpdateMusicPlayPause();
        transport.Children.Add(play);
        transport.Children.Add(TransportButton("Следующий трек", "M16,18H18V6H16M6,18L14.5,12L6,6V18Z", NextTrack_Click));
        card.Children.Add(transport);

        var levels = new UniformGrid { Columns = 2, Margin = new Thickness(0, 8, 0, 0) };
        _musicVolume = AddLevel("Громкость плеера", new Thickness(0, 0, 12, 0));
        _musicVolume.ValueChanged += HomePlayerVolume_ValueChanged;
        _musicMaster = AddLevel("Громкость Windows", new Thickness(12, 0, 0, 0));
        _musicMaster.IsVisibleChanged += (_, _) => UpdateVolumeObservation();
        _musicMaster.ValueChanged += (_, e) =>
        {
            if (!_updatingMusicMaster && _musicMaster?.IsVisible == true)
                _volumeIsland?.SetVolume(e.NewValue);
        };
        _musicMaster.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler((_, _) => _volumeIsland?.RequestSnapshot()));
        card.Children.Add(levels);
        UpdateHomePlayerVolume(_playerVolume);
        RenderHomePlayerTimeline();

        TextBlock TimeLabel(Dock side)
        {
            var label = FeatureText("–:––", 11, "ThemeMutedBrush");
            label.VerticalAlignment = VerticalAlignment.Center;
            label.Margin = new Thickness(0);
            DockPanel.SetDock(label, side);
            timeline.Children.Add(label);
            return label;
        }
        Slider AddLevel(string label, Thickness margin)
        {
            var column = new StackPanel { Margin = margin };
            column.Children.Add(FeatureText(label, 11, "ThemeMutedBrush"));
            var slider = PlayerSlider(label, 100);
            column.Children.Add(slider);
            levels.Children.Add(column);
            return slider;
        }
    }

    private Slider PlayerSlider(string label, double maximum)
    {
        var slider = new Slider { Style = (Style)FindResource("PlayerSlider"), Maximum = maximum, LargeChange = 5, IsEnabled = false };
        AutomationProperties.SetName(slider, L(label));
        ToolTipService.SetShowOnDisabled(slider, true);
        return slider;
    }

    private Button TransportButton(string label, string icon, RoutedEventHandler click)
    {
        var button = FeatureButton(label, () => click(this, new RoutedEventArgs(Button.ClickEvent)));
        button.Padding = new Thickness(14, 7, 14, 7);
        var path = new Path { Data = Geometry.Parse(icon), Width = 13, Height = 13, Stretch = Stretch.Uniform };
        path.SetResourceReference(Path.FillProperty, "ThemeTextBrush");
        button.Content = path;
        button.ToolTip = L(label);
        AutomationProperties.SetName(button, L(label));
        return button;
    }

    private void UpdateMusicPlayPause()
    {
        if (_musicPlayPause != null) _musicPlayPause.Data = PlayPauseVectorIcon.Data;
    }

    private void UpdateVolumeObservation()
    {
        if (_windowClosed || _volumeIsland is not { } audio) return;
        audio.ObserveVolume = WidgetIsVisible("volume") || _musicMaster?.IsVisible == true;
        if (audio.ObserveVolume) audio.RequestSnapshot();
    }

    private void UpdateMusicMasterVolume(VolumeIslandService.Snapshot snapshot)
    {
        if (_musicMaster is not { } slider) return;
        slider.IsEnabled = true;
        if (slider.IsMouseCaptureWithin) return;
        _updatingMusicMaster = true;
        try { slider.Value = Math.Round(snapshot.Volume * 100); }
        finally { _updatingMusicMaster = false; }
        slider.ToolTip = $"{L("Громкость Windows")} · {snapshot.Volume:P0}";
    }
}
