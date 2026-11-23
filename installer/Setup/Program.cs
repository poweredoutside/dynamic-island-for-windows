using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DynamicIslandSetup;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--verify-payload")
        {
            try { VerifyAsync(args[1]).GetAwaiter().GetResult(); return 0; }
            catch (Exception ex) { File.WriteAllText(args[1], JsonSerializer.Serialize(new { success = false, error = ex.Message })); return 1; }
        }
        var app = new Application();
        var window = new SetupWindow(args.Length == 1 && args[0] == "--update");
        if (args.Length is 2 or 3 && args[0] == "--render-preview")
        {
            if (args.Length == 3)
            {
                if (!Enum.TryParse<SetupPage>(args[2], ignoreCase: true, out var page) || !Enum.IsDefined(page)) return 2;
                window.ShowPreviewPage(page);
            }
            var root = (FrameworkElement)window.Content;
            var size = new Size(SetupWindow.ClientWidth, SetupWindow.ClientHeight);
            root.Measure(size); root.Arrange(new Rect(size)); root.UpdateLayout();
            var bitmap = new RenderTargetBitmap(SetupWindow.ClientWidth, SetupWindow.ClientHeight, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(root);
            var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = File.Create(args[1]); png.Save(file);
            return 0;
        }
        using var mutex = new Mutex(true, @"Local\DynamicIslandWin.Setup", out bool first);
        if (!first) { MessageBox.Show("Установщик OC - Dynamic Island уже открыт.", "OC - Dynamic Island", MessageBoxButton.OK, MessageBoxImage.Information); return 1; }
        return app.Run(window);
    }

    private static async Task VerifyAsync(string reportPath)
    {
        using var payload = new InstallerPayload();
        var timing = Stopwatch.StartNew();
        await payload.ExtractAsync(includeTemperatureDriver: false);
        double extractionSeconds = timing.Elapsed.TotalSeconds;
        if (File.Exists(Path.Combine(payload.DirectoryPath, "PawnIO_setup.exe")))
            throw new Exception("Setup extracted the optional driver without being asked.");
        // Executes the same hidden process code as installation, using a planning
        // mode that cannot deploy packages or change certificate trust.
        string probe = Path.Combine(payload.DirectoryPath, "Verify.ps1");
        await File.WriteAllTextAsync(probe, "Add-Type -TypeDefinition 'using System; using System.Runtime.InteropServices; public static class ConsoleProbe { [DllImport(\"kernel32.dll\")] public static extern IntPtr GetConsoleWindow(); }'\n"
            + "[pscustomobject]@{ Plan = (& (Join-Path $PSScriptRoot 'Install.ps1') -PlanOnly); ConsoleWindowPresent = ([ConsoleProbe]::GetConsoleWindow() -ne [IntPtr]::Zero) } | ConvertTo-Json -Compress", new UTF8Encoding(true));
        var result = await InstallerPayload.RunPowerShellAsync(probe, [], null);
        if (result.ExitCode != 0) throw new Exception(result.Output);
        using var plan = JsonDocument.Parse(result.Output.Trim());
        bool console = plan.RootElement.GetProperty("ConsoleWindowPresent").GetBoolean();
        if (console) throw new Exception("PowerShell must not create a console window.");
        bool existingInstallFlowChecked = false;
        if (plan.RootElement.GetProperty("Plan").GetProperty("Action").GetString() == "AlreadyInstalled")
        {
            // This branch returns before deployment, shutdown or trust changes.
            var existing = await InstallerPayload.RunPowerShellAsync(Path.Combine(payload.DirectoryPath, "Install.ps1"),
                ["-NoLaunch", "-CloseRunningApp", "-Gui"], null);
            if (existing.ExitCode != 0 || !existing.Output.Contains("DI_APPID:DynamicIslandWin_")
                || !existing.Output.Contains("DI_STATUS:AlreadyInstalled", StringComparison.Ordinal))
                throw new Exception("Already-installed flow failed: " + existing.Output);
            existingInstallFlowChecked = true;
        }
        using (var executable = File.OpenRead(Environment.ProcessPath!))
        using (var pe = new PEReader(executable))
            if (pe.PEHeaders.PEHeader?.Subsystem != Subsystem.WindowsGui) throw new Exception("Setup must use the Windows GUI subsystem.");
        string hash;
        await using (var package = File.OpenRead(Path.Combine(payload.DirectoryPath, "DynamicIsland.msix")))
            hash = Convert.ToHexString(await SHA256.HashDataAsync(package));
        string driverHash;
        using (var driver = InstallerPayload.OpenResource("PawnIO_setup.exe"))
            driverHash = Convert.ToHexString(await SHA256.HashDataAsync(driver));
        if (driverHash != "1F519A22E47187F70A1379A48CA604981C4FCF694F4E65B734AAA74A9FBA3032") throw new Exception("Embedded PawnIO installer differs from the verified dependency.");
        await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(new
        {
            success = true, standalone = true, subsystem = "WindowsGui", hiddenPowerShell = !console, existingInstallFlowChecked,
            packagedProcess = HasPackageIdentity(),
            extractionSeconds, embeddedPackageSha256 = hash, embeddedDriverSha256 = driverHash, plan = plan.RootElement.Clone()
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static bool HasPackageIdentity()
    {
        uint length = 0;
        return GetCurrentPackageFullName(ref length, IntPtr.Zero) != 15700;
    }
    [DllImport("kernel32.dll")] private static extern int GetCurrentPackageFullName(ref uint length, IntPtr name);
}

internal sealed class InstallerPayload : IDisposable
{
    private static readonly string Root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "DynamicIslandSetup"));
    internal string DirectoryPath { get; } = Path.Combine(Root, Guid.NewGuid().ToString("N"));

    internal async Task ExtractAsync(bool includeTemperatureDriver)
    {
        Directory.CreateDirectory(DirectoryPath);
        foreach (string name in new[] { "DynamicIsland.msix", "DynamicIsland.cer", "Install.ps1" })
            await ExtractFileAsync(name);
        if (includeTemperatureDriver) await ExtractFileAsync("PawnIO_setup.exe");
    }

    internal static Stream OpenResource(string name) =>
        Assembly.GetExecutingAssembly().GetManifestResourceStream("Payload." + name)
            ?? throw new InvalidDataException("В установщике отсутствует файл " + name);

    private async Task ExtractFileAsync(string name)
    {
        using var source = OpenResource(name);
        await using var destination = new FileStream(Path.Combine(DirectoryPath, name), FileMode.CreateNew,
            FileAccess.Write, FileShare.None, bufferSize: 1, useAsync: true);
        await source.CopyToAsync(destination, 1024 * 1024);
    }

    internal static string Version
        => Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "";

    internal static async Task<(int ExitCode, string Output)> RunPowerShellAsync(
        string script, string[] arguments, Action<string>? onLine)
    {
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe"))
        {
            UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        // PowerShell 7's module path can otherwise load incompatible security and
        // Appx modules into Windows PowerShell 5.1 when setup is started from a shell.
        start.Environment.Remove("PSModulePath");
        foreach (string value in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script }.Concat(arguments))
            start.ArgumentList.Add(value);
        using var process = Process.Start(start) ?? throw new IOException("Не удалось запустить установку.");
        var output = new StringBuilder();
        object gate = new();
        async Task ReadAsync(StreamReader reader)
        {
            while (await reader.ReadLineAsync() is { } line)
            {
                lock (gate) { if (output.Length < 64000) output.AppendLine(line); }
                onLine?.Invoke(line);
            }
        }
        await Task.WhenAll(ReadAsync(process.StandardOutput), ReadAsync(process.StandardError), process.WaitForExitAsync());
        return (process.ExitCode, output.ToString());
    }

    public void Dispose()
    {
        string target = Path.GetFullPath(DirectoryPath);
        if (!target.StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return;
        try { if (Directory.Exists(target)) Directory.Delete(target, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
