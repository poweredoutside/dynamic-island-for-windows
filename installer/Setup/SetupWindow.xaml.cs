using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using DynamicIslandWin.Services;

namespace DynamicIslandSetup;

internal enum SetupPage { Welcome, Options, Installing, Complete, Error }

internal sealed partial class SetupWindow : Window
{
    internal const int ClientWidth = 640;
    internal const int ClientHeight = 460;

    private readonly bool _updateMode;
    private SetupPage _page;
    private bool _busy;
    private bool _installingAudio;
    private string? _errorLog;
    private string? _appId;

    internal SetupWindow(bool updateMode)
    {
        _updateMode = updateMode;
        InitializeComponent();
        Client.Width = ClientWidth;
        Client.Height = ClientHeight;
        if (updateMode) Title = "Обновление — OC - Dynamic Island";
        ShowPage(updateMode ? SetupPage.Installing : SetupPage.Welcome);
        Closing += (_, e) => e.Cancel = _busy;
        if (updateMode) Loaded += async (_, _) => await InstallAsync();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        if (SystemParameters.HighContrast) return;
        var handle = new WindowInteropHelper(this).Handle;
        const int useImmersiveDarkMode = 20;
        int enabled = 1;
        if (DwmSetWindowAttribute(handle, useImmersiveDarkMode, ref enabled, sizeof(int)) != 0)
            DwmSetWindowAttribute(handle, 19, ref enabled, sizeof(int)); // Earlier Windows 10 builds.
        const int captionColor = 35;
        const int textColor = 36;
        int charcoal = 0x2B2227; // COLORREF, RGB #27222B.
        int white = 0xFAFAFA;
        DwmSetWindowAttribute(handle, captionColor, ref charcoal, sizeof(int));
        DwmSetWindowAttribute(handle, textColor, ref white, sizeof(int));
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Key != Key.Escape) return;
        e.Handled = true;
        if (!_busy) Close();
    }

    private void ShowPage(SetupPage page, string? detail = null)
    {
        _page = page;
        var content = page switch
        {
            SetupPage.Welcome => (
                "Вас приветствует мастер установки OC - Dynamic Island",
                $"Мастер установит OC - Dynamic Island {InstallerPayload.Version} для Windows x64.",
                "Нажмите «Далее», чтобы выбрать параметры установки."),
            SetupPage.Options => (
                "Параметры установки",
                $"Готово к установке OC - Dynamic Island {InstallerPayload.Version}.",
                "При обновлении сохраняются настройки приложения. Для установки сертификата сборки Windows может запросить подтверждение."),
            SetupPage.Installing => (
                _updateMode ? "Обновление OC - Dynamic Island" : "Установка OC - Dynamic Island",
                "Подготовка файлов…",
                "Дождитесь завершения установки."),
            SetupPage.Complete => (
                "Завершение установки OC - Dynamic Island",
                detail ?? "Приложение установлено и доступно в меню «Пуск».",
                ""),
            _ => (
                "Не удалось установить OC - Dynamic Island",
                "Проверьте запрос Windows. Если приложение ещё открыто, закройте его через меню в трее и повторите установку.",
                "")
        };
        (PageHeading.Text, PageIntro.Text, PageNote.Text) = content;
        PageNote.Visibility = Visible(PageNote.Text.Length > 0);
        Options.Visibility = Visible(page == SetupPage.Options);
        Completion.Visibility = Visible(page == SetupPage.Complete);
        InstallProgress.Visibility = Visible(page == SetupPage.Installing);
        InstallProgress.IsIndeterminate = page == SetupPage.Installing;
        LogButton.Visibility = Visible(page == SetupPage.Error && _errorLog != null);

        BackButton.Visibility = Visible(page == SetupPage.Options);
        Primary.Visibility = Visible(page != SetupPage.Installing);
        Primary.Content = page switch
        {
            SetupPage.Welcome => "_Далее",
            SetupPage.Options => "_Установить",
            SetupPage.Complete => "_Готово",
            _ => "_Повторить"
        };
        Primary.Margin = new Thickness(0, 0, page == SetupPage.Complete ? 0 : 10, 0);
        CancelButton.Visibility = Visible(page != SetupPage.Complete);
        CancelButton.Content = page == SetupPage.Error ? "Закрыть" : "Отмена";
        CancelButton.IsEnabled = page != SetupPage.Installing;
        if (IsLoaded && Primary.IsVisible) Primary.Focus();

        static Visibility Visible(bool value) => value ? Visibility.Visible : Visibility.Collapsed;
    }

    internal void ShowPreviewPage(SetupPage page)
    {
        if (page == SetupPage.Error) _errorLog = "setup.log";
        ShowPage(page);
        if (page == SetupPage.Installing)
        {
            InstallProgress.IsIndeterminate = false;
            InstallProgress.Value = 45;
            PageIntro.Text = "Установка приложения…";
        }
    }

    private void Back_Click(object sender, RoutedEventArgs e) => ShowPage(SetupPage.Welcome);
    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    private async void Primary_Click(object sender, RoutedEventArgs e)
    {
        switch (_page)
        {
            case SetupPage.Welcome: ShowPage(SetupPage.Options); break;
            case SetupPage.Options:
            case SetupPage.Error: await InstallAsync(); break;
            case SetupPage.Complete: Finish(); break;
        }
    }

    private void OpenLog_Click(object sender, RoutedEventArgs e)
    {
        if (_errorLog == null) return;
        try { Process.Start(new ProcessStartInfo("notepad.exe") { UseShellExecute = true, ArgumentList = { _errorLog } }); }
        catch (Exception ex) { PageIntro.Text = "Не удалось открыть журнал. " + ex.Message; }
    }

    private void Finish()
    {
        if (LaunchApp.IsChecked == true && _appId != null)
        {
            try
            {
                Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = false,
                    ArgumentList = { @"shell:AppsFolder\" + _appId } });
            }
            catch (Exception ex) { PageIntro.Text = "Не удалось открыть приложение. " + ex.Message; return; }
        }
        Close();
    }

    private async Task InstallAsync()
    {
        if (_busy) return;
        _busy = true;
        _installingAudio = false;
        _errorLog = null;
        ShowPage(SetupPage.Installing);
        try
        {
            bool installDriver = TemperatureDriver.IsChecked == true;
            bool installEqualizer = EqualizerApo.IsChecked == true;
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
                .FirstOrDefault(line => Regex.IsMatch(line, @"^DI_APPID:DynamicIslandWin_[a-z0-9]{13}!App$"))?[9..];
            if (_appId == null) throw new InvalidOperationException("Установка не подтвердила регистрацию приложения.");

            string detail = result.Output.Contains("DI_STATUS:AlreadyInstalled", StringComparison.Ordinal)
                ? "Эта версия уже установлена. Приложение доступно в меню «Пуск»."
                : "Приложение установлено и доступно в меню «Пуск».";
            if (result.Output.Contains("DI_WARNING:TemperatureDriver", StringComparison.Ordinal))
                detail += " Драйвер температуры процессора не установлен. Его можно повторно установить в игровом центре.";
            if (installEqualizer && !await InstallEqualizerAsync())
                detail += " Установка Equalizer APO не завершилась. Повторить её можно в разделе «Музыка».";
            ShowPage(SetupPage.Complete, detail);
        }
        catch (Exception ex)
        {
            string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DynamicIslandWin", "Setup");
            try
            {
                Directory.CreateDirectory(directory);
                string log = Path.Combine(directory, "setup.log");
                await File.WriteAllTextAsync(log, ex.ToString());
                _errorLog = log;
            }
            catch (Exception logError) when (logError is IOException or UnauthorizedAccessException) { }
            ShowPage(SetupPage.Error);
        }
        finally { _busy = false; }
        if (_updateMode && _page == SetupPage.Complete) Finish();
    }

    private void HandleInstallStatus(string line)
    {
        if (!line.StartsWith("DI_STATUS:", StringComparison.Ordinal)) return;
        string phase = line[10..];
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_page != SetupPage.Installing || _installingAudio) return;
            PageIntro.Text = phase switch
            {
                "Checking" => "Проверка установленной версии…",
                "Closing" => "Закрытие приложения перед обновлением…",
                "Trust" => "Подтвердите запрос Windows…",
                "Installing" => "Установка приложения…",
                "TemperatureDriver" => "Установка драйвера датчиков температуры…",
                "Cleaning" => "Удаление временных файлов…",
                _ => PageIntro.Text
            };
            PageNote.Text = phase == "Trust"
                ? "Windows запрашивает разрешение добавить сертификат сборки. Подтвердите запрос, чтобы продолжить."
                : "Дождитесь завершения установки.";
        }));
    }

    private async Task<bool> InstallEqualizerAsync()
    {
        if (EqualizerApoInstaller.FindInstallation() != null) return true;
        _installingAudio = true;
        PageIntro.Text = "Скачивание Equalizer APO…";
        PageNote.Text = "Установщик проверит контрольную сумму файла перед запуском.";
        try
        {
            using var setup = await EqualizerApoInstaller.DownloadAsync();
            PageIntro.Text = "Установка Equalizer APO…";
            PageNote.Text = "Подтвердите запрос Windows и выберите устройство воспроизведения в окне Equalizer APO.";
            await setup.RunAsync();
            return true;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or System.Net.Http.HttpRequestException or OperationCanceledException
            or System.ComponentModel.Win32Exception or UnauthorizedAccessException or InvalidOperationException)
        {
            return false;
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
}
