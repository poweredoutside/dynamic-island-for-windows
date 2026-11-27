using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;

namespace DynamicIslandWin.Services;

// A GUI host included with the app. Desktop COM registrations are
// isolated from MSIX callers, so the callback must run outside the package context.
// Context travels only over a current-user pipe; command lines contain a random nonce.
internal static class NotificationActivationBroker
{
    private sealed record Request(string AppId, string? Arguments, Guid Activator, bool Probe);
    private static readonly Lazy<Task<string?>> PreparedHost = new(() => Task.Run(() =>
    {
        try { return PrepareHost(); }
        catch (Exception ex) { AppLog.Error("Prepare notification activation host", ex); return null; }
    }));

    internal sealed class DesktopSession
    {
        private readonly SemaphoreSlim _gate = new(1);
        private readonly CancellationTokenSource _shutdown = new();
        private Connection? _connection;

        internal Task WarmUpAsync(string? executable = null) => RunAsync(null, executable);

        internal async Task<bool> RunAsync(NotificationActivationService.Target? target, string? executable = null,
            CancellationToken cancellation = default)
        {
            using var request = CancellationTokenSource.CreateLinkedTokenSource(cancellation, _shutdown.Token);
            try
            {
                await _gate.WaitAsync(request.Token).ConfigureAwait(false);
                try
                {
                    if (_connection == null || _connection.Process.HasExited)
                    {
                        _connection?.Dispose();
                        _connection = null;
                        // File verification and process startup can take seconds on some PCs.
                        _connection = await Task.Run(() => ConnectAsync(null, executable, keepAlive: true,
                            request.Token), request.Token).ConfigureAwait(false);
                    }
                    if (_connection == null || target == null) return _connection != null;
                    request.Token.ThrowIfCancellationRequested();
                    try
                    {
                        return await _connection.SendAsync(target, probe: false, request.Token).ConfigureAwait(false);
                    }
                    catch
                    {
                        // A late response belongs to this request. Start a clean session on retry.
                        _connection.Dispose();
                        _connection = null;
                        throw;
                    }
                }
                finally { _gate.Release(); }
            }
            catch (OperationCanceledException) when (request.IsCancellationRequested) { return false; }
            catch (Exception ex) { AppLog.Error("Desktop notification activation session", ex); return false; }
        }

        internal async Task CloseAsync()
        {
            _shutdown.Cancel();
            await _gate.WaitAsync().ConfigureAwait(false);
            try { _connection?.Dispose(); _connection = null; }
            finally { _gate.Release(); }
        }
    }

    internal static async Task<bool> RunAsync(NotificationActivationService.Target target, bool probe = false,
        string? executable = null, CancellationToken cancellation = default)
    {
        if (target.Activator == Guid.Empty || cancellation.IsCancellationRequested) return false;
        try
        {
            using var connection = await ConnectAsync(target.AppId, executable, keepAlive: false, cancellation).ConfigureAwait(false);
            return connection != null && await connection.SendAsync(target, probe, cancellation).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { return false; }
        catch (Exception ex) { AppLog.Error("Notification activation broker", ex); return false; }
    }

    private static async Task<Connection?> ConnectAsync(string? appId, string? executable, bool keepAlive,
        CancellationToken cancellation)
    {
        string helper = executable ?? await PreparedHost.Value.WaitAsync(cancellation).ConfigureAwait(false)
            ?? await Task.Run(() => PrepareHost(), cancellation).ConfigureAwait(false);
        if (executable == null && !File.Exists(helper))
            helper = await Task.Run(() => PrepareHost(), cancellation).ConfigureAwait(false);
        cancellation.ThrowIfCancellationRequested();
        string nonce = Guid.NewGuid().ToString("N");
        var pipe = new NamedPipeServerStream("DynamicIsland.Activation." + nonce, PipeDirection.InOut, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        Process? process = null;
        bool connected = false;
        try
        {
            process = appId != null && appId.Contains('!')
                ? StartInSourcePackage(appId, helper, nonce) : StartOutsidePackage(helper, nonce, keepAlive);
            if (process == null) return null;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            timeout.CancelAfter(TimeSpan.FromSeconds(8));
            await pipe.WaitForConnectionAsync(timeout.Token).ConfigureAwait(false);
            if (!GetNamedPipeClientProcessId(pipe.SafePipeHandle, out uint clientId) || clientId != process.Id)
                return null;
            var connection = new Connection(pipe, process);
            connected = true;
            return connection;
        }
        finally
        {
            if (!connected)
            {
                pipe.Dispose();
                StopHost(process);
            }
        }
    }

    private sealed class Connection(NamedPipeServerStream pipe, Process process) : IDisposable
    {
        private readonly StreamWriter _writer = new(pipe, new UTF8Encoding(false), 1024, leaveOpen: true) { AutoFlush = true };
        private readonly StreamReader _reader = new(pipe, Encoding.UTF8, false, 1024, leaveOpen: true);
        internal Process Process { get; } = process;

        internal async Task<bool> SendAsync(NotificationActivationService.Target target, bool probe, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            if (!probe) AllowSetForegroundWindow((uint)Process.Id);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            timeout.CancelAfter(TimeSpan.FromSeconds(8));
            var request = new Request(target.AppId, target.Arguments, target.Activator, probe);
            await _writer.WriteLineAsync(JsonSerializer.Serialize(request).AsMemory(), timeout.Token).ConfigureAwait(false);
            return (await _reader.ReadLineAsync(timeout.Token).ConfigureAwait(false)) switch
            {
                "ok" => true,
                "unavailable" => false,
                _ => throw new IOException("Notification activation host disconnected.")
            };
        }

        public void Dispose()
        {
            pipe.Dispose();
            StopHost(Process);
        }
    }

    private static void StopHost(Process? process)
    {
        if (process == null) return;
        using (process)
        {
            try { if (!process.HasExited) process.Kill(); }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception) { AppLog.Error("Close notification activation broker", ex); }
        }
    }

    internal static string PrepareHost(string? source = null)
    {
        source ??= Path.Combine(AppContext.BaseDirectory, "Activation", "NotificationActivationHost.exe");
        string hash;
        using (var stream = File.OpenRead(source)) hash = Convert.ToHexString(SHA256.HashData(stream));
        string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DynamicIslandWin", "Activation", hash[..16]);
        Directory.CreateDirectory(directory);
        string destination = Path.Combine(directory, "NotificationActivationHost.exe");
        bool matches = false;
        if (File.Exists(destination))
        {
            using var stream = File.OpenRead(destination);
            matches = Convert.ToHexString(SHA256.HashData(stream)) == hash;
        }
        if (!matches)
        {
            string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { File.Copy(source, temporary); File.Move(temporary, destination, overwrite: true); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        // Resolve MSIX file redirection before CreateProcess. An image in WindowsApps
        // retains package identity even with desktop breakaway enabled.
        using var handle = File.OpenHandle(destination);
        var path = new StringBuilder(32768);
        uint length = GetFinalPathNameByHandle(handle, path, (uint)path.Capacity, 0);
        if (length == 0 || length >= path.Capacity) throw new Win32Exception();
        return path.ToString();
    }

    internal static async Task ServeAsync(string nonce, bool keepAlive = false)
    {
        if (!Guid.TryParseExact(nonce, "N", out _)) return;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(6));
        try
        {
            using var pipe = new NamedPipeClientStream(".", "DynamicIsland.Activation." + nonce, PipeDirection.InOut,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await pipe.ConnectAsync(timeout.Token).ConfigureAwait(false);
            using var reader = new StreamReader(pipe, Encoding.UTF8, false, 1024, leaveOpen: true);
            using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 1024, leaveOpen: true) { AutoFlush = true };
            // JSON can escape each UTF-16 code unit to six characters. Bound each request
            // before parsing, even when the sender omits its newline.
            var buffer = new char[104 * 1024];
            var readToken = keepAlive ? CancellationToken.None : timeout.Token;
            do
            {
                int count = 0;
                while (count < buffer.Length)
                {
                    int read = await reader.ReadAsync(buffer.AsMemory(count, 1), readToken).ConfigureAwait(false);
                    if (read == 0) return;
                    if (buffer[count++] == '\n') break;
                }
                if (count == buffer.Length) return;
                var request = JsonSerializer.Deserialize<Request>(buffer.AsSpan(0, count), new JsonSerializerOptions { MaxDepth = 4 });
                if (request == null || request.AppId is not { Length: > 0 and <= 512 } || request.Arguments is { Length: > 16384 } ||
                    request.Activator == Guid.Empty || request.Arguments?.Contains('\0') == true) return;
                var target = new NotificationActivationService.Target(request.AppId, request.Arguments, request.Activator);
                bool success = request.Probe
                    ? await NotificationActivationService.ProbeCallbackAsync(target).ConfigureAwait(false)
                    : await NotificationActivationService.ActivateAsync(target).ConfigureAwait(false);
                await writer.WriteLineAsync(success ? "ok" : "unavailable").ConfigureAwait(false);
            } while (keepAlive);
        }
        catch (Exception ex) { AppLog.Error("Serve notification activation", ex); }
    }

    private static Process? StartOutsidePackage(string executable, string nonce, bool keepAlive)
    {
        IntPtr size = IntPtr.Zero, attributes = IntPtr.Zero, policy = IntPtr.Zero;
        bool initialized = false;
        try
        {
            InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref size);
            attributes = Marshal.AllocHGlobal(size);
            if (!InitializeProcThreadAttributeList(attributes, 1, 0, ref size)) throw new Win32Exception();
            initialized = true;
            policy = Marshal.AllocHGlobal(4);
            Marshal.WriteInt32(policy, 1); // PROCESS_CREATION_DESKTOP_APP_BREAKAWAY_ENABLE_PROCESS_TREE
            if (!UpdateProcThreadAttribute(attributes, 0, new IntPtr(0x20012), policy, new IntPtr(4), IntPtr.Zero, IntPtr.Zero))
                throw new Win32Exception();
            var startup = new StartupInfoEx { StartupInfo = new StartupInfo { Size = Marshal.SizeOf<StartupInfoEx>(), Flags = 1, ShowWindow = 0 }, Attributes = attributes };
            var command = new StringBuilder('"' + executable + "\" --notification-activation-broker " + nonce);
            if (keepAlive) command.Append(" --keep-alive");
            if (!CreateProcess(executable, command, IntPtr.Zero, IntPtr.Zero, false, 0x80000 | 0x08000000,
                IntPtr.Zero, Path.GetDirectoryName(executable), ref startup, out var info)) throw new Win32Exception();
            try { return Process.GetProcessById((int)info.ProcessId); }
            finally { CloseHandle(info.Thread); CloseHandle(info.Process); }
        }
        catch (Exception ex) { AppLog.Error("Start notification activation broker", ex); return null; }
        finally
        {
            if (attributes != IntPtr.Zero)
            {
                if (initialized) DeleteProcThreadAttributeList(attributes);
                Marshal.FreeHGlobal(attributes);
            }
            if (policy != IntPtr.Zero) Marshal.FreeHGlobal(policy);
        }
    }

    private static Process? StartInSourcePackage(string appId, string executable, string nonce)
    {
        object? activator = null;
        int initialized = CoInitializeEx(IntPtr.Zero, 0);
        try
        {
            // The same Windows launcher used by Invoke-CommandInDesktopPackage.
            // Scope the callback to the notification's exact package, including when
            // Telegram Desktop and a fork both register the same activator CLSID.
            activator = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("168EB462-775F-42AE-9111-D714B2306C2E"))!);
            var launcher = (IDesktopAppXActivator)activator!;
            // NONPACKAGED_EXE | NO_ERROR_UI | CENTENNIAL_PROCESS. No update/elevation request.
            int hr = launcher.ActivateWithOptions(appId, executable, "--notification-activation-broker " + nonce,
                0x2 | 0x8 | 0x20, 0, out uint processHandle);
            Marshal.ThrowExceptionForHR(hr);
            var handle = new IntPtr(processHandle);
            try
            {
                uint processId = GetProcessId(handle);
                if (processId == 0) throw new Win32Exception();
                return Process.GetProcessById((int)processId);
            }
            finally { CloseHandle(handle); }
        }
        catch (Exception ex) { AppLog.Error("Start source package activation host", ex); return null; }
        finally
        {
            if (activator != null) Marshal.ReleaseComObject(activator);
            if (initialized >= 0) CoUninitialize();
        }
    }

    [ComImport, Guid("F158268A-D5A5-45CE-99CF-00D6C3F3FC0A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDesktopAppXActivator
    {
        [PreserveSig] int Activate([MarshalAs(UnmanagedType.LPWStr)] string appId,
            [MarshalAs(UnmanagedType.LPWStr)] string executable, [MarshalAs(UnmanagedType.LPWStr)] string arguments, out uint processHandle);
        [PreserveSig] int ActivateWithOptions([MarshalAs(UnmanagedType.LPWStr)] string appId,
            [MarshalAs(UnmanagedType.LPWStr)] string executable, [MarshalAs(UnmanagedType.LPWStr)] string arguments,
            uint options, uint parentProcessId, out uint processHandle);
    }
    [DllImport("ole32.dll")] private static extern int CoInitializeEx(IntPtr reserved, uint concurrency);
    [DllImport("ole32.dll")] private static extern void CoUninitialize();

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public int Size; public string? Reserved, Desktop, Title;
        public uint X, Y, XSize, YSize, XChars, YChars, FillAttribute, Flags;
        public ushort ShowWindow, ReservedSize; public IntPtr ReservedPointer, Input, Output, Error;
    }
    [StructLayout(LayoutKind.Sequential)] private struct StartupInfoEx { public StartupInfo StartupInfo; public IntPtr Attributes; }
    [StructLayout(LayoutKind.Sequential)] private struct ProcessInfo { public IntPtr Process, Thread; public uint ProcessId, ThreadId; }
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool InitializeProcThreadAttributeList(IntPtr list, int count, int flags, ref IntPtr size);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UpdateProcThreadAttribute(IntPtr list, uint flags, IntPtr attribute, IntPtr value, IntPtr size, IntPtr previous, IntPtr returnedSize);
    [DllImport("kernel32.dll")] private static extern void DeleteProcThreadAttributeList(IntPtr list);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateProcess(string application, StringBuilder command, IntPtr processAttributes, IntPtr threadAttributes,
        bool inherit, uint flags, IntPtr environment, string? directory, ref StartupInfoEx startup, out ProcessInfo process);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseHandle(IntPtr handle);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool AllowSetForegroundWindow(uint processId);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeClientProcessId(Microsoft.Win32.SafeHandles.SafePipeHandle pipe, out uint processId);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint GetProcessId(IntPtr process);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandle(Microsoft.Win32.SafeHandles.SafeFileHandle file,
        StringBuilder path, uint capacity, uint flags);
}
