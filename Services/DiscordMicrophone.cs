using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;

namespace DynamicIslandWin.Services;

internal static class DiscordMicrophone
{
    internal readonly record struct ToggleResult(bool? Muted, bool Confirmed, bool Sent = false);
    private sealed record Control(bool Muted, Action Change);
    private static readonly Condition Buttons = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button);
    private static readonly Condition ToggleButtons = new AndCondition(
        Buttons,
        new PropertyCondition(AutomationElement.IsTogglePatternAvailableProperty, true));

    internal static bool? ReadMuted(string client) => FindControl(client)?.Muted;

    internal static ToggleResult ToggleMuted(string client)
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            var control = FindControl(client);
            if (control != null)
            {
                try
                {
                    control.Change();
                    return WaitForChange(client, control.Muted);
                }
                catch (Exception ex) when (ex is ElementNotAvailableException or ElementNotEnabledException)
                {
                    // The element was rejected before the command could be sent.
                }
            }
            if (attempt < 2) Thread.Sleep(100);
        }
        return new(null, false);
    }

    private static ToggleResult WaitForChange(string client, bool before)
    {
        // Electron may replace the control after the click. Only read back, so a
        // slow response cannot trigger a second action that reverses the first one.
        bool? after = null;
        for (int poll = 0; poll < 12; poll++)
        {
            Thread.Sleep(100);
            after = ReadMuted(client) ?? after;
            if (after == !before) return new(after, true, Sent: true);
        }
        return new(after, false, Sent: true);
    }

    private static Control? FindControl(string client)
    {
        foreach (var handle in ClientWindows(client))
        {
            try
            {
                var window = AutomationElement.FromHandle(handle);
                foreach (AutomationElement button in window.FindAll(TreeScope.Descendants, ToggleButtons))
                {
                    if (!TryReadButton(button, voicePanel: true, out var control)) continue;
                    return control;
                }
                foreach (AutomationElement button in window.FindAll(TreeScope.Descendants, Buttons))
                {
                    if (!TryReadButton(button, voicePanel: false, out var control)) continue;
                    return control;
                }
            }
            catch (Exception ex) when (ex is ElementNotAvailableException or COMException or ArgumentException)
            {
                // Windows can disappear when Discord restarts or closes a popout.
            }
        }
        return null;
    }

    private static bool TryReadButton(AutomationElement button, bool voicePanel, out Control? control)
    {
        control = null;
        try
        {
            if (voicePanel)
            {
                if (!HasClass(button, "audioButtonWithMenu_") || !IsMicrophoneButton(button)) return false;
                if (!button.Current.IsEnabled || !button.TryGetCurrentPattern(TogglePattern.Pattern, out var pattern)) return false;
                var toggle = (TogglePattern)pattern;
                var state = toggle.Current.ToggleState;
                if (state == ToggleState.Indeterminate) return false;
                control = new(state == ToggleState.On, toggle.Toggle);
            }
            else
            {
                bool? muted = MutedFromAction(button.Current.Name);
                if (muted == null || !HasClass(button, "centerButton_") || !button.Current.IsEnabled
                    || !HasClass(NextButton(button), "contextMenuNub_")
                    || !button.TryGetCurrentPattern(InvokePattern.Pattern, out var pattern)) return false;
                control = new(muted.Value, ((InvokePattern)pattern).Invoke);
            }
            return true;
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or COMException)
        {
            return false;
        }
    }

    private static bool? MutedFromAction(string name) => name.Trim().ToLowerInvariant() switch
    {
        "mute" or "mute microphone" or "заглушить" or "выключить микрофон" or "вимкнути мікрофон"
            or "mikrofon stummschalten" or "silenciar micrófono" => false,
        "unmute" or "unmute microphone" or "вкл. микрофон" or "включить микрофон" or "увімкнути мікрофон"
            or "mikrofon einschalten" or "activar micrófono" => true,
        _ => null
    };

    private static bool IsMicrophoneButton(AutomationElement button)
    {
        // Discord's voice strip places mic, input settings, deafen, and output settings
        // in that order. The class prefixes survive style hash and language changes.
        var inputSettings = NextButton(button);
        if (inputSettings?.Current.Name is "Input Settings" or "Настройки ввода") return true;
        if (!HasClass(inputSettings, "buttonChevron_")) return false;
        var deafen = NextButton(inputSettings!);
        return HasClass(deafen, "audioButtonWithMenu_")
            && HasClass(NextButton(deafen!), "buttonChevron_");
    }

    private static AutomationElement? NextButton(AutomationElement element)
    {
        // Discord inserts an accessibility-only announcement group after a muted button.
        // Find the next actual control so both mute states use the same selector.
        var walker = TreeWalker.RawViewWalker;
        var next = walker.GetNextSibling(element);
        while (next != null && next.Current.ControlType != ControlType.Button)
            next = walker.GetNextSibling(next);
        return next;
    }

    private static bool HasClass(AutomationElement? element, string prefix) =>
        element != null && element.Current.ControlType == ControlType.Button
        && element.Current.ClassName.Split(' ').Any(name => name.StartsWith(prefix, StringComparison.Ordinal));

    private static List<IntPtr> ClientWindows(string client)
    {
        var processIds = new HashSet<uint>();
        foreach (var process in Process.GetProcessesByName(client))
        {
            using (process) processIds.Add((uint)process.Id);
        }

        var windows = new List<IntPtr>();
        if (processIds.Count == 0) return windows;
        var buffer = new StringBuilder(256);
        EnumWindows((handle, _) =>
        {
            GetWindowThreadProcessId(handle, out uint processId);
            if (!processIds.Contains(processId)) return true;
            if (GetClassName(handle, buffer, buffer.Capacity) == 0 || buffer.ToString() != "Chrome_WidgetWin_1") return true;
            if (GetWindowText(handle, buffer, buffer.Capacity) > 0 && buffer.ToString() == "Discord Overlay") return true;
            windows.Add(handle);
            return true;
        }, IntPtr.Zero);
        return windows;
    }

    private delegate bool EnumWindowsCallback(IntPtr handle, IntPtr data);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr data);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr handle, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr handle, StringBuilder name, int capacity);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr handle, StringBuilder title, int capacity);
}
