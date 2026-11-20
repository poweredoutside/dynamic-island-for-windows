using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using DynamicIslandWin.Services;
using Microsoft.Win32;
using Button = System.Windows.Controls.Button;
using Point = System.Windows.Point;
using Cursors = System.Windows.Input.Cursors;
using Color = System.Windows.Media.Color;
using Brushes = System.Windows.Media.Brushes;
using Path = System.IO.Path;
using Control = System.Windows.Controls.Control;
using FontFamily = System.Windows.Media.FontFamily;
using Size = System.Windows.Size;

namespace DynamicIslandWin;

public partial class MainWindow
{
    public partial class AppSettings
    {
        public double CompactWidth { get; set; } = 210;
        public double CompactHeight { get; set; } = 38;
        public double CornerRounding { get; set; } = 1;
        public double IdleOpacity { get; set; } = 1;
        public double BackgroundTransparency { get; set; }
        public bool LensGlass { get; set; }
        public bool IslandShadow { get; set; } = true;
        public string IslandFont { get; set; } = "Segoe UI";
        public string IdleCenter { get; set; } = "time";
        public string IdleRight { get; set; } = "weather";
        public bool Clock12Hour { get; set; }
        public bool ClockSeconds { get; set; }
        public int HoverDelayMs { get; set; } = 250;
        public bool ReminderSound { get; set; } = true;
        public bool LevelHud { get; set; } = true;
        public bool? VolumeIslandHud { get; set; }
        public bool FollowCursorMonitor { get; set; }
        public bool TitleUnreadFallback { get; set; } = true;
        public bool ExternalProgress { get; set; } = true;
        public bool Karaoke { get; set; }
        public bool OnlineLyrics { get; set; }
        public string GeminiModel { get; set; } = "gemini-3.8-live";
        public string GeminiVoice { get; set; } = "Kore";
        public string UpdateRepository { get; set; } = GitHubUpdateService.DefaultRepository;
        public bool AutoCheckUpdates { get; set; } = true;
        public double GeminiGain { get; set; } = .8;
        public List<UserTheme> CustomThemes { get; set; } = [];
        public List<string> TabOrder { get; set; } = [];
        public List<string> HiddenTabs { get; set; } = [];
        public List<string> ActionOrder { get; set; } = ["Region", "Full", "Translate", "Record", "Command", "Dnd"];
        public List<ScheduledTimer> Timers { get; set; } = [];
        public List<string> DeliveredReminders { get; set; } = [];
        public string LastSeenVersion { get; set; } = "";
        public bool OnboardingComplete { get; set; }
        public bool AutoGameHud { get; set; }
    }

    private readonly DispatcherTimer _featureTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly DispatcherTimer _hoverDelay = new();
    private GeminiVoiceService? _voice;
    private Task? _featureScan;
    private DateTime _nextFeatureScan, _nextCalendarRead;
    private VolumeIslandService? _volumeIsland;
    private readonly object _volumeGate = new();
    private VolumeIslandService.Snapshot _pendingVolume;
    private VolumeIslandService.Snapshot _displayedVolume;
    private bool _volumeDispatchPending;
    private bool _updatingVolumeSlider, _manualVolumeOpen;
    private int? _lastBrightness;
    private DateTime _nextBrightnessRead, _nextReminderCheck;
    private IReadOnlyList<CalendarMeeting> _meetings = [];
    private Action? _featureNoticeAction;
    private DateTime _featureNoticeUntil;
    private readonly Dictionary<IntPtr, int> _unreadCounts = [];
    private readonly Dictionary<IntPtr, (string Name, int Percent)> _jobs = [];
    private bool _manualResize;
    private string _featurePage = "system";
    private TextBlock? _voiceStatusText, _voiceTranscript;
    private DateTime _uiHeartbeat = DateTime.UtcNow;
    private System.Threading.Timer? _watchdog;
    private int _welcomeStep;
    private readonly Queue<(string Text, Action Action)> _featureNotices = [];
    private FpsService? _gameFps;
    private readonly DesktopCompositionFps _desktopFps = new();

    private string CalendarPath => Path.Combine(Path.GetDirectoryName(_settingsPath)!, "calendar.ics");

    private void InitializeFeatures()
    {
        _settings.TabOrder ??= [];
        _settings.HiddenTabs ??= []; _settings.ActionOrder ??= []; _settings.Timers ??= []; _settings.DeliveredReminders ??= [];
        ApplyFeatureAppearance();
        ApplyTheme(_themes.FirstOrDefault(t => t.Key == _settings.SelectedTheme) ?? _themes[0]);
        ConfigureLensGlass();
        InitializeNotesWidgets();
        IslandBorder.LostMouseCapture += (_, _) => { _isClickCandidate = false; if (_bodyDragging) { _bodyDragging = false; RememberIslandPlacement(); SaveSettings(); } };
        _settings.VolumeIslandHud ??= _settings.LevelHud;
        _volumeIsland = new VolumeIslandService(_settings.VolumeIslandHud == true);
        _volumeIsland.Changed += VolumeChanged;
        _volumeIsland.ObserveVolume = WidgetIsVisible("volume");
        if (_volumeIsland.ObserveVolume) _volumeIsland.RequestSnapshot();
        _hoverDelay.Tick += (_, _) => { _hoverDelay.Stop(); if (IslandBorder.IsMouseOver && !_isClickCandidate && !_bodyDragging && _currentState == IslandState.Compact && !_manualResize && !_isGameModeActive) MorphToState(IslandState.Hover); };
        _featureTimer.Tick += (_, _) => FeatureTick(); _featureTimer.Start();
        _nextUpdateCheck = DateTime.UtcNow.AddSeconds(5);
        if (_settings.GameModeHud) StartGameMetrics();
        _watchdog = new System.Threading.Timer(_ =>
        {
            if (DateTime.UtcNow - _uiHeartbeat > TimeSpan.FromSeconds(5) && !_windowClosed)
                AppLog.Error("UI heartbeat delayed", new TimeoutException());
        }, null, 10000, 15000);
        bool existed = File.Exists(_settingsPath);
        if (!existed && !_settings.OnboardingComplete) { _welcomeStep = 0; OpenFeaturePage("welcome"); }
        else if (_settings.LastSeenVersion != ProductVersion)
        {
            _settings.OnboardingComplete = true;
            FeatureNotice($"OC - Dynamic Island {ProductVersion} · {L("Что нового")}", () => OpenSettingsFeaturePage("updates"));
            _settings.LastSeenVersion = ProductVersion; SaveSettings();
        }
    }

    private void LoadFeatureThemes()
    {
        if (string.IsNullOrWhiteSpace(_settings.UpdateRepository)) _settings.UpdateRepository = GitHubUpdateService.DefaultRepository;
        _settings.CustomThemes = (_settings.CustomThemes ?? []).Where(ThemeCode.Valid).DistinctBy(t => t.Name).Take(32).ToList();
        _settings.ActionOrder ??= []; _settings.HiddenTabs ??= []; _settings.TabOrder ??= [];
        _settings.Timers ??= []; _settings.DeliveredReminders ??= [];
        _themes.RemoveAll(t => t.Key.StartsWith("User: ", StringComparison.Ordinal));
        foreach (var theme in _settings.CustomThemes) _themes.Add(new("User: " + theme.Name, theme.Island, theme.Scene, theme.Accent, theme.Light));
    }

    private void DisposeFeatures()
    {
        DisposeDesktopWidgets();
        _calendarCancellation.Cancel();
        DisposeLensGlass();
        _featureTimer.Stop(); _hoverDelay.Stop(); _watchdog?.Dispose();
        if (_volumeIsland != null) { _volumeIsland.Changed -= VolumeChanged; _volumeIsland.Dispose(); }
        StopGameMetrics();
        _updateCancellation?.Cancel();
        CancelLyricsSearch();
        if (_voice != null) _ = _voice.DisposeAsync();
    }

    private void ApplyFeatureAppearance()
    {
        _settings.CompactWidth = Math.Clamp(_settings.CompactWidth, 160, 650);
        _settings.CompactHeight = Math.Clamp(_settings.CompactHeight, 32, 100);
        _settings.CornerRounding = Math.Clamp(_settings.CornerRounding, 0, 1);
        string font = _settings.IslandFont is "Segoe UI" or "Consolas" or "Calibri" or "Verdana" ? _settings.IslandFont : "Segoe UI";
        if (System.Windows.Documents.TextElement.GetFontFamily(IslandBorder).Source != font)
            System.Windows.Documents.TextElement.SetFontFamily(IslandBorder, new FontFamily(font));
        // Render a cached silhouette, instead of blurring the entire live visual tree.
        UpdateGlassShadow();
        foreach (string key in _settings.ActionOrder.AsEnumerable().Reverse())
            if (FindName("BtnQuick" + key) is UIElement element && HomeQuickActionsBar.Children.Contains(element))
            { HomeQuickActionsBar.Children.Remove(element); HomeQuickActionsBar.Children.Insert(0, element); }
        UpdateCompactContent();
        StartIslandFrames();
    }

    private void FeatureTick()
    {
        _uiHeartbeat = DateTime.UtcNow;
        if (_windowClosed) return;
        var now = DateTimeOffset.Now;
        if (LevelHudBorder.IsMouseOver || VolumeHudSlider.IsKeyboardFocusWithin || VolumeHudSlider.IsMouseCaptureWithin)
            _featureNoticeUntil = DateTime.UtcNow.AddSeconds(3);
        else if (DateTime.UtcNow > _featureNoticeUntil)
        { LevelHudBorder.Visibility = Visibility.Collapsed; _manualVolumeOpen = false; }
        DrainFeatureNotices();
        UpdateCompactLyrics(); UpdateCursorAppearance();
        RefreshLiveTimer();
        TickAiAgents();
        if (_timedDnd is { } timed && timed.Until <= now && !_dndChanging) RestoreTimedDnd();
        if (_isGameModeActive)
        {
            UpdateFpsTarget();
            _desktopFps.Poll();
            UpdateGameHudValues();
        }
        if (_currentState == IslandState.Section && _featurePage == "system") RefreshGameMetricsControls();
        if (_settings.AutoCheckUpdates && !_updateBusy && DateTime.UtcNow >= _nextUpdateCheck)
            _ = CheckForUpdatesAsync(_settings.UpdateRepository, automatic: true);
        if (NeedsIslandFrames()) StartIslandFrames();
        if (now.UtcDateTime >= _nextReminderCheck)
        {
            _nextReminderCheck = now.UtcDateTime.AddSeconds(1);
            CheckScheduledReminders(now);
        }
        if (_featureScan is { IsCompleted: false } || DateTime.UtcNow < _nextFeatureScan) return;
        _nextFeatureScan = DateTime.UtcNow.AddSeconds(3);
        _featureScan = ScanFeaturesAsync();
    }

    private void CheckScheduledReminders(DateTimeOffset now)
    {
        bool refreshReminders = false;
        foreach (var timer in _settings.Timers.Where(t => t.Due <= now).ToArray())
        {
            _settings.Timers.Remove(timer); SaveSettings();
            FeatureNotice(timer.Label, () => OpenFeaturePage("calendar"));
            if (_settings.ReminderSound && !_isDndActive) System.Media.SystemSounds.Exclamation.Play();
            refreshReminders = true;
        }
        if (refreshReminders) RefreshCalendarAgenda();
        TickCalendar();
        foreach (var meeting in _meetings.Where(m => m.Join != null))
        {
            foreach (int minutes in new[] { 10, 0 })
            {
                string marker = meeting.Id + ":" + minutes;
                var due = meeting.Start.AddMinutes(-minutes);
                if (now < due || now - due > TimeSpan.FromMinutes(2) || _settings.DeliveredReminders.Contains(marker)) continue;
                _settings.DeliveredReminders.Add(marker);
                _settings.DeliveredReminders = _settings.DeliveredReminders.TakeLast(512).ToList(); SaveSettings();
                FeatureNotice($"{meeting.Title} · {L(minutes == 0 ? "начинается сейчас" : "через 10 минут")}", () => OpenWeb(meeting.Join!));
                if (_settings.ReminderSound && !_isDndActive) System.Media.SystemSounds.Asterisk.Play();
            }
        }
    }

    private void UpdateIdleContent()
    {
        string format = _settings.Clock12Hour ? (_settings.ClockSeconds ? "h:mm:ss tt" : "h:mm tt") : (_settings.ClockSeconds ? "HH:mm:ss" : "HH:mm");
        CompactClockText.Text = _settings.IdleCenter switch
        { "date" => DateTime.Now.ToString("dd MMM"), "both" => DateTime.Now.ToString(format + " · dd MMM"), _ => DateTime.Now.ToString(format) };
        if (_settings.IdleRight == "battery")
        {
            var battery = SystemOverview.Battery();
            CompactBatteryText.Text = battery.HasValue ? $"{battery.Value.Percent}%" : "AC";
        }
    }

    private void UpdateCompactContent()
    {
        bool showTrack = _isMediaPlaying && !_isGameModeActive && !_manualResize && !string.IsNullOrWhiteSpace(_currentMediaTitle);
        CompactTrackText.Text = showTrack ? _currentMediaTitle.Replace('\r', ' ').Replace('\n', ' ') : "";
        CompactTrackText.Visibility = showTrack ? Visibility.Visible : Visibility.Collapsed;
        string lyric = showTrack && _settings.Karaoke ? CurrentLyricLine() : "";
        CompactLyricsText.Text = lyric;
        CompactLyricsText.Visibility = lyric.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        CompactClockText.Visibility = showTrack ? Visibility.Collapsed : Visibility.Visible;
        CompactWeatherPanel.Visibility = !showTrack && _settings.IdleRight == "weather" ? Visibility.Visible : Visibility.Collapsed;
        CompactBatteryText.Visibility = !showTrack && _settings.IdleRight == "battery" ? Visibility.Visible : Visibility.Collapsed;
        CompactCharacterHost.Visibility = _settings.SelectedCharacter == "None" ? Visibility.Collapsed : Visibility.Visible;
        if (_currentState == IslandState.Compact && !_isGameModeActive)
        {
            _targetW = showTrack ? Math.Max(360, _settings.CompactWidth) : _settings.CompactWidth;
            _targetH = lyric.Length > 0 ? Math.Max(50, _settings.CompactHeight) : _settings.CompactHeight;
            _targetR = Math.Max(4, _targetH * .5 * _settings.CornerRounding);
            if (NeedsIslandFrames()) StartIslandFrames();
        }
    }

    private void UpdateCursorAppearance()
    {
        if (PresentationSource.FromVisual(IslandBorder) == null) return;
        var point = System.Windows.Forms.Cursor.Position;
        var center = IslandBorder.PointToScreen(new Point(IslandBorder.ActualWidth / 2, IslandBorder.ActualHeight / 2));
        double distance = Math.Sqrt(Math.Pow(point.X - center.X, 2) + Math.Pow(point.Y - center.Y, 2));
        double desired = _currentState == IslandState.Compact && !IslandBorder.IsMouseOver && distance > 180 ? Math.Clamp(_settings.IdleOpacity, .15, 1) : 1;
        double opacity = IslandBorder.Opacity + (desired - IslandBorder.Opacity) * .45;
        if (Math.Abs(desired - opacity) < .005) opacity = desired;
        if (IslandBorder.Opacity != opacity) IslandBorder.Opacity = opacity;
        if (_settings.FollowCursorMonitor && !_settings.PinPosition && _currentState == IslandState.Compact && !_isClickCandidate && !_bodyDragging && !_manualResize && !IslandBorder.IsMouseOver)
        {
            var screen = System.Windows.Forms.Screen.FromPoint(point);
            var current = System.Windows.Forms.Screen.FromHandle(new System.Windows.Interop.WindowInteropHelper(this).Handle);
            if (screen.DeviceName != current.DeviceName) MoveToMonitor(screen);
        }
    }

    private sealed record WindowSignal(IntPtr Handle, int Pid, string Process, string Title);
    private delegate bool EnumWindowsCallback(IntPtr handle, IntPtr data);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr data);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr handle);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr handle, System.Text.StringBuilder title, int count);
    private static IReadOnlyList<WindowSignal> ReadWindowSignals()
    {
        var items = new List<WindowSignal>();
        EnumWindows((handle, _) =>
        {
            if (!IsWindowVisible(handle)) return true;
            GetWindowThreadProcessId(handle, out uint pid);
            if (pid == Environment.ProcessId) return true;
            var text = new System.Text.StringBuilder(512); GetWindowText(handle, text, text.Capacity);
            if (text.Length == 0) return true;
            try { using var process = Process.GetProcessById((int)pid); items.Add(new(handle, (int)pid, process.ProcessName, text.ToString())); } catch { }
            return items.Count < 2048;
        }, IntPtr.Zero);
        return items;
    }
    private async Task ScanFeaturesAsync()
    {
        bool readGpu = DesktopSystemWidgetVisible || _isGameModeActive || SystemPageVisible;
        bool readBrightness = _settings.LevelHud && DateTime.UtcNow >= _nextBrightnessRead;
        bool readWindows = _settings.ExternalProgress || (_settings.TitleUnreadFallback && NotificationsEnabled);
        if (readBrightness) _nextBrightnessRead = DateTime.UtcNow.AddSeconds(6);
        if (!readGpu && !readBrightness && !readWindows && _jobs.Count == 0 && _unreadCounts.Count == 0) return;
        try
        {
            var snapshot = await Task.Run(() =>
            {
                int? brightness = readBrightness ? DesktopAudioService.Brightness() : null;
                int? gpu = readGpu ? DesktopAudioService.GpuUsage() : null;
                return (Windows: readWindows ? ReadWindowSignals() : Array.Empty<WindowSignal>(), Brightness: brightness, Gpu: gpu);
            });
            if (_windowClosed) return;
            if (_settings.LevelHud && _lastBrightness.HasValue && snapshot.Brightness is { } brightness && brightness != _lastBrightness)
                LevelNotice(L("Яркость") + $" · {brightness}%", brightness);
            if (readBrightness) _lastBrightness = snapshot.Brightness;
            if (readGpu) _lastGpuUsage = snapshot.Gpu ?? -1;
            UpdateWindowSignals(snapshot.Windows);
        }
        catch (Exception ex) { AppLog.Error("Background feature scan", ex); }
    }

    private void UpdateWindowSignals(IReadOnlyList<WindowSignal> windows)
    {
        bool useUnread = _settings.TitleUnreadFallback && NotificationsEnabled;
        bool canNotifyUnread = useUnread && (!_notifAccessGranted
            || (Registry.GetValue(@"HKEY_CURRENT_USER\Software\Policies\Microsoft\Windows\Explorer", "DisableNotificationCenter", 0) as int?) == 1
            || (Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\PushNotifications", "ToastEnabled", 1) as int?) == 0);
        foreach (var window in windows)
        {
            if (_settings.ExternalProgress && WindowSignals.Progress(window.Title) is { } percent)
            {
                if (_jobs.TryGetValue(window.Handle, out var old) && old.Percent < 100 && percent == 100)
                    FeatureNotice(window.Process + " · " + L("готово"), () => ActivateWindow(window.Handle));
                _jobs[window.Handle] = (window.Process, percent);
            }
            else _jobs.Remove(window.Handle);
            if (!useUnread) continue;
            bool messenger = window.Process.Contains("telegram", StringComparison.OrdinalIgnoreCase)
                || window.Process.Contains("exdesktop", StringComparison.OrdinalIgnoreCase)
                || window.Process.Contains("discord", StringComparison.OrdinalIgnoreCase);
            if (!messenger) continue;
            int unread = WindowSignals.UnreadCount(window.Title);
            if (canNotifyUnread && _unreadCounts.TryGetValue(window.Handle, out int oldUnread) && unread > oldUnread)
                FeatureNotice(window.Process + " · " + L("новое сообщение"), () => ActivateWindow(window.Handle));
            _unreadCounts[window.Handle] = unread;
        }
        if (!useUnread) _unreadCounts.Clear();
        foreach (var key in _jobs.Keys.Where(h => !windows.Any(w => w.Handle == h)).ToArray()) _jobs.Remove(key);
        foreach (var key in _unreadCounts.Keys.Where(h => !windows.Any(w => w.Handle == h)).ToArray()) _unreadCounts.Remove(key);
        var job = _settings.ExternalProgress ? _jobs.FirstOrDefault(p => p.Value.Percent < 100) : default;
        CompactProgressPanel.Visibility = job.Key != IntPtr.Zero ? Visibility.Visible : Visibility.Collapsed;
        if (job.Key != IntPtr.Zero)
        {
            CompactProgressLabel.Text = $"{job.Value.Name} · {job.Value.Percent}%";
            CompactProgressBar.Value = job.Value.Percent;
            UpdateProgressRing(job.Value.Percent);
            CompactProgressPanel.Tag = job.Key;
        }
    }
    private void SetExternalProgress(bool enabled)
    {
        _settings.ExternalProgress = enabled;
        TglExternalProgress.IsChecked = enabled;
        if (!enabled) { _jobs.Clear(); CompactProgressPanel.Visibility = Visibility.Collapsed; }
        _nextFeatureScan = DateTime.MinValue;
    }
    private void TglExternalProgress_Click(object sender, RoutedEventArgs e)
    { SetExternalProgress(TglExternalProgress.IsChecked == true); SaveSettings(); }
    private static void ActivateWindow(IntPtr handle) { if (IsIconic(handle)) ShowWindow(handle, 9); SetForegroundWindow(handle); }
    private void UpdateProgressRing(int percent)
    {
        double angle = Math.Clamp(percent, 0, 99.99) * Math.PI / 50;
        var figure = new PathFigure { StartPoint = new Point(12, 2) };
        figure.Segments.Add(new ArcSegment(new Point(12 + 10 * Math.Sin(angle), 12 - 10 * Math.Cos(angle)), new Size(10, 10), 0, percent > 50, SweepDirection.Clockwise, true));
        CompactProgressRing.Data = new PathGeometry([figure]);
    }
    private void Progress_Click(object sender, MouseButtonEventArgs e) { e.Handled = true; if (CompactProgressPanel.Tag is IntPtr handle) ActivateWindow(handle); }

    private void VolumeChanged(VolumeIslandService.Snapshot snapshot)
    {
        lock (_volumeGate)
        {
            _pendingVolume = snapshot;
            if (_volumeDispatchPending || _windowClosed) return;
            _volumeDispatchPending = true;
        }
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            VolumeIslandService.Snapshot latest;
            lock (_volumeGate) { latest = _pendingVolume; _volumeDispatchPending = false; }
            _widgetVolume = latest;
            if (WidgetIsVisible("volume")) RefreshDesktopWidgets();
            if (!_windowClosed && (_settings.VolumeIslandHud == true || _manualVolumeOpen))
                ShowVolumeSnapshot(latest);
        }));
    }

    private void SetVolumeIslandHud(bool enabled)
    {
        _settings.VolumeIslandHud = enabled;
        TglVolumeIsland.IsChecked = enabled;
        if (_volumeIsland != null) _volumeIsland.Enabled = enabled;
        if (!enabled && !_manualVolumeOpen) LevelHudBorder.Visibility = Visibility.Collapsed;
    }

    private void TglVolumeIsland_Click(object sender, RoutedEventArgs e)
    { SetVolumeIslandHud(TglVolumeIsland.IsChecked == true); SaveSettings(); }

    private void ShowVolumeSnapshot(VolumeIslandService.Snapshot snapshot)
    {
        _displayedVolume = snapshot;
        _updatingVolumeSlider = true;
        try { VolumeHudSlider.Value = Math.Clamp(snapshot.Volume * 100, 0, 100); }
        finally { _updatingVolumeSlider = false; }
        VolumeMuteButton.Content = snapshot.Muted ? "\uE74F" : "\uE767";
        VolumeMuteButton.ToolTip = L(snapshot.Muted ? "Включить звук" : "Выключить звук");
        LevelNotice(snapshot.Muted ? L("Звук выключен") : L("Громкость") + $" · {snapshot.Volume:P0}", snapshot.Volume * 100, true);
    }

    private void OpenVolume_Click(object sender, RoutedEventArgs e)
    {
        _manualVolumeOpen = true;
        if (_volumeIsland?.RequestSnapshot() != true)
        { _manualVolumeOpen = false; OpenFeaturePage("system"); }
    }

    private void VolumeHudSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingVolumeSlider || !IsLoaded) return;
        _featureNoticeUntil = DateTime.UtcNow.AddSeconds(3);
        _volumeIsland?.SetVolume(e.NewValue);
    }

    private void VolumeMute_Click(object sender, RoutedEventArgs e)
    { e.Handled = true; _volumeIsland?.ToggleMute(); }

    private void VolumeHud_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (VolumeHudSlider.Visibility != Visibility.Visible) return;
        e.Handled = true;
        VolumeHudSlider.Value = Math.Clamp(VolumeHudSlider.Value + e.Delta / 120d * 2, 0, 100);
    }

    private void LevelNotice(string text, double value, bool volume = false)
    {
        VolumeHudSlider.Visibility = VolumeMuteButton.Visibility = volume ? Visibility.Visible : Visibility.Collapsed;
        LevelHudBar.Visibility = volume ? Visibility.Collapsed : Visibility.Visible;
        LevelHudText.Text = L(text); LevelHudBar.Value = Math.Clamp(value, 0, 100);
        LevelHudBorder.Visibility = Visibility.Visible;
        _featureNoticeUntil = DateTime.UtcNow.AddSeconds(2);
    }
    private void FeatureNotice(string text, Action action)
    {
        if (_featureNotices.Count >= 20) _featureNotices.Dequeue();
        _featureNotices.Enqueue((L(text), action)); DrainFeatureNotices();
    }
    private void DrainFeatureNotices()
    {
        if (_featureNotices.Count == 0 || _visibleNotification != null || _isDndActive || _isGameModeActive || _toastHideTimer.IsEnabled) return;
        var item = _featureNotices.Dequeue(); ShowSideBubble(item.Text, ToastCategory.Info);
        _featureNoticeAction = item.Action; SideToastBorder.Cursor = Cursors.Hand;
    }
    private static void OpenWeb(Uri uri)
    { if (uri.Scheme is "https" or "http") Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); }

    private void StartManualResize(object? sender, RoutedEventArgs e)
    {
        _isClickCandidate = false;
        if (_isGameModeActive) { TglGameModeHud.IsChecked = false; ApplyGameModeState(false, false); }
        _manualResize = !_manualResize;
        MorphToState(IslandState.Compact);
        UpdateCompactContent();
        ResizeWidthThumb.Visibility = ResizeHeightThumb.Visibility = _manualResize ? Visibility.Visible : Visibility.Collapsed;
        ResizeHint.Visibility = _manualResize ? Visibility.Visible : Visibility.Collapsed;
    }
    private void ResizeWidth_DragDelta(object sender, DragDeltaEventArgs e)
    { _settings.CompactWidth = Math.Clamp(_settings.CompactWidth + e.HorizontalChange, 160, 650); ApplyFeatureAppearance(); }
    private void ResizeHeight_DragDelta(object sender, DragDeltaEventArgs e)
    { _settings.CompactHeight = Math.Clamp(_settings.CompactHeight + e.VerticalChange, 32, 100); ApplyFeatureAppearance(); }
    private void Resize_DragCompleted(object sender, DragCompletedEventArgs e) => SaveSettings();
}
