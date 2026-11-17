using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using DynamicIslandWin.Services;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        if (args.Contains("--live-probe")) return LiveProbe.Run();
        if (args.Contains("--motion-probe")) return MotionProbe.Run();
        string output = Path.GetFullPath("artifacts/glass-prototype");
        Directory.CreateDirectory(output);
        int checks = 0;
        void Check(bool value, string label) { if (!value) throw new Exception(label); checks++; }
        var center = GlassLensMap.Evaluate(240, 55, 480, 110, 55);
        Check(center.X == 0 && center.Y == 0, "The flat lens center does not move the background");
        var left = GlassLensMap.Evaluate(1, 55, 480, 110, 55);
        var right = GlassLensMap.Evaluate(479, 55, 480, 110, 55);
        Check(left.X > 0 && right.X < 0 && Math.Abs(left.X + right.X) < .0001, "Opposite edges refract inward symmetrically");
        var air = GlassLensMap.Evaluate(1, 55, 480, 110, 55, 1);
        Check(Math.Abs(air.X) < .0001 && Math.Abs(air.Y) < .0001, "Index 1 removes refraction");
        foreach (var size in new[] { (210, 38, 19), (740, 515, 28), (480, 64, 24), (160, 32, 4) })
            for (int y = 0; y < size.Item2; y += 7)
                for (int x = 0; x < size.Item1; x += 7)
                {
                    var sample = GlassLensMap.Evaluate(x, y, size.Item1, size.Item2, size.Item3);
                    Check(double.IsFinite(sample.X) && double.IsFinite(sample.Y) && Math.Abs(sample.X) <= 12
                        && Math.Abs(sample.Y) <= 12 && sample.Rim is >= 0 and <= 1, "Bounded finite lens at all island sizes");
                }
        var watch = Stopwatch.StartNew();
        var map = GlassLensMap.Create(740, 515, 28);
        double mapMs = watch.Elapsed.TotalMilliseconds;
        Check(map.IsFrozen && map.PixelWidth <= 768 && map.PixelHeight <= 768, "Lens map is bounded and transferable across threads");
        var neutral = new byte[4];
        map.CopyPixels(new Int32Rect(map.PixelWidth / 2, map.PixelHeight / 2, 1, 1), neutral, 4, 0);
        Check(neutral[1] == 128 && neutral[2] == 128, "Neutral map encodes exactly zero displacement");

        var backdrop = Backdrop(480, 110);
        Save(GlassLensMap.Create(480, 110, 55), Path.Combine(output, "map.png"));
        var identity = Lens(backdrop, true);
        ((LiquidGlassEffect)identity.Effect).Sampling = new Point4D(0, 0, 0, 0);
        ((LiquidGlassEffect)identity.Effect).Tint = Colors.Transparent;
        ((LiquidGlassEffect)identity.Effect).Strength = 0;
        Save(Render(identity, 480, 110), Path.Combine(output, "identity.png"));
        var plain = Lens(backdrop, false);
        var glass = Lens(backdrop, true);
        var plainPixels = Render(plain, 480, 110);
        var glassPixels = Render(glass, 480, 110);
        var identityBytes = Bytes(Render(identity, 480, 110));
        var plainBytes = Bytes(plainPixels);
        int identityError = 0;
        for (int y = 15; y < 95; y++) for (int x = 50; x < 430; x++) for (int channel = 0; channel < 3; channel++)
            identityError = Math.Max(identityError, Math.Abs(identityBytes[(y * 480 + x) * 4 + channel] - plainBytes[(y * 480 + x) * 4 + channel]));
        Check(identityError <= 2, "A disabled lens preserves background pixels, without sampling stripes or stretching");
        Check(!Bytes(plainPixels).SequenceEqual(Bytes(glassPixels)), "Actual WPF pixel shader changes the rendered backdrop");
        var corner = new byte[4]; glassPixels.CopyPixels(new Int32Rect(0, 0, 1, 1), corner, 4, 0);
        Check(corner[3] == 0, "Rounded lens does not paint rectangular corners");
        Save(glassPixels, Path.Combine(output, "lens.png"));

        var layout = new StackPanel { Background = new SolidColorBrush(Color.FromRgb(22, 27, 42)), Margin = new Thickness(0) };
        void Add(string title, bool enabled)
        {
            layout.Children.Add(new TextBlock { Text = title, Foreground = Brushes.White, FontSize = 15, Margin = new Thickness(28, 22, 0, 12) });
            var lens = Lens(backdrop, enabled);
            var capsule = new Grid { Width = 480, Height = 110, Margin = new Thickness(28, 0, 28, 0) };
            capsule.Children.Add(lens);
            capsule.Children.Add(new TextBlock { Text = "12:48        21°       ♪", Foreground = Brushes.White, FontSize = 24,
                FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
            layout.Children.Add(capsule);
        }
        Add("Исходный фон · без линзы", false);
        Add("WPF Liquid Glass · преломление и светлая грань", true);
        Save(Render(layout, 536, 340), Path.Combine(output, "comparison.png"));
        Console.WriteLine($"PASS: {checks} glass checks; map {mapMs:0.0} ms; WPF tier {RenderCapability.Tier >> 16}; previews: {output}");
        if (args.Contains("--preview"))
        {
            var preview = new Window { Title = "Dynamic Island · Liquid Glass prototype", Content = layout, Width = 570, Height = 430,
                WindowStartupLocation = WindowStartupLocation.CenterScreen, Background = layout.Background };
            preview.Closed += (_, _) => app.Shutdown();
            app.Run(preview);
        }
        return 0;
    }

    private static BitmapSource Backdrop(int width, int height)
    {
        var drawing = new DrawingVisual();
        using (var dc = drawing.RenderOpen())
        {
            dc.DrawRectangle(new LinearGradientBrush(Color.FromRgb(58, 186, 219), Color.FromRgb(126, 49, 203), 20), null, new Rect(0, 0, width, height));
            for (int x = -50; x < width + 50; x += 28)
                dc.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(150, 237, 247, 255)), 3), new Point(x, 0), new Point(x + 48, height));
            dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(238, 145, 57)), null, new Point(80, 80), 55, 55);
            dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(52, 213, 168)), null, new Point(400, 45), 65, 55);
        }
        var image = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); image.Render(drawing); image.Freeze(); return image;
    }

    private static Image Lens(BitmapSource backdrop, bool enabled)
    {
        var image = new Image { Source = backdrop, Width = 480, Height = 110, Stretch = Stretch.Fill,
            Clip = new RectangleGeometry(new Rect(0, 0, 480, 110), 55, 55) };
        if (enabled) image.Effect = new LiquidGlassEffect
        {
            Map = new ImageBrush(GlassLensMap.Create(480, 110, 55)),
            Sampling = new Point4D(12d / 480, 12d / 110, .8 / 480, .8 / 110), Tint = Color.FromArgb(46, 17, 19, 28)
        };
        return image;
    }
    private static RenderTargetBitmap Render(FrameworkElement visual, int width, int height)
    {
        visual.Measure(new Size(width, height)); visual.Arrange(new Rect(0, 0, width, height)); visual.UpdateLayout();
        var image = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); image.Render(visual); return image;
    }
    private static byte[] Bytes(BitmapSource bitmap)
    { var bytes = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4]; bitmap.CopyPixels(bytes, bitmap.PixelWidth * 4, 0); return bytes; }
    private static void Save(BitmapSource bitmap, string path)
    { var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var stream = File.Create(path); encoder.Save(stream); }
}
