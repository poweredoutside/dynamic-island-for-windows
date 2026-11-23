using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DynamicIslandWin.Services;

internal static class GitHubUpdateService
{
    internal const string RepositoryName = "poweredoutside/dynamic-island-for-windows";
    internal static readonly Uri ReleasesPage = new("https://github.com/" + RepositoryName + "/releases");
    internal const string InstallerFileName = "ocdynamicisland.exe";
    internal static Version CurrentVersion => typeof(GitHubUpdateService).Assembly.GetName().Version ?? new Version(0, 0, 0, 0);
    internal static bool IsNewerVersion(Version available, Version installed)
    {
        static Version Normalize(Version value) => new(value.Major, value.Minor, Math.Max(0, value.Build), Math.Max(0, value.Revision));
        return Normalize(available) > Normalize(installed);
    }
    internal sealed record Release(Version Version, string Name, Uri Page, Uri Installer, string InstallerName, Uri Checksums);
    private static readonly HttpClient Http = CreateHttp();
    private static HttpClient CreateHttp()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("DynamicIslandWin/" + CurrentVersion.ToString(3)); return client;
    }
    internal static Release Parse(string json)
    {
        if (json.Length > 2_000_000) throw new FormatException("Ответ сервера слишком большой.");
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        string tag = root.GetProperty("tag_name").GetString() ?? "";
        if (!Version.TryParse(tag.TrimStart('v', 'V'), out var version)) throw new FormatException("Версия релиза должна иметь вид v1.2.3.");
        if (root.TryGetProperty("draft", out var draft) && draft.GetBoolean()) throw new FormatException("Релиз не опубликован.");
        var assets = root.GetProperty("assets").EnumerateArray().Select(a => (Name: a.GetProperty("name").GetString() ?? "", Url: a.GetProperty("browser_download_url").GetString() ?? "")).ToArray();
        var installer = assets.FirstOrDefault(a => a.Name.Equals(InstallerFileName, StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrEmpty(installer.Name)) installer = assets.FirstOrDefault(a => a.Name.Equals($"DynamicIsland-Setup-{version.ToString(3)}-x64.exe", StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrEmpty(installer.Name)) installer = assets.FirstOrDefault(a => a.Name.Equals("Setup.exe", StringComparison.OrdinalIgnoreCase));
        var hashes = assets.FirstOrDefault(a => a.Name.Equals("SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrEmpty(installer.Name)) throw new FormatException("В релизе нет установщика ocdynamicisland.exe.");
        if (string.IsNullOrEmpty(hashes.Name)) throw new FormatException("В релизе нет SHA256SUMS.txt. Обновление отклонено.");
        string assetPath = $"/{RepositoryName}/releases/download/{Uri.EscapeDataString(tag)}/";
        Uri Asset((string Name, string Url) asset)
        {
            if (!Uri.TryCreate(asset.Url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || uri.Host != "github.com"
                || !uri.IsDefaultPort || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0
                || !uri.AbsolutePath.Equals(assetPath + Uri.EscapeDataString(asset.Name), StringComparison.OrdinalIgnoreCase))
                throw new FormatException("Файл обновления должен находиться в том же репозитории GitHub.");
            return uri;
        }
        return new(version, root.TryGetProperty("name", out var name) ? name.GetString() ?? tag : tag,
            new Uri(ReleasesPage + "/tag/" + Uri.EscapeDataString(tag)), Asset(installer), installer.Name, Asset(hashes));
    }
    internal static async Task<Release> CheckAsync(CancellationToken cancellation)
    {
        using var response = await Http.GetAsync("https://api.github.com/repos/" + RepositoryName + "/releases/latest", cancellation);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > 2_000_000) throw new FormatException("Ответ GitHub слишком большой.");
        return Parse(await response.Content.ReadAsStringAsync(cancellation));
    }
    internal static string Digest(string sums, string name)
    {
        if (sums.Length > 128_000) throw new FormatException("Некорректный файл контрольных сумм.");
        var matches = sums.Split('\n').Select(line => Regex.Match(line.Trim(), @"^([a-fA-F0-9]{64})\s+\*?(.+)$", RegexOptions.CultureInvariant))
            .Where(m => m.Success && m.Groups[2].Value.Equals(name, StringComparison.Ordinal)).ToArray();
        if (matches.Length != 1) throw new FormatException("Нет однозначной SHA-256 для установщика. Обновление отклонено.");
        return matches[0].Groups[1].Value;
    }
    internal static async Task<string> DownloadAsync(Release release, IProgress<double> progress, CancellationToken cancellation)
    {
        using var checksums = await Http.GetAsync(release.Checksums, cancellation); checksums.EnsureSuccessStatusCode();
        if (checksums.Content.Headers.ContentLength > 128_000) throw new FormatException("Некорректные контрольные суммы.");
        string expected = Digest(await checksums.Content.ReadAsStringAsync(cancellation), release.InstallerName);
        string root = UpdateDirectory();
        RejectLinks(root); Directory.CreateDirectory(root); RejectLinks(root);
        string directory = Path.Combine(root, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory); RejectLinks(directory);
        string path = Path.Combine(directory, InstallerFileName);
        try
        {
            using var response = await Http.GetAsync(release.Installer, HttpCompletionOption.ResponseHeadersRead, cancellation); response.EnsureSuccessStatusCode();
            const long maxSize = 750_000_000;
            if (response.Content.Headers.ContentLength > maxSize) throw new IOException("Установщик слишком большой.");
            await using (var input = await response.Content.ReadAsStreamAsync(cancellation))
            await using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, true))
            using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                byte[] buffer = new byte[64 * 1024]; long total = 0; int count;
                while ((count = await input.ReadAsync(buffer, cancellation)) > 0)
                {
                    total += count; if (total > maxSize) throw new IOException("Установщик слишком большой.");
                    hash.AppendData(buffer, 0, count); await output.WriteAsync(buffer.AsMemory(0, count), cancellation);
                    if (response.Content.Headers.ContentLength is > 0) progress.Report(Math.Min(99, total * 100.0 / response.Content.Headers.ContentLength.Value));
                }
                if (!CryptographicOperations.FixedTimeEquals(hash.GetHashAndReset(), Convert.FromHexString(expected))) throw new CryptographicException("SHA-256 установщика не совпадает. Обновление отклонено.");
            }
            path = await VerifyInstallerAsync(path, cancellation);
            progress.Report(100); return path;
        }
        catch { if (File.Exists(path)) File.Delete(path); throw; }
    }

    internal static string UpdateDirectory()
    {
        string root;
        try { root = Windows.Storage.ApplicationData.Current.LocalCacheFolder.Path; }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException)
        { root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData); }
        return Path.Combine(root, "DynamicIslandWin", "Updates");
    }

    internal static async Task<string> VerifyInstallerAsync(string path, CancellationToken cancellation)
    {
            // Resolve MSIX redirection before passing paths to a desktop process.
            path = DesktopProcess.PhysicalFilePath(path);
            string directory = Path.GetDirectoryName(path)!;
            RejectLinks(directory);
            // Windows validates the Authenticode signature against the local trust store.
            // Paths travel as a separate argument, never concatenated into shell code.
            const string script = "param([string]$InstallerPath) $ErrorActionPreference='Stop'; Import-Module ($PSHOME + '\\Modules\\Microsoft.PowerShell.Security\\Microsoft.PowerShell.Security.psd1'); $s=Microsoft.PowerShell.Security\\Get-AuthenticodeSignature -LiteralPath $InstallerPath; if($s.Status -eq 'Valid' -and $s.SignerCertificate.Subject -eq 'CN=DynamicIslandWin'){exit 0}; exit (10 + [int]$s.Status)";
            string verify = Path.Combine(directory, "verify.ps1"); RejectLinks(directory); await File.WriteAllTextAsync(verify, script, cancellation);
            verify = DesktopProcess.PhysicalFilePath(verify);
            try
            {
                using var process = DesktopProcess.Start(Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe"),
                    "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", verify, "-InstallerPath", path);
                try { await process.WaitForExitAsync(cancellation); }
                catch (OperationCanceledException) { if (!process.HasExited) process.Kill(); throw; }
                if (process.ExitCode != 0)
                {
                    var error = new CryptographicException("Windows не подтвердила подпись издателя установщика.");
                    error.Data["VerificationExitCode"] = process.ExitCode;
                    throw error;
                }
                return path;
            }
            finally { File.Delete(verify); }
    }

    internal static async Task StartInstallerAsync(string path, CancellationToken cancellation)
    {
        path = await VerifyInstallerAsync(path, cancellation);
        using var process = DesktopProcess.Start(path, "--update");
    }
    internal static void RejectLinks(string path)
    {
        for (var directory = new DirectoryInfo(Path.GetFullPath(path)); directory != null; directory = directory.Parent)
            if (directory.Exists && directory.Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new IOException("Папка обновлений не может проходить через ссылку.");
    }
}
