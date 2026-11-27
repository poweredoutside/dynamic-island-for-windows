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
        public bool? BrightnessIslandHud { get; set; }
        public bool FollowCursorMonitor { get; set; }
        public bool TitleUnreadFallback { get; set; } = true;
        public bool ExternalProgress { get; set; } = true;
        public bool Karaoke { get; set; }
        public bool OnlineLyrics { get; set; }
        public int LyricsLineCount { get; set; } = 1;
        public string GeminiModel { get; set; } = "gemini-3.8-live";
        public string GeminiVoice { get; set; } = GeminiVoiceService.DefaultVoice;
        public Guid? GeminiVoicePresetId { get; set; }
        public List<GeminiVoicePreset> GeminiVoicePresets { get; set; } = [];
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
        public bool GeminiContextAssistant { get; set; }
    }

    private readonly DispatcherTimer _featureTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private DispatcherTimer? _settingsSaveTimer;
    private readonly DispatcherTimer _hoverDelay = new();
    private GeminiVoiceService? _voice;
    private Task? _featureScan;
    private Task? _reminderSoundPlayback;
    private DateTime _nextFeatureScan, _nextCalendarRead;
    private VolumeIslandService? _volumeIsland;
    private BrightnessIslandService? _brightnessIsland;
    private Slider? _systemMaster;
    private readonly object _volumeGate = new();
    private VolumeIslandService.Snapshot _pendingVolume;
    private VolumeIslandService.Snapshot _displayedVolume;
    private bool _volumeDispatchPending;
    private bool _updatingVolumeSlider, _updatingWindowsVolume, _manualVolumeOpen;
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
        InitializeLiveSystemStatus();
        IslandBorder.LostMouseCapture += (_, _) => { _isClickCandidate = false; if (_bodyDragging) { _bodyDragging = false; RememberIslandPlacement(); SaveSettings(); } };
        _settings.VolumeIslandHud ??= _settings.LevelHud;
        _brightnessIsland = new BrightnessIslandService(Dispatcher, ShowBrightnessSnapshot);
        _brightnessIsland.SetEnabled(_settings.BrightnessIslandHud ?? _settings.LevelHud);
        _brightnessIsland.SetVolumeEnabled(_settings.VolumeIslandHud == true);
        _volumeIsland = new VolumeIslandService(_settings.VolumeIslandHud == true, _brightnessIsland.ObserveKey,
            recoverPreviousMutes: true);
        _volumeIsland.Changed += VolumeChanged;
        VolumeHudSlider.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler((_, _) => _volumeIsland?.RequestSnapshot()));
        UpdateVolumeObservation();
        _hoverDelay.Tick += (_, _) =>
        {
            _hoverDelay.Stop();
            if (IslandBorder.IsMouseOver && !_isClickCandidate && !_bodyDragging && _currentState == IslandState.Compact
                && !_manualResize && !_isGameModeActive)
                MorphToState(IslandState.Hover);
        };
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
            string updateNotice = ProductVersion == "1.1.30" ? RequiredSoundUpdate : "Что нового";
            FeatureNotice($"OC - Dynamic Island {ProductVersion} · {L(updateNotice)}", () => OpenSettingsFeaturePage("updates"));
            _settings.LastSeenVersion = ProductVersion; SaveSettings();
        }
    }

    private void LoadFeatureThemes()
    {
        _settings.CustomThemes = (_settings.CustomThemes ?? []).Where(ThemeCode.Valid).DistinctBy(t => t.Name).Take(32).ToList();
        _settings.ActionOrder ??= []; _settings.HiddenTabs ??= []; _settings.TabOrder ??= [];
        _settings.Timers ??= []; _settings.DeliveredReminders ??= [];
        _themes.RemoveAll(t => t.Key.StartsWith("User: ", StringComparison.Ordinal));
        foreach (var theme in _settings.CustomThemes) _themes.Add(new("User: " + theme.Name, theme.Island, theme.Scene, theme.Accent, theme.Light));
    }

    private void DisposeFeatures()
    {
        DisposeDesktopWidgets();
        DisposeLiveSystemStatus();
        _calendarCancellation.Cancel();
        DisposeLensGlass();
        _featureTimer.Stop(); _hoverDelay.Stop(); _watchdog?.Dispose();
        _settingsSaveTimer?.Stop();
        if (_volumeIsland != null) { _volumeIsland.Changed -= VolumeChanged; _volumeIsland.Dispose(); }
        _brightnessIsland?.Dispose();
        StopGameMetrics();
        _updateCancellation?.Cancel();
        CancelLyricsSearch();
        if (_voice != null) _ = _voice.DisposeAsync();
    }

    private void QueueSettingsSave()
    {
        if (_isLoadingSettings || _windowClosed) return;
        if (_settingsSaveTimer == null)
        {
            _settingsSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
            _settingsSaveTimer.Tick += (_, _) => SaveSettings();
        }
        _settingsSaveTimer.Stop();
        _settingsSaveTimer.Start();
    }

    private void ApplyFeatureAppearance()
    {
        _settings.CompactWidth = Math.Clamp(_settings.CompactWidth, 160, 650);
        _settings.CompactHeight = Math.Clamp(_settings.CompactHeight, 32, 100);
        _settings.CornerRounding = Math.Clamp(_settings.CornerRounding, 0, 1);
        UpdateDiscordCapsuleSize();
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
        RefreshAssistantContext();
        _brightnessIsland?.RefreshShellHook();
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
            _ = CheckForUpdatesAsync(automatic: true);
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
            PlayReminderSound();
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
                PlayReminderSound(meeting: true);
            }
        }
    }

    private void PlayReminderSound(bool meeting = false)
    {
        if (_windowClosed || !_settings.ReminderSound || _isDndActive || _reminderSoundPlayback is { IsCompleted: false }) return;
        _reminderSoundPlayback = ReminderAudio.PlayAsync(meeting);
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
        var lyric = showTrack && _settings.Karaoke ? CurrentLyricLines() : ("", "", "");
        ShowLyricLine(CompactLyricsText, lyric.Item1);
        ShowLyricLine(CompactNextLyric, lyric.Item2, animate: false);
        ShowLyricLine(CompactLastLyric, lyric.Item3, animate: false);
        CompactClockText.Visibility = showTrack ? Visibility.Collapsed : Visibility.Visible;
        CompactWeatherPanel.Visibility = !showTrack && _settings.IdleRight == "weather" ? Visibility.Visible : Visibility.Collapsed;
        CompactBatteryText.Visibility = !showTrack && _settings.IdleRight == "battery" ? Visibility.Visible : Visibility.Collapsed;
        CompactCharacterHost.Visibility = _settings.SelectedCharacter == "None" ? Visibility.Collapsed : Visibility.Visible;
        if (_currentState == IslandState.Compact && !_isGameModeActive)
        {
            _targetW = showTrack ? Math.Max(360, _settings.CompactWidth) : _settings.CompactWidth;
            int lyricHeight = lyric.Item1.Length == 0 ? 0 : 50 + (lyric.Item2.Length > 0 ? 15 : 0) + (lyric.Item3.Length > 0 ? 15 : 0);
            _targetH = Math.Max(lyricHeight, _settings.CompactHeight);
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
        var processes = new Dictionary<uint, string>();
        var title = new System.Text.StringBuilder(512);
        EnumWindows((handle, _) =>
        {
            if (!IsWindowVisible(handle)) return true;
            GetWindowThreadProcessId(handle, out uint pid);
            if (pid == Environment.ProcessId) return true;
            title.Clear();
            if (GetWindowText(handle, title, title.Capacity) == 0) return true;
            try
            {
                if (!processes.TryGetValue(pid, out string? name))
                {
                    using var process = Process.GetProcessById((int)pid);
                    processes[pid] = name = process.ProcessName;
                }
                items.Add(new(handle, (int)pid, name, title.ToString()));
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { }
            return items.Count < 2048;
        }, IntPtr.Zero);
        return items;
    }
    private async Task ScanFeaturesAsync()
    {
        bool readGpu = DesktopSystemWidgetVisible || _isGameModeActive || SystemPageVisible;
        bool readBrightness = (_settings.BrightnessIslandHud ?? _settings.LevelHud) && DateTime.UtcNow >= _nextBrightnessRead;
        bool readWindows = _settings.ExternalProgress || (_settings.TitleUnreadFallback && NotificationsEnabled);
        if (readBrightness) _nextBrightnessRead = DateTime.UtcNow.AddSeconds(6);
        if (!readGpu && !readBrightness && !readWindows && _jobs.Count == 0 && _unreadCounts.Count == 0) return;
        try
        {
            var snapshot = await Task.Run(() =>
            {
                long brightnessSampleAt = Environment.TickCount64;
                int? brightness = readBrightness ? DesktopAudioService.Brightness() : null;
                int? gpu = readGpu ? DesktopAudioService.GpuUsage() : null;
                return (Windows: readWindows ? ReadWindowSignals() : Array.Empty<WindowSignal>(), Brightness: brightness,
                    BrightnessSampleAt: brightnessSampleAt, Gpu: gpu);
            });
            if (_windowClosed) return;
            if (readBrightness)
            {
                _lastBrightness = snapshot.Brightness;
                if (snapshot.Brightness is { } brightness)
                    _brightnessIsland?.UpdateBrightness(brightness, snapshot.BrightnessSampleAt);
            }
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
            if (_windowClosed) return;
            if (_musicMaster?.IsVisible == true) UpdateWindowsVolumeSlider(_musicMaster, latest);
            if (_systemMaster?.IsVisible == true) UpdateWindowsVolumeSlider(_systemMaster, latest);
            if (_manualVolumeOpen || (_settings.VolumeIslandHud == true && !HasVisibleWindowsVolumeSlider))
                ShowVolumeSnapshot(latest);
            if (HasVisibleWindowsVolumeSlider || LevelHudBorder.IsVisible && VolumeHudSlider.IsVisible)
                _brightnessIsland?.VolumeHudShown();
            RefreshWidget("volume");
        }));
    }

    private bool HasVisibleWindowsVolumeSlider => _musicMaster?.IsVisible == true || _systemMaster?.IsVisible == true;

    private void ConnectWindowsVolumeSlider(Slider slider)
    {
        slider.IsEnabled = false;
        slider.IsVisibleChanged += (_, _) => UpdateVolumeObservation();
        slider.ValueChanged += (_, e) =>
        {
            if (!_updatingWindowsVolume && slider.IsVisible && slider.IsEnabled) _volumeIsland?.SetVolume(e.NewValue);
        };
        slider.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler((_, _) => _volumeIsland?.RequestSnapshot()));
        UpdateVolumeObservation();
    }

    private void UpdateVolumeObservation()
    {
        if (_windowClosed || _volumeIsland is not { } audio) return;
        audio.ObserveVolume = WidgetIsVisible("volume") || HasVisibleWindowsVolumeSlider;
        if (audio.ObserveVolume) audio.RequestSnapshot();
    }

    private void UpdateWindowsVolumeSlider(Slider slider, VolumeIslandService.Snapshot snapshot)
    {
        slider.IsEnabled = true;
        if (slider.IsMouseCaptureWithin) return;
        _updatingWindowsVolume = true;
        try { slider.Value = Math.Round(snapshot.Volume * 100); }
        finally { _updatingWindowsVolume = false; }
        slider.ToolTip = $"{L("Громкость Windows")} · {snapshot.Volume:P0}";
    }

    private void SetVolumeIslandHud(bool enabled)
    {
        _settings.VolumeIslandHud = enabled;
        TglVolumeIsland.IsChecked = enabled;
        if (_volumeIsland != null) _volumeIsland.Enabled = enabled;
        _brightnessIsland?.SetVolumeEnabled(enabled);
        if (!enabled && !_manualVolumeOpen && VolumeHudSlider.Visibility == Visibility.Visible)
            LevelHudBorder.Visibility = Visibility.Collapsed;
    }

    private void TglVolumeIsland_Click(object sender, RoutedEventArgs e)
    { SetVolumeIslandHud(TglVolumeIsland.IsChecked == true); SaveSettings(); }

    private void SetBrightnessIslandHud(bool enabled)
    {
        _settings.BrightnessIslandHud = enabled;
        TglBrightnessIsland.IsChecked = enabled;
        _brightnessIsland?.SetEnabled(enabled);
        _nextBrightnessRead = DateTime.MinValue;
        if (!enabled && LevelHudBar.Visibility == Visibility.Visible)
            LevelHudBorder.Visibility = Visibility.Collapsed;
    }

    private void TglBrightnessIsland_Click(object sender, RoutedEventArgs e)
    { SetBrightnessIslandHud(TglBrightnessIsland.IsChecked == true); SaveSettings(); }

    private bool ShowBrightnessSnapshot(int brightness)
    {
        _lastBrightness = brightness;
        if (_windowClosed || !IsVisible || WindowState == WindowState.Minimized ||
            IslandBorder.Visibility != Visibility.Visible || VolumeHudSlider.IsMouseCaptureWithin) return false;
        LevelNotice(L("Яркость") + $" · {brightness}%", brightness);
        return LevelHudBorder.IsVisible;
    }

    private void ShowVolumeSnapshot(VolumeIslandService.Snapshot snapshot)
    {
        _displayedVolume = snapshot;
        _updatingVolumeSlider = true;
        try { if (!VolumeHudSlider.IsMouseCaptureWithin) VolumeHudSlider.Value = Math.Clamp(snapshot.Volume * 100, 0, 100); }
        finally { _updatingVolumeSlider = false; }
        VolumeMuteButton.Content = snapshot.Muted ? "\uE74F" : "\uE767";
        VolumeMuteButton.ToolTip = L(snapshot.Muted ? "Включить звук" : "Выключить звук");
        LevelNotice(snapshot.Muted ? L("Звук выключен") : L("Громкость") + $" · {snapshot.Volume:P0}", snapshot.Volume * 100, true);
    }

    private void OpenVolume_Click(object sender, RoutedEventArgs e)
    {
        _manualVolumeOpen = true;
        _featureNoticeUntil = DateTime.UtcNow.AddSeconds(3);
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
