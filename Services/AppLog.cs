using System.IO;

namespace DynamicIslandWin.Services;

internal static class AppLog
{
    private static readonly object Gate = new();

    // Callers supply only technical metadata, never notification text or fingerprints.
    public static void Notification(string metadata) => Write("notifications.log", metadata);
    public static void Glass(string metadata) => Write("glass.log", metadata);

    public static void Error(string operation, Exception exception)
        => Write("integration.log", $"{operation}: {exception.GetType().Name} 0x{exception.HResult:X8}");

    private static void Write(string file, string metadata)
    {
        try
        {
            lock (Gate)
            {
            string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DynamicIslandWin", "Logs");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, file);
            if (File.Exists(path) && new FileInfo(path).Length > 256 * 1024) File.Delete(path);
            // Never log notification contents.
            File.AppendAllText(path, $"{DateTimeOffset.Now:O} pid={Environment.ProcessId} {metadata}{Environment.NewLine}");
            }
        }
        catch { /* Logging must not prevent app shutdown or recovery. */ }
    }
}
