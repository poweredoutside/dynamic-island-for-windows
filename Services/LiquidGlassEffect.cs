using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Media3D;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;

namespace DynamicIslandWin.Services;

internal sealed class LiquidGlassEffect : ShaderEffect
{
    private static readonly PixelShader LensShader = LoadShader();
    private static PixelShader LoadShader()
    {
        var shader = new PixelShader { UriSource = new Uri("/DynamicIslandWin;component/Shaders/LiquidGlass.ps", UriKind.Relative) };
        shader.Freeze();
        return shader;
    }

    public static readonly DependencyProperty InputProperty = RegisterPixelShaderSamplerProperty(nameof(Input), typeof(LiquidGlassEffect), 0, SamplingMode.Bilinear);
    public static readonly DependencyProperty MapProperty = RegisterPixelShaderSamplerProperty(nameof(Map), typeof(LiquidGlassEffect), 1, SamplingMode.Bilinear);
    public static readonly DependencyProperty SamplingProperty = DependencyProperty.Register(nameof(Sampling), typeof(Point4D), typeof(LiquidGlassEffect), new UIPropertyMetadata(new Point4D(.05, .3, .003, .02), PixelShaderConstantCallback(0)));
    public static readonly DependencyProperty TintProperty = DependencyProperty.Register(nameof(Tint), typeof(Color), typeof(LiquidGlassEffect), new UIPropertyMetadata(Color.FromArgb(36, 17, 19, 28), PixelShaderConstantCallback(1)));
    public static readonly DependencyProperty StrengthProperty = DependencyProperty.Register(nameof(Strength), typeof(double), typeof(LiquidGlassEffect), new UIPropertyMetadata(1d, PixelShaderConstantCallback(2)));
    public static readonly DependencyProperty PointerProperty = DependencyProperty.Register(nameof(Pointer), typeof(System.Windows.Point), typeof(LiquidGlassEffect), new UIPropertyMetadata(new System.Windows.Point(.25, .1), PixelShaderConstantCallback(3)));
    public Brush Input { get => (Brush)GetValue(InputProperty); set => SetValue(InputProperty, value); }
    public Brush Map { get => (Brush)GetValue(MapProperty); set => SetValue(MapProperty, value); }
    public Point4D Sampling { get => (Point4D)GetValue(SamplingProperty); set => SetValue(SamplingProperty, value); }
    public Color Tint { get => (Color)GetValue(TintProperty); set => SetValue(TintProperty, value); }
    public double Strength { get => (double)GetValue(StrengthProperty); set => SetValue(StrengthProperty, value); }
    public System.Windows.Point Pointer { get => (System.Windows.Point)GetValue(PointerProperty); set => SetValue(PointerProperty, value); }

    internal LiquidGlassEffect()
    {
        PixelShader = LensShader;
        UpdateShaderValue(InputProperty); UpdateShaderValue(MapProperty);
        UpdateShaderValue(SamplingProperty); UpdateShaderValue(TintProperty); UpdateShaderValue(StrengthProperty);
        UpdateShaderValue(PointerProperty);
    }
}
