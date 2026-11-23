using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;
using DynamicIslandWin.Services;
using Windows.Media.Control;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using Forms = System.Windows.Forms;
using Path = System.Windows.Shapes.Path;
using Point = System.Windows.Point;
using Rectangle = System.Windows.Shapes.Rectangle;
using Control = System.Windows.Controls.Control;

namespace DynamicIslandWin;

public partial class MainWindow : Window
{
    private const double GameHudWidth = 480;
    private const double GameHudHeight = 64;
    private const double GameHudRadius = 24;
    private enum IslandState { Compact, Hover, Expanded, Weather, Clipboard, Settings, Section }
    private IslandState _currentState = IslandState.Compact;

    private enum ToastCategory { CapsLock, NumLock, Layout, Clipboard, DoNotDisturb, Messenger, AppNotification, Info }

    private bool _isLoadingSettings = true;
    private Point _dragStartPoint;
    private bool _isClickCandidate;

    // 120 FPS пружинный движок + счётчик FPS
    private double _curW = 210, _targetW = 210, _velW;
    private double _curH = 38,  _targetH = 38,  _velH;
    private double _curR = 19,  _targetR = 19,  _velR;
    private readonly Stopwatch _frameClock = Stopwatch.StartNew();
    private double _lastFrameTime;

    private readonly DispatcherTimer _telemetryTimer = new();
    private readonly DispatcherTimer _weatherRefreshTimer = new();
    private readonly DispatcherTimer _keyboardPollTimer = new();
    private readonly DispatcherTimer _toastHideTimer = new();
    private readonly DispatcherTimer _mouseLeaveTimer = new();
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(8) };

    private GlobalSystemMediaTransportControlsSessionManager? _mediaManager;
    private bool _mediaManagerInitializing;
    private long _nextMediaManagerAttempt;
    private bool _isMediaPlaying;
    private string _currentMediaTitle = "";


    // Состояние игрового режима и звонка Discord
    private bool _isGameModeActive;
    private bool _pauseAutoGameHud;
    private WindowsDndService.State? _dndStateBeforeGameMode;
    private bool _gameModeOwnsDnd;
    private readonly DiscordCallMonitor _discordMonitor = new(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));
    private DiscordCallMonitor.Call? _discordCall;
    private long _nextDiscordPoll;
    private bool _discordProbeBusy;

    // Перехват уведомлений мессенджеров и всех приложений
    private UserNotificationListener? _notifListener;
    private bool _notifAccessGranted;
    private readonly NotificationPolicy _notificationPolicy = new();
    private IslandNotification? _pendingNotification;
    private IslandNotification? _visibleNotification;
    private long _notificationVisualGeneration;
    private bool _notificationBusy;
    private bool _notificationInitializing;
    private bool _notificationBaseline;
    private bool _notificationEventSubscribed;
    private bool _notificationReadAgain;
    private long _notificationEventVersion;
    private NotificationBannerService? _notificationBanners;
    private bool _bannerWatchdogStarted;
    private DateTime _nextBannerSync;
    private bool _windowClosed;
    private DateTime _nextNotificationPoll;
    private DateTime _nextNotificationHeartbeat;
    private readonly WindowsDndService _windowsDnd = new();
    private bool _dndStateKnown;
    private bool _dndChanging;

    // Состояние погоды (по умолчанию город не задан)
    private bool _hasWeatherData;
    private string _resolvedCityName = "";
    private double _lastTempC, _lastFeelsLikeC, _lastTempMaxC, _lastTempMinC;
    private int _lastHumidity, _lastWindDeg, _lastPrecipProb, _lastWeatherCode;
    private double _lastWindSpeedMs, _lastPressureHpa = 1013.0, _lastPrecipitationMm, _lastUvIndex;
    private string _lastSunrise = "--:--", _lastSunset = "--:--", _lastWeatherUpdatedClock = "--:--";
    private readonly string[] _hourlyLabels = ["--", "--", "--", "--"];
    private readonly double[] _hourlyTempsC = [0, 0, 0, 0];

    // По умолчанию «Не беспокоить» выключен (false)
    private bool _isDndActive = false;
    private bool _lastCapsLock;
    private bool _lastNumLock;
    private IntPtr _lastKeyboardLayout;
    private string _lastClipboardHash = string.Empty;

    private bool _isRecordingHotkey;
    private ScreenRecordingService? _screenRecording;
    private uint _hotkeyModifiers = MOD_CONTROL | MOD_ALT;
    private uint _hotkeyVk = 0x4F;

    private Forms.NotifyIcon? _trayIcon;
    private ulong _prevIdleTime, _prevKernelTime, _prevUserTime;
    private int _lastCpuUsage = -1;
    private int _lastGpuUsage = -1;

    private AppSettings _settings = new();
    private readonly string _settingsPath = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "DynamicIslandWin", "settings.json");

    public record ClipboardHistoryItem(string Text, DateTime CopiedAt, string? ImageFile = null);
    private List<ClipboardHistoryItem> _clipboardHistory = new();
    private readonly string _clipboardHistoryPath = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "DynamicIslandWin", "clipboard_history.json");

    public record ThemePreset(string Key, string C1, string C2, string DotHex, bool IsLight = false);

    private readonly List<ThemePreset> _themes =
    [
        new("Midnight", "#0B0A12", "#141222", "#34F5C5"),
        new("Graphite", "#1D202A", "#282C3A", "#60A5FA"),
        new("Ocean",    "#0B3866", "#125296", "#38BDF8"),
        new("Forest",   "#0F3830", "#164E43", "#34D399"),
        new("Sunset",   "#3D161E", "#591E2B", "#FBBF24"),
        new("Rose",     "#3B1231", "#571B48", "#F472B6"),
        new("Lavender", "#1E1738", "#2E2356", "#A78BFA"),
        new("Neon",     "#090D16", "#111A2E", "#22D3EE"),
        new("Snow",     "#F8FAFC", "#E2E8F0", "#0EA5E9", true),
        new("Sand",     "#FEF3C7", "#FDE68A", "#F59E0B", true),
        new("Mint",     "#D1FAE5", "#A7F3D0", "#10B981", true),
        new("Sky",      "#E0F2FE", "#BAE6FD", "#3B82F6", true)
    ];

    private readonly string[] _characterKeys =
    [
        "Gemini", "ChatGPT", "Google", "OpenAI", "Codex", "Antigravity",
        "Claude", "Claude Code", "Eyes", "Cat", "Ghost", "Robot",
        "Fish", "Planet", "Slime", "Pulse", "Bunny", "Owl",
        "Dino", "Star", "Cloud", "None", "Puppy", "Fox", "Panda", "Bear", "Frog", "Penguin", "Chick", "Piggy", "Koala", "Mouse", "Bee", "Octopus", "Mushroom", "Cactus", "Alien", "Bat", "Moon", "Sun", "Flame", "Droplet", "Heart", "Pumpkin"
    ];

    private readonly string[] _availableLanguages = [SystemLanguage, "English", "Русский", "Українська", "Deutsch", "Español"];

    public partial class AppSettings
    {
        public string Language { get; set; } = SystemLanguage;
        public string SelectedTheme { get; set; } = "Midnight";
        public string SelectedCharacter { get; set; } = "Eyes";
        public string CustomPhotoPath { get; set; } = "";
        public string WeatherCity { get; set; } = "";
        public bool UseFahrenheit { get; set; } = false;
        public bool LiquidGlass { get; set; } = true;
        public bool Snowfall { get; set; }
        public double IslandScalePercent { get; set; } = 100;
        public double? WindowLeft { get; set; } = null;
        public string? IslandMonitor { get; set; }
        public double IslandOffset { get; set; }
        public bool DockIslandAtTop { get; set; }

        public bool ExpandOnHover { get; set; } = true;
        public bool CloseOnMouseLeave { get; set; } = true;
        public bool RememberDashboardTab { get; set; }
        public string LastDashboardTab { get; set; } = "home";
        public bool MusicEqualizerEnabled { get; set; }
        public int[] MusicEqualizerGains { get; set; } = [];
        public bool PinPosition { get; set; } = false;
        public bool ShowCapsLock { get; set; } = true;
        public bool ShowNumLock { get; set; } = true;
        public bool ShowLayout { get; set; } = true;
        public bool DiscordCallRightIsland { get; set; } = true;
        public bool GameModeHud { get; set; } = false;
        public bool MessengerNotifications { get; set; } = true;
        public bool AllAppNotifications { get; set; } = false;
        public int NotificationDurationSeconds { get; set; } = 5;
        public bool ShowDoNotDisturb { get; set; } = true;
        public bool ClipboardWatch { get; set; } = true;
        public bool ExcludeFromCapture { get; set; } = true;
        public bool HideInFullscreen { get; set; } = false;
        public bool RunOnStartup { get; set; } = false;

        public bool ShowHomeQuickActions { get; set; } = true;
        public bool ShowHomeMusic { get; set; } = true;
        public bool ShowHomeWeather { get; set; } = true;
        public bool ShowHomeClipboard { get; set; } = true;
        public bool ActRegion { get; set; } = true;
        public bool ActFull { get; set; } = true;
        public bool ActTranslate { get; set; } = true;
        public bool ActRecord { get; set; } = true;
        public bool ActCommand { get; set; } = true;
        public bool ActDnd { get; set; } = true;

        public uint HotkeyModifiers { get; set; } = MOD_CONTROL | MOD_ALT;
        public uint HotkeyVk { get; set; } = 0x4F;
        public string HotkeyLabel { get; set; } = "Ctrl + Alt + O";
        public string CustomCommand { get; set; } = "wt.exe";
        public string TranslateTargetLang { get; set; } = "ru";
        public string AiEndpointCommand { get; set; } = "gemini";
    }

    #region Win32 & PDH API

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const uint WDA_NONE = 0x00000000;
    private const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;
    private const int WM_HOTKEY = 0x0312;
    private const int WM_CLIPBOARDUPDATE = 0x031D;

    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint MOD_WIN = 0x0008;

    private const int HOTKEY_CUSTOM_TOGGLE = 9001;
    private const int HOTKEY_SNIP_S = 9003;
    private const int HOTKEY_CMD_SPACE = 9004;
    private const int HOTKEY_RECORD_R = 9006;

    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);
    [DllImport("user32.dll")] private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool AddClipboardFormatListener(IntPtr hwnd);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    [DllImport("user32.dll")] private static extern IntPtr GetKeyboardLayout(uint idThread);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr FindWindowEx(IntPtr parentHandle, IntPtr childAfter, string? className, string? windowTitle);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetSystemTimes(out FILETIME lpIdleTime, out FILETIME lpKernelTime, out FILETIME lpUserTime);

    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct FILETIME
    {
        public uint dwLowDateTime;
        public uint dwHighDateTime;
        public ulong ToUInt64() => ((ulong)dwHighDateTime << 32) | dwLowDateTime;
    }

    #endregion

    public MainWindow()
    {
        InitializeComponent();
        CaptureAdditionalLabels(this);
        CaptureAdditionalLabels(IslandBorder.ContextMenu);
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_TOOLWINDOW);

        HwndSource source = HwndSource.FromHwnd(hwnd);
        source.AddHook(HwndHook);

        AddClipboardFormatListener(hwnd);

        LoadSettings();
        PollWindowsDndState(notifyIfChanged: false);
        LoadClipboardHistory();
        InitializeTrayIcon();
        ApplyCurrentSettingsToUi();
        RestoreIslandPlacement();

        _isLoadingSettings = false;

        if (HasPackageIdentity())
        {
            try
            {
                var startup = await Windows.ApplicationModel.StartupTask.GetAsync("DynamicIslandStartup");
                if (_settings.RunOnStartup && startup.State == Windows.ApplicationModel.StartupTaskState.Disabled)
                    await startup.RequestEnableAsync();
                TglRunOnStartup.IsChecked = startup.State is Windows.ApplicationModel.StartupTaskState.Enabled or Windows.ApplicationModel.StartupTaskState.EnabledByPolicy;
            }
            catch (Exception ex) { AppLog.Error("Read startup setting", ex); }
        }

        RegisterCustomToggleHotkey(hwnd);
        RegisterHotKey(hwnd, HOTKEY_SNIP_S, MOD_CONTROL | MOD_ALT, 0x53);
        RegisterHotKey(hwnd, HOTKEY_RECORD_R, MOD_CONTROL | MOD_ALT, 0x52);
        RegisterHotKey(hwnd, HOTKEY_CMD_SPACE, MOD_CONTROL | MOD_ALT, 0x20);

        StartIslandFrames();

        _lastCapsLock = Console.CapsLock;
        _lastNumLock  = Console.NumberLock;
        _lastKeyboardLayout = GetCurrentKeyboardLayout();

        PollWindowsDndState(notifyIfChanged: false);

        _telemetryTimer.Interval = TimeSpan.FromSeconds(1);
        _telemetryTimer.Tick += async (_, _) => await UpdateTelemetryAsync();
        _telemetryTimer.Start();

        _weatherRefreshTimer.Interval = TimeSpan.FromMinutes(10);
        _weatherRefreshTimer.Tick += async (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(_settings.WeatherCity))
                await FetchWeatherForCityAsync(_settings.WeatherCity, false);
        };
        _weatherRefreshTimer.Start();

        _keyboardPollTimer.Interval = TimeSpan.FromMilliseconds(160);
        _keyboardPollTimer.Tick += (_, _) => PollKeyboardFullscreenAndNotifications();
        _keyboardPollTimer.Start();

        _toastHideTimer.Interval = TimeSpan.FromSeconds(NotificationPolicy.ClampDuration(_settings.NotificationDurationSeconds));
        _toastHideTimer.Tick += (_, _) =>
        {
            _toastHideTimer.Stop();
            if (ShowNextNotification()) return;
            ClearNotificationInteraction();
            var dur = TimeSpan.FromMilliseconds(180);
            SideToastBorder.BeginAnimation(OpacityProperty, new DoubleAnimation(0, dur));
            SideToastSlide.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-12, dur));
        };

        _mouseLeaveTimer.Interval = TimeSpan.FromMilliseconds(220);
        _mouseLeaveTimer.Tick += (_, _) =>
        {
            _mouseLeaveTimer.Stop();
            if (IslandBorder.IsMouseOver || _isClickCandidate || _bodyDragging || Mouse.Captured != null) return;

            if (_currentState == IslandState.Hover)
                MorphToState(IslandState.Compact);
            else if (_currentState is IslandState.Settings or IslandState.Expanded or IslandState.Weather or IslandState.Clipboard)
            {
                if (TglCloseOnLeave.IsChecked ?? true)
                    MorphToState(IslandState.Compact);
            }
        };

        SwitchSettingsPage(PageBehavior, NavBehaviorBtn);
        InitializeFeatures();
        // One slow integration must not postpone notification/keyboard timers.
        _ = InitializeMediaManagerAsync();
        _ = InitializeNotificationListenerAsync();
        await UpdateTelemetryAsync();

        if (!string.IsNullOrWhiteSpace(_settings.WeatherCity))
            await FetchWeatherForCityAsync(_settings.WeatherCity, false);
        else
            RenderWeatherUi();
    }

    private void Window_Closing(object? sender, CancelEventArgs e) => SaveSettings();

    private async Task InitializeMediaManagerAsync()
    {
        if (_windowClosed || _mediaManager != null || _mediaManagerInitializing
            || Environment.TickCount64 < _nextMediaManagerAttempt) return;
        _mediaManagerInitializing = true;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync().AsTask(timeout.Token);
            if (_windowClosed) return;
            _mediaManager = manager;
            manager.SessionsChanged += MediaSessionsChanged;
            manager.CurrentSessionChanged += MediaSessionsChanged;
            await RefreshMediaSessionStateAsync();
        }
        catch (Exception ex)
        {
            DetachMediaManager();
            _nextMediaManagerAttempt = Environment.TickCount64 + 5000;
            AppLog.Error("Initialize media manager", ex);
        }
        finally { _mediaManagerInitializing = false; }
    }

    private void DetachMediaManager()
    {
        if (_mediaManager is not { } media) return;
        _mediaManager = null;
        ObserveMediaSession(null);
        _selectedMedia = null;
        try
        {
            media.SessionsChanged -= MediaSessionsChanged;
            media.CurrentSessionChanged -= MediaSessionsChanged;
        }
        catch (COMException ex) { AppLog.Error("Unsubscribe media manager", ex); }
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        _windowClosed = true;
        DetachMediaManager();
        DisposeFeatures();
        _screenRecording?.Dispose();
        _keyboardPollTimer.Stop();
        _telemetryTimer.Stop();
        _weatherRefreshTimer.Stop();
        _toastHideTimer.Stop();
        _mouseLeaveTimer.Stop();
        DisposeNotificationBanners();
        if (_notifListener != null && _notificationEventSubscribed)
            _notifListener.NotificationChanged -= NotificationListener_Changed;
        RestoreTimedDnd();
        RestoreDndAfterGameMode();
        _windowsDnd.Dispose();
        StopIslandFrames();
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        RemoveClipboardFormatListener(hwnd);
        UnregisterHotKey(hwnd, HOTKEY_CUSTOM_TOGGLE);
        UnregisterHotKey(hwnd, HOTKEY_SNIP_S);
        UnregisterHotKey(hwnd, HOTKEY_RECORD_R);
        UnregisterHotKey(hwnd, HOTKEY_CMD_SPACE);
        _trayIcon?.Dispose();
    }

    #region 1. Звонок Discord справа + 2. Игровой режим (FPS, CPU, °C, GPU + Не беспокоить)

    private (string Value, bool Desktop) GameFpsReading() => _gameFps?.Fps is { } rate
        ? ($"{rate:0}", false) : _desktopFps.Rate is { } desktop ? ($"{desktop:0}", true) : ("—", false);

    private string GameFpsStatus()
    {
        string status = L(_gameFps?.Status ?? "Игровой режим включает HUD, FPS и температуру CPU. Windows может запросить права администратора.");
        return _isGameModeActive && GameFpsReading().Desktop
            ? L("FPS DWM — кадры рабочего стола, не FPS игры.") + "\n" + status : status;
    }

    private void UpdateGameHudValues()
    {
        int temperature = _gameFps?.CpuTemperatureC ?? -1;
        var (fps, desktop) = GameFpsReading();
        GameHudFpsText.Text     = fps;
        GameHudFpsLabel.Text    = desktop ? "FPS · DWM" : "FPS";
        GameHudFpsCard.ToolTip  = GameFpsStatus();
        GameHudCpuText.Text     = UsageDisplay(_lastCpuUsage);
        GameHudCpuTempText.Text = temperature >= 0 ? $"{temperature}°C" : "—";
        GameHudCpuTempText.ToolTip = temperature >= 0 ? null : L(GameTemperatureStatus());
        GameHudGpuText.Text     = UsageDisplay(_lastGpuUsage);

        Brush tempBrush;
        if (temperature >= 80)
            tempBrush = (Brush)FindResource("ThemeDangerBrush");
        else if (temperature >= 65)
            tempBrush = (Brush)FindResource("ThemeWarningBrush");
        else
            tempBrush = (Brush)FindResource("ThemeTextBrush");

        GameHudCpuTempText.Foreground = tempBrush;
        if (GameHudTempIcon != null)
            GameHudTempIcon.Fill = tempBrush;
        UpdateTelemetrySummaries();
    }

    private void UpdateFpsTarget()
    {
        if (_gameFps == null) return;
        GetWindowThreadProcessId(GetForegroundWindow(), out uint pid);
        if (pid == Environment.ProcessId) return;
        int target = (int)pid;
        if (_gameFps.TargetPid != target) _gameFps.Fps = null;
        _gameFps.TargetPid = target;
    }

    private void UpdateTelemetrySummaries()
    {
        var (rate, desktop) = GameFpsReading();
        string fps = desktop ? $"{rate} FPS DWM" : rate == "—" ? "FPS —" : $"{rate} FPS";
        var visibility = _isGameModeActive ? Visibility.Visible : Visibility.Collapsed;
        string? tooltip = _isGameModeActive ? GameFpsStatus() : null;
        UpdateIndicator(DashboardFpsText);
        UpdateIndicator(SettingsFpsText);
        string fpsPrefix = _isGameModeActive ? fps : "";
        string temperature = _gameFps?.CpuTemperatureC is >= 0 ? $" ({_gameFps.CpuTemperatureC}°C)" : "";
        HoverSysText.Text = _isGameModeActive ? fpsPrefix + temperature : "";

        void UpdateIndicator(TextBlock indicator)
        {
            indicator.Visibility = visibility;
            indicator.Text = _isGameModeActive ? fps : "";
            indicator.ToolTip = tooltip;
        }
    }

    private void StartGameMetrics()
    {
        if (_gameFps?.IsRunning == true) return;
        _gameFps?.Dispose();
        _temperatureDriverInstalled = CpuTemperatureService.IsDriverInstalled;
        _gameFps = new FpsService();
        UpdateFpsTarget();
        _ = _gameFps.StartAsync();
    }

    private void StopGameMetrics()
    {
        _gameFps?.Dispose();
        _gameFps = null;
        UpdateTelemetrySummaries();
    }

    private void ApplyGameModeState(bool active, bool collapseToHud)
    {
        if (_isGameModeActive == active)
        {
            if (_isGameModeActive)
            {
                UpdateGameHudValues();
                if (_currentState == IslandState.Compact)
                    _targetW = GameHudWidth;
            }
            return;
        }

        _isGameModeActive = active;
        ConfigureLensGlass();

        if (_isGameModeActive)
        {
            _desktopFps.Poll();
            if (!_isLoadingSettings && _gameFps == null) StartGameMetrics();
            _pendingNotification = null;
            HideNotificationBubble();
            if (_windowsDnd.TryRead(out var previousDnd))
            {
                _dndStateBeforeGameMode = previousDnd;
                _gameModeOwnsDnd = !previousDnd.IsActive;
                if (_gameModeOwnsDnd) SetDoNotDisturbState(true, notifyUser: false, fromGameMode: true);
            }

            CompactStandardPanel.Visibility = Visibility.Collapsed;
            CompactGameHudPanel.Visibility  = Visibility.Visible;
            UpdateCompactContent();
            UpdateGameHudValues();

            if (collapseToHud || _currentState == IslandState.Hover)
            {
                _currentState = IslandState.Hover; // Сбрасываем, чтобы MorphToState гарантированно выполнил переход
                MorphToState(IslandState.Compact);
            }
            else if (_currentState == IslandState.Compact)
            {
                _targetW = GameHudWidth;
                _targetH = GameHudHeight;
                _targetR = GameHudRadius;
            }
        }
        else
        {
            _desktopFps.Reset();
            UpdateTelemetrySummaries();
            RestoreDndAfterGameMode();

            CompactGameHudPanel.Visibility  = Visibility.Collapsed;
            CompactStandardPanel.Visibility = Visibility.Visible;

            UpdateCompactContent();
        }

        ApplyScaleForCurrentState();
    }

    private void UpdateDiscordCallState()
    {
        if (_discordCall is not { } call || TglDiscordCallRight.IsChecked == false
            || IslandBorder.Visibility != Visibility.Visible || _currentState is IslandState.Settings or IslandState.Section)
        {
            DiscordCallBorder.Visibility = Visibility.Collapsed;
            if (_discordOutputMenu is { IsOpen: true } menu) menu.IsOpen = false;
            return;
        }

        long seconds = Math.Max(0, (long)(DateTimeOffset.UtcNow - call.Started).TotalSeconds);
        DiscordCallTimerText.Text = $"{seconds / 60}:{seconds % 60:D2}";
        DiscordCallBorder.Visibility = Visibility.Visible;
    }

    private async Task RefreshDiscordCallAsync()
    {
        if (_discordProbeBusy || Environment.TickCount64 < _nextDiscordPoll
            || (TglDiscordCallRight.IsChecked == false && !SystemPageVisible)) return;
        _discordProbeBusy = true;
        _nextDiscordPoll = Environment.TickCount64 + 2000;
        try
        {
            var call = await Task.Run(_discordMonitor.Read);
            if (!_windowClosed)
            {
                if (_discordCall != call) _discordMicrophoneMuted = null;
                _discordCall = call;
                UpdateDiscordCallState();
                await RefreshDiscordMicrophoneAsync(call);
            }
        }
        catch (System.ComponentModel.Win32Exception ex) { AppLog.Error("Read Discord call state", ex); }
        finally { _discordProbeBusy = false; }
    }

    private bool IsForegroundGameOrFullscreen()
    {
        IntPtr fg = GetForegroundWindow();
        if (fg == IntPtr.Zero || fg == new WindowInteropHelper(this).Handle) return false;
        if (!GetWindowRect(fg, out RECT r)) return false;

        var screen = Forms.Screen.FromHandle(fg).Bounds;
        bool coversScreen = r.Left <= screen.Left + 2 && r.Top <= screen.Top + 2 && r.Right >= screen.Right - 2 && r.Bottom >= screen.Bottom - 2;
        if (!coversScreen) return false;

        try
        {
            GetWindowThreadProcessId(fg, out uint pid);
            using var proc = Process.GetProcessById((int)pid);
            string name = proc.ProcessName.ToLowerInvariant();
            return name is not ("explorer" or "shellexperiencehost" or "searchhost" or "applicationframehost");
        }
        catch { return false; }
    }

    private void DiscordCallTimer_Click(object sender, RoutedEventArgs e)
    {
        if (_discordCall is not { } call) return;
        e.Handled = true;
        IntPtr handle = IntPtr.Zero;
        foreach (var process in Process.GetProcessesByName(call.ProcessName))
        {
            using (process)
            {
                try { if (handle == IntPtr.Zero) handle = process.MainWindowHandle; }
                catch (InvalidOperationException) { }
                catch (System.ComponentModel.Win32Exception) { }
            }
        }
        if (handle != IntPtr.Zero) ActivateWindow(handle);
        else VoiceLauncher.Open("discord");
    }

    private void TglDiscordCallRight_Click(object sender, RoutedEventArgs e)
    {
        _nextDiscordPoll = 0;
        _ = RefreshDiscordCallAsync();
        UpdateDiscordCallState();
        SaveSettings();
    }

    private void TglGameModeHud_Click(object sender, RoutedEventArgs e)
    {
        SetGameModeEnabled(TglGameModeHud.IsChecked == true);
    }

    private void SetGameModeEnabled(bool enabled)
    {
        TglGameModeHud.IsChecked = enabled;
        _pauseAutoGameHud = !enabled && _settings.AutoGameHud;
        if (!enabled) StopGameMetrics();
        else if (!_isLoadingSettings) StartGameMetrics();
        ApplyGameModeState(enabled, collapseToHud: enabled);
        RefreshGameMetricsControls();
        SaveSettings();
    }

    private void CtxToggleGameMode_Click(object sender, RoutedEventArgs e) => SetGameModeEnabled(!_isGameModeActive);

    private void CtxOpenGameCenter_Click(object sender, RoutedEventArgs e) => OpenFeaturePage("system");

    private void IslandContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        CtxGameModeItem.IsChecked = _isGameModeActive;
        RefreshIslandPlacementControls();
    }

    #endregion

    #region 3 & 4. Перехват уведомлений Мессенджеров и Всех Приложений

    #endregion

    #region Боковая Капсула Уведомлений

    private void ShowSideBubble(string message, ToastCategory category, string shortBadge = "", bool bypassDnd = false)
    {
        if ((_isGameModeActive || _isDndActive) && !bypassDnd)
            return;
        if (_visibleNotification != null && !bypassDnd && category is not (ToastCategory.Messenger or ToastCategory.AppNotification))
            return;

        ClearNotificationInteraction();
        SideToastText.Text = message;
        SideToastBadgeShortText.Visibility = Visibility.Collapsed;
        SideToastVectorIcon.Visibility = Visibility.Visible;

        switch (category)
        {
            case ToastCategory.CapsLock:
                SideToastIconCircle.Background = new SolidColorBrush(Color.FromRgb(0xFF, 0x9F, 0x1C));
                SideToastBadgeShortText.Text = "•••";
                SideToastBadgeShortText.Visibility = Visibility.Visible;
                SideToastVectorIcon.Visibility = Visibility.Collapsed;
                break;

            case ToastCategory.NumLock:
                SideToastIconCircle.Background = new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6));
                SideToastBadgeShortText.Text = "123";
                SideToastBadgeShortText.Visibility = Visibility.Visible;
                SideToastVectorIcon.Visibility = Visibility.Collapsed;
                break;

            case ToastCategory.Layout:
                SideToastIconCircle.Background = new SolidColorBrush(Color.FromRgb(0x8B, 0x5C, 0xF6));
                SideToastBadgeShortText.Text = string.IsNullOrEmpty(shortBadge) ? "🌐" : shortBadge;
                SideToastBadgeShortText.Visibility = Visibility.Visible;
                SideToastVectorIcon.Visibility = Visibility.Collapsed;
                break;

            case ToastCategory.Messenger:
            case ToastCategory.AppNotification:
                SideToastIconCircle.Background = new SolidColorBrush(Color.FromRgb(0x63, 0x66, 0xF1));
                SideToastBadgeShortText.Text = string.IsNullOrEmpty(shortBadge) ? "🔔" : shortBadge;
                SideToastBadgeShortText.Visibility = Visibility.Visible;
                SideToastVectorIcon.Visibility = Visibility.Collapsed;
                break;

            case ToastCategory.Clipboard:
                SideToastIconCircle.Background = new SolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81));
                SideToastVectorIcon.Data = Geometry.Parse("M19,3H14.82C14.4,1.84 13.3,1 12,1C10.7,1 9.6,1.84 9.18,3H5A2,2 0 0,0 3,5V19A2,2 0 0,0 5,21H19A2,2 0 0,0 21,19V5A2,2 0 0,0 19,3M7,7H17V5H19V19H5V5H7V7Z");
                break;

            case ToastCategory.DoNotDisturb:
                SideToastIconCircle.Background = new SolidColorBrush(Color.FromRgb(0xA8, 0x55, 0xF7));
                SideToastVectorIcon.Data = Geometry.Parse("M18.97,15.95C19.8,15.87 20.69,17.05 20.16,17.8C19.84,18.25 19.5,18.67 19.08,19.07C15.17,23 8.84,23 4.94,19.07C1.03,15.17 1.03,8.83 4.94,4.93C5.34,4.53 5.76,4.17 6.21,3.85C6.96,3.32 8.14,4.21 8.06,5.04C7.79,7.9 8.75,10.87 10.95,13.06C13.14,15.26 16.1,16.22 18.97,15.95Z");
                break;

            default:
                SideToastIconCircle.Background = new SolidColorBrush(Color.FromRgb(0x34, 0xF5, 0xC5));
                SideToastVectorIcon.Data = Geometry.Parse("M12,2A10,10 0 0,1 22,12A10,10 0 0,1 12,22A10,10 0 0,1 2,12A10,10 0 0,1 12,2M11,16.5L18,9.5L16.59,8.09L11,13.67L7.91,10.59L6.5,12L11,16.5Z");
                break;
        }

        SideToastBorder.Visibility = Visibility.Visible;
        var dur = TimeSpan.FromMilliseconds(220);
        var ease = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.35 };
        SideToastBorder.BeginAnimation(OpacityProperty, new DoubleAnimation(1.0, dur));
        SideToastSlide.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-14, 0, dur) { EasingFunction = ease });

        _toastHideTimer.Stop();
        _toastHideTimer.Interval = TimeSpan.FromSeconds(NotificationPolicy.ClampDuration(_settings.NotificationDurationSeconds));
        _toastHideTimer.Start();
    }

    #endregion

    #region Синхронизация режима «Не беспокоить»

    private void UpdateDndVisuals()
    {
        TglDndActiveState.IsEnabled = _dndStateKnown && !_dndChanging;
        BtnQuickDnd.IsEnabled = _dndStateKnown && !_dndChanging;
        string? dndError = _dndStateKnown ? null : Tr("Windows DND state is unavailable", "Не удалось прочитать режим Windows", "Стан Windows недоступний", "Windows-Status nicht verfügbar", "Estado de Windows no disponible");
        TglDndActiveState.ToolTip = dndError;
        BtnQuickDnd.ToolTip = dndError;
        bool showBadge = _isDndActive && (TglShowDnd.IsChecked ?? true);
        CompactDndBadge.Visibility = showBadge ? Visibility.Visible : Visibility.Collapsed;

        if (TglDndActiveState != null)
            TglDndActiveState.IsChecked = _isDndActive;

        BtnQuickDnd.Background = _isDndActive
            ? new SolidColorBrush(Color.FromArgb(0x66, 0x8B, 0x5C, 0xF6))
            : (Brush)FindResource("ThemeInputBrush");

        TxtQuickDnd.Text = _isDndActive
            ? Tr("DND: ON", "Не беспокоить: Вкл", "Не турбувати: Увімк", "Nicht stören: EIN", "No molestar: ON")
            : Tr("Do Not Disturb", "Не беспокоить", "Не турбувати", "Nicht stören", "No molestar");
    }

    #endregion

    #region Погода (Open-Meteo API, по умолчанию без города)

    private async Task FetchWeatherForCityAsync(string cityQuery, bool showToastOnSuccess)
    {
        if (string.IsNullOrWhiteSpace(cityQuery))
        {
            _hasWeatherData = false;
            RenderWeatherUi();
            return;
        }

        try
        {
            string langCode = InterfaceLanguage switch
            {
                "Русский"    => "ru",
                "Українська" => "uk",
                "Deutsch"    => "de",
                "Español"    => "es",
                _            => "en"
            };

            string geoUrl = $"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(cityQuery.Trim())}&count=1&language={langCode}&format=json";
            string geoJson = await _http.GetStringAsync(geoUrl);

            using var geoDoc = JsonDocument.Parse(geoJson);
            if (!geoDoc.RootElement.TryGetProperty("results", out JsonElement results) || results.GetArrayLength() == 0)
            {
                if (showToastOnSuccess)
                    ShowSideBubble(Tr("City not found", "Город не найден", "Місто не знайдено", "Stadt nicht gefunden", "Ciudad no encontrada"), ToastCategory.Info, bypassDnd: true);
                return;
            }

            JsonElement first = results[0];
            _resolvedCityName = first.TryGetProperty("name", out var nProp) ? nProp.GetString() ?? cityQuery : cityQuery;
            double lat = first.GetProperty("latitude").GetDouble();
            double lon = first.GetProperty("longitude").GetDouble();

            string latStr = lat.ToString(CultureInfo.InvariantCulture);
            string lonStr = lon.ToString(CultureInfo.InvariantCulture);

            string weatherUrl =
                $"https://api.open-meteo.com/v1/forecast?latitude={latStr}&longitude={lonStr}" +
                "&current=temperature_2m,relative_humidity_2m,apparent_temperature,precipitation,weather_code,cloud_cover,surface_pressure,wind_speed_10m,wind_direction_10m,uv_index" +
                "&daily=temperature_2m_max,temperature_2m_min,sunrise,sunset,uv_index_max,precipitation_sum,precipitation_probability_max" +
                "&hourly=temperature_2m,weather_code&wind_speed_unit=ms&timezone=auto&forecast_days=2";

            string weatherJson = await _http.GetStringAsync(weatherUrl);
            using var wDoc = JsonDocument.Parse(weatherJson);
            JsonElement root = wDoc.RootElement;
            JsonElement cur = root.GetProperty("current");

            _lastTempC           = cur.GetProperty("temperature_2m").GetDouble();
            _lastFeelsLikeC      = cur.GetProperty("apparent_temperature").GetDouble();
            _lastHumidity        = cur.GetProperty("relative_humidity_2m").GetInt32();
            _lastWindSpeedMs     = cur.GetProperty("wind_speed_10m").GetDouble();
            _lastWindDeg         = cur.GetProperty("wind_direction_10m").GetInt32();
            _lastPressureHpa     = cur.GetProperty("surface_pressure").GetDouble();
            _lastPrecipitationMm = cur.GetProperty("precipitation").GetDouble();
            _lastWeatherCode     = cur.GetProperty("weather_code").GetInt32();
            if (cur.TryGetProperty("uv_index", out var uvProp))
                _lastUvIndex = uvProp.GetDouble();

            if (root.TryGetProperty("daily", out JsonElement daily))
            {
                if (daily.TryGetProperty("temperature_2m_max", out var maxArr) && maxArr.GetArrayLength() > 0)
                    _lastTempMaxC = maxArr[0].GetDouble();
                if (daily.TryGetProperty("temperature_2m_min", out var minArr) && minArr.GetArrayLength() > 0)
                    _lastTempMinC = minArr[0].GetDouble();
                if (daily.TryGetProperty("sunrise", out var srArr) && srArr.GetArrayLength() > 0)
                {
                    string sr = srArr[0].GetString() ?? "";
                    if (sr.Contains('T')) _lastSunrise = sr.Split('T')[1];
                }
                if (daily.TryGetProperty("sunset", out var ssArr) && ssArr.GetArrayLength() > 0)
                {
                    string ss = ssArr[0].GetString() ?? "";
                    if (ss.Contains('T')) _lastSunset = ss.Split('T')[1];
                }
                if (daily.TryGetProperty("precipitation_sum", out var pSumArr) && pSumArr.GetArrayLength() > 0)
                    _lastPrecipitationMm = pSumArr[0].GetDouble();
                if (daily.TryGetProperty("precipitation_probability_max", out var pProbArr) && pProbArr.GetArrayLength() > 0 && pProbArr[0].ValueKind == JsonValueKind.Number)
                    _lastPrecipProb = pProbArr[0].GetInt32();
            }

            if (root.TryGetProperty("hourly", out JsonElement hourly) &&
                hourly.TryGetProperty("time", out var times) &&
                hourly.TryGetProperty("temperature_2m", out var temps))
            {
                int currentHour = DateTime.Now.Hour;
                int startIndex = Math.Clamp(currentHour + 1, 0, Math.Max(0, times.GetArrayLength() - 4));
                for (int i = 0; i < 4 && (startIndex + i) < times.GetArrayLength(); i++)
                {
                    string tStr = times[startIndex + i].GetString() ?? "";
                    _hourlyLabels[i] = tStr.Contains('T') ? tStr.Split('T')[1][..2] : $"{(currentHour + 1 + i) % 24:D2}";
                    _hourlyTempsC[i] = temps[startIndex + i].GetDouble();
                }
            }

            _hasWeatherData = true;
            _lastWeatherUpdatedClock = DateTime.Now.ToString("HH:mm");
            RenderWeatherUi();

            if (showToastOnSuccess)
                ShowSideBubble($"{_resolvedCityName}: {FormatTempShort(_lastTempC)}", ToastCategory.Info, bypassDnd: true);
        }
        catch { }
    }

    private void RenderWeatherUi()
    {
        RefreshWidget("weather");
        if (!_hasWeatherData || string.IsNullOrWhiteSpace(_settings.WeatherCity))
        {
            CompactTempText.Text         = "--°";
            HomeWeatherLocationText.Text = Tr("Weather · No city set", "Погода · Город не указан", "Погода · Місто не вказано", "Wetter · Keine Stadt", "Clima · Sin ciudad");
            HomeWeatherDetailsText.Text  = Tr("Click here to enter your city", "Нажмите сюда, чтобы указать свой город", "Натисніть, щоб вказати місто", "Klicken um Stadt einzugeben", "Haz clic para elegir ciudad");

            DashUpdatedTimeText.Text = Tr("Enter city and press Enter", "Введите город и нажмите Enter", "Введіть місто і натисніть Enter", "Stadt eingeben + Enter", "Escribe ciudad y pulsa Enter");
            Tile1CityCap.Text        = Tr("NO CITY", "ГОРОД НЕ УКАЗАН", "МІСТО НЕ ВКАЗАНО", "KEINE STADT", "SIN CIUDAD");
            Tile1TempBig.Text        = "--°";
            Tile1Condition.Text      = "—";
            Tile1FeelsMinMax.Text    = "—";
            Tile2WindSpeed.Text      = "--";
            Tile2WindDir.Text        = "—";
            Tile3HumVal.Text         = "--%";
            Tile3HumBar.Value        = 0;
            Tile3HumState.Text       = "—";
            Tile4PressVal.Text       = "---";
            Tile4PressUnit.Text      = Tr("mmHg", "мм рт. ст.", "мм рт. ст.", "mmHg", "mmHg");
            Tile5Sunrise.Text        = "↑ --:--";
            Tile5Sunset.Text         = "↓ --:--";
            Tile6UvVal.Text          = "-";
            Tile6UvState.Text        = "—";
            Tile7PrecipVal.Text      = "--";
            Tile7PrecipProb.Text     = "—";
            return;
        }

        string tempShort = FormatTempShort(_lastTempC);
        string feelsShort = FormatTempShort(_lastFeelsLikeC);
        string maxShort = FormatTempShort(_lastTempMaxC);
        string minShort = FormatTempShort(_lastTempMinC);
        string windDir = GetCompassDirection(_lastWindDeg);
        string speedUnit = Tr("m/s", "м/с", "м/с", "m/s", "m/s");
        int mmHg = (int)Math.Round(_lastPressureHpa * 0.750062);
        string mmHgUnit = Tr("mmHg", "мм рт. ст.", "мм рт. ст.", "mmHg", "mmHg");
        string condText = GetWmoDescription(_lastWeatherCode);

        CompactTempText.Text = tempShort;
        HomeWeatherLocationText.Text = $"{_resolvedCityName} · {tempShort} · {condText}";
        HomeWeatherDetailsText.Text  =
            $"{Tr("Wind", "Ветер", "Вітер", "Wind", "Viento")} {_lastWindSpeedMs:F1} {speedUnit} ({windDir}) · " +
            $"{Tr("Hum", "Влажн.", "Волог.", "Feuchte", "Hum.")} {_lastHumidity}% · {mmHg} {mmHgUnit}";

        DashUpdatedTimeText.Text = $"{Tr("Updated at", "Обновлено в", "Оновлено о", "Aktualisiert um", "Actualizado a las")} {_lastWeatherUpdatedClock}";

        Tile1CityCap.Text     = _resolvedCityName.ToUpperInvariant();
        Tile1TempBig.Text     = tempShort;
        Tile1Condition.Text   = condText;
        Tile1FeelsMinMax.Text = $"{Tr("Feels like", "Ощущается", "Відчувається", "Gefühlt", "Sensación")} {feelsShort} · ↑{maxShort} ↓{minShort}";

        WindCompassRotate.Angle = _lastWindDeg;
        Tile2WindSpeed.Text     = $"{_lastWindSpeedMs:F1} {speedUnit}";
        Tile2WindDir.Text       = windDir;

        Tile3HumVal.Text   = $"{_lastHumidity}%";
        Tile3HumBar.Value  = _lastHumidity;
        Tile3HumState.Text = _lastHumidity >= 70
            ? Tr("Humid", "Влажно", "Волого", "Feucht", "Húmedo")
            : _lastHumidity <= 35
                ? Tr("Dry", "Сухо", "Сухо", "Trocken", "Seco")
                : Tr("Comfortable", "Комфортно", "Комфортно", "Angenehm", "Cómodo");

        Tile4PressVal.Text  = $"{mmHg}";
        Tile4PressUnit.Text = mmHgUnit;

        Tile5Sunrise.Text = $"↑ {_lastSunrise}";
        Tile5Sunset.Text  = $"↓ {_lastSunset}";

        int uvRound = (int)Math.Round(_lastUvIndex);
        Tile6UvVal.Text   = $"{uvRound}";
        Tile6UvState.Text = uvRound <= 2
            ? Tr("Low", "Низкий", "Низький", "Niedrig", "Bajo")
            : uvRound <= 5
                ? Tr("Moderate", "Средний", "Помірний", "Mäßig", "Moderado")
                : Tr("High", "Высокий", "Високий", "Hoch", "Alto");
        Tile6UvDotOffset.X = Math.Clamp(_lastUvIndex * 10.0, 2, 115);

        Tile7PrecipVal.Text  = $"{_lastPrecipitationMm:F1} {Tr("mm", "мм", "мм", "mm", "mm")}";
        Tile7PrecipProb.Text = $"{Tr("Today · probability", "За сегодня · вероятность", "За сьогодні · ймовірність", "Heute · Wahrscheinlichkeit", "Hoy · probabilidad")}\n{_lastPrecipProb}%";

        HrTime0.Text = _hourlyLabels[0]; HrTemp0.Text = FormatTempShort(_hourlyTempsC[0]);
        HrTime1.Text = _hourlyLabels[1]; HrTemp1.Text = FormatTempShort(_hourlyTempsC[1]);
        HrTime2.Text = _hourlyLabels[2]; HrTemp2.Text = FormatTempShort(_hourlyTempsC[2]);
        HrTime3.Text = _hourlyLabels[3]; HrTemp3.Text = FormatTempShort(_hourlyTempsC[3]);
    }

    private string FormatTempShort(double tempC)
    {
        if (_settings.UseFahrenheit)
        {
            int f = (int)Math.Round(tempC * 9.0 / 5.0 + 32.0);
            return $"{f}°";
        }
        return $"{(int)Math.Round(tempC)}°";
    }

    private string GetCompassDirection(int deg)
    {
        string[] en = ["N", "NE", "E", "SE", "S", "SW", "W", "NW"];
        string[] ru = ["С", "СВ", "В", "ЮВ", "Ю", "ЮЗ", "З", "СЗ"];
        string[] uk = ["Пн", "ПнСх", "Сх", "ПдСх", "Пд", "ПдЗх", "Зх", "ПнЗх"];
        int idx = (int)Math.Round((deg % 360) / 45.0) % 8;
        return InterfaceLanguage switch
        {
            "Русский"    => ru[idx],
            "Українська" => uk[idx],
            _            => en[idx]
        };
    }

    private string GetWmoDescription(int code) => code switch
    {
        0            => Tr("Clear sky", "Ясно", "Ясно", "Klarer Himmel", "Despejado"),
        1            => Tr("Mainly clear", "Малооблачно", "Малохмарно", "Überwiegend klar", "Mayormente despejado"),
        2            => Tr("Partly cloudy", "Переменная облачность", "Мінлива хмарність", "Teilweise bewölkt", "Parcialmente nublado"),
        3            => Tr("Overcast", "Пасмурно", "Похмуро", "Bedeckt", "Nublado"),
        45 or 48     => Tr("Fog", "Туман", "Туман", "Nebel", "Niebla"),
        51 or 53 or 55 => Tr("Drizzle", "Морось", "Мряка", "Nieselregen", "Llovizna"),
        61 or 63 or 65 => Tr("Rain", "Дождь", "Дощ", "Regen", "Lluvia"),
        71 or 73 or 75 or 77 => Tr("Snowfall", "Снег", "Сніг", "Schneefall", "Nieve"),
        80 or 81 or 82 => Tr("Rain showers", "Ливень", "Злива", "Regenschauer", "Chubascos"),
        95 or 96 or 99 => Tr("Thunderstorm", "Гроза", "Гроза", "Gewitter", "Tormenta"),
        _            => Tr("Cloudy", "Облачно", "Хмарно", "Bewölkt", "Nublado")
    };

    private void OpenWeatherDashboard_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        MorphToState(IslandState.Weather);
    }

    private void DashCityTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isLoadingSettings) return;
        if (WeatherCityInput != null && WeatherCityInput.Text != DashCityTextBox.Text)
            WeatherCityInput.Text = DashCityTextBox.Text;
    }

    private void WeatherCityInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isLoadingSettings) return;
        if (DashCityTextBox != null && DashCityTextBox.Text != WeatherCityInput.Text)
            DashCityTextBox.Text = WeatherCityInput.Text;
    }

    private void BtnClearWeatherCity_Click(object sender, RoutedEventArgs e)
    {
        WeatherCityInput.Text = "";
        DashCityTextBox.Text  = "";
        _settings.WeatherCity = "";
        _hasWeatherData       = false;
        RenderWeatherUi();
        SaveSettings();
    }

    private async void DashCityTextBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            string city = DashCityTextBox.Text.Trim();
            WeatherCityInput.Text = city;
            _settings.WeatherCity = city;
            SaveSettings();
            await FetchWeatherForCityAsync(city, true);
        }
    }

    private async void BtnSearchWeatherCity_Click(object sender, RoutedEventArgs e)
    {
        string city = WeatherCityInput.Text.Trim();
        _settings.WeatherCity = city;
        SaveSettings();
        await FetchWeatherForCityAsync(city, true);
    }

    private async void WeatherCityInput_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            string city = WeatherCityInput.Text.Trim();
            _settings.WeatherCity = city;
            SaveSettings();
            await FetchWeatherForCityAsync(city, true);
        }
    }

    private async void QuickCityChip_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.Content is string city)
        {
            WeatherCityInput.Text = city;
            DashCityTextBox.Text  = city;
            _settings.WeatherCity = city;
            SaveSettings();
            await FetchWeatherForCityAsync(city, true);
        }
    }

    private async void BtnRefreshWeather_Click(object sender, RoutedEventArgs e) =>
        await FetchWeatherForCityAsync(_settings.WeatherCity, true);

    private void TglFahrenheit_Click(object sender, RoutedEventArgs e)
    {
        _settings.UseFahrenheit = TglFahrenheit.IsChecked ?? false;
        RenderWeatherUi();
        SaveSettings();
    }

    #endregion

    #region Системный Трей

    private void InitializeTrayIcon()
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(System.Drawing.Color.Transparent);
            using var pillBrush = new SolidBrush(System.Drawing.Color.FromArgb(255, 30, 23, 56));
            using var accentBrush = new SolidBrush(System.Drawing.Color.FromArgb(255, 52, 245, 197));
            g.FillEllipse(pillBrush, 2, 7, 16, 18);
            g.FillEllipse(pillBrush, 14, 7, 16, 18);
            g.FillRectangle(pillBrush, 10, 7, 12, 18);
            g.FillEllipse(accentBrush, 8, 13, 6, 6);
        }

        _trayIcon = new Forms.NotifyIcon
        {
            Icon = System.Drawing.Icon.FromHandle(bmp.GetHicon()),
            Text = "OC - Dynamic Island",
            Visible = true
        };

        RebuildTrayMenu();
        _trayIcon.DoubleClick += (_, _) => Dispatcher.Invoke(ToggleIslandDashboard);
    }

    private void RebuildTrayMenu()
    {
        if (_trayIcon == null) return;
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(Tr("Expand / Collapse", "Развернуть / Свернуть", "Розгорнути / Згорнути", "Öffnen / Schließen", "Expandir / Contraer"), null, (_, _) =>
            Dispatcher.Invoke(ToggleIslandDashboard));

        menu.Items.Add(Tr("Weather Dashboard", "Окно погоды", "Вікно погоди", "Wetter-Ansicht", "Panel del clima"), null, (_, _) =>
            Dispatcher.Invoke(() => { ShowIsland(); MorphToState(IslandState.Weather); }));

        string dndLabel = _isDndActive
            ? Tr("🌙 Do Not Disturb: ON", "🌙 Не беспокоить: Вкл", "🌙 Не турбувати: Увімк", "🌙 Nicht stören: EIN", "🌙 No molestar: ON")
            : Tr("🌙 Do Not Disturb: OFF", "🌙 Не беспокоить: Выкл", "🌙 Не турбувати: Вимк", "🌙 Nicht stören: AUS", "🌙 No molestar: OFF");
        menu.Items.Add(dndLabel, null, (_, _) =>
            Dispatcher.Invoke(() => SetDoNotDisturbState(!_isDndActive, true)));

        menu.Items.Add(Tr("Settings", "Настройки", "Налаштування", "Einstellungen", "Ajustes"), null, (_, _) =>
            Dispatcher.Invoke(() => { ShowIsland(); MorphToState(IslandState.Settings); }));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(Tr("Exit", "Выйти", "Вийти", "Beenden", "Salir"), null, (_, _) =>
            Dispatcher.Invoke(ShutdownApplication));
        _trayIcon.ContextMenuStrip = menu;
    }

    private void ShutdownApplication()
    {
        SaveSettings();
        _trayIcon?.Dispose();
        System.Windows.Application.Current.Shutdown();
    }

    #endregion

    #region Плавный физический движок + Масштабирование (90% для Главной, Игрового HUD, Погоды и Настроек)

    private void OnCompositionTargetRendering(object? sender, EventArgs e)
    {
        double now = _frameClock.Elapsed.TotalSeconds;
        double dt = Math.Clamp(now - _lastFrameTime, 0.001, 0.033);
        _lastFrameTime = now;

        const double stiffness = 240.0;
        const double damping = 22.0;

        StepSpring(ref _curW, _targetW, ref _velW, stiffness, damping, dt);
        StepSpring(ref _curH, _targetH, ref _velH, stiffness, damping, dt);
        StepSpring(ref _curR, _targetR, ref _velR, stiffness, damping, dt);

        if (Math.Abs(_targetW - _curW) > 0.05 || Math.Abs(_velW) > 0.05)
            IslandBorder.Width = Math.Max(60, _curW);

        if (Math.Abs(_targetH - _curH) > 0.05 || Math.Abs(_velH) > 0.05)
            IslandBorder.Height = Math.Max(28, _curH);

        var corners = IslandCorners(Math.Abs(_targetR - _curR) > .05 ? Math.Round(_curR, 2) : _targetR);
        if (IslandBorder.CornerRadius != corners)
        {
            IslandBorder.CornerRadius = LiquidGlassOverlay.CornerRadius = CustomBgBorder.CornerRadius = corners;
        }

        UpdateGlassGeometry();
        UpdateGlassShadow();
        if (SettingsView.IsVisible && now - _lastMascotFrame >= 1.0 / 30)
            MascotBobTransform.Y = Math.Sin(now * 2.8) * 1.6;
        if (now - _lastMascotFrame >= 1.0 / 30)
        { AnimateFeatureMascots(now, Math.Clamp(now - _lastMascotFrame, .001, .05)); _lastMascotFrame = now; }
        if (!NeedsIslandFrames()) StopIslandFrames();
    }

    private bool _framesSubscribed;
    private bool NeedsIslandFrames() => IsVisible && IslandBorder.Visibility == Visibility.Visible &&
        (Math.Abs(_targetW - _curW) > .05 || Math.Abs(_targetH - _curH) > .05 || Math.Abs(_targetR - _curR) > .05 ||
         Math.Abs(_velW) > .05 || Math.Abs(_velH) > .05 || Math.Abs(_velR) > .05 ||
         Math.Abs(_attractionX - _attractionTargetX) > .01 || Math.Abs(_attractionY - _attractionTargetY) > .01 ||
         Math.Abs(_attractionVx) > .05 || Math.Abs(_attractionVy) > .05 ||
         IslandBorder.IsMouseOver || SettingsView.IsVisible ||
         _animatedMascots.Any(reference => reference.TryGetTarget(out var mascot) && mascot.IsVisible));
    private void StartIslandFrames()
    {
        if (_framesSubscribed || _windowClosed || !IsLoaded) return;
        _framesSubscribed = true; _lastFrameTime = _frameClock.Elapsed.TotalSeconds;
        CompositionTarget.Rendering += OnCompositionTargetRendering;
    }
    private void StopIslandFrames()
    {
        if (!_framesSubscribed) return;
        CompositionTarget.Rendering -= OnCompositionTargetRendering; _framesSubscribed = false;
    }

    private static void StepSpring(ref double current, double target, ref double velocity, double k, double c, double dt)
    {
        double force = (target - current) * k;
        double damp = -velocity * c;
        velocity += (force + damp) * dt;
        current += velocity * dt;
    }

    private void MorphToState(IslandState state)
    {
        if (_currentState == state) return;
        if (_currentState == IslandState.Clipboard) ClipboardHistoryListStack.Children.Clear();
        _currentState = state;
        StartIslandFrames();

        CompactView.Visibility            = Visibility.Collapsed;
        HoverView.Visibility              = Visibility.Collapsed;
        ExpandedView.Visibility           = Visibility.Collapsed;
        WeatherDashboardView.Visibility   = Visibility.Collapsed;
        ClipboardDashboardView.Visibility = Visibility.Collapsed;
        SettingsView.Visibility           = Visibility.Collapsed;
        DashboardSectionView.Visibility   = Visibility.Collapsed;
        bool showNavigation = state is IslandState.Expanded or IslandState.Weather or IslandState.Clipboard or IslandState.Section;
        DashboardHeader.Visibility = showNavigation ? Visibility.Visible : Visibility.Collapsed;
        if (showNavigation) BuildDashboardNavigation();

        switch (state)
        {
            case IslandState.Compact:
                _targetW = _isGameModeActive ? GameHudWidth : _settings.CompactWidth;
                _targetH = _isGameModeActive ? GameHudHeight : _settings.CompactHeight;
                _targetR = _isGameModeActive ? GameHudRadius : Math.Max(4, _targetH * .5 * _settings.CornerRounding);
                CompactView.Visibility = Visibility.Visible;
                UpdateCompactContent();
                break;

            case IslandState.Hover:
                _targetW = 410; _targetH = 56; _targetR = 26;
                HoverView.Visibility = Visibility.Visible;
                FadeInElement(HoverView);
                break;

            case IslandState.Expanded:
                _targetW = 720; _targetH = 278; _targetR = 28;
                ExpandedView.Visibility = Visibility.Visible;
                FadeInElement(ExpandedView);
                break;

            case IslandState.Weather:
                _targetW = 720; _targetH = 385; _targetR = 28;
                WeatherDashboardView.Visibility = Visibility.Visible;
                FadeInElement(WeatherDashboardView);
                break;

            case IslandState.Settings:
                _targetW = 740; _targetH = 515; _targetR = 30;
                SettingsView.Visibility = Visibility.Visible;
                FadeInElement(SettingsView);
                break;

            case IslandState.Clipboard:
                _targetW = 720; _targetH = 350; _targetR = 28;
                PruneExpiredClipboardItems();
                RenderClipboardHistoryUi();
                ClipboardDashboardView.Visibility = Visibility.Visible;
                FadeInElement(ClipboardDashboardView);
                break;

            case IslandState.Section:
                _targetW = 720; _targetH = DashboardPageHeight; _targetR = 28;
                DashboardSectionView.Visibility = Visibility.Visible;
                FadeInElement(DashboardSectionView);
                break;
        }

        UpdateDiscordCallState();
        ApplyScaleForCurrentState();
        RememberDashboardTab();
        UpdateDeviceObservation();
    }

    private void ApplyScaleForCurrentState()
    {
        double userScale = Math.Clamp(_settings.IslandScalePercent, 50, 130) / 100.0;

        // Ползунок масштаба меняет размер ТОЛЬКО у обычного свёрнутого островка (Compact без игрового режима).
        // Игровой центр (HUD), Главное меню (Expanded), Погода и Настройки всегда фиксированы на 90% (0.90).
        bool useCustomCompactScale = (_currentState == IslandState.Compact && !_isGameModeActive);
        double targetScale = useCustomCompactScale ? userScale : 0.90;

        var dur = TimeSpan.FromMilliseconds(220);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        IslandScaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(targetScale, dur) { EasingFunction = ease });
        IslandScaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(targetScale, dur) { EasingFunction = ease });

        SideToastScale.ScaleX = 0.90;
        SideToastScale.ScaleY = 0.90;
    }

    private static void FadeInElement(UIElement el)
    {
        el.Opacity = 0;
        el.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(190))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        });
    }

    #endregion

    #region Clipboard privacy
    private static bool LooksLikePasswordOrSecret(string text)
    {
        if (text.Length > 220) return false;
        if (Regex.IsMatch(text, @"^(sk-[A-Za-z0-9\-_]{16,}|ghp_[A-Za-z0-9]{20,}|gho_[A-Za-z0-9]{20,}|AKIA[0-9A-Z]{16}|eyJ[A-Za-z0-9\-_]+\.[A-Za-z0-9\-_]+|-----BEGIN .* KEY-----)", RegexOptions.IgnoreCase))
            return true;
        if (Regex.IsMatch(text, @"^\d{4}[\s\-]?\d{4}[\s\-]?\d{4}[\s\-]?\d{4}$"))
            return true;
        if (!text.Contains(' ') && text.Length is >= 8 and <= 64 && !text.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            bool hasUpper = text.Any(char.IsUpper);
            bool hasLower = text.Any(char.IsLower);
            bool hasDigit = text.Any(char.IsDigit);
            bool hasSpecial = text.Any(c => !char.IsLetterOrDigit(c) && c != '_' && c != '-' && c != '.');
            if ((hasUpper && hasLower && hasDigit && hasSpecial) || (hasSpecial && hasDigit && (hasUpper || hasLower)))
                return true;
        }
        return false;
    }

    #endregion

    #region Векторная Отрисовка 21 Персонажа

    private UIElement CreateCharacterVector(string key, double size = 36)
    {
        if (CreateFeatureMascot(key, size) is { } mascot) return mascot;
        var canvas = new Canvas { Width = 36, Height = 36 };
        Brush coral = (SolidColorBrush)new BrushConverter().ConvertFrom("#E87B5F")!;
        Brush cyan = (SolidColorBrush)new BrushConverter().ConvertFrom("#34F5C5")!;
        Brush yellow = (SolidColorBrush)new BrushConverter().ConvertFrom("#FBBF24")!;
        Brush pink = (SolidColorBrush)new BrushConverter().ConvertFrom("#F472B6")!;
        Brush lavender = (SolidColorBrush)new BrushConverter().ConvertFrom("#C4B5FD")!;

        switch (key)
        {
            case "Gemini":
                var gemGrad = new LinearGradientBrush
                {
                    StartPoint = new Point(0, 0), EndPoint = new Point(1, 1),
                    GradientStops = [new GradientStop((Color)ColorConverter.ConvertFromString("#4285F4"), 0.0), new GradientStop((Color)ColorConverter.ConvertFromString("#9B72CB"), 0.5), new GradientStop((Color)ColorConverter.ConvertFromString("#D96570"), 1.0)]
                };
                canvas.Children.Add(new Path { Data = Geometry.Parse("M18,3 C18.5,12 24,17.5 33,18 C24,18.5 18.5,24 18,33 C17.5,24 12,18.5 3,18 C12,17.5 17.5,12 18,3 Z"), Fill = gemGrad });
                break;

            case "ChatGPT":
                var gptBg = new Ellipse { Width = 28, Height = 28, Fill = (SolidColorBrush)new BrushConverter().ConvertFrom("#10A37F")! };
                Canvas.SetLeft(gptBg, 4); Canvas.SetTop(gptBg, 4);
                canvas.Children.Add(gptBg);
                canvas.Children.Add(new Path { Data = Geometry.Parse("M18,8.5 L24.5,12.2 L24.5,19.8 L18,23.5 L11.5,19.8 L11.5,12.2 Z M18,8.5 L18,16 M24.5,12.2 L18,16 M11.5,19.8 L18,16"), Stroke = Brushes.White, StrokeThickness = 2.1, StrokeLineJoin = PenLineJoin.Round });
                break;

            case "Google":
                canvas.Children.Add(new Path { Data = Geometry.Parse("M18,16 L29,16 C29.5,23 24.5,29 18,29 L18,24 C21.5,24 24,21.5 24,19.5 L18,19.5 Z"), Fill = (SolidColorBrush)new BrushConverter().ConvertFrom("#4285F4")! });
                canvas.Children.Add(new Path { Data = Geometry.Parse("M8,23.5 C10,27 14,29 18,29 L18,24 C15.2,24 13,22.5 12,20.5 Z"), Fill = (SolidColorBrush)new BrushConverter().ConvertFrom("#34A853")! });
                canvas.Children.Add(new Path { Data = Geometry.Parse("M8,12.5 C6.8,15.8 6.8,20.2 8,23.5 L12,20.5 C11.3,18.8 11.3,17.2 12,15.5 Z"), Fill = (SolidColorBrush)new BrushConverter().ConvertFrom("#FBBC05")! });
                canvas.Children.Add(new Path { Data = Geometry.Parse("M18,7 C13.5,7 9.8,9.2 8,12.5 L12,15.5 C13,13.2 15.2,12 18,12 C20.2,12 22,12.8 23.2,14 L26.8,10.5 C24.5,8.3 21.5,7 18,7 Z"), Fill = (SolidColorBrush)new BrushConverter().ConvertFrom("#EA4335")! });
                break;

            case "OpenAI":
                canvas.Children.Add(new Path { Data = Geometry.Parse("M18,6 L27,11.2 L27,21.8 L18,27 L9,21.8 L9,11.2 Z"), Stroke = Brushes.White, StrokeThickness = 2.2, StrokeLineJoin = PenLineJoin.Round });
                canvas.Children.Add(new Path { Data = Geometry.Parse("M18,11 L23,14 L23,19 L18,22 L13,19 L13,14 Z"), Stroke = cyan, StrokeThickness = 1.8 });
                break;

            case "Codex":
                var cdxBox = new Border { Width = 28, Height = 22, CornerRadius = new CornerRadius(6), Background = (SolidColorBrush)new BrushConverter().ConvertFrom("#1E293B")!, BorderBrush = cyan, BorderThickness = new Thickness(1.6) };
                Canvas.SetLeft(cdxBox, 4); Canvas.SetTop(cdxBox, 7);
                canvas.Children.Add(cdxBox);
                canvas.Children.Add(new Polyline { Points = [new Point(10, 14), new Point(15, 18), new Point(10, 22)], Stroke = cyan, StrokeThickness = 2.2 });
                break;

            case "Antigravity":
                var agSphere = new Ellipse { Width = 16, Height = 16, Fill = cyan };
                Canvas.SetLeft(agSphere, 10); Canvas.SetTop(agSphere, 9);
                var agOrbit = new Ellipse { Width = 28, Height = 8, Stroke = lavender, StrokeThickness = 2, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new RotateTransform(-16) };
                Canvas.SetLeft(agOrbit, 4); Canvas.SetTop(agOrbit, 14);
                canvas.Children.Add(agSphere); canvas.Children.Add(agOrbit);
                break;

            case "Claude":
                for (int i = 0; i < 12; i++)
                {
                    var ray = new Rectangle { Width = 3.6, Height = 26, RadiusX = 1.8, RadiusY = 1.8, Fill = coral, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new RotateTransform(i * 30) };
                    Canvas.SetLeft(ray, 16.2); Canvas.SetTop(ray, 5);
                    canvas.Children.Add(ray);
                }
                break;

            case "Claude Code":
                var body = new Rectangle { Width = 22, Height = 13, RadiusX = 2, RadiusY = 2, Fill = coral };
                Canvas.SetLeft(body, 7); Canvas.SetTop(body, 9);
                var eyeL = new Rectangle { Width = 3.2, Height = 4.5, Fill = Brushes.Black }; Canvas.SetLeft(eyeL, 11.5); Canvas.SetTop(eyeL, 13);
                var eyeR = new Rectangle { Width = 3.2, Height = 4.5, Fill = Brushes.Black }; Canvas.SetLeft(eyeR, 21.3); Canvas.SetTop(eyeR, 13);
                canvas.Children.Add(body); canvas.Children.Add(eyeL); canvas.Children.Add(eyeR);
                break;

            case "Eyes":
                for (int i = 0; i < 2; i++)
                {
                    var s = new Ellipse { Width = 13, Height = 16, Fill = Brushes.White }; Canvas.SetLeft(s, 4.5 + i * 14); Canvas.SetTop(s, 10);
                    var p = new Ellipse { Width = 6.5, Height = 6.5, Fill = Brushes.Black }; Canvas.SetLeft(p, 8.5 + i * 14); Canvas.SetTop(p, 15);
                    var glint = new Ellipse { Width = 2.5, Height = 2.5, Fill = Brushes.White }; Canvas.SetLeft(glint, 10 + i * 14); Canvas.SetTop(glint, 16);
                    canvas.Children.Add(s); canvas.Children.Add(p); canvas.Children.Add(glint);
                }
                break;

            case "Cat":
                canvas.Children.Add(new Path { Data = Geometry.Parse("M7,24 L7,9 L14,14.5 L22,14.5 L29,9 L29,24 C29,28 24,30 18,30 C12,30 7,28 7,24 Z"), Fill = Brushes.White });
                var cEye1 = new Ellipse { Width = 3.5, Height = 4, Fill = Brushes.Black }; Canvas.SetLeft(cEye1, 12); Canvas.SetTop(cEye1, 18);
                var cEye2 = new Ellipse { Width = 3.5, Height = 4, Fill = Brushes.Black }; Canvas.SetLeft(cEye2, 20.5); Canvas.SetTop(cEye2, 18);
                canvas.Children.Add(cEye1); canvas.Children.Add(cEye2);
                break;

            case "Ghost":
                canvas.Children.Add(new Path { Data = Geometry.Parse("M9,29 L9,16 C9,8.5 27,8.5 27,16 L27,29 L23.5,25.5 L20,29 L16,29 L12.5,25.5 Z"), Fill = Brushes.White });
                break;

            case "Robot":
                var rHead = new Border { Width = 22, Height = 17, CornerRadius = new CornerRadius(5), Background = Brushes.White }; Canvas.SetLeft(rHead, 7); Canvas.SetTop(rHead, 11);
                var rVisor = new Border { Width = 16, Height = 8, CornerRadius = new CornerRadius(3), Background = Brushes.Black }; Canvas.SetLeft(rVisor, 10); Canvas.SetTop(rVisor, 15);
                canvas.Children.Add(rHead); canvas.Children.Add(rVisor);
                break;

            case "Fish":
                var fTail = new Polygon { Points = [new Point(22, 19), new Point(30, 12), new Point(28, 19), new Point(30, 26)], Fill = coral };
                var fBody = new Ellipse { Width = 18, Height = 12, Fill = yellow }; Canvas.SetLeft(fBody, 7); Canvas.SetTop(fBody, 13);
                canvas.Children.Add(fTail); canvas.Children.Add(fBody);
                break;

            case "Planet":
                var pl = new Ellipse { Width = 17, Height = 17, Fill = cyan }; Canvas.SetLeft(pl, 9.5); Canvas.SetTop(pl, 9.5);
                var ring = new Ellipse { Width = 30, Height = 9, Stroke = yellow, StrokeThickness = 2.6, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new RotateTransform(-22) };
                Canvas.SetLeft(ring, 3); Canvas.SetTop(ring, 13.5);
                canvas.Children.Add(pl); canvas.Children.Add(ring);
                break;

            case "Slime":
                canvas.Children.Add(new Path { Data = Geometry.Parse("M7,26 C7,12 29,12 29,26 C29,29 7,29 7,26 Z"), Fill = cyan });
                break;

            case "Pulse":
                canvas.Children.Add(new Polyline { Points = [new Point(4, 19), new Point(10, 19), new Point(13, 9), new Point(17, 27), new Point(21, 12), new Point(24, 19), new Point(31, 19)], Stroke = coral, StrokeThickness = 2.6 });
                break;

            case "Bunny":
                var bEarL = new Ellipse { Width = 5.5, Height = 14, Fill = Brushes.White }; Canvas.SetLeft(bEarL, 11); Canvas.SetTop(bEarL, 4);
                var bEarR = new Ellipse { Width = 5.5, Height = 14, Fill = Brushes.White }; Canvas.SetLeft(bEarR, 19.5); Canvas.SetTop(bEarR, 4);
                var bHead = new Ellipse { Width = 18, Height = 15, Fill = Brushes.White }; Canvas.SetLeft(bHead, 9); Canvas.SetTop(bHead, 14);
                canvas.Children.Add(bEarL); canvas.Children.Add(bEarR); canvas.Children.Add(bHead);
                break;

            case "Owl":
                var oBody = new Ellipse { Width = 20, Height = 19, Fill = lavender }; Canvas.SetLeft(oBody, 8); Canvas.SetTop(oBody, 9);
                canvas.Children.Add(oBody);
                break;

            case "Dino":
                canvas.Children.Add(new Path { Data = Geometry.Parse("M15,7 L27,7 L27,14 L21,14 L21,16 L25,16 L25,18 L21,18 L21,24 L23,24 L23,28 L19,28 L19,24 L15,24 L15,28 L11,28 L11,21 L7,21 L7,16 L11,16 L11,13 L15,13 Z"), Fill = cyan });
                break;

            case "Star":
                canvas.Children.Add(new Polygon { Points = [new Point(18, 4), new Point(21.8, 12.5), new Point(31, 13.5), new Point(24, 19.8), new Point(26, 29), new Point(18, 24.2), new Point(10, 29), new Point(12, 19.8), new Point(5, 13.5), new Point(14.2, 12.5)], Fill = yellow });
                break;

            default:
                var cl2 = new Ellipse { Width = 20, Height = 15, Fill = Brushes.White }; Canvas.SetLeft(cl2, 8); Canvas.SetTop(cl2, 11);
                canvas.Children.Add(cl2);
                break;
        }

        RegisterLegacyMascot(canvas, key);
        return new Viewbox { Width = size, Height = size, Child = canvas };
    }

    private void UpdateCharacterVectors(string characterKey)
    {
        CompactCharacterHost.Content     = CreateCharacterVector(characterKey, 22);
        if (GameHudCharacterHost != null)
            GameHudCharacterHost.Content = CreateCharacterVector(characterKey, 20);
        HoverCharacterHost.Content       = CreateCharacterVector(characterKey, 28);
        ExpandedCharacterHost.Content    = CreateCharacterVector(characterKey, 26);
        SettingsCharacterHost.Content    = CreateCharacterVector(characterKey, 26);
    }

    #endregion

    #region ПОЛНАЯ ЛОКАЛИЗАЦИЯ ВСЕХ ЭЛЕМЕНТОВ ИНТЕРФЕЙСА (5 ЯЗЫКОВ)

    private string Tr(string en, string ru, string uk, string de, string es) => InterfaceLanguage switch
    {
        "Русский"    => ru,
        "Українська" => uk,
        "Deutsch"    => de,
        "Español"    => es,
        _            => en
    };

    private string GetLocalizedCharacterName(string key) => key switch
    {
        "Eyes"   => Tr("Eyes", "Глазки", "Очі", "Augen", "Ojos"),
        "Cat"    => Tr("Cat", "Кот", "Кіт", "Katze", "Gato"),
        "Ghost"  => Tr("Ghost", "Призрак", "Привид", "Geist", "Fantasma"),
        "Robot"  => Tr("Robot", "Робот", "Робот", "Roboter", "Robot"),
        "Fish"   => Tr("Fish", "Рыбка", "Рибка", "Fisch", "Pez"),
        "Planet" => Tr("Planet", "Планета", "Планета", "Planet", "Planeta"),
        "Slime"  => Tr("Slime", "Слайм", "Слайм", "Schleim", "Slime"),
        "Pulse"  => Tr("Pulse", "Пульс", "Пульс", "Puls", "Pulso"),
        "Bunny"  => Tr("Bunny", "Зайчик", "Зайчик", "Hase", "Conejo"),
        "Owl"    => Tr("Owl", "Сова", "Сова", "Eule", "Búho"),
        "Dino"   => Tr("Dino", "Дино", "Діно", "Dino", "Dino"),
        "Star"   => Tr("Star", "Звёздочка", "Зірочка", "Stern", "Estrella"),
        "Cloud"  => Tr("Cloud", "Облачко", "Хмаринка", "Wolke", "Nube"),
        "None" => Tr("No character", "Без персонажа", "Без персонажа", "Kein Charakter", "Sin personaje"),
        "Puppy" => Tr("Puppy", "Щенок", "Цуценя", "Welpe", "Cachorro"),
        "Fox" => Tr("Fox", "Лиса", "Лисиця", "Fuchs", "Zorro"),
        "Panda" => Tr("Panda", "Панда", "Панда", "Panda", "Panda"),
        "Bear" => Tr("Bear", "Медведь", "Ведмідь", "Bär", "Oso"),
        "Frog" => Tr("Frog", "Лягушка", "Жаба", "Frosch", "Rana"),
        "Penguin" => Tr("Penguin", "Пингвин", "Пінгвін", "Pinguin", "Pingüino"),
        "Chick" => Tr("Chick", "Цыплёнок", "Курча", "Küken", "Pollito"),
        "Piggy" => Tr("Piggy", "Поросёнок", "Порося", "Ferkel", "Cerdito"),
        "Koala" => Tr("Koala", "Коала", "Коала", "Koala", "Koala"),
        "Mouse" => Tr("Mouse", "Мышка", "Мишка", "Maus", "Ratón"),
        "Bee" => Tr("Bee", "Пчела", "Бджола", "Biene", "Abeja"),
        "Octopus" => Tr("Octopus", "Осьминог", "Восьминіг", "Oktopus", "Pulpo"),
        "Mushroom" => Tr("Mushroom", "Гриб", "Гриб", "Pilz", "Seta"),
        "Cactus" => Tr("Cactus", "Кактус", "Кактус", "Kaktus", "Cactus"),
        "Alien" => Tr("Alien", "Пришелец", "Прибулець", "Alien", "Alienígena"),
        "Bat" => Tr("Bat", "Летучая мышь", "Кажан", "Fledermaus", "Murciélago"),
        "Moon" => Tr("Moon", "Луна", "Місяць", "Mond", "Luna"),
        "Sun" => Tr("Sun", "Солнце", "Сонце", "Sonne", "Sol"),
        "Flame" => Tr("Flame", "Огонёк", "Вогник", "Flamme", "Llama"),
        "Droplet" => Tr("Droplet", "Капля", "Крапля", "Tropfen", "Gota"),
        "Heart" => Tr("Heart", "Сердце", "Серце", "Herz", "Corazón"),
        "Pumpkin" => Tr("Pumpkin", "Тыква", "Гарбуз", "Kürbis", "Calabaza"),
        _        => key
    };

    private string GetLocalizedThemeName(string key) => key switch
    {
        "Midnight" => Tr("Midnight", "Полночь", "Північ", "Mitternacht", "Medianoche"),
        "Graphite" => Tr("Graphite", "Графит", "Графіт", "Graphit", "Grafito"),
        "Ocean"    => Tr("Ocean", "Океан", "Океан", "Ozean", "Océano"),
        "Forest"   => Tr("Forest", "Лес", "Ліс", "Wald", "Bosque"),
        "Sunset"   => Tr("Sunset", "Закат", "Захід", "Sonnenuntergang", "Atardecer"),
        "Rose"     => Tr("Rose", "Роза", "Троянда", "Rose", "Rosa"),
        "Lavender" => Tr("Lavender", "Лаванда", "Лаванда", "Lavendel", "Lavanda"),
        "Neon"     => Tr("Neon", "Неон", "Неон", "Neon", "Neón"),
        "Snow"     => Tr("Snow", "Снег", "Сніг", "Schnee", "Nieve"),
        "Sand"     => Tr("Sand", "Песок", "Пісок", "Sand", "Arena"),
        "Mint"     => Tr("Mint", "Мята", "М'ята", "Minze", "Menta"),
        "Sky"      => Tr("Sky", "Небо", "Небо", "Himmel", "Cielo"),
        _          => key
    };

    private void BuildLanguageDropdown()
    {
        LangItemsStack.Children.Clear();
        foreach (string lang in _availableLanguages)
        {
            var btn = new System.Windows.Controls.Button
            {
                Style = (Style)FindResource("GlassPillBtn"),
                Background = _settings.Language == lang
                    ? new SolidColorBrush(Color.FromArgb(0x55, 0x34, 0xF5, 0xC5))
                    : Brushes.Transparent,
                Content = LanguageChoiceLabel(lang),
                HorizontalContentAlignment = System.Windows.HorizontalAlignment.Left,
                Margin = new Thickness(0, 2, 0, 2)
            };
            if (_settings.Language == lang) btn.SetResourceReference(Control.BackgroundProperty, "ThemeSelectionBrush");
            btn.Click += async (_, _) =>
            {
                _settings.Language = lang;
                LangListPopup.Visibility = Visibility.Collapsed;
                ApplyLocalization();
                BuildThemesGrid();
                BuildCharactersGrid();
                RebuildTrayMenu();
                SaveSettings();
                if (!string.IsNullOrWhiteSpace(_settings.WeatherCity))
                    await FetchWeatherForCityAsync(_settings.WeatherCity, false);
            };
            LangItemsStack.Children.Add(btn);
        }
    }

    private void LangDropdownBtn_Click(object sender, RoutedEventArgs e)
    {
        LangListPopup.Visibility = LangListPopup.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ApplyLocalization()
    {
        HoverSettingsText.Text   = Tr("Settings", "Настройки", "Налаштування", "Optionen", "Ajustes");
        LblSettingsTitle.Text    = Tr("Settings", "Настройки", "Налаштування", "Einstellungen", "Ajustes");
        BtnBackToHomeText.Text   = Tr("Home", "Главная", "Головна", "Start", "Inicio");

        if (string.IsNullOrEmpty(_currentMediaTitle))
        {
            HoverTrackTitle.Text  = "OC - Dynamic Island";
            HoverTrackArtist.Text = Tr("Click to open hub", "Нажмите для открытия", "Натисніть для відкриття", "Klicken zum Öffnen", "Clic para abrir");
            MediaTitleText.Text   = Tr("No active media", "Нет активного медиа", "Немає активного медіа", "Keine aktiven Medien", "Sin multimedia");
            MediaArtistText.Text  = Tr("Spotify · YouTube · Browser", "Spotify · Яндекс Музыка · Браузер", "Spotify · YouTube · Браузер", "Spotify · YouTube · Browser", "Spotify · YouTube · Navegador");
        }

        if (LblClipboardDashTitle != null)
            LblClipboardDashTitle.Text = Tr("Clipboard · Last 24h", "Буфер обмена · за 24 ч", "Буфер обміну · за 24 год", "Zwischenablage · 24 Std.", "Portapapeles · 24 h");
        if (BtnClearClipboardHistory != null)
            BtnClearClipboardHistory.Content = Tr("Clear all", "Очистить всё", "Очистити все", "Alles löschen", "Borrar todo");
        if (ClipboardEmptyStateText != null)
            ClipboardEmptyStateText.Text = Tr("No copied text in the last 24 hours", "История за последние 24 часа пуста", "Історія за останні 24 години порожня", "Keine Einträge in den letzten 24 Stunden", "Sin textos en las últimas 24 horas");
        RenderClipboardHistoryUi();


        TxtQuickRegion.Text    = Tr("Area", "Область", "Область", "Bereich", "Área");
        TxtQuickFull.Text      = Tr("Fullscreen", "Весь экран", "Весь екран", "Vollbild", "Pantalla");
        TxtQuickTranslate.Text = Tr("Translate", "Перевод", "Переклад", "Übersetzen", "Traducir");
        TxtQuickRecord.Text    = Tr("Record", "Запись экрана", "Запис екрана", "Aufnahme", "Grabar");
        TxtQuickCommand.Text   = Tr("Command", "Команда", "Команда", "Befehl", "Comando");

        LblDashCityWord.Text     = Tr("City", "Город", "Місто", "Stadt", "Ciudad");
        Tile2WindCap.Text        = Tr("WIND", "ВЕТЕР", "ВІТЕР", "WIND", "VIENTO");
        Tile3HumCap.Text         = Tr("HUMIDITY", "ВЛАЖНОСТЬ", "ВОЛОГІСТЬ", "FEUCHTIGKEIT", "HUMEDAD");
        Tile4PressCap.Text       = Tr("PRESSURE", "ДАВЛЕНИЕ", "ТИСК", "DRUCK", "PRESIÓN");
        Tile5SunCap.Text         = Tr("SUN", "СОЛНЦЕ", "СОНЦЕ", "SONNE", "SOL");
        Tile6UvCap.Text          = Tr("UV INDEX", "УФ-ИНДЕКС", "УФ-ІНДЕКС", "UV-INDEX", "ÍNDICE UV");
        Tile7PrecipCap.Text      = Tr("PRECIPITATION", "ОСАДКИ", "ОПАДИ", "NIEDERSCHLAG", "LLUVIA");
        Tile8HourlyCap.Text      = Tr("HOURLY", "ПО ЧАСАМ", "ПО ГОДИНАХ", "STÜNDLICH", "POR HORAS");

        NavAppearanceText.Text = Tr("Appearance", "Внешний вид", "Зовнішній вигляд", "Aussehen", "Apariencia");
        NavCharacterText.Text  = Tr("Character", "Персонаж", "Персонаж", "Charakter", "Personaje");
        NavHomeText.Text       = Tr("Home", "Главная", "Головна", "Startseite", "Inicio");
        NavWeatherText.Text    = Tr("Weather", "Погода", "Погода", "Wetter", "Clima");
        NavBehaviorText.Text   = Tr("Behavior", "Поведение", "Поведінка", "Verhalten", "Conducta");
        NavActionsText.Text    = Tr("Actions", "Действия", "Дії", "Aktionen", "Acciones");
        NavAiText.Text         = Tr("AI Agents", "ИИ-агенты", "ШІ-агенти", "KI-Agenten", "Agentes IA");
        BtnExitApp.Content     = Tr("Exit", "Выйти", "Вийти", "Beenden", "Salir");

        LblThemesCap.Text      = Tr("THEMES", "ТЕМЫ", "ТЕМИ", "THEMEN", "TEMAS");
        BtnPickPhoto.Content   = Tr("Custom photo...", "Своё фото...", "Власне фото...", "Eigenes Foto...", "Foto propia...");
        LblLiquidGlass.Text    = Tr("Liquid Glass Effect", "Эффект жидкого стекла", "Ефект рідкого скла", "Flüssigglas-Effekt", "Efecto cristal líquido");
        LblLiquidGlassSub.Text = Tr("Specular highlights & translucent refraction", "Прозрачные поверхности, живые блики и стеклянные грани", "Дзеркальні відблиски та заломлення", "Glanzlichter & Glasbrechung", "Reflejos especulares de cristal");
        LblScaleTitle.Text     = Tr("Compact Island Scale", "Размер свёрнутого островка", "Розмір згорнутого острівця", "Größe der kompakten Insel", "Tamaño de isla compacta");
        LblScaleSub.Text       = Tr("Home, Game HUD & Settings always stay at 90%", "Главное меню, Игровой режим и Настройки всегда 90%", "Головне меню, Ігровий режим і Налаштування завжди 90%", "Start, Spiel-HUD & Optionen bleiben immer bei 90%", "Inicio, Modo Juego y Ajustes siempre al 90%");

        ChkHomeQuickActions.Content = Tr("Quick Actions", "Быстрые действия", "Швидкі дії", "Schnellaktionen", "Acciones rápidas");
        ChkHomeMusic.Content        = Tr("Music", "Музыка", "Музика", "Musik", "Música");
        ChkHomeWeather.Content      = Tr("Weather", "Погода", "Погода", "Wetter", "Clima");
        ChkHomeApps.Content         = Tr("Clipboard", "Буфер обмена", "Буфер обміну", "Zwischenablage", "Portapapeles");
        LblActionButtonsCap.Text    = Tr("Action Buttons", "Кнопки действий", "Кнопки дій", "Aktions-Buttons", "Botones de acción");

        ChkActRegion.Content    = Tr("Area", "Область", "Область", "Bereich", "Área");
        ChkActFull.Content      = Tr("Fullscreen", "Весь экран", "Весь екран", "Vollbild", "Pantalla");
        ChkActTranslate.Content = Tr("Translate", "Перевод", "Переклад", "Übersetzen", "Traducir");
        ChkActRecord.Content    = Tr("Screen Record", "Запись экрана", "Запис екрана", "Aufnahme", "Grabar pantalla");
        ChkActCommand.Content   = Tr("Command", "Команда", "Команда", "Befehl", "Comando");
        ChkActDnd.Content       = Tr("Do Not Disturb", "Не беспокоить", "Не турбувати", "Nicht stören", "No molestar");

        LblWeatherCityHeader.Text    = Tr("Your City / Location", "Ваш город / Населённый пункт", "Ваше місто / Локація", "Ihre Stadt / Standort", "Tu ciudad / Ubicación");
        LblWeatherCitySub.Text       = Tr("Enter any city name in Russian or English for live detailed weather",
                                          "Укажите свой город на русском или английском для подробной погоды",
                                          "Вкажіть своє місто для детального прогнозу погоди",
                                          "Stadt eingeben für detaillierte Live-Wetterdaten",
                                          "Ingresa tu ciudad para ver el clima detallado");
        BtnSearchWeatherCity.Content = Tr("Search & Apply", "Найти и применить", "Знайти і застосувати", "Suchen", "Buscar");
        LblUseFahrenheit.Text        = Tr("Use Fahrenheit (°F) instead of Celsius (°C)", "Использовать Фаренгейты (°F) вместо Цельсия (°C)", "Використовувати Фаренгейти (°F) замість Цельсія (°C)", "Fahrenheit (°F) statt Celsius (°C) verwenden", "Usar Fahrenheit (°F) en lugar de Celsius (°C)");

        LblLangTitle.Text          = Tr("Interface Language", "Язык интерфейса", "Мова інтерфейсу", "Sprache", "Idioma de interfaz");
        LblLangSub.Text            = Tr("Defaults to the Windows display language", "По умолчанию используется язык Windows", "Типово використовується мова Windows", "Standard: Windows-Anzeigesprache", "Por defecto se usa el idioma de Windows");
        LblExpandHover.Text        = Tr("Expand on hover", "Раскрывать при наведении", "Розкривати при наведенні", "Beim Hover öffnen", "Expandir al pasar el ratón");
        LblCloseOnLeave.Text       = Tr("Close settings when mouse leaves", "Закрывать настройки при отводе мыши", "Закривати налаштування при відведенні миші", "Schließen wenn Maus verlässt", "Cerrar al apartar el ratón");
        LblCloseOnLeaveSub.Text    = Tr("Automatically collapse back to compact pill when cursor moves away", "Автоматически сворачивать остров, когда курсор уходит в сторону", "Автоматично згортати острів при відведенні курсора", "Automatisch einklappen beim Wegbewegen", "Contraer al alejar el cursor");
        LblPinPos.Text             = Tr("Pin in place (disable dragging)", "Закрепить на месте (не перетаскивать)", "Закріпити на місці (не перетягувати)", "Position fixieren", "Fijar posición");
        LblPinPosSub.Text          = Tr("Turn off to drag the island freely along the top edge", "Выключите, чтобы свободно перетаскивать остров по верху", "Вимкніть, щоб вільно перетягувати острів", "Ausschalten zum Verschieben", "Desactivar para mover libremente");
        LblCaps.Text               = Tr("Show Caps Lock", "Показывать Caps Lock", "Показувати Caps Lock", "Caps Lock anzeigen", "Mostrar Caps Lock");
        LblNumLock.Text            = Tr("Show Num Lock", "Показывать Num Lock", "Показувати Num Lock", "Num Lock anzeigen", "Mostrar Num Lock");
        LblLayout.Text             = Tr("Show keyboard layout switch", "Показывать смену раскладки", "Показувати зміну розкладки", "Tastaturlayout anzeigen", "Mostrar cambio de idioma");
        LblDiscordRight.Text       = Tr("Discord Call — separate island on the right", "Звонок Discord — отдельным островком справа", "Дзвінок Discord — окремим острівцем праворуч", "Discord-Anruf — separate Insel rechts", "Llamada de Discord — isla separada a la derecha");
        LblDiscordRightSub.Text    = Tr("Stays visible on the right even during Game Mode", "Отображается отдельным островком справа даже во время игрового режима", "Відображається праворуч навіть у ігровому режимі", "Bleibt auch im Spielmodus rechts sichtbar", "Visible a la derecha incluso en Modo Juego");
        LocalizeDiscordCallControls();
        LblGameModeHud.Text        = Tr("Game Mode (Show FPS, CPU, °C, GPU)", "Игровой режим (Показ FPS, CPU, °C, GPU)", "Ігровий режим (Показ FPS, CPU, °C, GPU)", "Spielmodus (FPS, CPU, °C, GPU anzeigen)", "Modo Juego (Mostrar FPS, CPU, °C, GPU)");
        LblGameModeHudSub.Text     = Tr("Enables Do Not Disturb and mutes notifications (Discord call pill on the right stays active)", "Во время игрового режима включается «Не беспокоить» и уведомления не приходят (кроме звонка Discord справа)", "Під час ігрового режиму вмикається «Не турбувати» (крім дзвінка Discord)", "Aktiviert Nicht stören im Spiel (außer Discord rechts)", "Activa No molestar en juegos (excepto Discord)");
        LblMessengerNotifs.Text    = Tr("Telegram, exDesktop, Discord, Viber…", "Сообщения Telegram, exDesktop, Discord, Viber…", "Повідомлення Telegram, exDesktop, Discord, Viber…", "Nachrichten von Telegram, Discord, Viber... auf der Insel", "Mensajes de Telegram, Discord, Viber... en la isla");
        LblMessengerNotifsSub.Text = Tr("Redirects incoming messenger notifications directly to the island", "Перехватывает уведомления мессенджеров и выводит их прямо в островок", "Перехоплює сповіщення месенджерів і виводить в острівець", "Leitet Messenger-Benachrichtigungen auf die Insel", "Redirige notificaciones de mensajería a la isla");
        LblAllAppNotifs.Text       = Tr("Notifications from all apps, not just messengers", "Уведомления всех приложений, не только мессенджеров", "Сповіщення всіх програм, не тільки месенджерів", "Benachrichtigungen aller Apps, nicht nur Messenger", "Notificaciones de todas las aplicaciones");
        LblAllAppNotifsSub.Text    = Tr("In Telegram Desktop enable Settings -> Notifications -> System notifications", "В Telegram Desktop включите «Настройки → Уведомления → Системные уведомления»", "У Telegram увімкніть «Системні сповіщення»", "In Telegram Systembenachrichtigungen aktivieren", "En Telegram activa las notificaciones del sistema");
        UpdateNotificationStatus();
        LblNotificationDuration.Text = Tr("Notification duration", "Время показа уведомления", "Час показу сповіщення", "Anzeigedauer", "Duración de notificaciones");
        UpdateNotificationDurationLabel();
        LblShowDnd.Text            = Tr("Show «Do Not Disturb» (Windows sync)", "Показывать «Не беспокоить» (синхронизация с Windows)", "Показувати «Не турбувати» (синхронізація з Windows)", "«Nicht stören» anzeigen (Windows-Sync)", "Mostrar «No molestar» (sincronizado con Windows)");
        LblShowDndSub.Text         = Tr("Two-way sync with Windows Do Not Disturb / Focus Assist", "Двусторонняя синхронизация с системным режимом «Не беспокоить» в Windows", "Двостороння синхронізація з режимом «Не турбувати» у Windows", "Zwei-Wege-Synchronisation mit Windows Nicht stören", "Sincronización bidireccional con No molestar de Windows");
        if (LblDndActiveNow != null)
            LblDndActiveNow.Text   = Tr("Do Not Disturb is currently active", "Режим «Не беспокоить» сейчас активен", "Режим «Не турбувати» зараз активний", "«Nicht stören» ist derzeit aktiv", "El modo «No molestar» está activo");
        LblClipboardWatch.Text     = Tr("Show copied text (excludes passwords)", "Показывать скопированный текст (кроме паролей)", "Показувати скопійований текст (крім паролів)", "Kopierten Text zeigen (ohne Passwörter)", "Mostrar texto copiado (sin contraseñas)");
        LblClipboardSub.Text       = Tr("Automatically ignores passwords, tokens & API keys", "Автоматически игнорирует пароли, токены и ключи", "Автоматично ігнорує паролі та токени", "Ignoriert automatisch Passwörter & Token", "Ignora contraseñas y claves API");
        LblCapture.Text            = Tr("Hide island from screenshots & screen share", "Скрывать остров на скриншотах и демонстрации экрана", "Приховувати острів на скріншотах і записі", "Auf Screenshots ausblenden", "Ocultar en capturas");
        LblCaptureSub.Text         = Tr("Uses native Windows WDA_EXCLUDEFROMCAPTURE", "Невидим в OBS, Discord, Zoom и на скриншотах", "Невидимий в OBS, Discord, Zoom і на скріншотах", "Unsichtbar in OBS, Discord & Zoom", "Invisible en OBS, Discord y Zoom");
        LblFullscreen.Text         = Tr("Hide completely in fullscreen games (if Game HUD is off)", "Прятать в полноэкранных играх (если выключен игровой HUD)", "Ховати в іграх (якщо вимкнено HUD)", "In Vollbildspielen ausblenden", "Ocultar en pantalla completa");
        LblHotkeyTitle.Text        = Tr("Open island shortcut", "Открывать остров клавишами", "Відкривати острів клавішами", "Tastenkombination", "Atajo de teclado");
        LblHotkeySub.Text          = Tr("Click button and press any key combination", "Нажмите на кнопку и зажмите любую комбинацию клавиш", "Натисніть кнопку та затисніть комбінацію клавіш", "Klicken und beliebige Tasten drücken", "Haz clic y pulsa cualquier combinación");
        LblStartup.Text            = Tr("Run with Windows", "Запускать вместе с Windows", "Запускати разом з Windows", "Mit Windows starten", "Iniciar con Windows");
        LblCleanDesktop.Text       = Tr("Clean desktop: hide desktop icons", "Чистый рабочий стол: прятать значки", "Чистий робочий стіл: ховати значки", "Desktop-Symbole ausblenden", "Ocultar iconos del escritorio");
        BtnCenterIsland.Content    = Tr("＝  Align to center", "＝  Выровнять по центру", "＝  Вирівняти по центру", "＝  Zentrieren", "＝  Centrar");
        BtnResetAll.Content        = Tr("Reset settings", "Сбросить настройки", "Скинути налаштування", "Zurücksetzen", "Restablecer");

        LblCustomCmdTitle.Text  = Tr("Command for «Command» button", "Команда для кнопки «Команда»", "Команда для кнопки «Команда»", "Befehl für «Befehl»-Button", "Comando personalizado");
        LblTranslateTarget.Text = Tr("Translate target language", "Переводить на язык", "Перекладати на мову", "Zielsprache", "Traducir al idioma");
        LblHotkeysHint.Text     = Tr("Ctrl+Alt+S — Snip area · Ctrl+Alt+R — Screen record · Ctrl+Alt+Space — Run command",
                                     "Ctrl+Alt+S — скриншот области · Ctrl+Alt+R — запись · Ctrl+Alt+Space — командная строка",
                                     "Ctrl+Alt+S — скріншот області · Ctrl+Alt+R — запис · Ctrl+Alt+Space — командний рядок",
                                     "Ctrl+Alt+S — Bereich · Ctrl+Alt+R — Aufnahme · Ctrl+Alt+Space — Befehl",
                                     "Ctrl+Alt+S — Captura · Ctrl+Alt+R — Grabar · Ctrl+Alt+Space — Comando");
        LblAiHeader.Text = Tr("AI agents", "ИИ-агенты", "ШІ-агенти", "KI-Agenten", "Agentes IA");
        RefreshAiAgentsPage();

        ApplyAdditionalLocalization();
        UpdateDndVisuals();
        RenderWeatherUi();
    }

    #endregion

    #region Рекордер комбинации клавиш, Темы, Персонажи, События мыши

    private void HotkeyRecordBtn_Click(object sender, RoutedEventArgs e)
    {
        _isRecordingHotkey = true;
        HotkeyDisplayText.Text = Tr("Press keys...", "Нажмите клавиши...", "Натисніть клавіші...", "Tasten drücken...", "Pulsa teclas...");
        HotkeyRecordBtn.Focus();
    }

    private void HotkeyRecordBtn_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (!_isRecordingHotkey) return;
        e.Handled = true;

        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
            return;

        uint mods = 0;
        var parts = new List<string>();
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) { mods |= MOD_CONTROL; parts.Add("Ctrl"); }
        if ((Keyboard.Modifiers & ModifierKeys.Alt) != 0)     { mods |= MOD_ALT;     parts.Add("Alt"); }
        if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0)   { mods |= MOD_SHIFT;   parts.Add("Shift"); }
        if ((Keyboard.Modifiers & ModifierKeys.Windows) != 0) { mods |= MOD_WIN;     parts.Add("Win"); }

        if (mods == 0)
        {
            mods = MOD_CONTROL | MOD_ALT;
            parts.Add("Ctrl");
            parts.Add("Alt");
        }

        uint vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        parts.Add(key.ToString());

        _hotkeyModifiers = mods;
        _hotkeyVk = vk;
        string label = string.Join(" + ", parts);
        HotkeyDisplayText.Text = label;
        _isRecordingHotkey = false;

        _settings.HotkeyModifiers = mods;
        _settings.HotkeyVk = vk;
        _settings.HotkeyLabel = label;

        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        RegisterCustomToggleHotkey(hwnd);
        SaveSettings();
        ShowSideBubble(label, ToastCategory.Info, bypassDnd: true);
    }

    private void RegisterCustomToggleHotkey(IntPtr hwnd)
    {
        UnregisterHotKey(hwnd, HOTKEY_CUSTOM_TOGGLE);
        RegisterHotKey(hwnd, HOTKEY_CUSTOM_TOGGLE, _hotkeyModifiers, _hotkeyVk);
    }

    private void SwitchSettingsPage(UIElement targetPage, System.Windows.Controls.Button activeNavBtn)
    {
        PageAppearance.Visibility = Visibility.Collapsed;
        PageCharacter.Visibility  = Visibility.Collapsed;
        PageHome.Visibility       = Visibility.Collapsed;
        PageWeather.Visibility    = Visibility.Collapsed;
        PageBehavior.Visibility   = Visibility.Collapsed;
        PageActions.Visibility    = Visibility.Collapsed;
        PageAi.Visibility         = Visibility.Collapsed;
        SettingsFeatureScroll.Visibility = Visibility.Collapsed;
        if (targetPage != SettingsFeatureScroll) _settingsFeaturePage = null;

        targetPage.Visibility = Visibility.Visible;
        FadeInElement(targetPage);

        NavAppearanceBtn.Background = Brushes.Transparent;
        NavCharacterBtn.Background  = Brushes.Transparent;
        NavHomeBtn.Background       = Brushes.Transparent;
        NavWeatherBtn.Background    = Brushes.Transparent;
        NavBehaviorBtn.Background   = Brushes.Transparent;
        NavActionsBtn.Background    = Brushes.Transparent;
        NavAiBtn.Background         = Brushes.Transparent;
        if (_activeSettingsNav != null) _activeSettingsNav.Background = Brushes.Transparent;

        _activeSettingsNav = activeNavBtn;
        activeNavBtn.SetResourceReference(Control.BackgroundProperty, "ThemeSelectionBrush");
    }

    private void NavAppearance_Click(object sender, RoutedEventArgs e) => SwitchSettingsPage(PageAppearance, NavAppearanceBtn);
    private void NavCharacter_Click(object sender, RoutedEventArgs e)  => SwitchSettingsPage(PageCharacter, NavCharacterBtn);
    private void NavHome_Click(object sender, RoutedEventArgs e)       => SwitchSettingsPage(PageHome, NavHomeBtn);
    private void NavWeather_Click(object sender, RoutedEventArgs e)    => SwitchSettingsPage(PageWeather, NavWeatherBtn);
    private void NavBehavior_Click(object sender, RoutedEventArgs e)   => SwitchSettingsPage(PageBehavior, NavBehaviorBtn);
    private void NavActions_Click(object sender, RoutedEventArgs e)    => SwitchSettingsPage(PageActions, NavActionsBtn);
    private void NavAi_Click(object sender, RoutedEventArgs e)
    {
        SwitchSettingsPage(PageAi, NavAiBtn);
        RefreshAiAgentsPage();
    }

    private void BuildThemesGrid()
    {
        ThemesUniformGrid.Children.Clear();
        foreach (var theme in _themes)
        {
            bool selected = _settings.SelectedTheme == theme.Key;
            var stack = new StackPanel { Margin = new Thickness(0, 0, 10, 10), Cursor = System.Windows.Input.Cursors.Hand };

            var cardGrad = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0), EndPoint = new Point(1, 1),
                GradientStops = [new GradientStop((Color)ColorConverter.ConvertFromString(theme.C1), 0), new GradientStop((Color)ColorConverter.ConvertFromString(theme.C2), 1)]
            };

            var preview = new Border
            {
                Height = 46, CornerRadius = new CornerRadius(12), Background = cardGrad,
                BorderBrush = (Brush)FindResource(selected ? "ThemeAccentBrush" : "ThemeBorderBrush"),
                BorderThickness = new Thickness(selected ? 2 : 1)
            };

            stack.Children.Add(preview);
            var themeLabel = new TextBlock { Text = GetLocalizedThemeName(theme.Key), FontSize = 11.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(2, 4, 0, 0) };
            themeLabel.SetResourceReference(TextBlock.ForegroundProperty, "ThemeTextBrush");
            stack.Children.Add(themeLabel);

            stack.MouseLeftButtonUp += (_, _) =>
            {
                _settings.SelectedTheme = theme.Key;
                CustomBgBrush.ImageSource = null;
                _settings.CustomPhotoPath = "";
                ApplyTheme(theme);
                BuildThemesGrid();
                SaveSettings();
            };

            var custom = _settings.CustomThemes.FirstOrDefault(t => theme.Key == "User: " + t.Name);
            if (custom != null) stack.ContextMenu = CreateThemeCopyMenu(custom);
            ThemesUniformGrid.Children.Add(stack);
        }
    }

    private void ApplyTheme(ThemePreset theme)
    {
        ApplyThemeResources(theme);
        Color c1 = (Color)ColorConverter.ConvertFromString(theme.C1);
        Color c2 = (Color)ColorConverter.ConvertFromString(theme.C2);
        c1.A = _settings.LiquidGlass ? (byte)178 : (byte)255;
        c2.A = _settings.LiquidGlass ? (byte)156 : (byte)255;
        IslandBorder.Background.Opacity = BackgroundOpacity;
        var dur = TimeSpan.FromMilliseconds(240);
        GradStop1.BeginAnimation(GradientStop.ColorProperty, new ColorAnimation(c1, dur));
        GradStop2.BeginAnimation(GradientStop.ColorProperty, new ColorAnimation(c2, dur));
        GradStop3.BeginAnimation(GradientStop.ColorProperty, new ColorAnimation(c1, dur));
        SideToastBorder.SetResourceReference(Border.BackgroundProperty, "ThemeCapsuleBrush");
        DiscordCallBorder.SetResourceReference(Border.BackgroundProperty, "ThemeCapsuleBrush");
    }

    private void BuildCharactersGrid()
    {
        CharactersUniformGrid.Children.Clear();
        foreach (string chKey in _characterKeys)
        {
            bool isSelected = _settings.SelectedCharacter == chKey;
            var card = new Border
            {
                Background = (Brush)FindResource(isSelected ? "ThemeSelectionBrush" : "ThemeSurfaceBrush"),
                BorderBrush = (Brush)FindResource(isSelected ? "ThemeAccentBrush" : "ThemeBorderBrush"),
                BorderThickness = new Thickness(isSelected ? 2 : 1),
                CornerRadius = new CornerRadius(15),
                Margin = new Thickness(4),
                Padding = new Thickness(6, 9, 6, 7),
                Cursor = System.Windows.Input.Cursors.Hand
            };

            var stack = new StackPanel { HorizontalAlignment = System.Windows.HorizontalAlignment.Center };
            stack.Children.Add(CreateCharacterVector(chKey, 34));
            card.SetResourceReference(Border.BackgroundProperty, isSelected ? "ThemeSelectionBrush" : "ThemeSurfaceBrush");
            card.SetResourceReference(Border.BorderBrushProperty, isSelected ? "ThemeAccentBrush" : "ThemeBorderBrush");
            var characterLabel = new TextBlock { Text = GetLocalizedCharacterName(chKey), FontSize = 11.5, FontWeight = FontWeights.SemiBold, HorizontalAlignment = System.Windows.HorizontalAlignment.Center, Margin = new Thickness(0, 5, 0, 0) };
            characterLabel.SetResourceReference(TextBlock.ForegroundProperty, "ThemeTextBrush");
            stack.Children.Add(characterLabel);

            card.Child = stack;
            card.MouseLeftButtonUp += (_, _) =>
            {
                _settings.SelectedCharacter = chKey;
                UpdateCharacterVectors(chKey);
                BuildCharactersGrid();
                SaveSettings();
            };

            CharactersUniformGrid.Children.Add(card);
        }
    }

    private void ScaleSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isLoadingSettings) return;
        ScaleValueText.Text = $"{(int)e.NewValue}%";
        _settings.IslandScalePercent = e.NewValue;
        ApplyScaleForCurrentState();
        SaveSettings();
    }

    private void TglLiquidGlass_Click(object sender, RoutedEventArgs e)
    {
        bool enabled = TglLiquidGlass.IsChecked ?? true;
        LiquidGlassOverlay.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        _settings.LiquidGlass = enabled;
        ApplyTheme(_themes.FirstOrDefault(t => t.Key == _settings.SelectedTheme) ?? _themes[0]);
        SaveSettings();
    }

    private void TglSnowfall_Click(object sender, RoutedEventArgs e)
    {
        _settings.Snowfall = TglSnowfall.IsChecked == true;
        IslandSnowfall.Visibility = _settings.Snowfall ? Visibility.Visible : Visibility.Collapsed;
        SaveSettings();
    }

    private void IslandBorder_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        _mouseLeaveTimer.Stop();
        StartIslandFrames();
        if (!_isClickCandidate && !_bodyDragging && _currentState == IslandState.Compact && !_isGameModeActive && !_manualResize && (TglExpandOnHover.IsChecked ?? true))
        { _hoverDelay.Interval = TimeSpan.FromMilliseconds(Math.Clamp(_settings.HoverDelayMs, 0, 2000) + 1); _hoverDelay.Start(); }
    }

    private void IslandBorder_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        _hoverDelay.Stop();
        _mouseLeaveTimer.Stop();
        StartIslandFrames();
        if (!_isClickCandidate && !_bodyDragging) _mouseLeaveTimer.Start();
    }

    private void IslandBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_manualResize && !IsInteractiveIslandChild(e.OriginalSource as DependencyObject) && _currentState is IslandState.Compact or IslandState.Hover)
        {
            BeginBodyGesture(PointToScreen(e.GetPosition(this)));
            Mouse.Capture(IslandBorder, CaptureMode.SubTree);
            e.Handled = true;
        }
    }

    private void IslandBorder_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        UpdateGlassReflection(e);
        if ((!_isClickCandidate && !_bodyDragging) || e.LeftButton != MouseButtonState.Pressed)
            return;

        Point currentPos = PointToScreen(e.GetPosition(this));
        if (!_bodyDragging && Math.Abs(currentPos.X - _dragStartPoint.X) <= 8 && Math.Abs(currentPos.Y - _dragStartPoint.Y) <= 8)
            return;

        if (!CanDragIsland) { _isClickCandidate = false; return; }

        if (_currentState is IslandState.Compact or IslandState.Hover)
        {
            _bodyDragging = true; _isClickCandidate = false;
            double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
            Left = _bodyDragLeft + (currentPos.X - _dragStartPoint.X) / scale;
        }
    }

    private void IslandBorder_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        bool click = _isClickCandidate;
        bool drag = _bodyDragging;
        _isClickCandidate = false; _bodyDragging = false;
        if (IslandBorder.IsMouseCaptured) IslandBorder.ReleaseMouseCapture();
        if (drag) { RememberIslandPlacement(); SaveSettings(); }
        if (click && _currentState is IslandState.Compact or IslandState.Hover)
        {
            OpenDashboard();
            e.Handled = true;
        }
    }

    private double _lastMascotFrame, _bodyDragLeft;
    private bool _bodyDragging;
    private void BeginBodyGesture(Point screenPoint)
    {
        _hoverDelay.Stop(); _mouseLeaveTimer.Stop();
        _dragStartPoint = screenPoint; _bodyDragLeft = Left; _isClickCandidate = true;
    }
    private static bool IsInteractiveIslandChild(DependencyObject? source)
    {
        while (source != null)
        {
            if (source is System.Windows.Controls.Primitives.ButtonBase or System.Windows.Controls.Primitives.TextBoxBase
                or System.Windows.Controls.Primitives.Thumb or Slider or System.Windows.Controls.ComboBox) return true;
            if (source is FrameworkElement element && element.Cursor == System.Windows.Input.Cursors.Hand) return true;
            if (source is Visual || source is System.Windows.Media.Media3D.Visual3D) source = VisualTreeHelper.GetParent(source);
            else source = LogicalTreeHelper.GetParent(source);
        }
        return false;
    }

    private void OpenSettingsBtn_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        MorphToState(IslandState.Settings);
    }

    private void BackToExpanded_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        MorphToState(IslandState.Expanded);
    }

    private void CollapseBtn_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        MorphToState(IslandState.Compact);
    }

    private void CtxHideUp_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        MorphToState(IslandState.Compact);
        IslandBorder.Visibility = Visibility.Collapsed;
        HiddenIslandHandle.Visibility = Visibility.Visible;
        UpdateDiscordCallState();
        StopIslandFrames();
    }

    private void ShowIsland()
    {
        HiddenIslandHandle.Visibility = Visibility.Collapsed;
        IslandBorder.Visibility = Visibility.Visible;
        UpdateDiscordCallState();
        StartIslandFrames();
    }

    private void ToggleIslandDashboard()
    {
        ShowIsland();
        if (_currentState is IslandState.Compact or IslandState.Hover) OpenDashboard();
        else MorphToState(IslandState.Compact);
    }

    private void HiddenIslandHandle_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        ShowIsland();
        if (_settings.RememberDashboardTab) OpenDashboard();
    }

    private async void CtxCheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        OpenSettingsFeaturePage("updates");
        e.Handled = true;
        await CheckForUpdatesAsync(automatic: false);
    }

    #endregion

    #region Системные хуки, Телеметрия, Медиа и Действия

    private void TglExcludeCapture_Click(object sender, RoutedEventArgs e)
    {
        ApplyExcludeFromCapture(TglExcludeCapture.IsChecked ?? true);
        SaveSettings();
    }

    private void ApplyExcludeFromCapture(bool exclude)
    {
        _settings.ExcludeFromCapture = exclude;
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        SetWindowDisplayAffinity(hwnd, exclude ? WDA_EXCLUDEFROMCAPTURE : WDA_NONE);
    }

    private void PollKeyboardFullscreenAndNotifications()
    {
        if (DateTime.UtcNow >= _nextDndRead)
        { _nextDndRead = DateTime.UtcNow.AddMilliseconds(500); PollWindowsDndState(notifyIfChanged: true); }
        PollSystemNotificationsAsync();

        if (TglShowCapsLock.IsChecked ?? true)
        {
            bool caps = Console.CapsLock;
            if (caps != _lastCapsLock)
            {
                _lastCapsLock = caps;
                string msg = caps
                    ? Tr("Caps Lock enabled", "Caps Lock включён", "Caps Lock увімкнено", "Caps Lock aktiviert", "Caps Lock activado")
                    : Tr("Caps Lock disabled", "Caps Lock выключен", "Caps Lock вимкнено", "Caps Lock deaktiviert", "Caps Lock desactivado");
                ShowSideBubble(msg, ToastCategory.CapsLock);
            }
        }

        if (TglShowNumLock.IsChecked ?? true)
        {
            bool num = Console.NumberLock;
            if (num != _lastNumLock)
            {
                _lastNumLock = num;
                string msg = num
                    ? Tr("Num Lock enabled", "Num Lock включён", "Num Lock увімкнено", "Num Lock aktiviert", "Num Lock activado")
                    : Tr("Num Lock disabled", "Num Lock выключен", "Num Lock вимкнено", "Num Lock deaktiviert", "Num Lock desactivado");
                ShowSideBubble(msg, ToastCategory.NumLock);
            }
        }

        if (TglShowLayout.IsChecked ?? true)
        {
            IntPtr layout = GetCurrentKeyboardLayout();
            if (layout != IntPtr.Zero && layout != _lastKeyboardLayout)
            {
                _lastKeyboardLayout = layout;
                int langId = (ushort)layout.ToInt64() & 0xFFFF;
                (string shortCode, string fullName) = langId switch
                {
                    0x0419 => ("RU", Tr("Russian (RU)", "Русский (RU)", "Російська (RU)", "Russisch (RU)", "Ruso (RU)")),
                    0x0409 => ("EN", Tr("English (EN)", "Английский (EN)", "Англійська (EN)", "Englisch (EN)", "Inglés (EN)")),
                    0x0422 => ("UK", Tr("Ukrainian (UK)", "Украинский (UK)", "Українська (UK)", "Ukrainisch (UK)", "Ucraniano (UK)")),
                    0x0407 => ("DE", Tr("German (DE)", "Немецкий (DE)", "Німецька (DE)", "Deutsch (DE)", "Alemán (DE)")),
                    0x040A => ("ES", Tr("Spanish (ES)", "Испанский (ES)", "Іспанська (ES)", "Spanisch (ES)", "Español (ES)")),
                    _      => ("🌐", $"0x{langId:X4}")
                };
                ShowSideBubble(fullName, ToastCategory.Layout, shortCode);
            }
        }

        bool fullscreen = ((TglHideFullscreen.IsChecked ?? false) || _settings.AutoGameHud) && IsForegroundGameOrFullscreen();
        if (!fullscreen)
        {
            GetWindowThreadProcessId(GetForegroundWindow(), out uint pid);
            if (pid != 0 && pid != Environment.ProcessId) _pauseAutoGameHud = false;
        }
        bool gameMode = TglGameModeHud.IsChecked == true || (_settings.AutoGameHud && !_pauseAutoGameHud && fullscreen);
        if (!gameMode && !_settings.AutoGameHud && _gameFps != null) StopGameMetrics();
        ApplyGameModeState(gameMode, collapseToHud: gameMode);
        if ((TglHideFullscreen.IsChecked ?? false) && !_isGameModeActive)
        {
            Visibility = fullscreen ? Visibility.Hidden : Visibility.Visible;
        }
        else if (Visibility != Visibility.Visible)
        {
            Visibility = Visibility.Visible;
        }
    }

    private static IntPtr GetCurrentKeyboardLayout()
    {
        IntPtr fg = GetForegroundWindow();
        if (fg == IntPtr.Zero) return IntPtr.Zero;
        uint tid = GetWindowThreadProcessId(fg, out _);
        return GetKeyboardLayout(tid);
    }

    private void TglHideDesktopIcons_Click(object sender, RoutedEventArgs e)
    {
        SetDesktopIconsHidden(TglHideDesktopIcons.IsChecked == true);
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern bool PostMessageW(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    private bool _desktopIconsChanging;

    private async void SetDesktopIconsHidden(bool hidden)
    {
        if (_desktopIconsChanging) return;
        _desktopIconsChanging = true;
        var view = FindDesktopView();
        bool? current = ReadDesktopIconsHidden(view);
        try
        {
            // Use Explorer's own "Show desktop icons" command so its menu and persistent setting agree.
            if (current is bool before && before != hidden && PostMessageW(view, 0x0111, (IntPtr)0x7402, IntPtr.Zero))
            {
                for (int attempt = 0; attempt < 5 && !_windowClosed; attempt++)
                {
                    await Task.Delay(80);
                    current = ReadDesktopIconsHidden();
                    if (current == hidden) break;
                }
            }
        }
        finally { _desktopIconsChanging = false; }
        if (_windowClosed) return;
        TglHideDesktopIcons.IsChecked = current;
        TglHideDesktopIcons.IsEnabled = current.HasValue;
        if (SystemPageVisible) PaintControlTiles();
    }

    private static bool? ReadDesktopIconsHidden() => ReadDesktopIconsHidden(FindDesktopView());

    private static bool? ReadDesktopIconsHidden(IntPtr view)
    {
        if (view == IntPtr.Zero) return null;
        var icons = FindWindowEx(view, IntPtr.Zero, "SysListView32", null);
        return icons == IntPtr.Zero ? null : (GetWindowLong(icons, -16) & 0x10000000) == 0;
    }

    private static IntPtr FindDesktopView()
    {
        IntPtr progman = FindWindow("Progman", null);
        IntPtr defView = progman == IntPtr.Zero ? IntPtr.Zero : FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
        if (defView == IntPtr.Zero)
        {
            IntPtr workerW = IntPtr.Zero;
            do
            {
                workerW = FindWindowEx(IntPtr.Zero, workerW, "WorkerW", null);
                if (workerW != IntPtr.Zero) defView = FindWindowEx(workerW, IntPtr.Zero, "SHELLDLL_DefView", null);
            } while (workerW != IntPtr.Zero && defView == IntPtr.Zero);
        }
        return defView;
    }

    private async void TglRunOnStartup_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (HasPackageIdentity())
            {
                var task = await Windows.ApplicationModel.StartupTask.GetAsync("DynamicIslandStartup");
                if (TglRunOnStartup.IsChecked ?? false)
                    await task.RequestEnableAsync();
                else task.Disable();
                TglRunOnStartup.IsChecked = task.State is Windows.ApplicationModel.StartupTaskState.Enabled or Windows.ApplicationModel.StartupTaskState.EnabledByPolicy;
                if (!(TglRunOnStartup.IsChecked ?? false) && task.State == Windows.ApplicationModel.StartupTaskState.DisabledByUser)
                    ShowSideBubble(Tr("Enable startup in Windows Settings > Apps > Startup", "Включите автозапуск в Windows: Приложения → Автозагрузка", "Увімкніть автозапуск у Windows", "Autostart in Windows aktivieren", "Activa el inicio automático en Windows"), ToastCategory.Info, bypassDnd: true);
                SaveSettings();
                return;
            }
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
            if (key != null)
            {
                if (TglRunOnStartup.IsChecked ?? false)
                {
                    string exe = Environment.ProcessPath ?? "";
                    if (!string.IsNullOrEmpty(exe)) key.SetValue("DynamicIslandWin", $"\"{exe}\"");
                }
                else key.DeleteValue("DynamicIslandWin", false);
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("Change startup setting", ex);
            TglRunOnStartup.IsChecked = false;
        }
        SaveSettings();
    }

    private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_CLIPBOARDUPDATE)
        {
            OnClipboardChanged();
            handled = true;
        }
        else if (msg == WM_HOTKEY)
        {
            int id = wParam.ToInt32();
            if (id == HOTKEY_CUSTOM_TOGGLE)
            {
                ToggleIslandDashboard();
                handled = true;
            }
            else if (id == HOTKEY_SNIP_S) { ActionSnipRegion_Click(this, new RoutedEventArgs()); handled = true; }
            else if (id == HOTKEY_RECORD_R) { ActionRecord_Click(this, new RoutedEventArgs()); handled = true; }
            else if (id == HOTKEY_CMD_SPACE) { ActionRunCommand_Click(this, new RoutedEventArgs()); handled = true; }
        }
        return IntPtr.Zero;
    }

    private async Task UpdateTelemetryAsync()
    {
        string now = DateTime.Now.ToString("HH:mm");
        UpdateIdleContent();
        ExpandedClockText.Text    = now;
        SettingsClockText.Text    = now;

        if (_isGameModeActive || SystemPageVisible || WidgetIsVisible("cpu") || WidgetIsVisible("system"))
            _lastCpuUsage = GetCpuUsage();
        else
        {
            _lastCpuUsage = -1;
            _prevKernelTime = 0;
        }
        if (_activeSettingsNav == NavBehaviorBtn && _currentState == IslandState.Settings)
        {
            var hidden = ReadDesktopIconsHidden();
            TglHideDesktopIcons.IsChecked = hidden;
            TglHideDesktopIcons.IsEnabled = hidden.HasValue;
        }
        RefreshSystemOverview();

        UpdateDiscordCallState();
        _ = RefreshDiscordCallAsync();

        if (DateTime.UtcNow >= _nextClipboardExpiry)
            PruneExpiredClipboardItems(checkFiles: false);

        if (_mediaManager == null) await InitializeMediaManagerAsync();
        else await RefreshMediaSessionStateAsync();
    }

    private DateTime _nextDndRead;
    private bool _mediaReadBusy;
    private bool _mediaReadAgain;
    private GlobalSystemMediaTransportControlsSession? _observedMediaSession;
    private void MediaSessionsChanged(GlobalSystemMediaTransportControlsSessionManager sender, object args) =>
        Dispatcher.BeginInvoke(new Action(() => _ = RefreshMediaSessionStateAsync()));

    private void SelectedMediaChanged(GlobalSystemMediaTransportControlsSession sender, object args) =>
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (Equals(sender, _observedMediaSession)) _ = RefreshMediaSessionStateAsync();
        }));

    private void ObserveMediaSession(GlobalSystemMediaTransportControlsSession? session)
    {
        if (Equals(_observedMediaSession, session)) return;
        if (_observedMediaSession is { } previous)
        {
            try
            {
                previous.PlaybackInfoChanged -= SelectedMediaChanged;
                previous.MediaPropertiesChanged -= SelectedMediaChanged;
            }
            catch (Exception ex) when (ex is COMException or InvalidOperationException) { AppLog.Error("Unsubscribe media session", ex); }
        }
        _observedMediaSession = session;
        if (session == null) return;
        try
        {
            session.PlaybackInfoChanged += SelectedMediaChanged;
            session.MediaPropertiesChanged += SelectedMediaChanged;
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException) { AppLog.Error("Subscribe media session", ex); }
    }

    private async Task RefreshMediaSessionStateAsync()
    {
        if (_windowClosed || _mediaManager == null) return;
        if (_mediaReadBusy) { _mediaReadAgain = true; return; }
        _mediaReadBusy = true;
        try
        {
            do
            {
                _mediaReadAgain = false;
                await RefreshMediaSessionCoreAsync();
            } while (_mediaReadAgain && !_windowClosed && _mediaManager != null);
        }
        finally { _mediaReadBusy = false; }
    }

    private async Task RefreshMediaSessionCoreAsync()
    {
        bool playerVisible = PlayerControlsVisible;
        MediaSessions.Selection? media = null;
        if (_mediaManager is { } manager)
        {
            try { media = await MediaSessions.ReadAsync(manager, playerVisible || WidgetIsVisible("music")); }
            catch (Exception ex) when (ex is COMException or InvalidOperationException) { }
        }
        if (_windowClosed) return;

        var track = media?.Track;
        bool playing = media?.Playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
        double duration = 0;
        double? position = null;
        PlayerTimeline? playerTimeline = null;
        if (media != null && (playerVisible || (playing && (_settings.Karaoke || _settings.OnlineLyrics))))
        {
            try
            {
                var timeline = media.Session.GetTimelineProperties();
                duration = (timeline.EndTime - timeline.StartTime).TotalSeconds;
                double reported = Math.Max(0, (timeline.Position - timeline.StartTime).TotalSeconds);
                if (timeline.LastUpdatedTime != default)
                {
                    double elapsed = playing ? Math.Max(0, (DateTimeOffset.UtcNow - timeline.LastUpdatedTime).TotalSeconds) : 0;
                    double rate = media.Playback.PlaybackRate ?? 1;
                    if (double.IsFinite(rate)) elapsed *= rate;
                    position = duration > 0 ? Math.Min(duration, reported + elapsed) : elapsed <= 600 ? reported + elapsed : reported;
                }
                if (playerVisible && duration > 0)
                {
                    bool canSeek = media.Playback.Controls?.IsPlaybackPositionEnabled == true && TryGetSeekBounds(timeline, out _, out _);
                    playerTimeline = new(Math.Clamp(position ?? reported, 0, duration), duration, canSeek);
                }
            }
            catch (Exception ex) when (ex is COMException or InvalidOperationException or OverflowException) { }
        }

        _selectedMedia = media;
        ObserveMediaSession(media?.Session);
        bool trackChanged = _widgetTrack != (track?.Title ?? "") || _widgetArtist != (track?.Artist ?? "");
        _widgetTrack = track?.Title ?? "";
        _widgetArtist = track?.Artist ?? "";
        if (trackChanged) RefreshWidget("music");
        if (playing && media != null) UpdateLyricsTrack(media.Track, position, media.Album, duration);
        UpdateHomePlayerTimeline(track, playerTimeline);
        ApplyMediaState(playing, _widgetTrack, _widgetArtist, track?.Source ?? "");
        if (PlayerControlsVisible) RefreshPlayerTransportControls();
        _ = RefreshHomePlayerVolumeAsync(PlayerControlsVisible && track is { Source.Length: > 0 } ? track.Source : null);
    }

    private void ApplyMediaState(bool activePlaying, string trackTitle, string trackArtist, string sourceAppId)
    {
        _hasPlayerMedia = trackTitle.Length > 0;
        UpdatePlayerControlVisibility();
        _isMediaPlaying = activePlaying;
        _currentMediaTitle = activePlaying ? trackTitle : "";
        if (!activePlaying) _lyricsPosition = null;
        RefreshLyricsStatus();
        UpdateCompactContent();

        PlayPauseVectorIcon.Data = _isMediaPlaying
            ? Geometry.Parse("M14,19H18V5H14M6,19H10V5H6V19Z")
            : Geometry.Parse("M8,5.14V19.14L19,12.14L8,5.14Z");
        UpdateMusicPlayPause();

        HomeMusicTrack.Cursor = _hasPlayerMedia ? System.Windows.Input.Cursors.Hand : System.Windows.Input.Cursors.Arrow;

        if (string.IsNullOrWhiteSpace(trackArtist)) trackArtist = MediaSources.BrowserName(sourceAppId) ?? "";

        if (_isMediaPlaying)
        {
            if (string.IsNullOrWhiteSpace(trackArtist))
                trackArtist = Tr("Playing now", "Сейчас играет", "Зараз грає", "Läuft gerade", "Reproduciendo");
            HoverTrackTitle.Text  = trackTitle;
            HoverTrackArtist.Text = trackArtist;
        }
        else
        {
            HoverTrackTitle.Text  = "OC - Dynamic Island";
            HoverTrackArtist.Text = Tr("Click to open hub", "Нажмите для открытия", "Натисніть для відкриття", "Klicken zum Öffnen", "Clic para abrir");
        }
        MediaTitleText.Text = trackTitle.Length > 0 ? trackTitle
            : Tr("No active media", "Нет активного медиа", "Немає активного медіа", "Keine aktiven Medien", "Sin multimedia");
        MediaArtistText.Text = trackTitle.Length > 0 ? trackArtist
            : Tr("Spotify · YouTube · Browser", "Spotify · Яндекс Музыка · Браузер", "Spotify · YouTube · Браузер", "Spotify · YouTube · Browser", "Spotify · YouTube · Navegador");
    }

    private int GetCpuUsage()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user)) return -1;
        bool hasPreviousReading = _prevKernelTime != 0;
        ulong idleDiff = idle.ToUInt64() - _prevIdleTime;
        ulong totalDiff = (kernel.ToUInt64() - _prevKernelTime) + (user.ToUInt64() - _prevUserTime);
        _prevIdleTime = idle.ToUInt64();
        _prevKernelTime = kernel.ToUInt64();
        _prevUserTime = user.ToUInt64();
        if (!hasPreviousReading || totalDiff == 0 || idleDiff > totalDiff) return -1;
        return Math.Clamp((int)(100.0 * (totalDiff - idleDiff) / totalDiff), 0, 100);
    }

    private void HomeMusicWidget_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_playerTrack is not { Source.Length: > 0 } track) return;
        e.Handled = true;

        IntPtr playerHwnd = FindActiveMediaPlayerWindow(track);
        if (playerHwnd != IntPtr.Zero)
        {
            if (IsIconic(playerHwnd))
                ShowWindow(playerHwnd, 9); // SW_RESTORE
            SetForegroundWindow(playerHwnd);
            MorphToState(IslandState.Compact);
        }
    }

    private static IntPtr FindActiveMediaPlayerWindow(LyricsService.Track track)
    {
        IntPtr appWindow = IntPtr.Zero;
        var processes = Process.GetProcesses();
        try
        {
            foreach (var p in processes)
            {
                try
                {
                    IntPtr window = p.MainWindowHandle;
                    if (window == IntPtr.Zero || !PlayerAudioService.MatchesProcess(track.Source, (uint)p.Id)) continue;
                    if (p.MainWindowTitle.Contains(track.Title, StringComparison.OrdinalIgnoreCase)) return window;
                    if (appWindow == IntPtr.Zero) appWindow = window;
                }
                catch (Exception ex) when (ex is InvalidOperationException or Win32Exception) { }
            }
        }
        finally { foreach (var p in processes) p.Dispose(); }
        return appWindow;
    }

    private async void PlayPause_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        await RunPlayerActionAsync(PlayerAction.PlayPause);
    }

    private async void NextTrack_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        await RunPlayerActionAsync(PlayerAction.Next);
    }

    private async void PrevTrack_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        await RunPlayerActionAsync(PlayerAction.Previous);
    }

    private void HomeFilter_Changed(object sender, RoutedEventArgs e)
    {
        ApplyHomeWidgetVisibility();
        SaveSettings();
    }

    private void ApplyHomeWidgetVisibility()
    {
        HomeQuickActionsBar.Visibility = (ChkHomeQuickActions.IsChecked ?? true) ? Visibility.Visible : Visibility.Collapsed;
        HomeMusicWidget.Visibility     = (ChkHomeMusic.IsChecked ?? true) ? Visibility.Visible : Visibility.Collapsed;
        HomeWeatherWidget.Visibility   = (ChkHomeWeather.IsChecked ?? true) ? Visibility.Visible : Visibility.Collapsed;
        HomeClipboardWidget.Visibility = (ChkHomeApps.IsChecked ?? true) ? Visibility.Visible : Visibility.Collapsed;

        BtnQuickRegion.Visibility    = (ChkActRegion.IsChecked ?? true) ? Visibility.Visible : Visibility.Collapsed;
        BtnQuickFull.Visibility      = (ChkActFull.IsChecked ?? true) ? Visibility.Visible : Visibility.Collapsed;
        BtnQuickTranslate.Visibility = (ChkActTranslate.IsChecked ?? true) ? Visibility.Visible : Visibility.Collapsed;
        BtnQuickRecord.Visibility    = (ChkActRecord.IsChecked ?? true) ? Visibility.Visible : Visibility.Collapsed;
        BtnQuickCommand.Visibility   = (ChkActCommand.IsChecked ?? true) ? Visibility.Visible : Visibility.Collapsed;
        BtnQuickDnd.Visibility       = (ChkActDnd.IsChecked ?? true) ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void ActionSnipRegion_Click(object sender, RoutedEventArgs e) => await CaptureScreenshotAsync(false);

    private async void ActionFullScreenShot_Click(object sender, RoutedEventArgs e) => await CaptureScreenshotAsync(true);

    private bool _captureUiBusy;
    private async Task CaptureScreenshotAsync(bool fullScreen)
    {
        if (_captureUiBusy) return;
        _captureUiBusy = true;
        try
        {
            PrepareCaptureUi();
            await Task.Delay(350);
            if (_windowClosed) return;
            if (fullScreen)
            {
                await SaveScreenshotAsync(await ScreenCaptureService.CaptureMonitorAsync(new WindowInteropHelper(this).Handle));
            }
            else
            {
                var bitmap = await RegionCaptureWindow.SelectAsync(new WindowInteropHelper(this).Handle,
                    Tr("Drag to capture an area · Esc to cancel", "Выделите область · Esc — отмена", "Виділіть область · Esc — скасувати", "Bereich markieren · Esc zum Abbrechen", "Selecciona un área · Esc para cancelar"));
                if (bitmap != null) await SaveScreenshotAsync(bitmap);
            }
        }
        catch (OperationCanceledException) when (_windowClosed) { }
        catch (Exception ex)
        {
            AppLog.Error("Screenshot", ex);
            ShowSideBubble(Tr("Could not capture or save a screenshot. Check free space and clipboard access.", "Не удалось сделать или сохранить скриншот. Проверьте место на диске и доступ к буферу обмена.", "Не вдалося зберегти знімок. Перевірте вільне місце та буфер обміну.", "Screenshot fehlgeschlagen. Speicherplatz und Zwischenablage prüfen.", "No se pudo guardar la captura. Comprueba el espacio y el portapapeles."), ToastCategory.Info, bypassDnd: true);
        }
        finally { _captureUiBusy = false; RestoreCaptureUi(); }
    }

    private async Task SaveScreenshotAsync(BitmapSource bitmap)
    {
        string directory = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "OC - Dynamic Island");
        Directory.CreateDirectory(directory);
        string path = System.IO.Path.Combine(directory, "Screenshot-" + DateTime.Now.ToString("yyyy-MM-dd-HHmmss-fff") + ".png");
        await Task.Run(() =>
        {
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = File.Create(path); encoder.Save(file);
        });
        for (int attempt = 0; ; attempt++)
        {
            try { System.Windows.Clipboard.SetImage(bitmap); break; }
            catch (ExternalException) when (attempt < 4) { await Task.Delay(80); }
        }
        ShowSideBubble(L("Скриншот скопирован и сохранён: Изображения / OC - Dynamic Island"), ToastCategory.Info, bypassDnd: true);
    }

    private void PrepareCaptureUi()
    {
        MorphToState(IslandState.Compact);
        IslandBorder.ContextMenu.IsOpen = false;
        if (Mouse.Captured != null) Mouse.Capture(null);
        _glassGeneration++;
        _glassTimer.Stop();
        // Release the native magnifier while Snipping Tool owns capture. Keep
        // the last glass frame visible until the system operation completes.
        _glassCapture?.Dispose(); _glassCapture = null;
        Topmost = false;
        Activate();
    }

    private void RestoreCaptureUi()
    {
        if (_windowClosed) return;
        Topmost = true;
        ConfigureLensGlass();
    }

    private void ActionTranslate_Click(object sender, RoutedEventArgs e)
    {
        string lang = TranslateLangTextBox.Text.Trim();
        string txt = System.Windows.Clipboard.ContainsText() ? Uri.EscapeDataString(System.Windows.Clipboard.GetText()) : "";
        Process.Start(new ProcessStartInfo($"https://translate.google.com/?sl=auto&tl={lang}&text={txt}") { UseShellExecute = true });
    }

    private async void ActionRecord_Click(object sender, RoutedEventArgs e)
    {
        if (!BtnQuickRecord.IsEnabled || _captureUiBusy) return;
        _captureUiBusy = true;
        BtnQuickRecord.IsEnabled = false;
        BtnQuickRecord.ToolTip = Tr("Finish or cancel in Snipping Tool", "Завершите или отмените запись в «Ножницах»", "Завершіть запис у Ножицях", "Aufnahme im Snipping Tool beenden", "Finaliza en Recortes");
        try
        {
            _screenRecording ??= new ScreenRecordingService();
            PrepareCaptureUi();
            await Task.Delay(350);
            string? saved = await _screenRecording.RecordAsync(new WindowInteropHelper(this).Handle);
            if (_windowClosed || saved == null) return;
            ShowSideBubble(Tr("Video saved to Videos / OC - Dynamic Island", "Видео сохранено: Видео / OC - Dynamic Island", "Відео збережено: Відео / OC - Dynamic Island", "Video gespeichert: Videos / OC - Dynamic Island", "Vídeo guardado: Vídeos / OC - Dynamic Island"), ToastCategory.Info, bypassDnd: true);
            var reveal = new ProcessStartInfo("explorer.exe") { UseShellExecute = false };
            reveal.ArgumentList.Add("/select,"); reveal.ArgumentList.Add(saved);
            Process.Start(reveal);
        }
        catch (OperationCanceledException) when (_windowClosed) { }
        catch (Exception ex)
        {
            AppLog.Error("Screen recording", ex);
            if (!_windowClosed)
                ShowSideBubble(ex is PlatformNotSupportedException
                    ? Tr("Recording requires Windows 11 Snipping Tool.", "Для записи нужны «Ножницы» Windows 11.", "Потрібні Ножиці Windows 11.", "Windows 11 Snipping Tool erforderlich.", "Se requiere Recortes de Windows 11.")
                    : Tr("Could not start or save the recording. Check Snipping Tool and free disk space.", "Не удалось начать или сохранить запись. Проверьте «Ножницы» и место на диске.", "Не вдалося записати відео. Перевірте Ножиці та місце на диску.", "Aufnahme fehlgeschlagen. Snipping Tool und Speicherplatz prüfen.", "No se pudo grabar. Comprueba Recortes y el espacio libre."), ToastCategory.Info, bypassDnd: true);
        }
        finally
        {
            _captureUiBusy = false; RestoreCaptureUi();
            if (!_windowClosed) { BtnQuickRecord.IsEnabled = true; BtnQuickRecord.ToolTip = null; }
        }
    }

    private void ActionRunCommand_Click(object sender, RoutedEventArgs e)
    {
        string cmd = CustomCmdTextBox.Text.Trim();
        if (!string.IsNullOrEmpty(cmd))
        {
            try { Process.Start(new ProcessStartInfo(cmd) { UseShellExecute = true }); }
            catch { Process.Start(new ProcessStartInfo("cmd.exe", $"/c {cmd}") { UseShellExecute = true }); }
        }
    }

    private void PickCustomPhoto_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Images|*.png;*.jpg;*.jpeg;*.webp;*.bmp" };
        if (dlg.ShowDialog() == true)
        {
            if (LoadIslandPhoto(dlg.FileName) is not { } photo) return;
            _settings.CustomPhotoPath = dlg.FileName;
            CustomBgBrush.ImageSource = photo;
            SaveSettings();
        }
    }

    private void OpenTelegram_Click(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo("https://t.me/outsidecompany") { UseShellExecute = true });

    private void CenterIsland_Click(object sender, RoutedEventArgs e)
    {
        CenterIslandOnScreen();
        _settings.WindowLeft = null;
        _settings.IslandOffset = 0;
        RememberIslandPlacement();
        SaveSettings();
    }

    private void CustomCmdTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isLoadingSettings) return;
        SaveSettings();
    }

    #endregion

    #region Сохранение и загрузка настроек (settings.json)

    private void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        UpdateDndVisuals();
        SaveSettings();
    }

    private void SaveSettings()
    {
        _settingsSaveTimer?.Stop();
        if (_isLoadingSettings) return;

        try
        {
            _settings.ExpandOnHover          = TglExpandOnHover.IsChecked ?? true;
            _settings.CloseOnMouseLeave      = TglCloseOnLeave.IsChecked ?? true;
            _settings.PinPosition            = TglPinPosition.IsChecked ?? false;
            _settings.ShowCapsLock           = TglShowCapsLock.IsChecked ?? true;
            _settings.ShowNumLock            = TglShowNumLock.IsChecked ?? true;
            _settings.ShowLayout             = TglShowLayout.IsChecked ?? true;
            _settings.DiscordCallRightIsland = TglDiscordCallRight.IsChecked ?? true;
            _settings.GameModeHud            = TglGameModeHud.IsChecked ?? false;
            _settings.MessengerNotifications = TglMessengerNotifs.IsChecked ?? true;
            _settings.AllAppNotifications    = TglAllAppNotifs.IsChecked ?? false;
            _settings.NotificationDurationSeconds = NotificationPolicy.ClampDuration(NotificationDurationSlider.Value);
            _settings.ShowDoNotDisturb       = TglShowDnd.IsChecked ?? true;
            _settings.ClipboardWatch         = TglClipboardWatch.IsChecked ?? true;
            _settings.ExcludeFromCapture     = TglExcludeCapture.IsChecked ?? true;
            _settings.HideInFullscreen       = TglHideFullscreen.IsChecked ?? false;
            _settings.RunOnStartup           = TglRunOnStartup.IsChecked ?? false;

            _settings.LiquidGlass            = TglLiquidGlass.IsChecked ?? true;
            _settings.IslandScalePercent     = ScaleSlider.Value;
            _settings.UseFahrenheit          = TglFahrenheit.IsChecked ?? false;
            _settings.WeatherCity            = WeatherCityInput.Text.Trim();

            _settings.ShowHomeQuickActions   = ChkHomeQuickActions.IsChecked ?? true;
            _settings.ShowHomeMusic          = ChkHomeMusic.IsChecked ?? true;
            _settings.ShowHomeWeather        = ChkHomeWeather.IsChecked ?? true;
            _settings.ShowHomeClipboard      = ChkHomeApps.IsChecked ?? true;
            _settings.ActRegion              = ChkActRegion.IsChecked ?? true;
            _settings.ActFull                = ChkActFull.IsChecked ?? true;
            _settings.ActTranslate           = ChkActTranslate.IsChecked ?? true;
            _settings.ActRecord              = ChkActRecord.IsChecked ?? true;
            _settings.ActCommand             = ChkActCommand.IsChecked ?? true;
            _settings.ActDnd                 = ChkActDnd.IsChecked ?? true;

            _settings.CustomCommand          = CustomCmdTextBox.Text;
            _settings.TranslateTargetLang    = TranslateLangTextBox.Text;

            string? dir = System.IO.Path.GetDirectoryName(_settingsPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            string json = JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_settingsPath, json, Encoding.UTF8);
        }
        catch { }
    }

    private void LoadSettings()
    {
        try
        {
            if (File.Exists(_settingsPath))
            {
                string json = File.ReadAllText(_settingsPath, Encoding.UTF8);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json);
                if (loaded != null)
                    _settings = loaded;
            }
        }
        catch { }
    }

    private void ApplyCurrentSettingsToUi()
    {
        _isLoadingSettings = true;
        LoadFeatureThemes();
        TglVolumeIsland.IsChecked = _settings.VolumeIslandHud ?? _settings.LevelHud;

        WeatherCityInput.Text         = _settings.WeatherCity;
        DashCityTextBox.Text          = _settings.WeatherCity;
        TglFahrenheit.IsChecked       = _settings.UseFahrenheit;

        TglLiquidGlass.IsChecked      = _settings.LiquidGlass;
        LiquidGlassOverlay.Visibility = _settings.LiquidGlass ? Visibility.Visible : Visibility.Collapsed;
        TglSnowfall.IsChecked         = _settings.Snowfall;
        IslandSnowfall.Visibility     = _settings.Snowfall ? Visibility.Visible : Visibility.Collapsed;

        ScaleSlider.Value             = Math.Clamp(_settings.IslandScalePercent, 50, 130);
        ScaleValueText.Text           = $"{(int)ScaleSlider.Value}%";
        _settings.BackgroundTransparency = double.IsFinite(_settings.BackgroundTransparency) ? Math.Clamp(_settings.BackgroundTransparency, 0, 90) : 0;
        TransparencySlider.Value = _settings.BackgroundTransparency;
        TransparencyValueText.Text = $"{_settings.BackgroundTransparency:0}%";

        TglExpandOnHover.IsChecked    = _settings.ExpandOnHover;
        TglCloseOnLeave.IsChecked     = _settings.CloseOnMouseLeave;
        TglRememberDashboardTab.IsChecked = _settings.RememberDashboardTab;
        RefreshIslandPlacementControls();
        ApplyIslandPlacementStyle();
        TglShowCapsLock.IsChecked     = _settings.ShowCapsLock;
        TglShowNumLock.IsChecked      = _settings.ShowNumLock;
        TglShowLayout.IsChecked       = _settings.ShowLayout;
        TglDiscordCallRight.IsChecked = _settings.DiscordCallRightIsland;
        TglGameModeHud.IsChecked      = _settings.GameModeHud;
        TglMessengerNotifs.IsChecked  = _settings.MessengerNotifications;
        TglAllAppNotifs.IsChecked     = _settings.AllAppNotifications;
        _settings.NotificationDurationSeconds = NotificationPolicy.ClampDuration(_settings.NotificationDurationSeconds);
        NotificationDurationSlider.Value = _settings.NotificationDurationSeconds;
        UpdateNotificationDurationLabel();
        TglShowDnd.IsChecked          = _settings.ShowDoNotDisturb;

        // DND is read from Windows and never reset by local settings.
        if (TglDndActiveState != null)
            TglDndActiveState.IsChecked = _isDndActive;

        TglClipboardWatch.IsChecked   = _settings.ClipboardWatch;
        SetExternalProgress(_settings.ExternalProgress);
        TglExcludeCapture.IsChecked   = _settings.ExcludeFromCapture;
        TglHideFullscreen.IsChecked   = _settings.HideInFullscreen;
        TglRunOnStartup.IsChecked     = _settings.RunOnStartup;
        var desktopIconsHidden = ReadDesktopIconsHidden();
        TglHideDesktopIcons.IsChecked = desktopIconsHidden;
        TglHideDesktopIcons.IsEnabled = desktopIconsHidden.HasValue;

        ChkHomeQuickActions.IsChecked = _settings.ShowHomeQuickActions;
        ChkHomeMusic.IsChecked        = _settings.ShowHomeMusic;
        ChkHomeWeather.IsChecked      = _settings.ShowHomeWeather;
        ChkHomeApps.IsChecked         = _settings.ShowHomeClipboard;
        ChkActRegion.IsChecked        = _settings.ActRegion;
        ChkActFull.IsChecked          = _settings.ActFull;
        ChkActTranslate.IsChecked     = _settings.ActTranslate;
        ChkActRecord.IsChecked        = _settings.ActRecord;
        ChkActCommand.IsChecked       = _settings.ActCommand;
        ChkActDnd.IsChecked           = _settings.ActDnd;
        ApplyHomeWidgetVisibility();

        _hotkeyModifiers              = _settings.HotkeyModifiers;
        _hotkeyVk                     = _settings.HotkeyVk;
        HotkeyDisplayText.Text        = _settings.HotkeyLabel;
        CustomCmdTextBox.Text         = _settings.CustomCommand;
        TranslateLangTextBox.Text     = _settings.TranslateTargetLang;
        RefreshAiAgentsPage();

        ApplyLocalization();

        var theme = _themes.FirstOrDefault(t => t.Key == _settings.SelectedTheme) ?? _themes[0];
        ApplyTheme(theme);
        UpdateCharacterVectors(_settings.SelectedCharacter);

        BuildThemesGrid();
        BuildCharactersGrid();

        CustomBgBrush.ImageSource = LoadIslandPhoto(_settings.CustomPhotoPath);

        ApplyExcludeFromCapture(_settings.ExcludeFromCapture);
        UpdateDiscordCallState();
        ApplyGameModeState(_settings.GameModeHud, collapseToHud: false);
        ApplyScaleForCurrentState();

        ApplyFeatureAppearance();
        BuildDashboardNavigation();

        _isLoadingSettings = false;
    }

    private async void ResetSettings_Click(object sender, RoutedEventArgs e)
    {
        _settings = new AppSettings();
        SetOnlineLyrics(false);
        if (File.Exists(_settingsPath)) File.Delete(_settingsPath);
        _hasWeatherData = false;
        ApplyCurrentSettingsToUi();
        RestoreIslandPlacement();
        SaveSettings();
        await FetchWeatherForCityAsync(_settings.WeatherCity, false);
    }

    private void ExitMenu_Click(object sender, RoutedEventArgs e) => ShutdownApplication();

    #endregion
}
