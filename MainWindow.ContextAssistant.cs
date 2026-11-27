using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using DynamicIslandWin.Services;
using Rectangle = System.Drawing.Rectangle;

namespace DynamicIslandWin;

public partial class MainWindow
{
    private readonly record struct AssistantView(IntPtr Window, uint Process, Rectangle Island, Rectangle Capture);
    private AssistantView? _assistantView;
    private (IntPtr Window, uint Process)? _assistantManualWindow;
    private CancellationTokenSource? _assistantRequest;
    private DateTime _assistantDue, _assistantNextRequest;
    private byte[]? _assistantLastImage;
    private string _assistantKey = "";
    private string? _assistantError;
    private bool _assistantHidden;

    private void TglContextAssistant_Click(object sender, RoutedEventArgs e)
    {
        _settings.GeminiContextAssistant = TglContextAssistant.IsChecked == true;
        ReloadAssistantKey();
        SaveSettings();
    }

    private void OpenAssistantKey_Click(object sender, RoutedEventArgs e)
        => SwitchSettingsPage(PageAi, NavAiBtn);

    private void ReloadAssistantKey()
    {
        _assistantKey = GeminiKeyStore.Read().Trim();
        _assistantError = null;
        _assistantNextRequest = DateTime.MinValue;
        _assistantManualWindow = null;
        _assistantRequest?.Cancel();
        SetAssistantView(null);
        SetAssistantHidden(false);
        if (_assistantKey.Length == 0) _settings.GeminiContextAssistant = false;
        if (!_settings.GeminiContextAssistant && !_glassTimer.IsEnabled)
        {
            _glassCapture?.Dispose();
            _glassCapture = null;
        }
        TglContextAssistant.IsChecked = _settings.GeminiContextAssistant;
        UpdateAssistantStatus();
    }

    private void UpdateAssistantStatus()
    {
        var status = _assistantKey.Length == 0 ? "Добавьте API-ключ Gemini, чтобы включить ассистента." : _assistantError;
        AssistantStatus.Text = status == null ? "" : L(status);
        AssistantStatus.Visibility = status == null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void RefreshAssistantContext()
    {
        if (!_settings.GeminiContextAssistant || _assistantKey.Length == 0) return;
        if (_captureUiBusy || _currentState != IslandState.Compact || _isGameModeActive || _manualResize ||
            !IsVisible || IslandBorder.Visibility == Visibility.Collapsed)
        {
            SetAssistantView(null);
            return;
        }

        IntPtr foreground = GetForegroundWindow();
        GetWindowThreadProcessId(foreground, out uint pid);
        var identity = (foreground, pid);
        if (pid == 0 || pid == Environment.ProcessId) { SetAssistantView(null); return; }
        if (_assistantManualWindow == identity) return;
        _assistantManualWindow = null;
        var view = ReadAssistantView(foreground, pid);
        SetAssistantView(view);
        var now = DateTime.UtcNow;
        if (view is { } current && _assistantRequest == null && now >= _assistantDue &&
            now >= _assistantNextRequest && !IslandBorder.IsMouseOver && !_isClickCandidate && !_bodyDragging)
            _ = AnalyzeAssistantViewAsync(current, _assistantKey);
    }

    private AssistantView? ReadAssistantView(IntPtr window, uint pid)
    {
        if (window == GetShellWindow() || PresentationSource.FromVisual(IslandBorder) == null ||
            IslandBorder.ActualWidth < 2 || IslandBorder.ActualHeight < 2 || IsIconic(window)) return null;
        const int extendedFrameBounds = 9;
        if (GetAssistantWindowBounds(window, extendedFrameBounds, out var frame, Marshal.SizeOf<RECT>()) != 0 &&
            !GetWindowRect(window, out frame)) return null;
        var bounds = Rectangle.FromLTRB(frame.Left, frame.Top, frame.Right, frame.Bottom);
        var start = IslandBorder.PointToScreen(new System.Windows.Point(0, 0));
        var end = IslandBorder.PointToScreen(new System.Windows.Point(IslandBorder.ActualWidth, IslandBorder.ActualHeight));
        var island = Rectangle.FromLTRB((int)Math.Round(start.X), (int)Math.Round(start.Y),
            (int)Math.Round(end.X), (int)Math.Round(end.Y));
        var monitor = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(
            island.Left + island.Width / 2, island.Top + island.Height / 2)).Bounds;
        var capture = GeminiIslandAnalysis.CaptureBounds(island, bounds, monitor);
        return capture.IsEmpty ? null : new(window, pid, island, capture);
    }

    private void SetAssistantView(AssistantView? view)
    {
        if (_assistantView == view) return;
        _assistantRequest?.Cancel();
        _assistantView = view;
        _assistantLastImage = null;
        _assistantDue = DateTime.UtcNow.AddMilliseconds(600);
        SetAssistantHidden(false);
    }

    private async Task AnalyzeAssistantViewAsync(AssistantView view, string key)
    {
        using var request = new CancellationTokenSource();
        _assistantRequest = request;
        _assistantDue = DateTime.UtcNow.AddSeconds(15);
        try
        {
            var source = new WindowInteropHelper(this).Handle;
            _glassCapture ??= new FilteredBackdropCapture();
            var capture = await _glassCapture.CaptureAsync(view.Capture, [source]);
            if (request.IsCancellationRequested || !AssistantViewIsCurrent(view)) return;
            if (capture == null) throw new InvalidOperationException("The overlap could not be captured.");

            var (image, hash) = await Task.Run(() =>
            {
                var png = new PngBitmapEncoder();
                png.Frames.Add(BitmapFrame.Create(capture.Image));
                using var stream = new MemoryStream();
                png.Save(stream);
                var image = stream.ToArray();
                return (image, SHA256.HashData(image));
            }, request.Token);
            if (request.IsCancellationRequested || !AssistantViewIsCurrent(view) ||
                (_assistantLastImage != null && hash.AsSpan().SequenceEqual(_assistantLastImage))) return;
            var overlap = Rectangle.Intersect(view.Island, view.Capture);
            overlap.Offset(-view.Capture.X, -view.Capture.Y);
            _assistantNextRequest = DateTime.UtcNow.AddSeconds(5);
            bool obstructs = await GeminiIslandAnalysis.IsObstructingAsync(key, image, overlap, request.Token);
            if (request.IsCancellationRequested || !AssistantViewIsCurrent(view)) return;
            _assistantLastImage = hash;
            _assistantError = null;
            UpdateAssistantStatus();
            if (!IslandBorder.IsMouseOver && !_isClickCandidate && !_bodyDragging) SetAssistantHidden(obstructs);
            else _assistantLastImage = null;
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (request.IsCancellationRequested || !AssistantViewIsCurrent(view)) return;
            _assistantNextRequest = DateTime.UtcNow.AddMinutes(1);
            _assistantError = ex is HttpRequestException { StatusCode: HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized }
                ? "Gemini отклонил ключ. Проверьте доступ к Gemini API."
                : "Не удалось выполнить анализ Gemini. Островок показан.";
            UpdateAssistantStatus();
            _assistantLastImage = null;
            SetAssistantHidden(false);
            AppLog.Error("Gemini island analysis", ex);
        }
        finally { _assistantRequest = null; }
    }

    private bool AssistantViewIsCurrent(AssistantView view)
        => !_windowClosed && _settings.GeminiContextAssistant && _assistantView == view &&
            _currentState == IslandState.Compact && !_captureUiBusy && !_isGameModeActive && !_manualResize &&
            IsVisible && IslandBorder.Visibility != Visibility.Collapsed && GetForegroundWindow() == view.Window &&
            GetWindowThreadProcessId(view.Window, out uint pid) != 0 && pid == view.Process &&
            ReadAssistantView(view.Window, pid) == view;

    private void SetAssistantHidden(bool hidden)
    {
        if (_assistantHidden == hidden) return;
        _assistantHidden = hidden;
        if (IslandBorder.Visibility != Visibility.Collapsed)
            IslandBorder.Visibility = hidden ? Visibility.Hidden : Visibility.Visible;
    }

    private void ShowIslandOverAssistant()
    {
        if (!_settings.GeminiContextAssistant) return;
        IntPtr window = GetForegroundWindow();
        GetWindowThreadProcessId(window, out uint pid);
        if (pid != 0 && pid != Environment.ProcessId) _assistantManualWindow = (window, pid);
        SetAssistantView(null);
        SetAssistantHidden(false);
    }

    private void DisposeContextAssistant() => _assistantRequest?.Cancel();

    [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")]
    private static extern int GetAssistantWindowBounds(IntPtr window, int attribute, out RECT bounds, int length);
    [DllImport("user32.dll")] private static extern IntPtr GetShellWindow();
}
