using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Color = System.Windows.Media.Color;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;

namespace DynamicIslandWin;

public sealed class SnowfallLayer : FrameworkElement
{
    private const int MaxFlakes = 32;
    private static readonly Pen DarkPalette = MakePen(Color.FromArgb(185, 232, 247, 255));
    private static readonly Pen LightPalette = MakePen(Color.FromArgb(150, 55, 106, 150));
    private static readonly Vector[] Spokes = Enumerable.Range(0, 3)
        .Select(i => new Vector(Math.Cos(i * Math.PI / 3), Math.Sin(i * Math.PI / 3)))
        .ToArray();
    private readonly DrawingVisual _drawing = new();
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly Flake[] _flakes = new Flake[MaxFlakes];
    private long _lastFrame;
    private double _elapsed;

    internal bool UseLightPalette { get; set; }

    public SnowfallLayer()
    {
        AddVisualChild(_drawing);
        var random = Random.Shared;
        for (int i = 0; i < _flakes.Length; i++)
        {
            _flakes[i] = new Flake(
                random.NextDouble(), random.NextDouble(), 1.4 + 2.7 * random.NextDouble(),
                24 + 35 * random.NextDouble(), random.NextDouble() * Math.Tau);
        }
        _timer.Tick += (_, _) =>
        {
            long now = Stopwatch.GetTimestamp();
            DrawFrame(Math.Min(.1, Stopwatch.GetElapsedTime(_lastFrame, now).TotalSeconds));
            _lastFrame = now;
        };
        Loaded += (_, _) => UpdateAnimation();
        Unloaded += (_, _) => _timer.Stop();
        IsVisibleChanged += (_, _) => UpdateAnimation();
    }

    protected override int VisualChildrenCount => 1;
    protected override Visual GetVisualChild(int index) => index == 0 ? _drawing : throw new ArgumentOutOfRangeException(nameof(index));

    private void UpdateAnimation()
    {
        if (IsLoaded && IsVisible)
        {
            if (_timer.IsEnabled) return;
            _lastFrame = Stopwatch.GetTimestamp();
            DrawFrame(0);
            _timer.Start();
        }
        else
        {
            _timer.Stop();
        }
    }

    private void DrawFrame(double delta)
    {
        using var context = _drawing.RenderOpen();
        double width = ActualWidth, height = ActualHeight;
        if (width <= 0 || height <= 0) return;

        _elapsed += delta;
        int count = (int)Math.Clamp(width / 31 + height / 34, 6, MaxFlakes);
        var pen = UseLightPalette ? LightPalette : DarkPalette;
        for (int i = 0; i < count; i++)
        {
            ref var flake = ref _flakes[i];
            double radius = flake.Radius;
            double travel = height + 2 * radius;
            flake.Progress = (flake.Progress + delta * flake.Speed / travel) % 1;
            var center = new Point(
                flake.Across * width + Math.Sin(_elapsed * 1.25 + flake.Phase) * radius * 2,
                flake.Progress * travel - radius);
            foreach (var spoke in Spokes)
            {
                var arm = spoke * radius;
                context.DrawLine(pen, center - arm, center + arm);
            }
        }
    }

    private static Pen MakePen(Color color)
    {
        var pen = new Pen(new SolidColorBrush(color), .9) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        pen.Freeze();
        return pen;
    }

    private struct Flake(double across, double progress, double radius, double speed, double phase)
    {
        internal readonly double Across = across, Radius = radius, Speed = speed, Phase = phase;
        internal double Progress = progress;
    }
}
