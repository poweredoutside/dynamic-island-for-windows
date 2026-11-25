using Microsoft.Win32;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace DynamicIslandWin.Services;

internal static class ReminderAudio
{
    internal static Task PlayAsync(bool meeting) => Task.Run(async () =>
    {
        try
        {
            string alias = meeting ? "SystemAsterisk" : "SystemExclamation";
            string? path = Registry.GetValue(
                $@"HKEY_CURRENT_USER\AppEvents\Schemes\Apps\.Default\{alias}\.Current", "", null) as string;
            if (string.IsNullOrWhiteSpace(path)) return; // Windows sound scheme can mute an event.
            await PlayWaveAsync(Environment.ExpandEnvironmentVariables(path)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            AppLog.Error("Play reminder sound", ex);
        }
    });

    internal static async Task PlayWaveAsync(string path)
    {
        using var wave = new WaveFileReader(path);
        if (wave.Length == 0) return;
        using var audio = new MMDeviceEnumerator();
        using var device = audio.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        using var playback = new WasapiOut(device, AudioClientShareMode.Shared, true, 100);
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        playback.PlaybackStopped += (_, args) =>
        {
            if (args.Exception is { } error) finished.TrySetException(error);
            else finished.TrySetResult();
        };
        playback.Init(wave);
        playback.Play();
        await finished.Task.WaitAsync(wave.TotalTime + TimeSpan.FromSeconds(2)).ConfigureAwait(false);
    }
}
