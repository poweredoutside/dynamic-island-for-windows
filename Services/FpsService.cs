using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
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
    private Process? _helper;
    private NamedPipeServerStream? _pipe;
    internal int TargetPid;
    internal double? Fps;
    internal int CpuTemperatureC = -1;
    internal string Status = "Измерение игры выключено.";
    private static readonly Guid Dxgi = new("CA11C036-0102-4A2D-A6AD-F03CFED5D3C9");
    private static readonly Guid D3d9 = new("783ACA0A-790E-4D7F-8451-AA850511C6B9");

    internal async Task StartAsync()
    {
        _pipe = new("DynamicIsland.Fps." + _nonce, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        try
        {
            Status = "Ожидание разрешения Windows…";
            _helper = Process.Start(new ProcessStartInfo(Environment.ProcessPath!)
            { Arguments = $"--fps-helper {Environment.ProcessId} {_nonce}", Verb = "runas", UseShellExecute = true, WindowStyle = ProcessWindowStyle.Hidden });
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token); timeout.CancelAfter(TimeSpan.FromSeconds(45));
            await _pipe.WaitForConnectionAsync(timeout.Token);
            if (!GetNamedPipeClientProcessId(_pipe.SafePipeHandle, out uint client) || client != _helper?.Id) throw new IOException("Unexpected FPS helper");
            using var writer = new StreamWriter(_pipe, leaveOpen: true) { AutoFlush = true };
            using var reader = new StreamReader(_pipe, leaveOpen: true);
            Status = "ETW подключён. Ожидаю игру DirectX.";
            while (!_stop.IsCancellationRequested)
            {
                await writer.WriteLineAsync(TargetPid.ToString(System.Globalization.CultureInfo.InvariantCulture));
                string? line = await ReadBoundedLineAsync(reader, 64, _stop.Token);
                if (line == null) break;
                ParseSample(line, out Fps, out CpuTemperatureC);
                Status = Fps is > 0 ? "Измеряется частота Present игры (DirectX)." : "Нет кадров DirectX у выбранного окна.";
            }
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) { Status = "Разрешение администратора отменено."; }
        catch (OperationCanceledException) { Status = "Сеанс измерения остановлен."; }
        catch (Exception ex) { AppLog.Error("FPS helper connection", ex); Status = "Счётчик игры недоступен. Попробуйте включить его снова."; }
        finally { Fps = null; CpuTemperatureC = -1; _pipe?.Dispose(); }
    }

    internal static void ParseSample(string line, out double? fps, out int temperature)
    {
        string[] fields = line.Split('|');
        fps = fields.Length == 2 && double.TryParse(fields[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double value) && value is >= 0 and < 10000 ? value : null;
        temperature = fields.Length == 2 && int.TryParse(fields[1], out int celsius) && celsius is >= 5 and <= 125 ? celsius : -1;
    }

    internal static bool IsPresent(Guid provider, int id) => (provider == Dxgi && id is 42 or 55) || (provider == D3d9 && id == 1);
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
            if (!string.Equals(parent.MainModule?.FileName, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase)) return;
            using var stop = new CancellationTokenSource();
            _ = parent.WaitForExitAsync().ContinueWith(_ => stop.Cancel(), TaskScheduler.Default);
            using var pipe = new NamedPipeClientStream(".", "DynamicIsland.Fps." + nonce, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            using var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(stop.Token); connectTimeout.CancelAfter(10000);
            await pipe.ConnectAsync(connectTimeout.Token);
            if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle, out uint server) || server != parentId) return;
            using var trace = new TraceEventSession("DynamicIsland.Fps." + nonce) { StopOnDispose = true };
            int target = 0; long frames = 0;
            trace.Source.Dynamic.All += data =>
            {
                if (data.ProcessID != Volatile.Read(ref target) || !IsPresent(data.ProviderGuid, (int)data.ID)) return;
                try { if (data.PayloadByName("Flags") is int flags && (flags & 1) != 0) return; } catch { }
                Interlocked.Increment(ref frames);
            };
            trace.EnableProvider(Dxgi, TraceEventLevel.Verbose, 0x8000000000000002);
            trace.EnableProvider(D3d9, TraceEventLevel.Verbose, 0x8000000000000002);
            var consume = Task.Run(() => trace.Source.Process());
            using var reader = new StreamReader(pipe, leaveOpen: true);
            using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
            using var temperature = new CpuTemperatureService();
            try
            {
                while (!stop.IsCancellationRequested)
                {
                    string? command = await ReadBoundedLineAsync(reader, 12, stop.Token);
                    if (command == null || !int.TryParse(command, out int pid) || pid < 0) break;
                    Volatile.Write(ref target, pid); Interlocked.Exchange(ref frames, 0);
                    var clock = Stopwatch.StartNew(); await Task.Delay(1000, stop.Token);
                    double fps = Interlocked.Exchange(ref frames, 0) / clock.Elapsed.TotalSeconds;
                    int celsius = temperature.ReadCelsius();
                    await writer.WriteLineAsync(fps.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + "|" + celsius.ToString(System.Globalization.CultureInfo.InvariantCulture));
                }
            }
            finally { trace.Stop(); await consume; }
        }
        catch { /* Never write user-controlled filesystem paths while elevated. */ }
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
    public void Dispose() { _stop.Cancel(); _pipe?.Dispose(); _helper?.Dispose(); }
}
