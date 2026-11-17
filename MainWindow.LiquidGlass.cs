using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using DynamicIslandWin.Services;
using Point = System.Windows.Point;
using Color = System.Windows.Media.Color;
using Image = System.Windows.Controls.Image;
using Rectangle = System.Drawing.Rectangle;

namespace DynamicIslandWin;

public partial class MainWindow
{
    private sealed class GlassSurface(Border border, Image image, Grid layer, string? fallbackKey, UIElement? sheen)
    {
        internal readonly Border Border = border;
        internal readonly Image Image = image;
        internal readonly Grid Layer = layer;
        internal readonly RectangleGeometry Clip = new();
        internal readonly string? FallbackKey = fallbackKey;
        internal readonly UIElement? Sheen = sheen;
        internal LiquidGlassEffect? Effect;
        internal (int Width, int Height, int Radius) Geometry;
    }

    private readonly DispatcherTimer _glassTimer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private readonly List<GlassSurface> _glassSurfaces = new();
    private FilteredBackdropCapture? _glassCapture;
    private LiquidGlassEffect? _glassEffect;
    private bool _glassCaptureBusy, _glassTimerInitialized;
    private int _glassGeneration;
    private DateTime _glassRetryAfter;

    private void TglLensGlass_Click(object sender, RoutedEventArgs e)
    {
        _settings.LensGlass = TglLensGlass.IsChecked == true;
        ConfigureLensGlass(); SaveSettings();
    }

    private void InitializeGlassSurfaces()
    {
        if (_glassSurfaces.Count != 0) return;
        AddGlassSurface(new(IslandBorder, LiquidGlassBackdrop, IslandSurfaceLayer, null, LiquidGlassOverlay));
        RegisterGlassSurface(CompactProgressPanel);
        RegisterGlassSurface(LevelHudBorder);
        RegisterGlassSurface(DiscordCallBorder, sheen: DiscordGlassOverlay);
        RegisterGlassSurface(SideToastBorder, sheen: SideToastGlassOverlay);
        RegisterGlassSurface(LangListPopup, "ThemeFloatingBrush");
        IslandBorder.ContextMenu.Opened += (_, _) =>
        {
            IslandBorder.ContextMenu.ApplyTemplate();
            if (VisualTreeHelper.GetChildrenCount(IslandBorder.ContextMenu) > 0
                && VisualTreeHelper.GetChild(IslandBorder.ContextMenu, 0) is Border border)
                RegisterGlassSurface(border, "ThemeFloatingBrush");
        };
        void ConnectComboBoxes(DependencyObject parent)
        {
            if (parent is System.Windows.Controls.ComboBox combo)
                combo.DropDownOpened += (_, _) =>
                {
                    if (combo.Template.FindName("PART_Popup", combo) is Popup { Child: Border border })
                        RegisterGlassSurface(border, "ThemeFloatingBrush");
                };
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++) ConnectComboBoxes(VisualTreeHelper.GetChild(parent, i));
        }
        ConnectComboBoxes(this);
    }

    private void RegisterGlassSurface(Border border, string fallbackKey = "ThemeCapsuleBrush", UIElement? sheen = null)
    {
        // Popup templates can be recreated; release references to closed trees.
        _glassSurfaces.RemoveAll(s => s.FallbackKey == "ThemeFloatingBrush" && !s.Border.IsLoaded && s.Border != LangListPopup);
        if (_glassSurfaces.Any(s => s.Border == border)) return;
        var content = border.Child;
        border.Child = null;
        var image = new Image { Stretch = Stretch.Fill, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
        var layer = new Grid();
        // A backdrop must not contribute its previous bitmap size to measure:
        // otherwise an auto-sized notification could grow but never shrink.
        image.SetBinding(WidthProperty, new System.Windows.Data.Binding("ActualWidth") { Source = layer });
        image.SetBinding(HeightProperty, new System.Windows.Data.Binding("ActualHeight") { Source = layer });
        var canvas = new Canvas { IsHitTestVisible = false };
        canvas.Children.Add(image);
        layer.Children.Add(canvas);
        layer.Children.Add(new Border { Padding = border.Padding, Child = content });
        border.Padding = new Thickness(0);
        border.Child = layer;
        AddGlassSurface(new(border, image, layer, fallbackKey, sheen));
    }

    private void AddGlassSurface(GlassSurface surface)
    {
        _glassSurfaces.Add(surface);
        // Clip the parent after the shader, not the shader's input. A shader
        // returning opaque pixels otherwise paints outside an Image's clip.
        surface.Layer.Clip = surface.Clip;
        surface.Layer.SizeChanged += (_, _) => UpdateGlassSurfaceGeometry(surface);
        UpdateGlassSurfaceGeometry(surface);
    }

    private void UpdateGlassSurfaceGeometry(GlassSurface surface)
    {
        double width = surface.Layer.ActualWidth, height = surface.Layer.ActualHeight;
        if (surface.Image.Width != width) surface.Image.Width = width;
        if (surface.Image.Height != height) surface.Image.Height = height;
        double radius = Math.Max(0, surface.Border.CornerRadius.TopLeft - surface.Border.BorderThickness.Left);
        radius = Math.Min(radius, Math.Min(width, height) / 2);
        surface.Clip.Rect = new Rect(0, 0, width, height);
        surface.Clip.RadiusX = surface.Clip.RadiusY = radius;
        if (surface.Effect != null && width > 0 && height > 0)
            surface.Effect.Sampling = new Point4D(GlassLensMap.MaxDisplacement / width,
                GlassLensMap.MaxDisplacement / height, .8 / width, .8 / height);
    }

    private void UpdateGlassGeometry()
    {
        foreach (var surface in _glassSurfaces) UpdateGlassSurfaceGeometry(surface);
    }

    private void ConfigureLensGlass()
    {
        _glassGeneration++;
        if (!_glassTimerInitialized)
        {
            _glassTimerInitialized = true;
            _glassTimer.Tick += async (_, _) => await RefreshLensGlassAsync();
        }
        InitializeGlassSurfaces();
        bool enabled = _settings.LiquidGlass && _settings.LensGlass;
        TglLensGlass.IsChecked = _settings.LensGlass;
        TglLensGlass.IsEnabled = _settings.LiquidGlass;
        if (enabled && !_captureUiBusy && !_windowClosed && IsLoaded) _glassTimer.Start();
        else
        {
            _glassTimer.Stop();
            _glassCapture?.Dispose(); _glassCapture = null;
        }
        ClearLensBackdrop();
        UpdateLensGlassStatus();
    }

    private void ClearLensBackdrop()
    {
        foreach (var surface in _glassSurfaces) ClearLensSurface(surface);
    }

    private void ClearLensSurface(GlassSurface surface)
    {
        surface.Image.Visibility = Visibility.Collapsed;
        surface.Image.Source = null;
        if (surface.FallbackKey != null) surface.Border.SetResourceReference(Border.BackgroundProperty, surface.FallbackKey);
        else if (surface.Border.Background != null) surface.Border.Background.Opacity = BackgroundOpacity;
        if (surface.Sheen != null) surface.Sheen.Visibility = _settings.LiquidGlass ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateLensGlassStatus() => LblLensGlassStatus.Text = L(
        "Преломление живого фона во всех капсулах и меню. Работает при любом режиме скриншотов.");

    private async Task RefreshLensGlassAsync()
    {
        if (_captureUiBusy || _glassCaptureBusy || _windowClosed || DateTime.UtcNow < _glassRetryAfter) return;
        if (!_settings.LiquidGlass || !_settings.LensGlass || !IsLoaded || !IsVisible || WindowState == WindowState.Minimized)
        { ClearLensBackdrop(); return; }
        _glassCaptureBusy = true;
        int generation = _glassGeneration;
        try
        {
            var visible = new List<(GlassSurface Surface, Rectangle Bounds, double Width, double Height, int Radius)>();
            foreach (var surface in _glassSurfaces)
            {
                var element = surface.Layer;
                if (!surface.Border.IsVisible || PresentationSource.FromVisual(element) == null)
                { ClearLensSurface(surface); continue; }
                double width = element.ActualWidth, height = element.ActualHeight;
                if (width < 2 || height < 2) continue;
                Point topLeft = element.PointToScreen(new Point(0, 0));
                Point bottomRight = element.PointToScreen(new Point(width, height));
                var bounds = Rectangle.FromLTRB((int)Math.Floor(Math.Min(topLeft.X, bottomRight.X)), (int)Math.Floor(Math.Min(topLeft.Y, bottomRight.Y)),
                    (int)Math.Ceiling(Math.Max(topLeft.X, bottomRight.X)), (int)Math.Ceiling(Math.Max(topLeft.Y, bottomRight.Y)));
                visible.Add((surface, bounds, width, height, (int)Math.Round(surface.Border.CornerRadius.TopLeft)));
            }
            if (visible.Count == 0) return;
            var excluded = visible.Select(s => (PresentationSource.FromVisual(s.Surface.Border) as HwndSource)?.Handle ?? IntPtr.Zero)
                .Append(new WindowInteropHelper(this).Handle).Where(h => h != IntPtr.Zero).Distinct().ToArray();
            // All visible capsules share a single capture; local crops keep
            // their individual lens geometry and never affect foreground text.
            var union = visible.Select(s => s.Bounds).Aggregate(Rectangle.Union);
            _glassCapture ??= new FilteredBackdropCapture();
            var frame = await _glassCapture.CaptureAsync(union, excluded);
            if (_windowClosed || generation != _glassGeneration || !IsVisible) return;
            if (frame == null) { ClearLensBackdrop(); return; }
            var maps = await Task.Run(() => visible.Select(s =>
                s.Surface.Effect == null || s.Surface.Geometry != ((int)Math.Round(s.Width), (int)Math.Round(s.Height), s.Radius)
                    ? GlassLensMap.Create(s.Width, s.Height, s.Radius) : null).ToArray());
            if (_windowClosed || generation != _glassGeneration) return;
            var theme = _themes.FirstOrDefault(t => t.Key == _settings.SelectedTheme) ?? _themes[0];
            var tint = (Color)System.Windows.Media.ColorConverter.ConvertFromString(theme.C1);
            tint.A = theme.IsLight ? (byte)60 : (byte)92;
            for (int i = 0; i < visible.Count; i++)
            {
                var (surface, bounds, width, height, radius) = visible[i];
                surface.Effect ??= new LiquidGlassEffect();
                if (maps[i] != null)
                {
                    surface.Effect.Map = new ImageBrush(maps[i]) { Stretch = Stretch.Fill };
                    surface.Geometry = ((int)Math.Round(width), (int)Math.Round(height), radius);
                }
                surface.Effect.Sampling = new Point4D(GlassLensMap.MaxDisplacement / width, GlassLensMap.MaxDisplacement / height, .8 / width, .8 / height);
                surface.Effect.Tint = tint;
                var pointer = System.Windows.Input.Mouse.GetPosition(surface.Border);
                surface.Effect.Pointer = new Point(Math.Clamp(pointer.X / width, 0, 1), Math.Clamp(pointer.Y / height, 0, 1));
                var crop = new CroppedBitmap(frame.Image, new Int32Rect(bounds.X - union.X, bounds.Y - union.Y, bounds.Width, bounds.Height));
                crop.Freeze();
                surface.Image.Effect = surface.Effect;
                UpdateGlassSurfaceGeometry(surface);
                surface.Image.Source = crop;
                surface.Image.Opacity = BackgroundOpacity;
                surface.Image.Visibility = Visibility.Visible;
                if (surface.FallbackKey != null) surface.Border.Background = System.Windows.Media.Brushes.Transparent;
                else surface.Border.Background.Opacity = 0;
                if (surface.Sheen != null) surface.Sheen.Visibility = Visibility.Collapsed;
                if (surface.Border == IslandBorder) _glassEffect = surface.Effect;
                if (maps[i] != null) AppLog.Glass($"active surface={surface.Border.Name} size={bounds.Width}x{bounds.Height} captureMs={frame.Milliseconds:F1}");
            }
            _glassTimer.Interval = TimeSpan.FromMilliseconds(Math.Clamp(frame.Milliseconds * 2, 50, 250));
        }
        catch (Exception ex)
        {
            ClearLensBackdrop(); _glassRetryAfter = DateTime.UtcNow.AddSeconds(10);
            AppLog.Error("Liquid Glass backdrop", ex);
        }
        finally { _glassCaptureBusy = false; }
    }

    private void DisposeLensGlass()
    {
        _glassGeneration++; _glassTimer.Stop();
        _glassCapture?.Dispose(); _glassCapture = null;
        foreach (var surface in _glassSurfaces) { surface.Image.Source = null; surface.Image.Effect = null; surface.Effect = null; }
        _glassEffect = null;
    }
}
