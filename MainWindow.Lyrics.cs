using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using DynamicIslandWin.Services;

namespace DynamicIslandWin;

public partial class MainWindow
{
    private LyricsService.Track? _lyricsTrack, _importedLyricsTrack;
    private LyricsService.Line[] _onlineLyrics = [], _importedLyrics = [];
    private CancellationTokenSource? _lyricsSearch;
    private bool _lyricsSearchAttempted;
    private string _lyricsSearchStatus = "";
    private string? _lyricsImportStatus;
    private double? _lyricsPosition;
    private long _lyricsPositionTime;
    private TextBlock? _lyricsStatusText;

    private void UpdateLyricsTrack(LyricsService.Track track, double? position, string album, double duration)
    {
        if (_lyricsTrack != track)
        {
            CancelLyricsSearch();
            _lyricsTrack = track;
            _onlineLyrics = [];
            _lyricsSearchAttempted = false;
            _lyricsSearchStatus = "";
            _lyricsImportStatus = null;
        }

        _lyricsPosition = position;
        _lyricsPositionTime = Stopwatch.GetTimestamp();
        if (_settings.OnlineLyrics && !_lyricsSearchAttempted && _importedLyricsTrack != track && track.Artist.Length > 0)
            _ = FindLyricsAsync(track, album, duration);
    }

    private string CurrentLyricLine()
    {
        if (_lyricsTrack == null || _lyricsPosition is not double position) return "";
        var lines = _importedLyricsTrack == _lyricsTrack ? _importedLyrics : _settings.OnlineLyrics ? _onlineLyrics : [];
        return LyricsService.LineAt(lines, position + Stopwatch.GetElapsedTime(_lyricsPositionTime).TotalSeconds);
    }

    private void UpdateCompactLyrics()
    {
        if (_currentState == IslandState.Compact && CompactTrackText.Visibility == Visibility.Visible
            && _settings.Karaoke && CompactLyricsText.Text != CurrentLyricLine())
            UpdateCompactContent();
    }

    private void CancelLyricsSearch()
    {
        var search = _lyricsSearch;
        _lyricsSearch = null;
        search?.Cancel();
    }

    private async Task FindLyricsAsync(LyricsService.Track track, string album, double duration)
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        _lyricsSearch = cancellation;
        _lyricsSearchAttempted = true;
        _lyricsSearchStatus = "Ищу синхронизированный текст…";
        RefreshLyricsStatus();
        try
        {
            var lines = await LyricsService.FindAsync(_http, track, album, duration, cancellation.Token);
            if (_lyricsSearch != cancellation) return;
            _onlineLyrics = lines;
            _lyricsSearchStatus = lines.Length > 0 ? "Синхронизированный текст найден." : "Синхронизированный текст не найден.";
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
            _lyricsSearchAttempted = false;
            _lyricsSearchStatus = "";
        }
        RefreshLyricsStatus();
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
            : _lyricsTrack.Artist.Length == 0 ? "Для поиска нужен исполнитель. Можно импортировать LRC."
            : _lyricsSearchStatus;
        _lyricsStatusText.Text = L(status);
    }
}
