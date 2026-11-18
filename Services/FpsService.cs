using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;

namespace DynamicIslandWin.Services;

// Counts actual Present calls of the selected foreground process. This is render
// submission FPS, not display-time FPS or the island's CompositionTarget rate.
internal sealed class FpsService : IDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private readonly string _nonce = Guid.NewGuid().ToString("N");
    private NamedPipeServerStream? _pipe;
    internal bool IsRunning { get; private set; }
    internal int TargetPid;
    internal double? Fps;
    internal int CpuTemperatureC = -1;
    internal bool HasSample { get; private set; }
    internal string Status = "Измерение игры выключено.";
    private const string TraceStarting = "?";
    private static readonly Guid Dxgi = new("CA11C036-0102-4A2D-A6AD-F03CFED5D3C9");
    private static readonly Guid D3d9 = new("783ACA0A-790E-4D7F-8451-AA850511C6B9");
    private static readonly Guid DxgKrnl = new("802EC45A-1E99-4B83-9920-87C98277BA9D");

    internal async Task StartAsync()
    {
        if (IsRunning || _stop.IsCancellationRequested) return;
        IsRunning = true;
        try
        {
            Status = "Ожидание разрешения Windows…";
            using var pipe = CreateServerPipe("DynamicIsland.Fps." + _nonce);
            _pipe = pipe;
            string executable = DesktopProcess.PhysicalFilePath(Environment.ProcessPath!);
            using var helper = await Task.Run(() => Process.Start(new ProcessStartInfo(executable)
            {
                Arguments = $"--fps-helper {Environment.ProcessId} {_nonce}", Verb = "runas", UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden
            })) ?? throw new IOException("Game metrics helper did not start");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            var connect = pipe.WaitForConnectionAsync(timeout.Token);
            var exit = helper.WaitForExitAsync(timeout.Token);
            if (await Task.WhenAny(connect, exit) == exit && exit.IsCompletedSuccessfully && !connect.IsCompleted)
            {
                timeout.Cancel();
                try { await connect; } catch (OperationCanceledException) { }
                throw new IOException("Game metrics helper exited before connecting");
            }
            try { await connect; }
            catch (OperationCanceledException ex) when (!_stop.IsCancellationRequested)
            { throw new TimeoutException("Waiting for the elevated game metrics pipe", ex); }
            timeout.Cancel();
            if (!GetNamedPipeClientProcessId(pipe.SafePipeHandle, out uint client) || client != helper.Id) throw new IOException("Unexpected FPS helper");
            using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
            using var reader = new StreamReader(pipe, leaveOpen: true);
            Status = "Сборщик подключён. Запускаю трассировку кадров…";
            while (!_stop.IsCancellationRequested)
            {
                int pid = TargetPid;
                await writer.WriteLineAsync(pid.ToString(CultureInfo.InvariantCulture).AsMemory(), _stop.Token);
                using var sampleTimeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                sampleTimeout.CancelAfter(TimeSpan.FromSeconds(10));
                string? line;
                try { line = await ReadBoundedLineAsync(reader, 64, sampleTimeout.Token); }
                catch (OperationCanceledException ex) when (!_stop.IsCancellationRequested)
                { throw new TimeoutException("Waiting for a game metrics sample", ex); }
                if (line == null) throw new EndOfStreamException("Game metrics helper disconnected");
                ParseSample(line, out var fps, out CpuTemperatureC);
                HasSample = true;
                Fps = pid > 0 && pid == TargetPid && fps is > 0 ? fps : null;
                Status = line.StartsWith(TraceStarting + "|", StringComparison.Ordinal) ? "Сборщик подключён. Запускаю трассировку кадров…"
                    : fps == null ? "Трассировка кадров недоступна. Температура CPU проверяется отдельно."
                    : Fps is > 0 ? "Измеряется частота кадров активной игры." : "Нет событий отправки кадров у активного окна. Переключитесь на игру.";
            }
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) { Status = "Разрешение администратора отменено."; }
        catch (Exception) when (_stop.IsCancellationRequested) { Status = "Сеанс измерения остановлен."; }
        catch (TimeoutException ex)
        {
            AppLog.Error("Game metrics timeout: " + ex.Message, ex);
            Status = "Сборщик не ответил вовремя. Переключите игровой режим.";
        }
        catch (Exception ex) { AppLog.Error("Game metrics helper", ex); Status = "Сборщик показателей отключился. Переключите игровой режим."; }
        finally { Fps = null; CpuTemperatureC = -1; HasSample = false; _pipe = null; IsRunning = false; }
    }

    internal static NamedPipeServerStream CreateServerPipe(string name)
    {
        using var identity = WindowsIdentity.GetCurrent();
        var user = identity.User ?? throw new InvalidOperationException("The current Windows user has no SID");
        var security = new PipeSecurity();
        security.SetAccessRuleProtection(true, false);
        security.SetOwner(user);
        security.AddAccessRule(new PipeAccessRule(user, PipeAccessRights.FullControl, AccessControlType.Allow));
        return NamedPipeServerStreamAcl.Create(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous, 4096, 4096, security);
    }

    internal static void ParseSample(string line, out double? fps, out int temperature)
    {
        string[] fields = line.Split('|');
        fps = fields.Length == 2 && double.TryParse(fields[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double value) && value is >= 0 and < 10000 ? value : null;
        temperature = fields.Length == 2 && int.TryParse(fields[1], CultureInfo.InvariantCulture, out int celsius) && celsius is >= 5 and <= 125 ? celsius : -1;
    }

    private static readonly int[] DxgiPresentIds = [42, 55];
    private static readonly int[] D3d9PresentIds = [1];
    private const int KernelHistoryStart = 171;
    private const int KernelHistoryDetailedStart = 215;
    private const int KernelPresentInfo = 184;
    private static readonly int[] KernelPresentIds = [KernelHistoryStart, KernelHistoryDetailedStart, KernelPresentInfo];
    internal enum PresentKind { None, Runtime, KernelHistory, KernelHistoryDetailed, KernelPresent }

    internal static bool IsPresent(Guid provider, int id) => (provider == Dxgi && DxgiPresentIds.Contains(id)) || (provider == D3d9 && D3d9PresentIds.Contains(id));

    internal static PresentKind ClassifyPresent(TraceEvent data)
    {
        Guid provider = data.ProviderGuid;
        int id = (int)data.ID;
        if (IsPresent(provider, id))
        {
            if (provider == Dxgi && (ReadUInt32(data, data.PointerSize) & 1) != 0) return PresentKind.None;
            return PresentKind.Runtime;
        }
        if (provider != DxgKrnl || !KernelPresentIds.Contains(id)) return PresentKind.None;
        if (id == KernelPresentInfo) return PresentKind.KernelPresent;
        uint model = ReadUInt32(data, 2 * data.PointerSize);
        return model is 1 or 6 ? PresentKind.None : id == KernelHistoryStart ? PresentKind.KernelHistory : PresentKind.KernelHistoryDetailed;
    }

    private static uint ReadUInt32(TraceEvent data, int offset) => data.EventDataLength >= offset + sizeof(uint) && data.DataStart != IntPtr.Zero
        ? unchecked((uint)Marshal.ReadInt32(data.DataStart, offset)) : 0;

    internal static long SelectFrameCount(long runtime, long history, long detailedHistory, long present) =>
        runtime > 0 ? runtime : Math.Max(history, detailedHistory) is > 0 and var kernelHistory ? kernelHistory : present;
    internal static async Task RunHelperAsync(int parentId, string nonce)
    {
        // Elevated code writes no files, follows no user-provided path and receives
        // only a PID over a same-user pipe authenticated against the parent PID.
        if (!Guid.TryParseExact(nonce, "N", out _) || parentId <= 0) return;
        using var identity = WindowsIdentity.GetCurrent();
        if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator)) return;
        try
        {
            using var parent = Process.GetProcessById(parentId);
            string? parentPath = parent.MainModule?.FileName;
            string executable = Environment.ProcessPath!;
            if (!string.Equals(parentPath, executable, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(parentPath, DesktopProcess.PhysicalFilePath(executable), StringComparison.OrdinalIgnoreCase)) return;
            using var stop = new CancellationTokenSource();
            using var pipe = new NamedPipeClientStream(".", "DynamicIsland.Fps." + nonce, PipeDirection.InOut, PipeOptions.Asynchronous, TokenImpersonationLevel.Identification);
            using var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(stop.Token); connectTimeout.CancelAfter(10000);
            await pipe.ConnectAsync(connectTimeout.Token).ConfigureAwait(false);
            if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle, out uint server) || server != parentId) return;
            using var parentWatch = new CancellationTokenSource();
            var parentExit = WatchParentAsync(parent, stop, parentWatch.Token);
            int target = 0;
            long[] frameCounts = new long[(int)PresentKind.KernelPresent + 1];
            int traceState = 0;
            int temperature = -1;
            var consume = Task.Factory.StartNew(() => CollectPresentEvents(nonce, stop.Token, () => Volatile.Read(ref target),
                kind => Interlocked.Increment(ref frameCounts[(int)kind]),
                state => Volatile.Write(ref traceState, state)),
                CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            var sensors = Task.Run(async () =>
            {
                using var sensor = new CpuTemperatureService();
                using var poll = new PeriodicTimer(TimeSpan.FromSeconds(1));
                do { Volatile.Write(ref temperature, sensor.ReadCelsius()); }
                while (await poll.WaitForNextTickAsync(stop.Token).ConfigureAwait(false));
            });
            using var reader = new StreamReader(pipe, leaveOpen: true);
            using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
            try
            {
                while (!stop.IsCancellationRequested)
                {
                    string? command = await ReadBoundedLineAsync(reader, 12, stop.Token);
                    if (command == null || !int.TryParse(command, CultureInfo.InvariantCulture, out int pid) || pid < 0) break;
                    Volatile.Write(ref target, pid);
                    for (int index = 1; index < frameCounts.Length; index++) Interlocked.Exchange(ref frameCounts[index], 0);
                    var clock = Stopwatch.StartNew(); await Task.Delay(1000, stop.Token).ConfigureAwait(false);
                    double fps = SelectFrameCount(Interlocked.Exchange(ref frameCounts[(int)PresentKind.Runtime], 0),
                        Interlocked.Exchange(ref frameCounts[(int)PresentKind.KernelHistory], 0),
                        Interlocked.Exchange(ref frameCounts[(int)PresentKind.KernelHistoryDetailed], 0),
                        Interlocked.Exchange(ref frameCounts[(int)PresentKind.KernelPresent], 0)) / clock.Elapsed.TotalSeconds;
                    string value = Volatile.Read(ref traceState) switch
                    {
                        > 0 => fps.ToString("F2", CultureInfo.InvariantCulture),
                        0 => TraceStarting,
                        _ => "-"
                    };
                    await writer.WriteLineAsync((value + "|" + Volatile.Read(ref temperature).ToString(CultureInfo.InvariantCulture)).AsMemory(), stop.Token);
                }
            }
            finally
            {
                parentWatch.Cancel();
                stop.Cancel();
                await consume.ConfigureAwait(false);
                try { await sensors.ConfigureAwait(false); } catch (OperationCanceledException) { }
                await parentExit.ConfigureAwait(false);
            }
        }
        catch { /* Never write user-controlled filesystem paths while elevated. */ }
    }

    private static void CollectPresentEvents(string nonce, CancellationToken stop, Func<int> target, Action<PresentKind> count, Action<int> setState)
    {
        try
        {
            if (stop.IsCancellationRequested) return;
            using var trace = new TraceEventSession("DynamicIsland.Fps." + nonce) { StopOnDispose = true };
            bool hasDxgi = EnablePresentProvider(trace, Dxgi, DxgiPresentIds, 0x2);
            bool hasD3d9 = EnablePresentProvider(trace, D3d9, D3d9PresentIds, 0x2);
            bool hasKernel = EnablePresentProvider(trace, DxgKrnl, KernelPresentIds, OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000) ? 0x8000000UL : 0x1UL);
            if (!(hasDxgi || hasD3d9 || hasKernel)) return;
            var source = trace.Source;
            source.AllEvents += data =>
            {
                int selected = target();
                if (selected == 0 || data.ProcessID != selected) return;
                var kind = ClassifyPresent(data);
                if (kind != PresentKind.None) count(kind);
            };
            using var cancel = stop.Register(() => trace.Stop(noThrow: true));
            if (stop.IsCancellationRequested) return;
            setState(1);
            source.Process();
        }
        catch { }
        finally { setState(-1); }
    }

    private static bool EnablePresentProvider(TraceEventSession trace, Guid provider, int[] eventIds, ulong keywords)
    {
        try
        {
            trace.EnableProvider(provider, TraceEventLevel.Informational, keywords, new TraceEventProviderOptions { EventIDsToEnable = eventIds });
            return true;
        }
        catch { return false; }
    }

    private static async Task WatchParentAsync(Process parent, CancellationTokenSource stop, CancellationToken cancellation)
    {
        try { await parent.WaitForExitAsync(cancellation).ConfigureAwait(false); stop.Cancel(); }
        catch (OperationCanceledException) { }
    }
    private static async Task<string?> ReadBoundedLineAsync(StreamReader reader, int max, CancellationToken cancellation)
    {
        char[] buffer = new char[max]; int count = 0;
        while (count < max)
        {
            if (await reader.ReadAsync(buffer.AsMemory(count, 1), cancellation) == 0) return null;
            if (buffer[count] == '\n') return new string(buffer, 0, count).TrimEnd('\r'); count++;
        }
        return null;
    }
    [DllImport("kernel32.dll")] private static extern bool GetNamedPipeClientProcessId(Microsoft.Win32.SafeHandles.SafePipeHandle pipe, out uint pid);
    [DllImport("kernel32.dll")] private static extern bool GetNamedPipeServerProcessId(Microsoft.Win32.SafeHandles.SafePipeHandle pipe, out uint pid);
    public void Dispose() { _stop.Cancel(); _pipe?.Dispose(); }
}
