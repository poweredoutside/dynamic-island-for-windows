using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DynamicIslandWin;

internal static class MotionProbe
{
    internal static int Run()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var window = new MainWindow { Width = 1100, Height = 650, Left = 0, Top = 0 };
        object? Call(string name, params object[] args) => typeof(MainWindow).GetMethod(name, flags)!.Invoke(window, args);
        void Set(string name, object value) => typeof(MainWindow).GetField(name, flags)!.SetValue(window, value);
        T Element<T>(string name) => (T)window.FindName(name);
        window.Loaded -= (RoutedEventHandler)typeof(MainWindow).GetMethod("Window_Loaded", flags)!.CreateDelegate(typeof(RoutedEventHandler), window);
        window.Closing -= (CancelEventHandler)typeof(MainWindow).GetMethod("Window_Closing", flags)!.CreateDelegate(typeof(CancelEventHandler), window);
        window.Closed -= (EventHandler)typeof(MainWindow).GetMethod("Window_Closed", flags)!.CreateDelegate(typeof(EventHandler), window);
        Set("_isLoadingSettings", true);
        var settings = (MainWindow.AppSettings)typeof(MainWindow).GetField("_settings", flags)!.GetValue(window)!;
        settings.LiquidGlass = true; settings.LensGlass = true; settings.ExcludeFromCapture = false;
        settings.IslandScalePercent = 60;
        var fixture = new Window { Left = 0, Top = 0, Width = SystemParameters.PrimaryScreenWidth, Height = SystemParameters.PrimaryScreenHeight,
            WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, Background = new SolidColorBrush(Color.FromRgb(35, 128, 184)), Topmost = true, ShowInTaskbar = false };
        int frames = 0, staleClips = 0, badSources = 0, oversize = 0;
        var output = Path.GetFullPath("artifacts/glass-motion"); Directory.CreateDirectory(output);
        void Pump(int milliseconds)
        {
            var frame = new DispatcherFrame(); var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
            timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame);
        }
        try
        {
            fixture.Show(); window.Show(); window.UpdateLayout(); Call("ApplyExcludeFromCapture", false); Call("ConfigureLensGlass");
            var backdrop = Element<Image>("LiquidGlassBackdrop"); var island = Element<Border>("IslandBorder");
            var state = typeof(MainWindow).GetNestedType("IslandState", BindingFlags.NonPublic)!;
            var timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
            timer.Tick += (_, _) =>
            {
                frames++;
                var host = Element<Grid>("IslandSurfaceLayer");
                if (host.ActualWidth > island.ActualWidth + 1 || host.ActualHeight > island.ActualHeight + 1) oversize++;
                if (backdrop.Source is not BitmapSource source) return;
                if (host.Clip is not RectangleGeometry clip ||
                    (Math.Abs(clip.Rect.Width - backdrop.ActualWidth) > 1 || Math.Abs(clip.Rect.Height - backdrop.ActualHeight) > 1)) staleClips++;
                var bytes = new byte[source.PixelWidth * source.PixelHeight * 4]; source.CopyPixels(bytes, source.PixelWidth * 4, 0);
                int bad = 0, total = 0;
                for (int y = 2; y < source.PixelHeight - 2; y += 7) for (int x = 2; x < source.PixelWidth - 2; x += 7)
                {
                    int i = (y * source.PixelWidth + x) * 4; total++;
                    if (Math.Abs(bytes[i] - 184) > 6 || Math.Abs(bytes[i + 1] - 128) > 6 || Math.Abs(bytes[i + 2] - 35) > 6) bad++;
                }
                if (bad > total * .01) badSources++;
            };
            timer.Start();
            foreach (var name in new[] { "Hover", "Compact", "Expanded", "Compact", "Hover", "Expanded", "Compact", "Hover", "Compact" })
            {
                Call("MorphToState", Enum.Parse(state, name));
                Pump(120); Save(window, Path.Combine(output, $"{frames:D3}-{name}.png"));
                Pump(140);
            }
            Pump(400); timer.Stop();
            Console.WriteLine($"Motion: frames={frames} staleClips={staleClips} contaminatedSources={badSources} oversizedContent={oversize}");
            File.WriteAllText(Path.Combine(output, "report.txt"), $"frames={frames} staleClips={staleClips} contaminatedSources={badSources} oversizedContent={oversize}");
            return staleClips + badSources + oversize == 0 ? 0 : 1;
        }
        finally { Call("DisposeLensGlass"); Call("StopIslandFrames"); window.Close(); fixture.Close(); }
    }

    private static void Save(FrameworkElement visual, string path)
    {
        var image = new RenderTargetBitmap((int)visual.ActualWidth, (int)visual.ActualHeight, 96, 96, PixelFormats.Pbgra32); image.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); using var file = File.Create(path); encoder.Save(file);
    }
}
