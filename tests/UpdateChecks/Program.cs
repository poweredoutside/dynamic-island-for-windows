using System.Runtime.InteropServices;
using System.Text.Json;
using DynamicIslandWin.Services;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length is < 2 or > 3 || (args.Length == 3 && args[2] != "--apply")) return 2;
        string report = Path.GetFullPath(args[0]);
        try
        {
            RunAsync(report, Path.GetFullPath(args[1]), args.Length == 3).GetAwaiter().GetResult();
            return 0;
        }
        catch (Exception ex) { File.WriteAllText(report, JsonSerializer.Serialize(new { success = false, error = ex.ToString(), signatureExitCode = ex.Data["VerificationExitCode"] })); return 1; }
    }

    private static async Task RunAsync(string report, string installer, bool apply)
    {
        uint length = 0;
        bool packaged = GetCurrentPackageFullName(ref length, IntPtr.Zero) != 15700;
        string directory = Path.Combine(GitHubUpdateService.UpdateDirectory(), "probe " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string copy = Path.Combine(directory, "ocdynamicisland.exe");
        File.Copy(installer, copy);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        string physical = await GitHubUpdateService.VerifyInstallerAsync(copy, timeout.Token);
        if (apply)
        {
            await GitHubUpdateService.StartInstallerAsync(physical, timeout.Token);
            File.WriteAllText(report, JsonSerializer.Serialize(new { success = true, packagedCaller = packaged, installerStarted = true, physicalPath = physical }));
            return;
        }
        string childReport = Path.Combine(directory, "child.json");
        using var child = DesktopProcess.Start(physical, "--verify-payload", childReport);
        await child.WaitForExitAsync(timeout.Token);
        if (child.ExitCode != 0) throw new Exception("Installer planning self-test failed.");
        using var payload = JsonDocument.Parse(await File.ReadAllTextAsync(childReport));
        if (!payload.RootElement.GetProperty("success").GetBoolean() || payload.RootElement.GetProperty("packagedProcess").GetBoolean())
            throw new Exception("The updater's installer retained package identity.");
        string unsigned = Path.Combine(directory, "unsigned.exe");
        File.Copy(Environment.ProcessPath!, unsigned);
        bool rejected = false;
        try { await GitHubUpdateService.VerifyInstallerAsync(unsigned, timeout.Token); }
        catch (System.Security.Cryptography.CryptographicException) { rejected = true; }
        if (!rejected) throw new Exception("Unsigned installer accepted.");
        File.WriteAllText(report, JsonSerializer.Serialize(new
        {
            success = true, packagedCaller = packaged, physicalPath = physical,
            signatureChecked = true, unsignedRejected = rejected, installerHasNoPackageIdentity = true,
            installerPlanning = payload.RootElement.Clone()
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
    [DllImport("kernel32.dll")] private static extern int GetCurrentPackageFullName(ref uint length, IntPtr name);
}
