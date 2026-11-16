using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DynamicIslandWin.Services;

internal static class GitHubUpdateService
{
    internal const string DefaultRepository = "poweredoutside/dynamic-island-for-windows";
    internal const string InstallerFileName = "ocdynamicisland.exe";
    internal sealed record Release(Version Version, string Name, Uri Page, Uri Installer, string InstallerName, Uri Checksums);
    private static readonly HttpClient Http = CreateHttp();
    private static HttpClient CreateHttp()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("DynamicIslandWin/1.1.5"); return client;
    }
    internal static string Repository(string value)
    {
        value = value.Trim().TrimEnd('/');
        if (value.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase)) value = value[19..];
        if (!Regex.IsMatch(value, @"^[a-zA-Z0-9][a-zA-Z0-9-]{0,38}/[a-zA-Z0-9_.-]{1,100}$", RegexOptions.CultureInvariant))
            throw new FormatException("Укажите owner/repository или ссылку https://github.com/owner/repository.");
        return value;
    }
    internal static Release Parse(string repository, string json)
    {
        string repo = Repository(repository);
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
        string prefix = "https://github.com/" + repo + "/releases/download/";
        Uri Asset(string url)
        {
            if (!url.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Host != "github.com" || !string.IsNullOrEmpty(uri.UserInfo))
                throw new FormatException("Файл обновления должен находиться в том же репозитории GitHub.");
            return uri;
        }
        return new(version, root.TryGetProperty("name", out var name) ? name.GetString() ?? tag : tag,
            new Uri("https://github.com/" + repo + "/releases"), Asset(installer.Url), installer.Name, Asset(hashes.Url));
    }
    internal static async Task<Release> CheckAsync(string repository, CancellationToken cancellation)
    {
        string repo = Repository(repository);
        using var response = await Http.GetAsync("https://api.github.com/repos/" + repo + "/releases/latest", cancellation);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > 2_000_000) throw new FormatException("Ответ GitHub слишком большой.");
        return Parse(repo, await response.Content.ReadAsStringAsync(cancellation));
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
        string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DynamicIslandWin", "Updates");
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
                    if (response.Content.Headers.ContentLength is > 0) progress.Report(total * 100.0 / response.Content.Headers.ContentLength.Value);
                }
                if (!CryptographicOperations.FixedTimeEquals(hash.GetHashAndReset(), Convert.FromHexString(expected))) throw new CryptographicException("SHA-256 установщика не совпадает. Обновление отклонено.");
            }
            // Windows validates the Authenticode signature against the local trust store.
            // Paths travel as a separate argument, never concatenated into shell code.
            const string script = "param([string]$InstallerPath) $s=Get-AuthenticodeSignature -LiteralPath $InstallerPath; if($s.Status -eq 'Valid' -and $s.SignerCertificate.Subject -eq 'CN=DynamicIslandWin'){exit 0}; exit 1";
            string verify = Path.Combine(directory, "verify.ps1"); RejectLinks(directory); await File.WriteAllTextAsync(verify, script, cancellation);
            var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe")) { UseShellExecute = false, CreateNoWindow = true };
            foreach (string argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", verify, "-InstallerPath", path }) start.ArgumentList.Add(argument);
            using var process = Process.Start(start) ?? throw new IOException("Не удалось проверить подпись.");
            await process.WaitForExitAsync(cancellation);
            if (process.ExitCode != 0) throw new CryptographicException("Windows не подтвердила подпись издателя установщика.");
            File.Delete(verify); progress.Report(100); return path;
        }
        catch { if (File.Exists(path)) File.Delete(path); throw; }
    }
    internal static void RejectLinks(string path)
    {
        for (var directory = new DirectoryInfo(Path.GetFullPath(path)); directory != null; directory = directory.Parent)
            if (directory.Exists && directory.Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new IOException("Папка обновлений не может проходить через ссылку.");
    }
}
