namespace DynamicIslandWin;

public partial class App : System.Windows.Application
{
    private System.Threading.Mutex? _instanceMutex;
    private System.Threading.EventWaitHandle? _shutdownSignal;
    private System.Threading.RegisteredWaitHandle? _shutdownWait;
    private const string ShutdownEventName = @"Local\DynamicIslandWin.Shutdown";

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        if (e.Args.Length == 3 && e.Args[0] == "--fps-helper" && int.TryParse(e.Args[1], out int fpsParent))
        {
            ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown;
            _ = RunFpsHelperAndExitAsync(fpsParent, e.Args[2]); return;
        }
        if (e.Args.Length == 2 && e.Args[0] == "--capture-response")
        {
            ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown;
            _ = ForwardCaptureResponseAndExitAsync(e.Args[1]);
            return;
        }
        if (e.Args.Length == 1 && e.Args[0] == "--shutdown")
        {
            try { using var signal = System.Threading.EventWaitHandle.OpenExisting(ShutdownEventName); signal.Set(); }
            catch (System.Threading.WaitHandleCannotBeOpenedException) { }
            Shutdown();
            return;
        }
        if (e.Args.Length == 3 && e.Args[0] == "--restore-banners-after-exit" && int.TryParse(e.Args[1], out int parentId))
        {
            ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown;
            _ = RestoreBannersAfterExitAsync(parentId, e.Args[2]);
            return;
        }
        _instanceMutex = new System.Threading.Mutex(true, @"Local\DynamicIslandWin.Application", out bool firstInstance);
        if (!firstInstance) { Shutdown(); return; }
        _shutdownSignal = new System.Threading.EventWaitHandle(false, System.Threading.EventResetMode.AutoReset, ShutdownEventName);
        _shutdownWait = System.Threading.ThreadPool.RegisterWaitForSingleObject(_shutdownSignal,
            (_, _) => Dispatcher.BeginInvoke(new Action(Shutdown)), null, Timeout.Infinite, executeOnlyOnce: true);
        base.OnStartup(e);
        new MainWindow().Show();
    }

    private async Task ForwardCaptureResponseAndExitAsync(string response)
    {
        try { await Services.ScreenRecordingService.ForwardResponseAsync(response); }
        finally { Shutdown(); }
    }

    private async Task RunFpsHelperAndExitAsync(int parent, string nonce)
    { try { await Services.FpsService.RunHelperAsync(parent, nonce); } finally { Shutdown(); } }


    private async Task RestoreBannersAfterExitAsync(int parentId, string owner)
    {
        try
        {
            try { using var parent = System.Diagnostics.Process.GetProcessById(parentId); await parent.WaitForExitAsync(); }
            catch (ArgumentException) { /* Parent already exited. */ }
            using var banners = new Services.NotificationBannerService();
            banners.Restore(owner);
        }
        catch (Exception ex) { Services.AppLog.Error("Banner recovery watchdog", ex); }
        finally { Shutdown(); }
    }

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        _shutdownWait?.Unregister(null);
        _shutdownSignal?.Dispose();
        _instanceMutex?.Dispose();
        base.OnExit(e);
    }
}
