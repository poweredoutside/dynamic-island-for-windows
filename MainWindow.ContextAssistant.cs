using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Media;
using DynamicIslandWin.Services;

namespace DynamicIslandWin;

public partial class MainWindow
{
    private (IntPtr Handle, uint ProcessId) _lastAssistantWindow;
    private ForegroundIslandContext _assistantContext;

    private void TglContextAssistant_Click(object sender, RoutedEventArgs e)
    {
        _settings.ContextAssistant = TglContextAssistant.IsChecked == true;
        _lastAssistantWindow = default;
        _assistantContext = default;
        if (_settings.ContextAssistant) RefreshAssistantContext();
        UpdateCompactContent();
        SaveSettings();
    }

    private void RefreshAssistantContext()
    {
        if (!_settings.ContextAssistant) return;

        IntPtr window = GetForegroundWindow();
        if (window == IntPtr.Zero) return;
        GetWindowThreadProcessId(window, out uint pid);
        // Clicking the island and interacting with Settings preserve the last external app.
        if (pid == 0 || pid == Environment.ProcessId || _lastAssistantWindow == (window, pid)) return;
        _lastAssistantWindow = (window, pid);

        var next = default(ForegroundIslandContext);
        try
        {
            using var process = Process.GetProcessById((int)pid);
            string name = process.ProcessName;
            if (name is "StartMenuExperienceHost" or "SearchHost" or "SearchApp" or "ShellExperienceHost") return;
            var buffer = new StringBuilder(128);
            GetAssistantWindowClass(window, buffer, buffer.Capacity);
            string windowClass = buffer.ToString();
            if (windowClass is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "MultitaskingViewFrame") return;
            next = ForegroundIslandContext.ForWindow(name, windowClass);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception)
        {
            // The window may have closed or its process may be inaccessible.
        }

        if (_assistantContext == next) return;
        _assistantContext = next;
        UpdateCompactContent();
    }

    private bool UpdateAssistantShortcut()
    {
        string? tab = _settings.ContextAssistant && !_manualResize && !_isGameModeActive
            ? _assistantContext.GetTab(_isMediaPlaying ? _selectedMedia?.Track.Source : null) : null;
        if (tab == null || _settings.HiddenTabs?.Contains(tab) == true)
        {
            AssistantShortcut.Visibility = Visibility.Collapsed;
            AssistantShortcut.Tag = null;
            return false;
        }

        AssistantShortcut.Visibility = Visibility.Visible;
        var section = DashboardSections[tab];
        if (!Equals(AssistantShortcut.Tag, tab))
        {
            AssistantShortcut.Tag = tab;
            AssistantShortcutIcon.Data = Geometry.Parse(section.Icon);
        }
        AssistantShortcut.ToolTip = L(section.Label);
        return true;
    }

    private void AssistantShortcut_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (UpdateAssistantShortcut() && AssistantShortcut.Tag is string tab) OpenDashboardTab(tab);
    }

    [DllImport("user32.dll", EntryPoint = "GetClassNameW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int GetAssistantWindowClass(IntPtr window, StringBuilder className, int capacity);
}
