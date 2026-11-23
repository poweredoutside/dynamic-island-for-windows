using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using Microsoft.Win32;

namespace DynamicIslandWin.Services;

// Shared by the island and the standalone Windows setup.
internal static class EqualizerApoInstaller
{
    private const string FileName = "EqualizerAPO-x64-1.4.2.exe";
    private const long PackageSize = 11_980_366;
    // Checksum published with the official x64 release on SourceForge.
    private const string Sha256 = "7403BE7427BBE1936A40DDED082829B6E217FC4F5990FEE5CBA501F0AE055AFA";
    private const string DownloadUrl = "https://downloads.sourceforge.net/project/equalizerapo/1.4.2/" + FileName;
    private static readonly HttpClient Http = new();

    internal sealed record Installation(string ConfigDirectory, string? DeviceSelector);

    internal static Installation? FindInstallation()
    {
        string? config = null, root = null;
        try
        {
            using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var apo = machine.OpenSubKey(@"SOFTWARE\EqualizerAPO");
            config = apo?.GetValue("ConfigPath") as string;
            root = apo?.GetValue("InstallPath") as string;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }

        if (string.IsNullOrWhiteSpace(root) || !Path.IsPathFullyQualified(root))
            root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "EqualizerAPO");
        if (string.IsNullOrWhiteSpace(config) || !Path.IsPathFullyQualified(config)) config = Path.Combine(root, "config");
        if (!File.Exists(Path.Combine(root, "EqualizerAPO.dll")) || !File.Exists(Path.Combine(config, "config.txt"))) return null;
        string? selector = new[] { "DeviceSelector.exe", "Configurator.exe" }
            .Select(name => Path.Combine(root, name)).FirstOrDefault(File.Exists);
        return new(config, selector);
    }

    internal static async Task<VerifiedSetup> DownloadAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(75));
        var directory = Directory.CreateTempSubdirectory("DynamicIsland-Audio-");
        string path = Path.Combine(directory.FullName, FileName);
        FileStream? verifiedFile = null;
        try
        {
            using var response = await Http.GetAsync(DownloadUrl, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is { } length && length != PackageSize)
                throw new InvalidDataException("Unexpected Equalizer APO download size.");
            await using (var source = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false))
            await using (var destination = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, useAsync: true))
            {
                var buffer = new byte[64 * 1024];
                long total = 0;
                int count;
                while ((count = await source.ReadAsync(buffer.AsMemory(), timeout.Token).ConfigureAwait(false)) > 0)
                {
                    total += count;
                    if (total > PackageSize) throw new InvalidDataException("Equalizer APO download is too large.");
                    await destination.WriteAsync(buffer.AsMemory(0, count), timeout.Token).ConfigureAwait(false);
                }
            }

            // Keep the verified bytes locked against writes until the child installer exits.
            verifiedFile = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, useAsync: true);
            if (verifiedFile.Length != PackageSize || !CryptographicOperations.FixedTimeEquals(
                await SHA256.HashDataAsync(verifiedFile, timeout.Token).ConfigureAwait(false), Convert.FromHexString(Sha256)))
                throw new InvalidDataException("Equalizer APO download did not match the official SHA-256.");
            return new VerifiedSetup(directory, path, verifiedFile);
        }
        catch
        {
            verifiedFile?.Dispose();
            DeleteDownload(directory, path);
            throw;
        }
    }

    internal sealed class VerifiedSetup(DirectoryInfo directory, string path, FileStream verifiedFile) : IDisposable
    {
        internal async Task RunAsync(Func<string, string>? physicalPath = null)
        {
            string setup = physicalPath?.Invoke(path) ?? path;
            using var process = await Task.Run(() => Process.Start(new ProcessStartInfo(setup)
            {
                UseShellExecute = true, Verb = "runas", WorkingDirectory = Path.GetDirectoryName(setup)!
            })).ConfigureAwait(false) ?? throw new IOException("Equalizer APO setup did not start.");
            await process.WaitForExitAsync().ConfigureAwait(false);
            if (process.ExitCode != 0 || FindInstallation() == null)
                throw new IOException("Equalizer APO installation did not complete.");
        }

        public void Dispose()
        {
            verifiedFile.Dispose();
            DeleteDownload(directory, path);
        }
    }

    private static void DeleteDownload(DirectoryInfo directory, string file)
    {
        try { File.Delete(file); directory.Delete(recursive: false); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
