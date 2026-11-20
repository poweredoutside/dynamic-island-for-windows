using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace DynamicIslandWin.Services;

internal sealed class DiscordCallMonitor(string appData)
{
    private const int MaxReadBytes = 2 * 1024 * 1024;
    private static readonly Regex RtcEvent = new(
        @"^\[(?<time>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3})\]\s+\[\w+\]\s+\[RTCConnection\((?<id>[^,()]{1,64}), default\)\]\s+(?:RTC connection state: \w+ => (?<state>\w+)\s*$|(?<destroy>Destroy RTCConnection)\s*$)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly (string Process, string Folder)[] Clients =
        [("Discord", "discord"), ("DiscordPTB", "discordptb"), ("DiscordCanary", "discordcanary")];
    private readonly Dictionary<string, VoiceLog> _logs = [];

    internal readonly record struct Call(string ProcessName, DateTimeOffset Started);

    internal Call? Read()
    {
        Call? activeCall = null;
        foreach (var (processName, folder) in Clients)
        {
            var started = GetClientStart(processName);
            if (started == null)
            {
                _logs.Remove(processName);
                continue;
            }
            if (!_logs.TryGetValue(processName, out var log) || log.ClientStarted != started)
                _logs[processName] = log = new VoiceLog(started.Value);

            log.Refresh(Path.Combine(appData, folder, "logs", "renderer_js.log"));
            if (log.CallStarted is { } since && (!activeCall.HasValue || since > activeCall.Value.Started))
                activeCall = new(processName, since);
        }
        return activeCall;
    }

    private static DateTimeOffset? GetClientStart(string processName)
    {
        DateTimeOffset? earliest = null;
        foreach (var process in Process.GetProcessesByName(processName))
        {
            using (process)
            {
                try
                {
                    DateTimeOffset start = process.StartTime;
                    if (!earliest.HasValue || start < earliest.Value) earliest = start;
                }
                catch (InvalidOperationException) { }
                catch (System.ComponentModel.Win32Exception) { }
            }
        }
        return earliest;
    }

    internal sealed class VoiceLog(DateTimeOffset clientStarted)
    {
        private long _offset;
        private DateTime _creationTime;
        private string? _connection;
        internal DateTimeOffset ClientStarted { get; } = clientStarted;
        internal DateTimeOffset? CallStarted { get; private set; }

        internal void Refresh(string path)
        {
            try
            {
                using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                var creation = File.GetCreationTimeUtc(path);
                if (_creationTime != creation || file.Length < _offset)
                {
                    _creationTime = creation;
                    Reset();
                }
                long end = file.Length;
                if (end == _offset) return;
                long start = Math.Max(_offset, end - MaxReadBytes);
                file.Seek(start, SeekOrigin.Begin);
                var bytes = new byte[(int)(end - start)];
                int read = file.ReadAtLeast(bytes, bytes.Length, throwOnEndOfStream: false);
                if (read == 0) return;
                int lastNewLine = Array.LastIndexOf(bytes, (byte)'\n', read - 1);
                if (lastNewLine < 0) return;

                int first = start > _offset ? Array.IndexOf(bytes, (byte)'\n', 0, read) + 1 : 0;
                if (first <= lastNewLine)
                {
                    using var lines = new StringReader(Encoding.UTF8.GetString(bytes, first, lastNewLine - first + 1));
                    while (lines.ReadLine() is { } line) ReadLine(line);
                }
                _offset = start + lastNewLine + 1;
            }
            catch (FileNotFoundException) { Reset(); }
            catch (DirectoryNotFoundException) { Reset(); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { Reset(); }
        }

        private void ReadLine(string line)
        {
            if (!line.Contains("[RTCConnection(", StringComparison.Ordinal)) return;
            var match = RtcEvent.Match(line);
            if (!match.Success || !DateTimeOffset.TryParseExact(match.Groups["time"].Value, "yyyy-MM-dd HH:mm:ss.fff",
                CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var timestamp) || timestamp < ClientStarted)
                return;

            string id = match.Groups["id"].Value;
            string state = match.Groups["state"].Value;
            if (state == "RTC_CONNECTED")
            {
                if (_connection != id) CallStarted = timestamp;
                _connection = id;
            }
            else if (_connection == id && (match.Groups["destroy"].Success || state is "DISCONNECTED" or "RTC_DISCONNECTED"))
            {
                _connection = null;
                CallStarted = null;
            }
        }

        private void Reset()
        {
            _offset = 0;
            _connection = null;
            CallStarted = null;
        }
    }
}
