using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using Point = System.Windows.Point;
using Button = System.Windows.Controls.Button;
using Control = System.Windows.Controls.Control;
using Brush = System.Windows.Media.Brush;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Size = System.Windows.Size;
using Brushes = System.Windows.Media.Brushes;
using Pen = System.Windows.Media.Pen;

namespace DynamicIslandWin;

public partial class MainWindow
{
    private Button? _activeSettingsNav;

    private double BackgroundOpacity => 1 - (double.IsFinite(_settings.BackgroundTransparency)
        ? Math.Clamp(_settings.BackgroundTransparency, 0, 90) : 0) / 100;

    private void TransparencySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isLoadingSettings || TransparencyValueText == null) return;
        _settings.BackgroundTransparency = e.NewValue;
        TransparencyValueText.Text = $"{e.NewValue:0}%";
        ApplyTheme(_themes.FirstOrDefault(t => t.Key == _settings.SelectedTheme) ?? _themes[0]);
        SaveSettings();
    }

    private void ApplyThemeResources(ThemePreset theme)
    {
        var c1 = (Color)ColorConverter.ConvertFromString(theme.C1);
        var c2 = (Color)ColorConverter.ConvertFromString(theme.C2);
        var accent = (Color)ColorConverter.ConvertFromString(theme.DotHex);
        bool light = theme.IsLight;
        bool glass = _settings.LiquidGlass;
        Color ink = light ? Color.FromRgb(22, 30, 48) : Color.FromRgb(245, 247, 255);
        Color muted = light ? Color.FromRgb(75, 85, 105) : Color.FromRgb(170, 179, 202);
        // Light palettes need darker accents to keep small text readable.
        if (light) accent = Blend(accent, Color.FromRgb(9, 35, 59), .48);
        Resources["ThemeTextBrush"] = new SolidColorBrush(ink);
        Resources["ThemeSecondaryBrush"] = new SolidColorBrush(Blend(ink, c1, .18));
        Resources["ThemeMutedBrush"] = new SolidColorBrush(muted);
        Resources["ThemeDangerBrush"] = new SolidColorBrush(light ? Color.FromRgb(176, 35, 62) : Color.FromRgb(255, 145, 155));
        Resources["ThemeWarningBrush"] = new SolidColorBrush(light ? Color.FromRgb(150, 76, 8) : Color.FromRgb(255, 189, 119));
        Resources["ThemeAccentBrush"] = new SolidColorBrush(accent);
        Resources["ThemeOnAccentBrush"] = new SolidColorBrush(light ? Colors.White : Color.FromRgb(9, 24, 32));
        Resources["ThemeSelectionBrush"] = new SolidColorBrush(WithAlpha(accent, light ? (byte)35 : (byte)50));
        Resources["ThemeInputBrush"] = new SolidColorBrush(light ? Color.FromArgb(155, 255, 255, 255) : Color.FromArgb(35, 255, 255, 255));
        Resources["ThemeHoverBrush"] = new SolidColorBrush(light ? Color.FromArgb(30, 12, 30, 50) : Color.FromArgb(58, 255, 255, 255));
        Resources["ThemeBorderBrush"] = new SolidColorBrush(light ? Color.FromArgb(38, 22, 40, 65) : Color.FromArgb(45, 235, 244, 255));
        Resources["ThemePopupBrush"] = new SolidColorBrush(Blend(c1, c2, .5));
        Resources["ThemeFloatingBrush"] = glass
            ? new LinearGradientBrush(WithAlpha(c1, 194), WithAlpha(c2, 172), 65)
            : new SolidColorBrush(Blend(c1, c2, .5));
        Resources["ThemeSurfaceBrush"] = glass
            ? new LinearGradientBrush(light ? Color.FromArgb(155, 255, 255, 255) : Color.FromArgb(35, 255, 255, 255),
                light ? Color.FromArgb(62, 255, 255, 255) : WithAlpha(c2, 90), 75)
            : new SolidColorBrush(Blend(c1, light ? Colors.White : c2, .4));
        Resources["ThemeCapsuleBrush"] = new LinearGradientBrush(WithAlpha(c1, glass ? (byte)178 : (byte)255),
            WithAlpha(c2, glass ? (byte)156 : (byte)255), 65);
        Resources["GlassEdgeBrush"] = glass
            ? new LinearGradientBrush(light ? Color.FromArgb(240, 255, 255, 255) : Color.FromArgb(130, 235, 246, 255),
                WithAlpha(accent, 45), 75)
            : new SolidColorBrush(Colors.Transparent);
        Resources["GlassSheenBrush"] = glass ? new LinearGradientBrush
        {
            StartPoint = new Point(0, 0), EndPoint = new Point(.85, 1),
            GradientStops = new GradientStopCollection
            {
                new(WithAlpha(Colors.White, light ? (byte)100 : (byte)42), 0),
                new(WithAlpha(Colors.White, 5), .38),
                new(Colors.Transparent, .6), new(WithAlpha(accent, 22), 1)
            }
        } : new SolidColorBrush(Colors.Transparent);
        foreach (string key in new[] { "ThemeFloatingBrush", "ThemePopupBrush", "ThemeSurfaceBrush", "ThemeCapsuleBrush", "GlassEdgeBrush", "GlassSheenBrush" })
        {
            var background = ((Brush)Resources[key]).Clone();
            background.Opacity = BackgroundOpacity;
            Resources[key] = background;
        }
        Resources["ThemeFloatingEdgeBrush"] = glass ? Resources["GlassEdgeBrush"] : Resources["ThemeBorderBrush"];
        CustomBgBorder.Opacity = .32 * BackgroundOpacity;
        // The sheen brush already includes the background opacity. Only scale
        // the independent specular reflection here, keeping foregrounds opaque.
        GlassSpecularBorder.Opacity = .32 * BackgroundOpacity;
        // ContextMenu lives in a separate popup tree. Give it the same palette
        // explicitly so its custom template also updates while the menu is open.
        foreach (string key in Resources.Keys.OfType<string>())
            if ((key.StartsWith("Theme") || key.StartsWith("Glass")) && Resources[key] is Brush brush)
                IslandBorder.ContextMenu.Resources[key] = brush;
        UpdateGameHudValues();
        LiquidGlassOverlay.Visibility = glass ? Visibility.Visible : Visibility.Collapsed;
        IslandBorder.BorderThickness = new Thickness(glass ? 1 : 0);
        SideToastBorder.BorderThickness = new Thickness(glass ? 1 : 0);
        DiscordCallBorder.BorderThickness = new Thickness(glass ? 1 : 0);
        if (_activeSettingsNav != null)
            _activeSettingsNav.SetResourceReference(Control.BackgroundProperty, "ThemeSelectionBrush");
        LiquidGlassBackdrop.Opacity = BackgroundOpacity;
        ConfigureLensGlass();
    }

    private static Color Blend(Color a, Color b, double amount) => Color.FromRgb(
        (byte)(a.R + (b.R - a.R) * amount), (byte)(a.G + (b.G - a.G) * amount), (byte)(a.B + (b.B - a.B) * amount));

    private static Color WithAlpha(Color color, byte alpha) => Color.FromArgb(alpha, color.R, color.G, color.B);

    private void UpdateGlassReflection(MouseEventArgs e)
    {
        if (!_settings.LiquidGlass || IslandBorder.ActualWidth <= 0 || IslandBorder.ActualHeight <= 0) return;
        var point = e.GetPosition(IslandBorder);
        GlassSpecularBrush.Center = new Point(Math.Clamp(point.X / IslandBorder.ActualWidth, 0, 1),
            Math.Clamp(point.Y / IslandBorder.ActualHeight, 0, 1));
        GlassSpecularBrush.GradientOrigin = GlassSpecularBrush.Center;
        if (_glassEffect != null) _glassEffect.Pointer = GlassSpecularBrush.Center;
    }

    private Size _shadowSize;
    private double _shadowRadius;
    private void UpdateGlassShadow()
    {
        IslandShadow.OpacityMask = _settings.IslandShadow ? null : Brushes.Transparent;
        if (!_settings.IslandShadow) return;
        var size = new Size(Math.Max(1, IslandBorder.Width), Math.Max(1, IslandBorder.Height));
        double radius = IslandBorder.CornerRadius.TopLeft;
        if (size == _shadowSize && radius == _shadowRadius) return;
        _shadowSize = size; _shadowRadius = radius;
        // Only draw outside the capsule. A filled shadow silhouette underneath
        // translucent glass turns every theme opaque black.
        var drawing = new DrawingGroup();
        using (var context = drawing.Open())
        {
            context.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, size.Width + 56, size.Height + 56));
            for (int distance = 24; distance >= 1; distance--)
            {
                byte alpha = (byte)(35 * Math.Exp(-distance * distance / 100.0));
                var pen = new Pen(new SolidColorBrush(Color.FromArgb(alpha, 0, 0, 0)), 1.1);
                context.DrawRoundedRectangle(null, pen,
                    new Rect(28 - distance, 28 - distance, size.Width + distance * 2, size.Height + distance * 2),
                    radius + distance, radius + distance);
            }
        }
        drawing.Freeze();
        IslandShadow.Source = new DrawingImage(drawing);
        IslandShadow.Width = size.Width + 56; IslandShadow.Height = size.Height + 56;
    }
}
