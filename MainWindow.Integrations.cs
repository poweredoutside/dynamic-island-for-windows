using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.IO;
using Cursors = System.Windows.Input.Cursors;
using DynamicIslandWin.Services;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace DynamicIslandWin;

public partial class MainWindow
{
    private sealed record IslandNotification(uint Id, string Message, bool Messenger, string AppId,
        string AppName, Windows.ApplicationModel.AppInfo? AppInfo, DateTimeOffset Created,
        Task<NotificationActivationService.Target?> Activation);
    private readonly record struct NotificationObservation(UserNotification Source, uint Id, DateTimeOffset Created,
        string Text, int Elements, Task<NotificationActivationService.Target?>? Activation = null);

    private static NotificationObservation ReadNotificationObservation(UserNotification notification)
    {
        var visual = notification.Notification?.Visual;
        var binding = visual?.GetBinding(KnownNotificationBindings.ToastGeneric)
            ?? visual?.Bindings.FirstOrDefault(b => b.GetTextElements().Count > 0);
        var elements = binding?.GetTextElements();
        string text = elements == null ? "" : string.Join(" · ", elements.Select(t => t.Text).Where(t => !string.IsNullOrWhiteSpace(t)));
        return new(notification, notification.Id, notification.CreationTime, text, elements?.Count ?? 0);
    }

    private static NotificationObservation PrefetchNotificationActivation(NotificationObservation observation,
        bool wantMessengers, bool wantAllApps)
    {
        if (string.IsNullOrWhiteSpace(observation.Text)) return observation;
        try
        {
            var app = observation.Source.AppInfo;
            string appId = app?.AppUserModelId ?? "";
            if (wantAllApps || (wantMessengers && NotificationPolicy.IsMessenger(app?.DisplayInfo?.DisplayName ?? "", appId)))
                return observation with { Activation = CaptureNotificationActivation(observation, app, appId) };
        }
        catch (Exception ex) { AppLog.Error("Read notification activation source", ex); }
        return observation;
    }

    private static Task<NotificationActivationService.Target?> CaptureNotificationActivation(NotificationObservation observation,
        Windows.ApplicationModel.AppInfo? app, string appId)
        => NotificationActivationService.CaptureAsync(observation.Id, appId, observation.Created,
            NotificationActivationService.Fingerprint(observation.Text), GetNotificationPackageManifest(app));

    private readonly Dictionary<string, ImageSource> _notificationIcons = new();
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFullName(ref uint length, System.Text.StringBuilder? name);

    private static bool HasPackageIdentity()
    {
        uint length = 0;
        return GetCurrentPackageFullName(ref length, null) == 122; // ERROR_INSUFFICIENT_BUFFER
    }

    private bool NotificationsEnabled => (TglMessengerNotifs.IsChecked ?? false) || (TglAllAppNotifs.IsChecked ?? false);

    private async Task InitializeNotificationListenerAsync()
    {
        if (_notificationInitializing || _windowClosed) return;
        if (!NotificationsEnabled) { SyncNotificationBanners(); UpdateNotificationStatus(); return; }
        if (!HasPackageIdentity())
        {
            SetNotificationStatus(Tr("Install the MSIX package to enable notifications.", "Для уведомлений установите приложение через MSIX.", "Установіть пакет MSIX для сповіщень.", "Für Benachrichtigungen das MSIX-Paket installieren.", "Instala el paquete MSIX para recibir notificaciones."));
            return;
        }

        _notificationInitializing = true;
        try
        {
            _notifListener ??= UserNotificationListener.Current;
            var status = _notifListener.GetAccessStatus();
            if (status == UserNotificationListenerAccessStatus.Unspecified)
                status = await _notifListener.RequestAccessAsync(); // UI thread required.
            if (_windowClosed) return;
            _notifAccessGranted = status == UserNotificationListenerAccessStatus.Allowed;
            AppLog.Notification($"initialize access={status} version={typeof(MainWindow).Assembly.GetName().Version}");
            if (_notifAccessGranted && !_notificationEventSubscribed)
            {
                _notifListener.NotificationChanged += NotificationListener_Changed;
                _notificationEventSubscribed = true;
            }
            UpdateNotificationStatus();
            SyncNotificationBanners();
            if (_notifAccessGranted) await ReadNotificationsAsync();
        }
        catch (Exception ex)
        {
            _notifAccessGranted = false;
            AppLog.Error("Initialize notification listener", ex);
            SetNotificationStatus(Tr("Notification access is unavailable. Open Windows settings.", "Нет доступа к уведомлениям. Откройте параметры Windows.", "Немає доступу до сповіщень.", "Kein Zugriff auf Benachrichtigungen.", "No hay acceso a las notificaciones."));
        }
        finally { _notificationInitializing = false; }
    }

    private async void TglNotificationHooks_Click(object sender, RoutedEventArgs e)
    {
        SaveSettings();
        _pendingNotification = null;
        HideNotificationBubble();
        // Rebaseline while changing filters so existing notifications aren't replayed.
        _notificationBaseline = false;
        await InitializeNotificationListenerAsync();
    }

    private async void NotificationListener_Changed(UserNotificationListener sender, UserNotificationChangedEventArgs args)
    {
        if (_windowClosed || Dispatcher.HasShutdownStarted) return;
        Interlocked.Increment(ref _notificationEventVersion);
        AppLog.Notification($"event kind={args.ChangeKind} id={args.UserNotificationId}");
        try
        {
            // Capture a short-lived toast directly from its event, before a full
            // enumeration/UI backlog can let the sender withdraw it.
            bool capture = _notificationBaseline;
            bool wantMessengers = _settings.MessengerNotifications, wantAllApps = _settings.AllAppNotifications;
            var notification = args.ChangeKind == UserNotificationChangedKind.Added
                ? await Task.Run<NotificationObservation?>(() =>
                {
                    if (sender.GetNotification(args.UserNotificationId) is not { } item) return null;
                    var observation = ReadNotificationObservation(item);
                    return capture ? PrefetchNotificationActivation(observation, wantMessengers, wantAllApps) : observation;
                }).WaitAsync(TimeSpan.FromSeconds(2))
                : null;
            if (_windowClosed || Dispatcher.HasShutdownStarted) return;
            await Dispatcher.InvokeAsync(() =>
            {
                if (notification is { } observation && NotificationsEnabled && _notifAccessGranted)
                {
                    PollWindowsDndState(false);
                    ObserveNotification(observation);
                    ShowNextNotification();
                }
                if (_notificationBusy) _notificationReadAgain = true;
                PollSystemNotificationsAsync();
            });
        }
        catch (Exception ex) { AppLog.Error("Notification event", ex); }
    }

    private async void PollSystemNotificationsAsync()
    {
        if (DateTime.UtcNow >= _nextBannerSync)
        {
            _nextBannerSync = DateTime.UtcNow.AddSeconds(10);
            SyncNotificationBanners();
        }
        // Events provide immediate updates. Polling also recovers after access changes
        // and missed Shell events; never run concurrent WinRT enumerations.
        if (_notificationBusy || DateTime.UtcNow < _nextNotificationPoll) return;
        _nextNotificationPoll = DateTime.UtcNow.AddSeconds(1);
        await ReadNotificationsAsync();
    }

    private async Task ReadNotificationsAsync()
    {
        if (_windowClosed || _notifListener == null || !NotificationsEnabled) return;
        if (_notificationBusy) { _notificationReadAgain = true; return; }
        _notificationBusy = true;
        _notificationReadAgain = false;
        try
        {
            bool allowed = _notifListener.GetAccessStatus() == UserNotificationListenerAccessStatus.Allowed;
            if (allowed != _notifAccessGranted)
            {
                _notifAccessGranted = allowed;
                _notificationBaseline = false;
                _pendingNotification = null;
                HideNotificationBubble();
                UpdateNotificationStatus();
                SyncNotificationBanners();
            }
            if (!allowed) return;

            // Cancel stalled WinRT requests so one read cannot disable the bridge forever.
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            long eventVersion = Interlocked.Read(ref _notificationEventVersion);
            var seen = _notificationBaseline ? _notificationPolicy.Snapshot() : null;
            bool wantMessengers = TglMessengerNotifs.IsChecked == true, wantAllApps = TglAllAppNotifs.IsChecked == true;
            // Shell enumeration must not occupy the WPF dispatcher.
            var notifications = await Task.Run(async () =>
            {
                var items = await _notifListener.GetNotificationsAsync(NotificationKinds.Toast).AsTask(timeout.Token);
                var observations = new List<NotificationObservation>(items.Count);
                foreach (var item in items)
                {
                    try
                    {
                        var observation = ReadNotificationObservation(item);
                        if (seen != null && (!seen.TryGetValue(observation.Id, out var previous) ||
                            previous != (observation.Created, observation.Text)))
                            observation = PrefetchNotificationActivation(observation, wantMessengers, wantAllApps);
                        observations.Add(observation);
                    }
                    catch (Exception ex) { AppLog.Error("Read notification observation", ex); }
                }
                return observations;
            });
            if (_windowClosed) return;
            if (eventVersion != Interlocked.Read(ref _notificationEventVersion))
            {
                _notificationReadAgain = true;
                return; // An event captured a newer state than this snapshot.
            }
            PollWindowsDndState(notifyIfChanged: false);
            if (DateTime.UtcNow >= _nextNotificationHeartbeat)
            {
                AppLog.Notification($"read count={notifications.Count} baseline={_notificationBaseline} dnd={_isDndActive} known={_dndStateKnown} game={_isGameModeActive} messengers={TglMessengerNotifs.IsChecked} all={TglAllAppNotifs.IsChecked}");
                _nextNotificationHeartbeat = DateTime.UtcNow.AddSeconds(15);
            }
            _notificationPolicy.Retain(notifications.Select(n => n.Id));
            foreach (var notification in notifications.OrderBy(n => n.Created))
                ObserveNotification(notification);
            _notificationBaseline = true;
            // New messages preempt transient keyboard/clipboard hints instead of
            // waiting up to a minute behind the shared toast timer.
            ShowNextNotification();
        }
        catch (Exception ex)
        {
            AppLog.Error("Read notifications", ex);
            // Preserve the baseline after transient errors: otherwise the first new
            // notification after recovery would silently become startup history.
        }
        finally
        {
            _notificationBusy = false;
            if (_notificationReadAgain) _nextNotificationPoll = DateTime.MinValue;
        }
    }

    private void ObserveNotification(NotificationObservation observation)
    {
        try
        {
            string text = observation.Text;
            if (!_notificationPolicy.Observe(observation.Id, observation.Created, text)) return;
            var notification = observation.Source;
            var app = notification.AppInfo;
            string appName = app?.DisplayInfo?.DisplayName ?? "App";
            string appId = app?.AppUserModelId ?? "";
            if (_notificationBanners is { } banners)
                RunBannerWork(() => banners.ObserveSource(appName, appId));
            AppLog.Notification($"observed id={observation.Id} created={observation.Created:O} baseline={_notificationBaseline} elements={observation.Elements}");
            if (!_notificationBaseline || string.IsNullOrWhiteSpace(text)) return;
            bool messenger = NotificationPolicy.IsMessenger(appName, appId);
            if (!NotificationPolicy.ShouldDisplay(messenger, TglMessengerNotifs.IsChecked ?? false,
                    TglAllAppNotifs.IsChecked ?? false, _isGameModeActive || _isDndActive || !_dndStateKnown)) return;
            var next = new IslandNotification(observation.Id, $"{appName}: {text}", messenger,
                appId, appName, app, observation.Created,
                observation.Activation ?? CaptureNotificationActivation(observation, app, appId));
            // The newest item wins a burst; originals remain in Windows history.
            _pendingNotification = next;
        }
        catch (Exception ex) { AppLog.Error("Read one notification", ex); }
    }

    private bool ShowNextNotification()
    {
        if (_isDndActive || _isGameModeActive || !_dndStateKnown || !NotificationsEnabled)
        {
            _pendingNotification = null;
            return false;
        }
        if (_pendingNotification is not { } notification) return false;
        _pendingNotification = null;
        ShowSideBubble(notification.Message, notification.Messenger ? ToastCategory.Messenger : ToastCategory.AppNotification,
            "✉");
        _visibleNotification = notification;
        SideToastBorder.Cursor = Cursors.Hand;
        SideToastBorder.ToolTip = notification.Message;
        _ = LoadNotificationIconAsync(notification, _notificationVisualGeneration);
        AppLog.Notification($"display id={notification.Id} source={notification.AppId} messenger={notification.Messenger} latencyMs={(DateTimeOffset.Now - notification.Created).TotalMilliseconds:F1} visible={IsVisible} window={WindowState}");
        return true;
    }

    private static string? GetNotificationPackageManifest(Windows.ApplicationModel.AppInfo? info)
    {
        try
        {
            string? path = info?.Package?.InstalledLocation?.Path;
            return string.IsNullOrWhiteSpace(path) ? null : Path.Combine(path, "AppxManifest.xml");
        }
        catch { return null; } // Unpackaged desktop senders have no package.
    }

    private void ClearNotificationInteraction()
    {
        _featureNoticeAction = null;
        _visibleNotification = null;
        _notificationVisualGeneration++;
        SideToastAppIcon.Source = null;
        SideToastAppIcon.Visibility = Visibility.Collapsed;
        SideToastBorder.Cursor = Cursors.Arrow;
        SideToastBorder.ToolTip = null;
    }

    private async Task LoadNotificationIconAsync(IslandNotification notification, long generation)
    {
        try
        {
            if (!_notificationIcons.TryGetValue(notification.AppId, out var icon))
            {
                icon = await NotificationIconService.LoadAsync(notification.AppId, notification.AppInfo);
                if (icon != null && !string.IsNullOrWhiteSpace(notification.AppId))
                {
                    if (_notificationIcons.Count >= 64) _notificationIcons.Clear();
                    _notificationIcons[notification.AppId] = icon;
                }
            }
            if (icon == null || generation != _notificationVisualGeneration || _windowClosed) return;
            SideToastAppIcon.Source = icon;
            SideToastAppIcon.Visibility = Visibility.Visible;
            SideToastVectorIcon.Visibility = Visibility.Collapsed;
            SideToastBadgeShortText.Visibility = Visibility.Collapsed;
            SideToastIconCircle.Background = System.Windows.Media.Brushes.Transparent;
            AppLog.Notification($"icon id={notification.Id} loaded=True");
        }
        catch (Exception ex) { AppLog.Error("Load notification app icon", ex); }
    }

    private async void SideToastBorder_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (_featureNoticeAction is { } featureAction) { HideNotificationBubble(); featureAction(); return; }
        if (_visibleNotification is not { } notification || string.IsNullOrWhiteSpace(notification.AppId)) return;
        // Capture the clicked item before awaiting: another sender can replace the visible bubble.
        HideNotificationBubble();
        NotificationActivationService.AllowForegroundActivation();
        try
        {
            var target = await notification.Activation;
            bool activated;
            if (target?.Protocol is { } uri)
            {
                var options = new Windows.System.LauncherOptions();
                if (target.ProtocolPackage is { } package) options.TargetApplicationPackageFamilyName = package;
                activated = await Windows.System.Launcher.LaunchUriAsync(uri, options);
            }
            else activated = target != null && await NotificationActivationBroker.RunAsync(target);
            AppLog.Notification($"activate id={notification.Id} context={target != null} protocol={target?.Protocol != null} handled={activated}");
            if (activated) return;
        }
        catch (Exception ex) { AppLog.Error("Invoke notification default action", ex); }

        try
        {
            bool launched = NotificationActivationService.OpenSourceApp(notification.AppId);
            AppLog.Notification($"open source id={notification.Id} handled={launched}");
        }
        catch (Exception ex) { AppLog.Error("Open notification app", ex); }
    }

    private void NotificationDurationSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isLoadingSettings || NotificationDurationValue == null) return;
        _settings.NotificationDurationSeconds = NotificationPolicy.ClampDuration(e.NewValue);
        UpdateNotificationDurationLabel();
        if (_toastHideTimer.IsEnabled)
        {
            _toastHideTimer.Stop();
            _toastHideTimer.Interval = TimeSpan.FromSeconds(_settings.NotificationDurationSeconds);
            _toastHideTimer.Start();
        }
        SaveSettings();
    }

    private void UpdateNotificationDurationLabel()
        => NotificationDurationValue.Text = $"{NotificationPolicy.ClampDuration(_settings.NotificationDurationSeconds)} " +
            Tr("s", "с", "с", "s", "s");

    private void SyncNotificationBanners()
    {
        if (_windowClosed) return;
        try
        {
            if (_notificationBanners == null && ((_notifAccessGranted && NotificationsEnabled) || File.Exists(NotificationBannerService.JournalPath)))
            {
                _notificationBanners = new NotificationBannerService();
            }
            bool startWatchdog = _notificationBanners != null && _notifAccessGranted && NotificationsEnabled && !_bannerWatchdogStarted;
            if (startWatchdog)
            {
                _bannerWatchdogStarted = true;
            }
            bool messengers = _notifAccessGranted && (TglMessengerNotifs.IsChecked ?? false);
            bool all = _notifAccessGranted && (TglAllAppNotifs.IsChecked ?? false);
            if (_notificationBanners is { } banners) RunBannerWork(() =>
            { if (startWatchdog) banners.StartWatchdog(); banners.Sync(messengers, all); });
        }
        catch (Exception ex)
        {
            AppLog.Error("Configure Windows notification banners", ex);
            SetNotificationStatus(Tr("Windows banners could not be hidden.", "Не удалось скрыть баннеры Windows.",
                "Не вдалося приховати банери Windows.", "Windows-Banner konnten nicht ausgeblendet werden.", "No se pudieron ocultar los banners de Windows."));
        }
    }

    private Task _bannerWork = Task.CompletedTask;
    private void RunBannerWork(Action action)
    {
        // The journal and its named mutex remain serialized, but never stall input.
        _bannerWork = _bannerWork.ContinueWith(_ =>
        {
            try { action(); }
            catch (Exception ex)
            {
                AppLog.Error("Configure Windows notification banners", ex);
                if (!_windowClosed) Dispatcher.BeginInvoke(new Action(() =>
                    SetNotificationStatus("Не удалось скрыть баннеры Windows.")));
            }
        }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
    }

    private void DisposeNotificationBanners()
    {
        if (_notificationBanners is not { } banners) return;
        RunBannerWork(banners.Dispose);
        _bannerWork.GetAwaiter().GetResult(); // Shutdown only; no dispatcher work inside the queue.
    }

    private void HideNotificationBubble()
    {
        _toastHideTimer.Stop();
        ClearNotificationInteraction();
        SideToastBorder.BeginAnimation(OpacityProperty, null);
        SideToastBorder.Opacity = 0;
        SideToastBorder.Visibility = Visibility.Collapsed;
    }

    private void SetNotificationStatus(string message) => TxtNotificationStatus.Text = message;

    private void UpdateNotificationStatus()
    {
        BtnNotificationAccess.Content = Tr("Windows notification access", "Доступ к уведомлениям Windows", "Доступ до сповіщень Windows", "Windows-Benachrichtigungszugriff", "Acceso a notificaciones de Windows");
        BtnNotificationAccess.Visibility = NotificationsEnabled ? Visibility.Visible : Visibility.Collapsed;
        SetNotificationStatus(!NotificationsEnabled
            ? Tr("Notifications are disabled.", "Уведомления выключены.", "Сповіщення вимкнено.", "Benachrichtigungen deaktiviert.", "Notificaciones desactivadas.")
            : _notifAccessGranted
                ? Tr("Connected to Windows notifications.", "Доступ к уведомлениям Windows разрешён.", "Доступ до сповіщень дозволено.", "Mit Windows-Benachrichtigungen verbunden.", "Conectado a las notificaciones de Windows.")
                : Tr("Allow notification access in Windows settings.", "Разрешите доступ к уведомлениям в параметрах Windows.", "Дозвольте доступ у параметрах Windows.", "Benachrichtigungszugriff in Windows erlauben.", "Permite el acceso en Configuración de Windows."));
    }

    private async void NotificationAccess_Click(object sender, RoutedEventArgs e)
    {
        if (!HasPackageIdentity()) { await InitializeNotificationListenerAsync(); return; }
        if (_notifListener?.GetAccessStatus() == UserNotificationListenerAccessStatus.Denied)
        {
            try { Process.Start(new ProcessStartInfo("ms-settings:privacy-notifications") { UseShellExecute = true }); }
            catch (Exception ex) { AppLog.Error("Open notification settings", ex); }
        }
        else await InitializeNotificationListenerAsync();
    }

    private void ActionToggleDnd_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        SetDoNotDisturbState(!_isDndActive, true);
    }

    private void TglDndActiveState_Click(object sender, RoutedEventArgs e)
        => SetDoNotDisturbState(TglDndActiveState.IsChecked ?? false, true);

    private async void SetDoNotDisturbState(bool active, bool notifyUser, bool fromGameMode = false)
    {
        if (_dndChanging) { UpdateDndVisuals(); return; }
        if (!fromGameMode)
        {
            _gameModeOwnsDnd = false;
            _timedDnd = null;
        }
        _dndChanging = true;
        try
        {
            if (!_windowsDnd.TrySetProfile(active ? WindowsDndService.Priority : WindowsDndService.Off,
                    endAutomaticRule: !active, out var error))
            {
                ShowDndError(error);
                return;
            }
            // Shell applies settings asynchronously. Only display confirmed state.
            bool confirmed = false;
            for (int attempt = 0; attempt < 12 && !_windowClosed; attempt++)
            {
                if (_windowsDnd.TryRead(out var state) && state.IsActive == active)
                {
                    ApplyWindowsDndState(state, notifyUser);
                    confirmed = true;
                    break;
                }
                await Task.Delay(100);
            }
            if (!confirmed && !_windowClosed) ShowDndError(null);
        }
        finally
        {
            _dndChanging = false;
            if (!_windowClosed)
            {
                PollWindowsDndState(false);
                UpdateDndVisuals();
            }
        }
    }

    private void ShowDndError(string? error)
    {
        ShowSideBubble(Tr("Windows did not confirm Do Not Disturb. Open System > Notifications.",
            "Windows не подтвердил «Не беспокоить». Откройте Система → Уведомления.",
            "Windows не підтвердив режим «Не турбувати».", "Windows hat Nicht stören nicht bestätigt.",
            "Windows no confirmó No molestar.") + (error == null ? "" : $" ({error})"), ToastCategory.Info, bypassDnd: true);
    }

    private void PollWindowsDndState(bool notifyIfChanged = true)
    {
        if (_dndChanging) return;
        if (!_windowsDnd.TryRead(out var state))
        {
            bool wasKnown = _dndStateKnown;
            _dndStateKnown = false;
            if (wasKnown) UpdateDndVisuals();
            return;
        }
        ApplyWindowsDndState(state, notifyIfChanged);
    }

    private void ApplyWindowsDndState(WindowsDndService.State state, bool notify)
    {
        bool changed = _isDndActive != state.IsActive;
        bool wasUnknown = !_dndStateKnown;
        if (_gameModeOwnsDnd && !_dndChanging && (!state.IsActive || state.SelectedProfile != WindowsDndService.Priority))
            _gameModeOwnsDnd = false; // User/system took over: don't undo their choice on exit.
        if (_timedDnd is { } timed && state.SelectedProfile != WindowsDndService.Priority && timed.Until > DateTimeOffset.Now)
            _timedDnd = null;
        _dndStateKnown = true;
        _isDndActive = state.IsActive;
        if (changed || wasUnknown)
        {
            UpdateDndVisuals();
            RefreshWidget("focus");
        }
        if (!changed) return;
        _pendingNotification = null;
        HideNotificationBubble();
        RebuildTrayMenu();
        if (notify && (TglShowDnd.IsChecked ?? true))
            ShowSideBubble(_isDndActive
                ? Tr("Do Not Disturb enabled", "Не беспокоить включён", "Не турбувати увімкнено", "Nicht stören aktiviert", "No molestar activado")
                : Tr("Do Not Disturb disabled", "Не беспокоить выключен", "Не турбувати вимкнено", "Nicht stören deaktiviert", "No molestar desactivado"),
                ToastCategory.DoNotDisturb, bypassDnd: true);
    }

    private void RestoreDndAfterGameMode()
    {
        if (_gameModeOwnsDnd && _dndStateBeforeGameMode is { } before &&
            _windowsDnd.TryRead(out var current) && current.SelectedProfile == WindowsDndService.Priority)
        {
            // Restore the exact manual profile; never disable an automatic Windows rule.
            if (!_windowsDnd.TrySetProfile(before.SelectedProfile, endAutomaticRule: false, out var error) && !_windowClosed)
                ShowDndError(error);
        }
        _gameModeOwnsDnd = false;
        _dndStateBeforeGameMode = null;
        if (!_windowClosed) PollWindowsDndState(false);
    }
}
