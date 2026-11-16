using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.Text.Json;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.Tracing.Etlx;
using Microsoft.Diagnostics.Tracing.Parsers;

int pid = int.Parse(args[0]);
string path = Path.GetFullPath(args[1]);
Directory.CreateDirectory(Path.GetDirectoryName(path)!);
var client = new DiagnosticsClient(pid);
using var session = client.StartEventPipeSession([
    new EventPipeProvider(ClrTraceEventParser.ProviderName, EventLevel.Informational, (long)ClrTraceEventParser.Keywords.Default),
    new EventPipeProvider("Microsoft-DotNETCore-SampleProfiler", EventLevel.Informational)
], requestRundown: true);
using (var output = File.Create(path))
{
    var copying = session.EventStream.CopyToAsync(output);
    await Task.Delay(6000);
    session.Stop(); await copying;
}
using var log = new TraceLog(TraceLog.CreateFromEventPipeDataFile(path));
var counts = new Dictionary<string, int>();
var threads = new Dictionary<int, int>();
var leaf = new Dictionary<string, int>();
foreach (var item in log.Events)
{
    if (!item.ProviderName.Contains("SampleProfiler")) continue;
    threads[item.ThreadID] = threads.GetValueOrDefault(item.ThreadID) + 1;
    var seen = new HashSet<string>();
    bool first = true;
    for (var stack = item.CallStack(); stack != null; stack = stack.Caller)
    {
        string name = stack.CodeAddress.FullMethodName;
        if (string.IsNullOrEmpty(name)) name = stack.CodeAddress.ModuleFile?.Name ?? "native";
        if (first) { leaf[name] = leaf.GetValueOrDefault(name) + 1; first = false; }
        if (!seen.Add(name)) continue;
        counts[name] = counts.GetValueOrDefault(name) + 1;
    }
}
string report = JsonSerializer.Serialize(new {
    Threads = threads.OrderByDescending(p => p.Value).Take(12),
    Leaf = leaf.OrderByDescending(p => p.Value).Take(20),
    Product = counts.Where(p => p.Key.Contains("DynamicIsland")).OrderByDescending(p => p.Value).Take(30),
    Inclusive = counts.OrderByDescending(p => p.Value).Take(25)
}, new JsonSerializerOptions { WriteIndented = true });
File.WriteAllText(path + ".json", report); Console.WriteLine(report);
