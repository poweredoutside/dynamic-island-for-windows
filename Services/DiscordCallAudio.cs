using System.Diagnostics;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace DynamicIslandWin.Services;

internal static class DiscordCallAudio
{
    internal sealed record Output(string Id, string Name, bool InUse);
    internal sealed record Outputs(IReadOnlyList<Output> Devices, string? PreferredId, uint ProcessId, bool CanRoute);

    internal static Outputs ReadOutputs(string client)
    {
        var processes = ClientProcesses(client);
        using var audio = new MMDeviceEnumerator();
        var outputs = new List<Output>();
        uint active = 0;
        foreach (var endpoint in audio.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        {
            using (endpoint)
            {
                uint pid = ActiveSession(endpoint, processes);
                if (active == 0) active = pid;
                outputs.Add(new(endpoint.ID, endpoint.FriendlyName, pid != 0));
            }
        }
        uint target = active != 0 ? active : processes.FirstOrDefault();
        if (target == 0) return new(outputs, null, 0, false);
        try
        {
            using var routing = new AppAudioRouting();
            return new(outputs, routing.GetOutput(target), target, true);
        }
        catch (COMException) { return new(outputs, null, target, false); }
    }

    internal static void ChangeOutput(string client, uint pid, string? id)
    {
        if (!ClientProcesses(client).Contains(pid)) throw new InvalidOperationException("The Discord process ended.");
        if (id != null)
        {
            using var audio = new MMDeviceEnumerator();
            using var output = audio.GetDevice(id);
            if (output.DataFlow != DataFlow.Render || output.State != DeviceState.Active)
                throw new InvalidOperationException("The output is no longer available.");
        }
        using var routing = new AppAudioRouting();
        routing.SetOutput(pid, id);
    }

    private static uint ActiveSession(MMDevice device, uint[] processes)
    {
        if (processes.Length == 0) return 0;
        try
        {
            var sessions = device.AudioSessionManager.Sessions;
            for (int i = 0; i < sessions.Count; i++)
            {
                using var session = sessions[i];
                if (session.State == AudioSessionState.AudioSessionStateActive && processes.Contains(session.GetProcessID))
                    return session.GetProcessID;
            }
        }
        catch (COMException) { } // Device may have disconnected mid-enumeration.
        return 0;
    }

    private static uint[] ClientProcesses(string client)
    {
        var processes = Process.GetProcessesByName(client);
        return processes.Select(process =>
        {
            using (process) return (uint)process.Id;
        }).ToArray();
    }
}
