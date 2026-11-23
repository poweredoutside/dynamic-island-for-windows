using System.Globalization;
using System.IO;
using System.Text;

namespace DynamicIslandWin.Services;

internal static class MusicEqualizer
{
    internal const int Minimum = -12, Maximum = 12;
    internal static IReadOnlyList<int> Frequencies { get; } = Array.AsReadOnly(new[] { 60, 250, 1000, 4000, 12000 });
    internal const string ConfigName = "dynamic-island-equalizer.txt";
    private const string Include = "Include: " + ConfigName;

    internal static int[] Normalize(IReadOnlyList<int>? gains) => Frequencies.Select((_, index) =>
        gains != null && index < gains.Count ? Math.Clamp(gains[index], Minimum, Maximum) : 0).ToArray();

    internal static void Apply(EqualizerApoInstaller.Installation installation, IReadOnlyList<int> gains, bool enabled)
    {
        string main = Path.Combine(installation.ConfigDirectory, "config.txt");
        DateTime lastWrite = File.GetLastWriteTimeUtc(main);
        string original;
        Encoding encoding;
        using (var reader = new StreamReader(main, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: true))
        {
            original = reader.ReadToEnd();
            encoding = reader.CurrentEncoding;
        }
        using var lines = new StringReader(original);
        bool hasInclude = false;
        while (lines.ReadLine() is { } line)
        {
            int colon = line.IndexOf(':');
            if (colon < 0 || !line[..colon].Trim().Equals("Include", StringComparison.OrdinalIgnoreCase)) continue;
            if (!line[(colon + 1)..].Trim().Equals(ConfigName, StringComparison.OrdinalIgnoreCase)) continue;
            hasInclude = true;
            break;
        }
        if (!enabled && !hasInclude) return;

        LocalFile.Write(Path.Combine(installation.ConfigDirectory, ConfigName), Filters(gains, enabled));
        if (hasInclude) return;

        if (File.GetLastWriteTimeUtc(main) != lastWrite)
            throw new IOException("The Equalizer APO configuration changed. Try applying the curve again.");
        string newline = original.Contains('\n') && !original.Contains("\r\n", StringComparison.Ordinal) ? "\n" : "\r\n";
        LocalFile.Write(main, Include + newline + original, encoding);
    }

    private static string Filters(IReadOnlyList<int> gains, bool enabled)
    {
        const string heading = "# Managed by OC - Dynamic Island\r\n";
        int[] values = Normalize(gains);
        if (!enabled || values.All(value => value == 0)) return heading;

        string points = string.Join("; ", Frequencies.Select((frequency, index) =>
            string.Create(CultureInfo.InvariantCulture, $"{frequency} {values[index]}")));
        int headroom = Math.Max(0, values.Max());
        return heading + string.Create(CultureInfo.InvariantCulture,
            $"If: stage == \"post-mix\"\r\nPreamp: {-headroom} dB\r\nGraphicEQ: {points}\r\nEndIf:\r\n");
    }
}
