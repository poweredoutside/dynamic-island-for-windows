using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Forms = System.Windows.Forms;

namespace DynamicIslandWin;

public partial class MainWindow
{
    private bool CanDragIsland => !_settings.PinPosition && !_settings.DockIslandAtTop;

    private void ApplyIslandPlacementStyle()
    {
        double inset = _settings.DockIslandAtTop ? 0 : 10;
        IslandBorder.Margin = new Thickness(0, inset, 0, 0);
        IslandShadow.Margin = new Thickness(-28, inset - 28, -28, -28);
        RightCapsuleStack.Margin = new Thickness(10, inset, 0, 0);
        var corners = IslandCorners(_curR);
        IslandBorder.CornerRadius = LiquidGlassOverlay.CornerRadius = CustomBgBorder.CornerRadius = corners;
        UpdateGlassGeometry();
        UpdateGlassShadow();
    }

    private CornerRadius IslandCorners(double radius)
    {
        radius = Math.Max(4, radius);
        return _settings.DockIslandAtTop ? new CornerRadius(0, 0, radius, radius) : new CornerRadius(radius);
    }

    private void RefreshIslandPlacementControls()
    {
        TglPinPosition.IsChecked = CtxPinIsland.IsChecked = _settings.PinPosition;
        TglDockIsland.IsChecked = CtxDockIsland.IsChecked = _settings.DockIslandAtTop;
    }

    private void PinIsland_Click(object sender, RoutedEventArgs e)
    {
        _settings.PinPosition = sender == CtxPinIsland ? CtxPinIsland.IsChecked : TglPinPosition.IsChecked == true;
        if (_settings.PinPosition) RememberIslandPlacement();
        RefreshIslandPlacementControls();
        SaveSettings();
    }

    private void DockIsland_Click(object sender, RoutedEventArgs e)
    {
        RememberIslandPlacement();
        _settings.DockIslandAtTop = sender == CtxDockIsland ? CtxDockIsland.IsChecked : TglDockIsland.IsChecked == true;
        RefreshIslandPlacementControls();
        ApplyIslandPlacementStyle();
        if (IsLoaded) MoveToMonitor(CurrentIslandMonitor(), _settings.IslandOffset);
        SaveSettings();
    }

    private Forms.Screen CurrentIslandMonitor() => Forms.Screen.FromHandle(new WindowInteropHelper(this).Handle);

    private void RestoreIslandPlacement()
    {
        var screen = Forms.Screen.AllScreens.FirstOrDefault(s => s.DeviceName == _settings.IslandMonitor)
            ?? Forms.Screen.PrimaryScreen ?? CurrentIslandMonitor();
        double offset = _settings.IslandOffset;
        if (string.IsNullOrEmpty(_settings.IslandMonitor) && _settings.WindowLeft is double legacyLeft)
            offset = legacyLeft - (SystemParameters.PrimaryScreenWidth - Width) / 2;
        MoveToMonitor(screen, offset);
    }

    private void RememberIslandPlacement()
    {
        IntPtr handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero || !GetWindowRect(handle, out var window)) return;
        var screen = CurrentIslandMonitor();
        _settings.IslandMonitor = screen.DeviceName;
        if (!_settings.DockIslandAtTop)
            _settings.IslandOffset = ((window.Left + window.Right) / 2d - screen.Bounds.Left - screen.Bounds.Width / 2d)
                / VisualTreeHelper.GetDpi(this).DpiScaleX;
        _settings.WindowLeft = null;
    }

    internal static int IslandWindowLeft(System.Drawing.Rectangle bounds, int width, double offset, double scale, bool docked)
    {
        double drift = !docked && double.IsFinite(offset) ? Math.Clamp(offset * scale, -bounds.Width / 2d, bounds.Width / 2d) : 0;
        return bounds.Left + (int)Math.Round((bounds.Width - width) / 2d + drift);
    }

    private void MoveToMonitor(Forms.Screen screen, double offset = 0)
    {
        var center = new System.Drawing.Point(screen.Bounds.Left + screen.Bounds.Width / 2, screen.Bounds.Top + screen.Bounds.Height / 2);
        GetDpiForMonitor(MonitorFromPoint(center, 2), 0, out uint dpi, out _);
        double scale = dpi == 0 ? 1 : dpi / 96d;
        int width = (int)Math.Round(Width * scale), height = (int)Math.Round(Height * scale);
        SetWindowPos(new WindowInteropHelper(this).Handle, IntPtr.Zero,
            IslandWindowLeft(screen.Bounds, width, offset, scale, _settings.DockIslandAtTop), screen.WorkingArea.Top, width, height, 0x0014);
    }

    private void CenterIslandOnScreen()
    {
        if (IsLoaded) MoveToMonitor(CurrentIslandMonitor());
    }

    [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint x, out uint y);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(System.Drawing.Point point, uint flags);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int cx, int cy, uint flags);
}
