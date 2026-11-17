using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.System;

namespace DynamicIslandWin.Services;

internal sealed class ScreenRecordingService : IDisposable
{
    internal const string CallbackScheme = "dynamicisland-capture";
    private static string PipeName => "DynamicIslandWin.Capture." + WindowsIdentity.GetCurrent().User!.Value;
    private readonly CancellationTokenSource _lifetime = new();
    private bool _busy;

    internal static Uri BuildCaptureUri(Guid request) => new(
        "ms-screenclip://capture/video?api-version=1.2&enabledModes=RecordAllModes"
        + "&user-agent=DynamicIslandWin&redirect-uri=" + Uri.EscapeDataString(CallbackScheme + "://response")
        + "&x-request-correlation-id=" + request.ToString("D"));

    internal const string SnippingToolPackage = "Microsoft.ScreenSketch_8wekyb3d8bbwe";
    private static LauncherOptions LaunchOptions(IntPtr owner)
    {
        var options = new LauncherOptions { TargetApplicationPackageFamilyName = SnippingToolPackage };
        if (owner != IntPtr.Zero) WinRT.Interop.InitializeWithWindow.Initialize(options, owner);
        return options;
    }

    private static async Task<string?> ReadCallbackAsync(NamedPipeServerStream pipe, CancellationToken token)
    {
        await pipe.WaitForConnectionAsync(token);
        using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
        using var readTimeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        readTimeout.CancelAfter(TimeSpan.FromSeconds(3));
        var chars = new char[16385];
        int length = 0;
        while (length < chars.Length)
        {
            int count = await reader.ReadAsync(chars.AsMemory(length, 1), readTimeout.Token);
            if (count == 0 || chars[length] == '\n') break;
            length += count;
        }
        return length <= 16384 ? new string(chars, 0, length).TrimEnd('\r') : null;
    }

    private static async Task VerifyRecordingProtocolAsync(IntPtr owner, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        Guid request = Guid.NewGuid();
        await using var pipe = new NamedPipeServerStream(PipeName, PipeDirection.In, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        var response = ReadCallbackAsync(pipe, timeout.Token);
        var uri = new Uri("ms-screenclip://discover?user-agent=DynamicIslandWin&redirect-uri="
            + Uri.EscapeDataString(CallbackScheme + "://response") + "&x-request-correlation-id=" + request);
        if (!await Launcher.LaunchUriAsync(uri, LaunchOptions(owner)).AsTask(timeout.Token))
            throw new InvalidOperationException("Snipping Tool is unavailable.");
        while (true)
        {
            string? value = await response;
            if (value != null && Uri.TryCreate(value, UriKind.Absolute, out var callback)
                && callback.Scheme == CallbackScheme && callback.Host == "response")
            {
                var query = new Windows.Foundation.WwwFormUrlDecoder(callback.Query);
                if (Guid.TryParse(query.GetFirstValueByName("x-request-correlation-id"), out var actual) && actual == request)
                {
                    using var document = System.Text.Json.JsonDocument.Parse(query.GetFirstValueByName("discover"));
                    if (!document.RootElement.GetProperty("capabilities").EnumerateArray()
                        .Any(c => c.GetProperty("path").GetString() == "capture/video"))
                        throw new PlatformNotSupportedException("Snipping Tool does not support video recording.");
                    return;
                }
            }
            pipe.Disconnect(); response = ReadCallbackAsync(pipe, timeout.Token);
        }
    }

    internal static bool TryReadResponse(string value, Guid request, out int code, out string? token)
    {
        code = 0; token = null;
        if (value.Length > 16384 || !Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme != CallbackScheme || uri.Host != "response" || uri.AbsolutePath is not ("" or "/")
            || uri.Fragment.Length != 0 || uri.UserInfo.Length != 0) return false;
        try
        {
            var query = new Windows.Foundation.WwwFormUrlDecoder(uri.Query);
            if (query.GroupBy(p => p.Name).Any(g => g.Count() != 1)) return false;
            if (!Guid.TryParse(query.GetFirstValueByName("x-request-correlation-id"), out var actual) || actual != request)
                return false;
            if (!int.TryParse(query.GetFirstValueByName("code"), out code)) return false;
            token = query.FirstOrDefault(p => p.Name == "file-access-token")?.Value;
            return code != 200 || !string.IsNullOrWhiteSpace(token);
        }
        catch (Exception ex) when (ex is ArgumentException or System.Runtime.InteropServices.COMException)
        { return false; }
    }

    // URI activation starts a short-lived process. Forward the response to the
    // original instance, which owns the pending correlation ID and package token.
    internal static async Task ForwardResponseAsync(string response)
    {
        if (response.Length > 16384) return;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        try
        {
            await using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.Out, PipeOptions.Asynchronous);
            await pipe.ConnectAsync(timeout.Token);
            await using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true);
            await writer.WriteLineAsync(response.AsMemory(), timeout.Token);
            await writer.FlushAsync(timeout.Token);
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or UnauthorizedAccessException)
        { AppLog.Error("Forward screen recording response", ex); }
    }

    // Returns null for user cancellation. The system overlay owns area selection,
    // countdown, audio options and Stop; no capture begins without the user's Start.
    internal async Task<string?> RecordAsync(IntPtr owner = default)
    {
        if (_busy) throw new InvalidOperationException("A screen recording request is already open.");
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
            throw new PlatformNotSupportedException("Screen recording requires Windows 11 Snipping Tool.");
        _busy = true;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        timeout.CancelAfter(TimeSpan.FromHours(12));
        try
        {
            // Fail promptly if Windows cannot activate the tool or route its
            // callbacks, instead of leaving the Record button disabled for hours.
            try { await VerifyRecordingProtocolAsync(owner, timeout.Token); }
            catch (OperationCanceledException) when (!timeout.IsCancellationRequested)
            { throw new TimeoutException("Snipping Tool did not respond. Restart Snipping Tool and retry."); }
            var request = Guid.NewGuid();
            await using var pipe = new NamedPipeServerStream(PipeName, PipeDirection.In, 1,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            var responseTask = ReadCallbackAsync(pipe, timeout.Token);
            if (!await Launcher.LaunchUriAsync(BuildCaptureUri(request), LaunchOptions(owner)).AsTask(timeout.Token))
                throw new InvalidOperationException("Windows could not launch screen recording.");
            AppLog.Notification("recording overlay launched");
            while (true)
            {
                string? response = await responseTask;
                AppLog.Notification($"recording callback received length={response?.Length ?? 0}");
                if (response != null && TryReadResponse(response, request, out int code, out var token))
                {
                    AppLog.Notification($"recording response code={code}");
                    if (code == 499) return null;
                    if (code != 200) throw new InvalidOperationException($"Snipping Tool returned {code}.");
                    var source = await SharedStorageAccessManager.RedeemTokenForFileAsync(token!).AsTask(timeout.Token);
                    if (!string.Equals(source.FileType, ".mp4", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Screen recording did not return an MP4.");
                    string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "OC - Dynamic Island");
                    Directory.CreateDirectory(directory);
                    var folder = await StorageFolder.GetFolderFromPathAsync(directory).AsTask(timeout.Token);
                    var saved = await source.CopyAsync(folder, "Recording-" + DateTime.Now.ToString("yyyy-MM-dd-HHmmss") + ".mp4",
                        NameCollisionOption.GenerateUniqueName).AsTask(timeout.Token);
                    AppLog.Notification("recording saved");
                    return saved.Path;
                }
                // Ignore unsolicited or stale callbacks without ending a recording.
                pipe.Disconnect();
                responseTask = ReadCallbackAsync(pipe, timeout.Token);
            }
        }
        finally { _busy = false; }
    }

    public void Dispose() { _lifetime.Cancel(); _lifetime.Dispose(); }
}
