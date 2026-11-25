using System.Diagnostics;
using System.Runtime.InteropServices;
using Interop.UIAutomationClient;

namespace DynamicIslandWin.Services;

internal static class YandexMusicVolume
{
    private static readonly object Gate = new();
    private static readonly string[] ProcessNames = ["Яндекс Музыка", "YandexMusic"];
    private static readonly string[] VolumeLabels =
        ["Управление громкостью", "Manage volume", "Дауыс деңгейін басқару", "Tovush balandligini boshqarish"];
    private static IUIAutomation2? _automation;
    private static IUIAutomationCondition? _volumeSlider;
    private static IUIAutomationRangeValuePattern? _slider;

    internal static PlayerAudioService.Snapshot? ReadOrSet(string source, float? volume)
    {
        if (!MediaSources.IsYandexMusic(source)) return null;
        lock (Gate)
        {
            try
            {
                _slider ??= FindSlider();
                if (_slider == null) return null;
                if (volume is { } target)
                {
                    float requested = Math.Clamp(target, 0, 1);
                    _slider.SetValue(requested);
                    // Chromium updates its accessibility value after the renderer handles the request.
                    for (int attempt = 0; attempt < 3; attempt++)
                    {
                        var confirmed = ReadLevel(_slider);
                        if (confirmed is { } level && Math.Abs(level.Volume - requested) < .005f) return level;
                        Thread.Sleep(25);
                    }
                }
                return ReadLevel(_slider);
            }
            catch (Exception ex) when (ex is COMException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                _slider = null; // The player may have replaced its controls or restarted.
                return null;
            }
        }
    }

    private static PlayerAudioService.Snapshot? ReadLevel(IUIAutomationRangeValuePattern slider)
    {
        double value = slider.CurrentValue;
        return double.IsFinite(value) && value is >= 0 and <= 1 ? new((float)value, value == 0) : null;
    }

    private static IUIAutomationRangeValuePattern? FindSlider()
    {
        // The managed WPF client can see the Chromium slider but misses its RangeValue pattern.
        var automation = _automation ??= new CUIAutomation8 { ConnectionTimeout = 650, TransactionTimeout = 1200 };
        var condition = _volumeSlider ??= automation.CreateAndCondition(
            automation.CreatePropertyCondition(UIA_PropertyIds.UIA_ControlTypePropertyId, UIA_ControlTypeIds.UIA_SliderControlTypeId),
            automation.CreateOrConditionFromArray(VolumeLabels.Select(label =>
                automation.CreatePropertyCondition(UIA_PropertyIds.UIA_NamePropertyId, label)).ToArray()));

        foreach (string name in ProcessNames)
        {
            var processes = Process.GetProcessesByName(name);
            try
            {
                foreach (var process in processes)
                {
                    try
                    {
                        if (process.MainWindowHandle == IntPtr.Zero) continue;
                        var window = automation.ElementFromHandle(process.MainWindowHandle);
                        var matches = window.FindAll(TreeScope.TreeScope_Descendants, condition);
                        IUIAutomationRangeValuePattern? offscreen = null;
                        for (int i = 0; i < matches.Length; i++)
                        {
                            var element = matches.GetElement(i);
                            if (element.CurrentIsEnabled == 0
                                || element.GetCurrentPattern(UIA_PatternIds.UIA_RangeValuePatternId) is not IUIAutomationRangeValuePattern range
                                || range.CurrentIsReadOnly != 0 || range.CurrentMinimum != 0 || range.CurrentMaximum != 1) continue;
                            if (element.CurrentIsOffscreen == 0) return range;
                            offscreen ??= range;
                        }
                        if (offscreen != null) return offscreen;
                    }
                    catch (Exception ex) when (ex is COMException or InvalidOperationException or System.ComponentModel.Win32Exception)
                    {
                        // Other player windows may still be available.
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
