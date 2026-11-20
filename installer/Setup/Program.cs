using System.ComponentModel;
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
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;

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
        if (args.Length == 2 && args[0] == "--render-preview")
        {
            var root = (FrameworkElement)window.Content;
            root.Measure(new Size(600, 460)); root.Arrange(new Rect(0, 0, 600, 460)); root.UpdateLayout();
            var bitmap = new RenderTargetBitmap(600, 460, 96, 96, PixelFormats.Pbgra32); bitmap.Render(root);
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

internal sealed class SetupWindow : Window
{
    private static readonly SolidColorBrush Ink = new(Color.FromRgb(243, 246, 255));
    private static readonly SolidColorBrush Muted = new(Color.FromRgb(172, 182, 203));
    private static readonly SolidColorBrush Mint = new(Color.FromRgb(52, 245, 197));
    private readonly TextBlock _status = new() { Text = "Готово к установке", Foreground = Ink, FontSize = 14, FontWeight = FontWeights.SemiBold };
    private readonly TextBlock _details = new() { Text = "Нажмите «Установить», чтобы продолжить.", Foreground = Muted, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 7, 0, 0) };
    private readonly ProgressBar _progress = new() { Height = 4, Foreground = Mint, Background = new SolidColorBrush(Color.FromRgb(45, 53, 70)), Margin = new Thickness(0, 20, 0, 0) };
    private readonly CheckBox _launch = new() { Content = "Запустить OC - Dynamic Island после установки", IsChecked = true, Foreground = Ink, FontSize = 12, Margin = new Thickness(0, 18, 0, 0) };
    private readonly CheckBox _temperatureDriver = new() { Content = "Установить PawnIO для температуры CPU (права администратора)", Foreground = Ink, FontSize = 12, Margin = new Thickness(0, 12, 0, 0) };
    private readonly Button _install = new() { Content = "Установить", MinWidth = 150, Padding = new Thickness(20, 11, 20, 11), Background = Mint, Foreground = new SolidColorBrush(Color.FromRgb(7, 36, 33)), FontSize = 13, FontWeight = FontWeights.SemiBold };
    private readonly Button _close = new() { Content = "Отмена", MinWidth = 96, Padding = new Thickness(18, 11, 18, 11), Margin = new Thickness(0, 0, 10, 0) };
    private bool _busy, _finished;
    private string? _errorLog;
    private string? _appId;
    private bool _driverUnavailable;
    private readonly bool _updateMode;

    internal SetupWindow(bool updateMode = false)
    {
        _updateMode = updateMode;
        Title = "Установка OC - Dynamic Island";
        Width = 616; Height = 500; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = new FontFamily("Segoe UI"); Background = new SolidColorBrush(Color.FromRgb(15, 19, 29));
        var root = new Grid { Margin = new Thickness(32, 26, 32, 26) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var heading = new StackPanel();
        heading.Children.Add(new TextBlock { Text = "●  OC - Dynamic Island", Foreground = Mint, FontSize = 11, FontWeight = FontWeights.SemiBold });
        heading.Children.Add(new TextBlock { Text = "Всё важное — на островке", Foreground = Ink, FontSize = 25, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 14, 0, 8) });
        heading.Children.Add(new TextBlock { Text = "Установка версии " + InstallerPayload.Version + "  ·  Windows x64", Foreground = Muted, FontSize = 12 });
        root.Children.Add(heading);
        var cardContent = new StackPanel();
        cardContent.Children.Add(_status); cardContent.Children.Add(_details); cardContent.Children.Add(_progress); cardContent.Children.Add(_temperatureDriver); cardContent.Children.Add(_launch);
        var card = new Border { CornerRadius = new CornerRadius(16), Padding = new Thickness(22),
            Background = new LinearGradientBrush(Color.FromRgb(32, 40, 57), Color.FromRgb(24, 31, 45), 65),
            BorderBrush = new SolidColorBrush(Color.FromRgb(57, 69, 88)), BorderThickness = new Thickness(1),
            Child = cardContent, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetRow(card, 1); root.Children.Add(card);
        var footer = new StackPanel();
        footer.Children.Add(new TextBlock { Text = "Настройки сохранятся при обновлении. Для тестового сертификата Windows может запросить подтверждение администратора.",
            Foreground = Muted, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 18) });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(_close); buttons.Children.Add(_install); footer.Children.Add(buttons);
        Grid.SetRow(footer, 2); root.Children.Add(footer); Content = root;
        _close.Click += (_, _) => Close(); _install.Click += Install_Click;
        Closing += (_, e) => { if (_busy) e.Cancel = true; };
        if (updateMode) Loaded += async (_, _) => await InstallAsync();
    }

    private async void Install_Click(object sender, RoutedEventArgs e)
        => await InstallAsync();

    private async Task InstallAsync()
    {
        if (_busy) return;
        if (_finished)
        {
            if (_errorLog != null)
            {
                Process.Start(new ProcessStartInfo("notepad.exe") { UseShellExecute = true, ArgumentList = { _errorLog } });
                return;
            }
            if (_launch.IsChecked == true && _appId != null)
            {
                try
                {
                    Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = false,
                        ArgumentList = { @"shell:AppsFolder\" + _appId } });
                }
                catch (Exception ex) { _details.Text = "Не удалось открыть приложение: " + ex.Message; return; }
            }
            Close(); return;
        }
        _busy = true; _close.IsEnabled = false; _install.IsEnabled = false; _launch.IsEnabled = false; _temperatureDriver.IsEnabled = false;
        _progress.IsIndeterminate = true; _status.Text = "Подготовка файлов…";
        _details.Text = "Пакет приложения уже включён в ocdynamicisland.exe. Скачивать дополнительные файлы не нужно.";
        try
        {
            bool installDriver = _temperatureDriver.IsChecked == true;
            var result = await Task.Run(async () =>
            {
                using var payload = new InstallerPayload();
                try
                {
                    await payload.ExtractAsync(installDriver);
                    return await InstallerPayload.RunPowerShellAsync(Path.Combine(payload.DirectoryPath, "Install.ps1"),
                        installDriver ? ["-NoLaunch", "-CloseRunningApp", "-Gui", "-InstallCpuTemperatureDriver"] : ["-NoLaunch", "-CloseRunningApp", "-Gui"],
                        HandleInstallStatus);
                }
                finally { HandleInstallStatus("DI_STATUS:Cleaning"); }
            });
            if (result.ExitCode != 0) throw new InvalidOperationException(result.Output);
            _appId = result.Output.Split('\n').Select(line => line.TrimEnd('\r'))
                .FirstOrDefault(line => System.Text.RegularExpressions.Regex.IsMatch(line, @"^DI_APPID:DynamicIslandWin_[a-z0-9]{13}!App$"))?[9..];
            if (_appId == null) throw new InvalidOperationException("Установка не подтвердила регистрацию приложения.");
            _driverUnavailable = result.Output.Contains("DI_WARNING:TemperatureDriver", StringComparison.Ordinal);
            _status.Text = "OC - Dynamic Island установлен";
            _details.Text = result.Output.Contains("DI_STATUS:AlreadyInstalled", StringComparison.Ordinal)
                ? "Эта версия уже установлена. Сборка с тем же номером не заменяет установленное приложение."
                : "Всё готово. Приложение доступно в меню «Пуск». При первом запуске разрешите доступ к уведомлениям.";
            if (_driverUnavailable) _details.Text += " Драйвер температуры CPU не установлен; повторить можно в игровом центре.";
            _progress.IsIndeterminate = false; _progress.Value = 100;
            _close.Content = "Закрыть"; _install.Content = "Готово"; _finished = true;
        }
        catch (Exception ex)
        {
            string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DynamicIslandWin", "Setup");
            try { Directory.CreateDirectory(directory); _errorLog = Path.Combine(directory, "setup.log"); await File.WriteAllTextAsync(_errorLog, ex.ToString()); }
            catch { _errorLog = null; }
            _status.Text = "Не удалось установить приложение";
            _details.Text = "Если запрос Windows был отменён, запустите установку ещё раз. Если приложение не закрылось, выйдите из него через меню в трее. Подробности доступны в журнале.";
            _install.Content = _errorLog != null ? "Открыть журнал" : "Повторить";
            _close.Content = "Закрыть"; _progress.IsIndeterminate = false; _progress.Value = 0;
            _finished = _errorLog != null;
        }
        finally { _busy = false; _close.IsEnabled = true; _install.IsEnabled = true; _launch.IsEnabled = true; _temperatureDriver.IsEnabled = true; }
        if (_updateMode && _finished && _errorLog == null) await InstallAsync();
    }

    private void HandleInstallStatus(string line)
    {
        if (!line.StartsWith("DI_STATUS:", StringComparison.Ordinal)) return;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!_busy) return;
            _status.Text = line[10..] switch
            {
                "Checking" => "Проверка установленной версии…",
                "Closing" => "Закрытие приложения перед обновлением…",
                "Trust" => "Подтвердите запрос Windows…",
                "Installing" => "Установка OC - Dynamic Island…",
                "TemperatureDriver" => "Установка драйвера датчиков CPU…",
                "Cleaning" => "Удаление временных файлов…",
                _ => _status.Text
            };
            if (line == "DI_STATUS:Trust") _details.Text = "Windows запрашивает разрешение добавить сертификат тестовой сборки. Если отменить запрос, установка не продолжится.";
        }));
    }
}
