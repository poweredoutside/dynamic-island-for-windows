using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Xml.Linq;
using Microsoft.Win32;
using Windows.ApplicationModel;
using Windows.Management.Deployment;
using Windows.System;

namespace DynamicIslandSetup;

internal sealed record InstallerIdentity(string Name, string Publisher, Version Version,
    ProcessorArchitecture Architecture, string ApplicationId)
{
    internal static InstallerIdentity Read(Stream stream)
    {
        XNamespace ns = "http://schemas.microsoft.com/appx/manifest/foundation/windows10";
        var root = XDocument.Load(stream).Root ?? throw new InvalidDataException("Пустой манифест MSIX.");
        var identity = root.Element(ns + "Identity");
        var application = root.Element(ns + "Applications")?.Elements(ns + "Application").Single();
        string? publisher = (string?)identity?.Attribute("Publisher");
        string? appId = (string?)application?.Attribute("Id");
        if ((string?)identity?.Attribute("Name") != "DynamicIslandWin" || string.IsNullOrWhiteSpace(publisher) ||
            string.IsNullOrWhiteSpace(appId) || !Version.TryParse((string?)identity?.Attribute("Version"), out var version) ||
            !Enum.TryParse((string?)identity?.Attribute("ProcessorArchitecture"), ignoreCase: true, out ProcessorArchitecture architecture))
            throw new InvalidDataException("Не удалось прочитать идентификатор пакета OC - Dynamic Island.");
        return new("DynamicIslandWin", publisher, version, architecture, appId);
    }

    internal string AppId(Package package) => package.Id.FamilyName + "!" + ApplicationId;
    internal bool Matches(Package package) => PackageVersion(package) == Version && package.Id.Architecture == Architecture;
    internal static Version PackageVersion(Package package)
    {
        var version = package.Id.Version;
        return new Version(version.Major, version.Minor, version.Build, version.Revision);
    }
}

internal static class PackageInstallation
{
    internal static InstallerIdentity ReadIdentity()
    {
        using var stream = InstallerPayload.OpenResource("AppxManifest.xml");
        return InstallerIdentity.Read(stream);
    }

    internal static Package? FindInstalled(PackageManager manager, InstallerIdentity identity)
        => manager.FindPackagesForUserWithPackageTypes("", identity.Name, identity.Publisher, PackageTypes.Main)
            .OrderByDescending(InstallerIdentity.PackageVersion).FirstOrDefault();

    internal static async Task<(int ExitCode, string Output)> InstallAsync(InstallerPayload payload, bool installDriver,
        Action<string>? status)
    {
        status?.Invoke("DI_STATUS:Checking");
        var identity = ReadIdentity();
        PackageManager? manager = null;
        Package? installed = null;
        if (!installDriver)
        {
            try
            {
                var query = new PackageManager();
                installed = FindInstalled(query, identity);
                if (installed != null && identity.Matches(installed) && installed.Status.VerifyIsOK())
                    return (0, Result(identity, installed, alreadyInstalled: true));
                if ((installed == null || InstallerIdentity.PackageVersion(installed) < identity.Version) && HasTrustedPublisher())
                    manager = query;
            }
            catch (Exception ex) when (ex is COMException or UnauthorizedAccessException or CryptographicException)
            {
                // The existing signed PowerShell installer also handles system policy and certificates.
            }
        }

        if (manager == null || (installed != null && !await TryCloseAppAsync(installed, status).ConfigureAwait(false)))
        {
            status?.Invoke("DI_STATUS:Preparing");
            await payload.ExtractAsync(installDriver).ConfigureAwait(false);
            return await InstallerPayload.RunPowerShellAsync(Path.Combine(payload.DirectoryPath, "Install.ps1"),
                installDriver ? ["-NoLaunch", "-CloseRunningApp", "-Gui", "-InstallCpuTemperatureDriver"] : ["-NoLaunch", "-CloseRunningApp", "-Gui"],
                status).ConfigureAwait(false);
        }

        status?.Invoke("DI_STATUS:Preparing");
        string packagePath = await payload.ExtractPackageAsync().ConfigureAwait(false);
        status?.Invoke("DI_STATUS:Installing");
        var operation = manager.AddPackageAsync(new Uri(packagePath), null, DeploymentOptions.None);
        try
        {
            var progress = new Progress<DeploymentProgress>(value => status?.Invoke("DI_PROGRESS:" + value.percentage));
            var result = await operation.AsTask(progress).ConfigureAwait(false);
            if (result.ExtendedErrorCode is { HResult: < 0 } error)
                throw new InvalidOperationException(result.ErrorText, error);
        }
        catch (Exception ex) when (ex is COMException or UnauthorizedAccessException)
        {
            if (operation.ErrorCode is { } error)
                throw new InvalidOperationException("Windows не удалось установить пакет MSIX. " + error.Message, ex);
            throw;
        }
        var registered = FindInstalled(manager, identity);
        if (registered == null || !identity.Matches(registered))
            throw new InvalidOperationException("Windows не зарегистрировала ожидаемую версию приложения.");
        try
        {
            using var startup = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
            startup?.DeleteValue("DynamicIslandWin", throwOnMissingValue: false);
        }
        catch (UnauthorizedAccessException) { }
        return (0, Result(identity, registered, alreadyInstalled: false));
    }

    private static bool HasTrustedPublisher()
    {
        using var stream = InstallerPayload.OpenResource("DynamicIsland.cer");
        using var bytes = new MemoryStream();
        stream.CopyTo(bytes);
        using var certificate = new X509Certificate2(bytes.ToArray());
        using var store = new X509Store(StoreName.TrustedPeople, StoreLocation.LocalMachine);
        store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
        return store.Certificates.Contains(certificate);
    }

    private static async Task<bool> TryCloseAppAsync(Package installed, Action<string>? status)
    {
        var processes = Process.GetProcessesByName("DynamicIslandWin");
        try
        {
            if (processes.Length == 0) return true;
            // Use the app's existing graceful shutdown signal, without starting
            // a second copy of the app just to relay it.
            if (!EventWaitHandle.TryOpenExisting(@"Local\DynamicIslandWin.Shutdown", out var shutdown)) return false;
            status?.Invoke("DI_STATUS:Closing");
            using (shutdown) shutdown.Set();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try
            {
                await Task.WhenAll(processes.Select(process => process.WaitForExitAsync(timeout.Token))).ConfigureAwait(false);
                return true;
            }
            catch (OperationCanceledException)
            {
                throw new TimeoutException($"Закройте OC - Dynamic Island {InstallerIdentity.PackageVersion(installed)} через меню в трее и повторите установку.");
            }
        }
        finally { foreach (var process in processes) process.Dispose(); }
    }

    private static string Result(InstallerIdentity identity, Package package, bool alreadyInstalled)
        => (alreadyInstalled ? "DI_STATUS:AlreadyInstalled\n" : "") + "DI_APPID:" + identity.AppId(package);
}
