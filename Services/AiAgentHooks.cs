using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DynamicIslandWin.Services;

internal static class AiAgentHooks
{
    internal enum Agent { Claude, Codex }

    private static string SharedPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DynamicIslandWin", "agent-notifications");
    internal static string Name(Agent agent) => agent == Agent.Claude ? "Claude Code" : "Codex";
    private static string MarkerName(Agent agent) => agent == Agent.Claude ? "claude.stop" : "codex.stop";
    internal static DateTime LastCompletion(Agent agent) => File.GetLastWriteTimeUtc(Path.Combine(SharedPath, MarkerName(agent)));

    private static string ConfigPath(Agent agent)
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string DefaultHome(string variable, string fallback)
        {
            string? value = Environment.GetEnvironmentVariable(variable);
            return !string.IsNullOrEmpty(value) && Path.IsPathFullyQualified(value) ? value : Path.Combine(home, fallback);
        }
        return agent == Agent.Claude ? Path.Combine(DefaultHome("CLAUDE_CONFIG_DIR", ".claude"), "settings.json")
            : Path.Combine(DefaultHome("CODEX_HOME", ".codex"), "hooks.json");
    }

    internal static bool Configured(Agent agent) => FindOwnHook(ReadConfig(ConfigPath(agent)), Command(agent)) != null;

    internal static bool Toggle(Agent agent)
    {
        string path = ConfigPath(agent);
        var config = ReadConfig(path);
        string command = Command(agent);
        bool enabled = UpdateConfig(config, command);
        if (File.Exists(path) && !File.Exists(path + ".before-oc-dynamic-island"))
            File.Copy(path, path + ".before-oc-dynamic-island");
        LocalFile.Write(path, config.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        return enabled;
    }

    internal static bool UpdateConfig(JsonObject config, string command)
    {
        if (FindOwnHook(config, command) is { } owned)
        {
            owned.Commands.Remove(owned.Command);
            if (owned.Commands.Count == 0) owned.Groups.Remove(owned.Group);
            return false;
        }

        var hooks = ObjectProperty(config, "hooks");
        if (!hooks.ContainsKey("Stop")) hooks["Stop"] = new JsonArray();
        if (hooks["Stop"] is not JsonArray groups) throw new JsonException("Stop hooks must be an array.");
        groups.Add(new JsonObject
        {
            ["hooks"] = new JsonArray(new JsonObject
            {
                ["type"] = "command",
                ["command"] = command,
                ["timeout"] = 10
            })
        });
        return true;
    }

    private static (JsonArray Groups, JsonObject Group, JsonArray Commands, JsonObject Command)? FindOwnHook(JsonObject config, string command)
    {
        if (config["hooks"] is not JsonObject hooks || hooks["Stop"] is not JsonArray groups) return null;
        foreach (var group in groups.OfType<JsonObject>())
        {
            if (group["hooks"] is not JsonArray commands) continue;
            foreach (var entry in commands.OfType<JsonObject>())
                if (entry["command"] is JsonValue value && value.TryGetValue<string>(out string? existing) && existing == command)
                    return (groups, group, commands, entry);
        }
        return null;
    }

    private static JsonObject ObjectProperty(JsonObject parent, string property)
    {
        if (!parent.ContainsKey(property)) parent[property] = new JsonObject();
        return parent[property] as JsonObject ?? throw new JsonException(property + " must be an object.");
    }

    private static JsonObject ReadConfig(string path) => !File.Exists(path) ? new JsonObject()
        : JsonNode.Parse(File.ReadAllText(path), documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }) as JsonObject
            ?? throw new JsonException("Agent settings must be a JSON object.");

    internal static string Command(Agent agent)
    {
        // Drain stdin without reading or retaining the agent's response. The
        // marker's timestamp is the only data consumed by the island.
        string script = "$ErrorActionPreference = 'Stop'; $ProgressPreference = 'SilentlyContinue'; "
            + "[Console]::OpenStandardInput().CopyTo([IO.Stream]::Null); "
            + "$dir = Join-Path $env:LOCALAPPDATA 'DynamicIslandWin\\agent-notifications'; "
            + "[IO.Directory]::CreateDirectory($dir) | Out-Null; "
            + $"[IO.File]::WriteAllText((Join-Path $dir '{MarkerName(agent)}'), '')";
        return "powershell.exe -NoProfile -NonInteractive -EncodedCommand " + Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
    }
}
