using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Point = System.Drawing.Point;
using Rectangle = System.Drawing.Rectangle;
using Image = System.Windows.Controls.Image;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;

namespace DynamicIslandWin.Services;

// A frozen desktop avoids capturing the selector itself. Pointer coordinates
// and the crop stay in physical pixels, including monitors left of the primary.
internal sealed class RegionCaptureWindow : Window
{
    private readonly BitmapSource _desktop;
    private readonly Rectangle _bounds;
    private readonly System.Windows.Shapes.Path _shade = new() { Fill = new SolidColorBrush(Color.FromArgb(110, 0, 0, 0)), IsHitTestVisible = false };
    private readonly System.Windows.Shapes.Path _outline = new() { Stroke = Brushes.White, StrokeThickness = 1, IsHitTestVisible = false };
    private readonly TaskCompletionSource<BitmapSource?> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Point? _start;

    private RegionCaptureWindow(BitmapSource desktop, Rectangle bounds, string hint)
    {
        _desktop = desktop; _bounds = bounds;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false; Topmost = true; Background = Brushes.Black;
        Cursor = System.Windows.Input.Cursors.Cross;
        var layer = new Grid { Background = Brushes.Transparent };
        layer.Children.Add(new Image { Source = desktop, Stretch = Stretch.Fill, IsHitTestVisible = false });
        layer.Children.Add(_shade); layer.Children.Add(_outline);
        layer.Children.Add(new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(230, 25, 25, 30)), CornerRadius = new CornerRadius(12),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(12, 20, 12, 0), Padding = new Thickness(18, 10, 18, 10), IsHitTestVisible = false,
            Child = new TextBlock { Text = hint, Foreground = Brushes.White, FontSize = 15 }
        });
        Content = layer;
        SourceInitialized += (_, _) => SetWindowPos(new WindowInteropHelper(this).Handle, new IntPtr(-1),
            bounds.Left, bounds.Top, bounds.Width, bounds.Height, 0x0040);
        Loaded += (_, _) => SetWindowPos(new WindowInteropHelper(this).Handle, new IntPtr(-1),
            bounds.Left, bounds.Top, bounds.Width, bounds.Height, 0x0040);
        SizeChanged += (_, _) => DrawSelection();
        MouseLeftButtonDown += (_, e) => { _start = Pointer(); CaptureMouse(); DrawSelection(); e.Handled = true; };
        MouseMove += (_, _) => { if (_start.HasValue) DrawSelection(); };
        MouseLeftButtonUp += (_, e) =>
        {
            if (!_start.HasValue) return;
            var selected = ScreenCaptureService.SelectionBounds(_start.Value, Pointer(), _bounds);
            ReleaseMouseCapture();
            if (selected.Width >= 2 && selected.Height >= 2)
            {
                var image = new CroppedBitmap(_desktop, new Int32Rect(selected.X - bounds.X, selected.Y - bounds.Y, selected.Width, selected.Height));
                image.Freeze(); _completion.TrySetResult(image); Close();
            }
            else { _start = null; DrawSelection(); }
            e.Handled = true;
        };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Close(); } };
        MouseRightButtonDown += (_, e) => { e.Handled = true; Close(); };
        Closed += (_, _) => { ReleaseMouseCapture(); _completion.TrySetResult(null); };
    }

    internal static async Task<BitmapSource?> SelectAsync(IntPtr owner, string hint)
    {
        var (image, bounds) = await ScreenCaptureService.CaptureDesktopAsync();
        var window = new RegionCaptureWindow(image, bounds, hint);
        new WindowInteropHelper(window).Owner = owner;
        window.Show();
        return await window._completion.Task;
    }

    private static Point Pointer() { GetCursorPos(out var point); return point; }

    private void DrawSelection()
    {
        var outer = new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight));
        var selected = _start.HasValue ? ScreenCaptureService.SelectionBounds(_start.Value, Pointer(), _bounds) : Rectangle.Empty;
        var rect = selected.IsEmpty ? Rect.Empty : new Rect((selected.X - _bounds.X) * ActualWidth / _bounds.Width,
            (selected.Y - _bounds.Y) * ActualHeight / _bounds.Height, selected.Width * ActualWidth / _bounds.Width, selected.Height * ActualHeight / _bounds.Height);
        var inner = new RectangleGeometry(rect);
        _shade.Data = new CombinedGeometry(GeometryCombineMode.Exclude, outer, inner);
        _outline.Data = inner;
    }

    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
}
