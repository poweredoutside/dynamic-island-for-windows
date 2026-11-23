using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using DynamicIslandWin.Services;
using Windows.Media.Control;
using Button = System.Windows.Controls.Button;
using PlaybackStatus = Windows.Media.Control.GlobalSystemMediaTransportControlsSessionPlaybackStatus;

namespace DynamicIslandWin;

public partial class MainWindow
{
    private readonly record struct PlayerTimeline(double Position, double Duration, bool CanSeek);
    private enum PlayerAction { PlayPause, Next, Previous }
    private MediaSessions.Selection? _selectedMedia;
    private LyricsService.Track? _playerTrack;
    private PlayerTimeline? _playerTimeline;
    private DispatcherTimer? _playerSeekTimer;
    private bool _updatingPlayerSeek, _playerSeeking, _updatingPlayerVolume;
    private string? _playerVolumeSource;
    private PlayerAudioService.Snapshot? _playerVolume;
    private float? _pendingPlayerVolume;
    private bool _writingPlayerVolume;
    private int _playerVolumeRevision;
    private long _nextPlayerVolumeRead;
    private bool _hasPlayerMedia;
    private bool PlayerControlsVisible => HomeMusicWidget.IsVisible || _musicPlayerCard?.IsVisible == true;
    private System.Windows.Controls.Slider PlayerSeekSlider => _musicSeek?.IsVisible == true ? _musicSeek : HomePlayerSeek;
    private System.Windows.Controls.Slider PlayerVolumeSlider => _musicVolume?.IsVisible == true ? _musicVolume : HomePlayerVolume;

    private void LocalizeHomePlayer()
    {
        AutomationProperties.SetName(HomePlayerVolume, L("Громкость плеера"));
        UpdateHomePlayerVolume(_playerVolume);
        AutomationProperties.SetName(HomePlayerSeek, L("Перемотка трека"));
        LocalizePlayerOutputButton(HomePlayerOutputButton);
        RenderHomePlayerTimeline();
    }

    private void UpdatePlayerControlVisibility()
    {
        var visibility = _hasPlayerMedia ? Visibility.Visible : Visibility.Collapsed;
        HomePlayerTimelineRow.Visibility = HomePlayerControlsRow.Visibility = visibility;
        if (_musicTimelineRow != null) _musicTimelineRow.Visibility = visibility;
        if (_musicTransportRow != null) _musicTransportRow.Visibility = visibility;
        if (_musicVolumeRow != null) _musicVolumeRow.Visibility = visibility;
        if (_hasPlayerMedia) return;
        _playerSeekTimer?.Stop();
        SetHomePlayerVolumeSource(null);
    }

    private void HomeMusicWidget_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if ((bool)e.NewValue)
        {
            _nextPlayerVolumeRead = 0;
            _ = RefreshMediaSessionStateAsync();
            return;
        }
        if (PlayerControlsVisible) return;
        _playerSeekTimer?.Stop();
        SetHomePlayerVolumeSource(null);
    }

    private void SetHomePlayerVolumeSource(string? source)
    {
        if (_playerVolumeSource == source) return;
        _playerVolumeSource = source;
        _nextPlayerVolumeRead = 0;
        _playerVolumeRevision++;
        _pendingPlayerVolume = null;
        if (PlayerVolumeSlider.IsMouseCaptureWithin && System.Windows.Input.Mouse.Captured is Thumb thumb)
            thumb.CancelDrag();
        UpdateHomePlayerVolume(null);
    }

    private async Task RefreshHomePlayerVolumeAsync(string? source)
    {
        SetHomePlayerVolumeSource(source);
        if (source == null || _writingPlayerVolume || PlayerVolumeSlider.IsMouseCaptureWithin) return;
        long now = Environment.TickCount64;
        if (now < _nextPlayerVolumeRead) return;
        _nextPlayerVolumeRead = now + 2000;
        int revision = _playerVolumeRevision;
        var snapshot = await Task.Run(() => ReadOrSetPlayerVolume(source));
        if (!_windowClosed && revision == _playerVolumeRevision && _playerVolume != snapshot) UpdateHomePlayerVolume(snapshot);
    }

    private void UpdateHomePlayerVolume(PlayerAudioService.Snapshot? snapshot)
    {
        _playerVolume = snapshot;
        HomePlayerVolume.IsEnabled = snapshot.HasValue;
        if (_musicVolume != null) _musicVolume.IsEnabled = snapshot.HasValue;
        if (!PlayerVolumeSlider.IsMouseCaptureWithin && !_pendingPlayerVolume.HasValue)
        {
            _updatingPlayerVolume = true;
            try
            {
                HomePlayerVolume.Value = Math.Round((snapshot?.Volume ?? 0) * 100);
                if (_musicVolume != null) _musicVolume.Value = HomePlayerVolume.Value;
            }
            finally { _updatingPlayerVolume = false; }
        }
        HomePlayerVolumeIcon.Text = snapshot is { Muted: true } or { Volume: 0 } ? "\uE74F" : "\uE767";
        HomePlayerVolume.ToolTip = snapshot is { } level
            ? $"{L("Громкость плеера")} · {level.Volume:P0}"
            : L("Аудиосессия плеера недоступна");
        if (_musicVolume != null) _musicVolume.ToolTip = HomePlayerVolume.ToolTip;
    }

    private async void HomePlayerVolume_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingPlayerVolume || _windowClosed || !PlayerControlsVisible || _playerVolume == null) return;
        _pendingPlayerVolume = (float)(e.NewValue / 100);
        _playerVolumeRevision++;
        if (_writingPlayerVolume) return;
        _writingPlayerVolume = true;
        try
        {
            while (!_windowClosed && _pendingPlayerVolume is { } target && _playerVolumeSource is { } source)
            {
                _pendingPlayerVolume = null;
                int revision = _playerVolumeRevision;
                if (_selectedMedia?.Track.Source != source)
                {
                    SetHomePlayerVolumeSource(null);
                    break;
                }
                var snapshot = await Task.Run(() => ReadOrSetPlayerVolume(source, target));
                if (!_windowClosed && revision == _playerVolumeRevision) UpdateHomePlayerVolume(snapshot);
                if (PlayerVolumeSlider.IsMouseCaptureWithin) await Task.Delay(65);
            }
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            AppLog.Error("Select media session volume", ex);
            SetHomePlayerVolumeSource(null);
        }
        finally { _writingPlayerVolume = false; }
    }

    private static PlayerAudioService.Snapshot? ReadOrSetPlayerVolume(string source, float? volume = null)
    {
        try { return PlayerAudioService.ReadOrSet(source, volume); }
        catch (COMException) { return null; }
    }

    private void RefreshPlayerTransportControls()
    {
        var controls = _selectedMedia?.Playback.Controls;
        Set(HomePlayerPrevious, _musicPrevious, controls?.IsPreviousEnabled == true, "Предыдущий трек");
        Set(HomePlayerPlayPause, _musicPlayPauseButton, controls?.IsPlayPauseToggleEnabled == true
            || (_isMediaPlaying ? controls?.IsPauseEnabled == true : controls?.IsPlayEnabled == true), "Воспроизвести / пауза");
        Set(HomePlayerNext, _musicNext, controls?.IsNextEnabled == true, "Следующий трек");

        void Set(Button home, Button? music, bool supported, string label)
        {
            Update(home);
            if (music != null) Update(music);

            void Update(Button button)
            {
                button.IsEnabled = supported;
                button.Opacity = supported ? 1 : .45;
                button.ToolTip = L(supported ? label : "Действие недоступно в этом плеере");
                AutomationProperties.SetName(button, L(label));
                ToolTipService.SetShowOnDisabled(button, true);
            }
        }
    }

    private async Task RunPlayerActionAsync(PlayerAction action)
    {
        if (_windowClosed || _selectedMedia is not { } media) return;
        try
        {
            var session = media.Session;
            var info = session.GetPlaybackInfo();
            var controls = info?.Controls;
            if (controls == null) return;
            var operation = action switch
            {
                PlayerAction.Next when controls.IsNextEnabled => session.TrySkipNextAsync(),
                PlayerAction.Previous when controls.IsPreviousEnabled => session.TrySkipPreviousAsync(),
                PlayerAction.PlayPause when controls.IsPlayPauseToggleEnabled => session.TryTogglePlayPauseAsync(),
                PlayerAction.PlayPause when info!.PlaybackStatus == PlaybackStatus.Playing && controls.IsPauseEnabled => session.TryPauseAsync(),
                PlayerAction.PlayPause when info!.PlaybackStatus != PlaybackStatus.Playing && controls.IsPlayEnabled => session.TryPlayAsync(),
                _ => null
            };
            if (operation == null) return;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            if (await operation.AsTask(timeout.Token)) await Task.Delay(120);
            await RefreshMediaSessionStateAsync();
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or OperationCanceledException)
        {
            AppLog.Error("Control media session", ex);
        }
    }

    private void UpdateHomePlayerTimeline(LyricsService.Track? track, PlayerTimeline? timeline)
    {
        if (_playerTrack?.Source != track?.Source && _playerOutputMenu is { IsOpen: true } menu) menu.IsOpen = false;
        if (_playerTrack != track || timeline is not { CanSeek: true })
        {
            _playerSeekTimer?.Stop();
            if (PlayerSeekSlider.IsMouseCaptureWithin && System.Windows.Input.Mouse.Captured is Thumb thumb)
                thumb.CancelDrag();
        }
        _playerTrack = track;
        _playerTimeline = timeline;
        RenderHomePlayerTimeline();
    }

    private void RenderHomePlayerTimeline()
    {
        if (_playerSeekTimer?.IsEnabled == true || PlayerSeekSlider.IsMouseCaptureWithin) return;
        HomePlayerSeek.IsEnabled = _playerTimeline is { CanSeek: true } && !_playerSeeking;
        HomePlayerSeek.ToolTip = L(_playerTimeline is { CanSeek: true } ? "Перемотка трека" : "Доступность перемотки зависит от плеера");
        if (_musicSeek != null)
        {
            _musicSeek.IsEnabled = HomePlayerSeek.IsEnabled;
            _musicSeek.ToolTip = HomePlayerSeek.ToolTip;
        }
        if (_playerSeeking) return;

        _updatingPlayerSeek = true;
        try
        {
            HomePlayerSeek.Maximum = Math.Max(1, Math.Floor(_playerTimeline?.Duration ?? 0));
            HomePlayerSeek.Value = Math.Floor(_playerTimeline?.Position ?? 0);
            HomePlayerElapsed.Text = _playerTimeline is { } current ? FormatPlayerTime(current.Position) : "–:––";
            HomePlayerDuration.Text = _playerTimeline is { } total ? FormatPlayerTime(total.Duration) : "–:––";
            if (_musicSeek != null)
            {
                _musicSeek.Maximum = HomePlayerSeek.Maximum;
                _musicSeek.Value = HomePlayerSeek.Value;
                _musicElapsed!.Text = HomePlayerElapsed.Text;
                _musicDuration!.Text = HomePlayerDuration.Text;
            }
        }
        finally { _updatingPlayerSeek = false; }
    }

    private static string FormatPlayerTime(double seconds)
    {
        var time = TimeSpan.FromSeconds(Math.Floor(seconds));
        return time.TotalHours >= 1
            ? $"{(long)time.TotalHours}:{time.Minutes:D2}:{time.Seconds:D2}"
            : $"{time.Minutes}:{time.Seconds:D2}";
    }

    private void HomePlayerSeek_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingPlayerSeek || _playerSeeking || !IsLoaded || !PlayerSeekSlider.IsEnabled) return;
        if (ReferenceEquals(sender, _musicSeek)) _musicElapsed!.Text = FormatPlayerTime(e.NewValue);
        else HomePlayerElapsed.Text = FormatPlayerTime(e.NewValue);
        if (PlayerSeekSlider.IsMouseCaptureWithin) return;

        if (_playerSeekTimer == null)
        {
            _playerSeekTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(140) };
            _playerSeekTimer.Tick += async (_, _) =>
            {
                _playerSeekTimer.Stop();
                if (!PlayerSeekSlider.IsMouseCaptureWithin) await SeekHomePlayerAsync();
            };
        }
        _playerSeekTimer.Stop();
        _playerSeekTimer.Start();
    }

    private async void HomePlayerSeek_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        _playerSeekTimer?.Stop();
        if (e.Canceled) RenderHomePlayerTimeline();
        else await SeekHomePlayerAsync();
    }

    private async Task SeekHomePlayerAsync()
    {
        if (_windowClosed || _playerSeeking || !PlayerControlsVisible
            || _playerTimeline is not { CanSeek: true } || _selectedMedia is not { } selected) return;
        var session = selected.Session;
        var track = selected.Track;

        double seconds = PlayerSeekSlider.Value;
        _playerSeeking = true;
        RenderHomePlayerTimeline();
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            var media = await session.TryGetMediaPropertiesAsync().AsTask(timeout.Token);
            if (_windowClosed || !PlayerControlsVisible || !Equals(_selectedMedia?.Session, session) || _playerTrack != track
                || string.IsNullOrWhiteSpace(media?.Title) || MediaSessions.ReadTrack(media, track.Source) != track) return;
            var timeline = session.GetTimelineProperties();
            if (session.GetPlaybackInfo()?.Controls?.IsPlaybackPositionEnabled != true
                || !TryGetSeekBounds(timeline, out var earliest, out var latest)) return;

            var requested = timeline.StartTime + TimeSpan.FromSeconds(seconds);
            var target = requested < earliest ? earliest : requested > latest ? latest : requested;
            if (!await session.TryChangePlaybackPositionAsync(target.Ticks).AsTask(timeout.Token))
            {
                if (!_windowClosed) ShowSideBubble(L("Плеер не выполнил перемотку."), ToastCategory.Info);
                return;
            }
            if (!_windowClosed && Equals(_selectedMedia?.Session, session) && _playerTrack == track && _playerTimeline is { } current)
            {
                var position = Math.Clamp((target - timeline.StartTime).TotalSeconds, 0, current.Duration);
                _playerTimeline = current with { Position = position };
                if (_lyricsTrack == track && _isMediaPlaying)
                {
                    _lyricsPosition = position;
                    _lyricsPositionTime = Stopwatch.GetTimestamp();
                }
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("Seek media player", ex);
            if (!_windowClosed) ShowSideBubble(L("Плеер не выполнил перемотку."), ToastCategory.Info);
        }
        finally
        {
            _playerSeeking = false;
            if (!_windowClosed) RenderHomePlayerTimeline();
        }
    }

    private static bool TryGetSeekBounds(GlobalSystemMediaTransportControlsSessionTimelineProperties timeline,
        out TimeSpan earliest, out TimeSpan latest)
    {
        earliest = timeline.StartTime;
        latest = timeline.EndTime;
        if (timeline.MaxSeekTime > timeline.MinSeekTime)
        {
            if (timeline.MinSeekTime > earliest) earliest = timeline.MinSeekTime;
            if (timeline.MaxSeekTime < latest) latest = timeline.MaxSeekTime;
        }
        return latest > earliest;
    }
}
