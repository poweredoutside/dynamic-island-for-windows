using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Automation;

namespace DynamicIslandWin.Services;

internal static class YandexMusicVolume
{
    private static readonly Condition VolumeSlider = new AndCondition(
        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Slider),
        new OrCondition(
            new PropertyCondition(AutomationElement.NameProperty, "Управление громкостью"),
            new PropertyCondition(AutomationElement.NameProperty, "Manage volume"),
            new PropertyCondition(AutomationElement.NameProperty, "Дауыс деңгейін басқару"),
            new PropertyCondition(AutomationElement.NameProperty, "Tovush balandligini boshqarish")));

    internal static PlayerAudioService.Snapshot? ReadOrSet(string source, float? volume)
    {
        if (!MediaSources.IsYandexMusic(source)) return null;
        foreach (string name in new[] { "Яндекс Музыка", "YandexMusic" })
        {
            var processes = Process.GetProcessesByName(name);
            try
            {
                foreach (var process in processes)
                {
                    try
                    {
                        if (process.MainWindowHandle == IntPtr.Zero) continue;
                        var window = AutomationElement.FromHandle(process.MainWindowHandle);
                        var slider = window.FindFirst(TreeScope.Descendants, VolumeSlider);
                        if (slider == null || !slider.Current.IsEnabled
                            || !slider.TryGetCurrentPattern(RangeValuePattern.Pattern, out var pattern))
                            continue;
                        var range = (RangeValuePattern)pattern;
                        var current = range.Current;
                        if (current.IsReadOnly || current.Minimum != 0 || current.Maximum != 1) continue;
                        if (volume.HasValue)
                        {
                            range.SetValue(Math.Clamp(volume.Value, 0, 1));
                            current = range.Current;
                        }
                        float level = (float)current.Value;
                        return float.IsFinite(level) && level is >= 0 and <= 1 ? new(level, level == 0) : null;
                    }
                    catch (Exception ex) when (ex is ElementNotAvailableException or ElementNotEnabledException
                        or COMException or ArgumentException or InvalidOperationException)
                    {
                        // The player can close or replace its controls while we read them.
                    }
                }
            }
            finally
            {
                foreach (var process in processes) process.Dispose();
            }
        }
        return null;
    }
}
