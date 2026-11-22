using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using DynamicIslandWin.Services;

namespace DynamicIslandWin;

public partial class MainWindow
{
    private LyricsService.Track? _lyricsTrack, _importedLyricsTrack;
    private LyricsService.Line[] _onlineLyrics = [], _importedLyrics = [];
    private string? _plainLyrics;
    private CancellationTokenSource? _lyricsSearch;
    private bool _lyricsSearchAttempted;
    private string _lyricsAlbum = "";
    private double _lyricsDuration;
    private string _lyricsSearchStatus = "";
    private string? _lyricsImportStatus;
    private double? _lyricsPosition;
    private long _lyricsPositionTime;
    private TextBlock? _lyricsStatusText;
    private TextBlock? _musicLyricLine;
    private TextBlock? _musicNextLyric, _musicLastLyric;
    private TextBlock? _plainLyricsText;
    private Expander? _plainLyricsPanel;
    private System.Windows.Controls.Button? _lyricsRetryButton;
    private static readonly DoubleAnimation LyricFade = LyricAnimation(.15, 1, 240);
    private static readonly DoubleAnimation LyricSlide = LyricAnimation(7, 0, 260);

    private void UpdateLyricsTrack(LyricsService.Track track, double? position, string album, double duration)
    {
        if (_lyricsTrack != track)
        {
            CancelLyricsSearch();
            _lyricsTrack = track;
            _onlineLyrics = [];
            _plainLyrics = null;
            _lyricsSearchAttempted = false;
            _lyricsSearchStatus = "";
            _lyricsImportStatus = null;
        }

        _lyricsPosition = position;
        _lyricsAlbum = album;
        _lyricsDuration = duration;
        _lyricsPositionTime = Stopwatch.GetTimestamp();
        if (_settings.OnlineLyrics && !_lyricsSearchAttempted && _importedLyricsTrack != track)
            _ = FindLyricsAsync(track, album, duration);
    }

    private (string Current, string Next, string Last) CurrentLyricLines()
    {
        if (_lyricsTrack == null || _lyricsPosition is not double position) return ("", "", "");
        var lines = _importedLyricsTrack == _lyricsTrack ? _importedLyrics : _settings.OnlineLyrics ? _onlineLyrics : [];
        int index = LyricsService.IndexAt(lines, position + Stopwatch.GetElapsedTime(_lyricsPositionTime).TotalSeconds);
        if (index < 0 || lines[index].Text.Length == 0) return ("", "", "");
        int count = Math.Clamp(_settings.LyricsLineCount, 1, 3);
        string Next(int from, out int found)
        {
            for (found = from + 1; found < lines.Length; found++)
                if (lines[found].Text.Length > 0 && lines[found].Seconds > lines[from].Seconds) return lines[found].Text;
            return "";
        }
        int nextIndex = index;
        string next = count >= 2 ? Next(index, out nextIndex) : "";
        return (lines[index].Text, next, count >= 3 && next.Length > 0 ? Next(nextIndex, out _) : "");
    }

    private void UpdateCompactLyrics()
    {
        if (_currentState == IslandState.Compact && CompactTrackText.Visibility == Visibility.Visible && _settings.Karaoke)
        {
            var (current, next, last) = CurrentLyricLines();
            if (CompactLyricsText.Text != current || CompactNextLyric.Text != next || CompactLastLyric.Text != last)
                UpdateCompactContent();
        }
        UpdateMusicLyrics();
    }

    private void UpdateMusicLyrics()
    {
        if (_currentState != IslandState.Section || _featurePage != "music" || _musicLyricLine == null) return;
        var (current, next, last) = _settings.Karaoke ? CurrentLyricLines() : ("", "", "");
        ShowLyricLine(_musicLyricLine, current);
        ShowLyricLine(_musicNextLyric!, next, animate: false);
        ShowLyricLine(_musicLastLyric!, last, animate: false);
    }

    private static void ShowLyricLine(TextBlock target, string line, bool animate = true)
    {
        if (target.Text == line) return;
        target.Text = line;
        target.Visibility = line.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (!animate || line.Length == 0 || !target.IsVisible || !SystemParameters.ClientAreaAnimation)
        {
            target.BeginAnimation(OpacityProperty, null);
            if (target.RenderTransform is TranslateTransform previous)
                previous.BeginAnimation(TranslateTransform.YProperty, null);
            return;
        }

        if (target.RenderTransform is not TranslateTransform offset)
            target.RenderTransform = offset = new TranslateTransform();
        target.BeginAnimation(OpacityProperty, LyricFade, HandoffBehavior.SnapshotAndReplace);
        offset.BeginAnimation(TranslateTransform.YProperty, LyricSlide, HandoffBehavior.SnapshotAndReplace);
    }

    private static DoubleAnimation LyricAnimation(double from, double to, int milliseconds)
    {
        var animation = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(milliseconds))
        {
            FillBehavior = FillBehavior.Stop,
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        animation.Freeze();
        return animation;
    }

    private void CancelLyricsSearch()
    {
        var search = _lyricsSearch;
        _lyricsSearch = null;
        search?.Cancel();
    }

    private async Task FindLyricsAsync(LyricsService.Track track, string album, double duration)
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        _lyricsSearch = cancellation;
        _lyricsSearchAttempted = true;
        _lyricsSearchStatus = "Ищу текст песни…";
        RefreshLyricsStatus();
        try
        {
            var result = await LyricsService.FindAsync(_http, track, album, duration, cancellation.Token);
            if (_lyricsSearch != cancellation) return;
            _onlineLyrics = result.Lines;
            _plainLyrics = result.Lines.Length == 0 ? result.Plain : null;
            _lyricsSearchStatus = result.Lines.Length > 0 ? "Синхронизированный текст найден."
                : _plainLyrics != null ? "Найден текст без синхронизации." : "Текст песни не найден.";
        }
        catch (Exception ex)
        {
            if (_lyricsSearch != cancellation) return;
            _lyricsSearchStatus = "Не удалось найти текст. Можно импортировать LRC.";
            if (ex is not OperationCanceledException) AppLog.Error("Find synchronized lyrics", ex);
        }
        finally
        {
            if (_lyricsSearch == cancellation)
            {
                _lyricsSearch = null;
                RefreshLyricsStatus();
                UpdateCompactContent();
                UpdateMusicLyrics();
            }
        }
    }

    private void SetOnlineLyrics(bool enabled)
    {
        _settings.OnlineLyrics = enabled;
        if (!enabled)
        {
            CancelLyricsSearch();
            _onlineLyrics = [];
            _plainLyrics = null;
            _lyricsSearchAttempted = false;
            _lyricsSearchStatus = "";
        }
        RefreshLyricsStatus();
    }

    private void RetryLyricsSearch()
    {
        if (_settings.OnlineLyrics && _lyricsSearch == null && _isMediaPlaying && _lyricsTrack is { } track
            && _importedLyricsTrack != track)
            _ = FindLyricsAsync(track, _lyricsAlbum, _lyricsDuration);
    }

    private async Task ImportLyricsAsync()
    {
        if (!_isMediaPlaying || _lyricsTrack is not { } track)
        {
            RefreshLyricsStatus();
            return;
        }
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = L("Файл текста песни|*.lrc"), CheckFileExists = true };
        if (dialog.ShowDialog(this) != true) return;
        _lyricsImportStatus = null;
        try
        {
            using var stream = File.OpenRead(dialog.FileName);
            var lines = LyricsService.Parse(await LyricsService.ReadTextAsync(stream, CancellationToken.None));
            if (_windowClosed) return;
            if (track != _lyricsTrack)
            {
                _lyricsImportStatus = "Трек сменился. Повторите импорт для нужной песни.";
                RefreshLyricsStatus();
                return;
            }
            if (lines.Length == 0 || lines.All(line => line.Text.Length == 0)) throw new InvalidDataException();
            CancelLyricsSearch();
            _importedLyricsTrack = track;
            _importedLyrics = lines;
            RefreshLyricsStatus();
            UpdateCompactContent();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            _lyricsImportStatus = "Не удалось импортировать LRC. Проверьте файл и временные метки.";
            RefreshLyricsStatus();
        }
    }

    private void RefreshLyricsStatus()
    {
        if (_lyricsStatusText == null || _currentState != IslandState.Section || _featurePage != "music") return;
        string status = !_isMediaPlaying || _lyricsTrack == null ? "Включите нужный трек, чтобы найти или импортировать текст."
            : _lyricsImportStatus is { } importStatus ? importStatus
            : _importedLyricsTrack == _lyricsTrack ? "Текст загружен из LRC для этого трека."
            : !_settings.OnlineLyrics ? "Поиск выключен. Можно импортировать LRC."
            : _lyricsSearchStatus;
        _lyricsStatusText.Text = L(status);
        bool canSearch = _isMediaPlaying && _settings.OnlineLyrics && _lyricsTrack != null && _importedLyricsTrack != _lyricsTrack;
        if (_lyricsRetryButton is { } retry)
        {
            retry.Visibility = canSearch ? Visibility.Visible : Visibility.Collapsed;
            retry.IsEnabled = _lyricsSearch == null;
        }
        if (_plainLyricsPanel is { } plain && _plainLyricsText is { } text)
        {
            bool showPlain = canSearch && _plainLyrics != null;
            if (text.Text != (showPlain ? _plainLyrics : "")) plain.IsExpanded = false;
            text.Text = showPlain ? _plainLyrics : "";
            plain.Visibility = showPlain ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}
