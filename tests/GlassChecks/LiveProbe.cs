using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DynamicIslandWin;

// This integration fixture owns both visible windows. The region sampled by the
// product contains our test pattern only. No desktop images are written to disk.
internal static class LiveProbe
{
    internal static int Run()
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        var window = new MainWindow { Title = "Liquid Glass · synthetic test", Width = 900, Height = 300, Left = 80, Top = 80 };
        object? Call(string name, params object?[] values) => typeof(MainWindow).GetMethod(name, flags)!.Invoke(window, values);
        T Field<T>(string name) => (T)typeof(MainWindow).GetField(name, flags)!.GetValue(window)!;
        void Set(string name, object value) => typeof(MainWindow).GetField(name, flags)!.SetValue(window, value);
        T Element<T>(string name) => (T)window.FindName(name);
        // Never run notification, clipboard, hotkey, sound or settings integrations.
        window.Loaded -= (RoutedEventHandler)typeof(MainWindow).GetMethod("Window_Loaded", flags)!.CreateDelegate(typeof(RoutedEventHandler), window);
        window.Closing -= (CancelEventHandler)typeof(MainWindow).GetMethod("Window_Closing", flags)!.CreateDelegate(typeof(CancelEventHandler), window);
        window.Closed -= (EventHandler)typeof(MainWindow).GetMethod("Window_Closed", flags)!.CreateDelegate(typeof(EventHandler), window);
        Set("_isLoadingSettings", true);
        var settings = Field<MainWindow.AppSettings>("_settings"); settings.LensGlass = true; settings.LiquidGlass = true; settings.ExcludeFromCapture = true;
        var background = new Grid { Background = new SolidColorBrush(Color.FromRgb(35, 128, 184)) };
        var pattern = new System.Windows.Shapes.Rectangle { Width = 330, Height = 300, Fill = new SolidColorBrush(Color.FromRgb(224, 119, 41)), HorizontalAlignment = HorizontalAlignment.Left };
        background.Children.Add(pattern);
        var fixture = new Window { Title = "Glass backdrop fixture", Width = 900, Height = 300, Left = 80, Top = 80,
            WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, Content = background, ShowInTaskbar = false, Topmost = true };
        string output = Path.GetFullPath("artifacts/glass-prototype"); Directory.CreateDirectory(output);
        try
        {
            fixture.Show(); window.Show(); window.UpdateLayout();
            Call("ApplyExcludeFromCapture", true);
            Call("ConfigureLensGlass");
            bool WaitFor(Func<bool> predicate, int milliseconds = 5000)
            {
                var watch = Stopwatch.StartNew();
                while (!predicate() && watch.ElapsedMilliseconds < milliseconds)
                {
                    var frame = new DispatcherFrame(); var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
                    timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame);
                }
                return predicate();
            }
            var backdrop = Element<Image>("LiquidGlassBackdrop");
            if (!WaitFor(() => backdrop.Source != null && backdrop.Visibility == Visibility.Visible))
                throw new Exception("The initially collapsed lens did not receive a desktop backdrop");
            var image = (BitmapSource)backdrop.Source;
            var pixel = new byte[4]; image.CopyPixels(new Int32Rect(image.PixelWidth / 2, image.PixelHeight / 2, 1, 1), pixel, 4, 0);
            if (Math.Abs(pixel[0] - 184) > 4 || Math.Abs(pixel[1] - 128) > 4 || Math.Abs(pixel[2] - 35) > 4)
                throw new Exception($"Backdrop captured the island itself instead of the synthetic pattern: {Convert.ToHexString(pixel)}");
            var durations = new List<double>();
            var nativeDurations = new List<double>();
            for (int i = 0; i < 20; i++)
            {
                Field<DispatcherTimer>("_glassTimer").Stop();
                if (!WaitFor(() => !Field<bool>("_glassCaptureBusy"))) throw new Exception("Capture worker stalled");
                var captureWatch = Stopwatch.StartNew();
                var work = (Task)Call("RefreshLensGlassAsync")!;
                if (!WaitFor(() => work.IsCompleted)) throw new Exception("Capture stalled");
                work.GetAwaiter().GetResult();
                durations.Add(captureWatch.Elapsed.TotalMilliseconds);
                var surface = Element<Grid>("IslandSurfaceLayer");
                var topLeft = surface.PointToScreen(new Point(0, 0));
                var bottomRight = surface.PointToScreen(new Point(surface.ActualWidth, surface.ActualHeight));
                var bounds = System.Drawing.Rectangle.FromLTRB((int)topLeft.X, (int)topLeft.Y, (int)bottomRight.X, (int)bottomRight.Y);
                var native = Task.Run(() => DynamicIslandWin.Services.GlassBackdropService.Capture(bounds));
                if (!WaitFor(() => native.IsCompleted)) throw new Exception("Native capture stalled");
                nativeDurations.Add(native.Result!.Milliseconds);
            }
            image = (BitmapSource)backdrop.Source;
            image.CopyPixels(new Int32Rect(image.PixelWidth / 2, image.PixelHeight / 2, 1, 1), pixel, 4, 0);
            if (Math.Abs(pixel[0] - 184) > 4 || Math.Abs(pixel[2] - 35) > 4) throw new Exception("Backdrop feedback appeared on later frames");
            // Change our background and verify it is a live sample, not a snapshot.
            background.Background = new SolidColorBrush(Color.FromRgb(79, 184, 102));
            Field<DispatcherTimer>("_glassTimer").Start();
            bool Changed()
            {
                if (backdrop.Source is not BitmapSource current) return false;
                var center = new byte[4]; current.CopyPixels(new Int32Rect(current.PixelWidth / 2, current.PixelHeight / 2, 1, 1), center, 4, 0);
                return Math.Abs(center[0] - 102) < 4 && Math.Abs(center[1] - 184) < 4 && Math.Abs(center[2] - 79) < 4;
            }
            if (!WaitFor(Changed)) throw new Exception("Live backdrop did not follow a changed fixture");
            var island = Element<Border>("IslandBorder"); island.Width = 480; island.Height = 64; island.CornerRadius = new CornerRadius(24);
            window.UpdateLayout();
            if (!WaitFor(() => backdrop.Source is BitmapSource resized && resized.PixelWidth > image.PixelWidth + 100))
                throw new Exception("Lens did not rebuild after geometry changed");
            foreach (var size in new[] { (740, 515), (210, 38), (480, 64), (740, 515), (210, 38), (480, 64) })
            {
                var oldFrame = backdrop.Source;
                island.Width = size.Item1; island.Height = size.Item2; window.UpdateLayout();
                if (!WaitFor(() => backdrop.Source is BitmapSource current && !ReferenceEquals(current, oldFrame)
                    && current.PixelHeight > size.Item2 * 1.1 && current.PixelHeight < size.Item2 * 1.4))
                    throw new Exception("Backdrop stalled during a compact/expanded size transition");
            }
            var geometryWatch = Stopwatch.StartNew();
            var largeMap = DynamicIslandWin.Services.GlassLensMap.Create(740, 515, 28);
            double largeMapMs = geometryWatch.Elapsed.TotalMilliseconds;
            // Display affinity controls screenshots only; filtered capture must
            // keep sampling the background with either setting, without feedback.
            var beforeToggle = backdrop.Source;
            Call("ApplyExcludeFromCapture", false);
            if (!WaitFor(() => !ReferenceEquals(backdrop.Source, beforeToggle) && Changed())) throw new Exception("Lens stopped when screenshot hiding was disabled");
            for (int i = 0; i < 8; i++)
            {
                if (!WaitFor(() => !Field<bool>("_glassCaptureBusy"))) throw new Exception("Capture worker stalled");
                var work = (Task)Call("RefreshLensGlassAsync")!;
                if (!WaitFor(() => work.IsCompleted)) throw new Exception("Inclusive capture stalled");
                work.GetAwaiter().GetResult();
                if (!Changed()) throw new Exception("Self-capture appeared with screenshot hiding disabled");
            }
            var visibleSurface = Element<Grid>("IslandSurfaceLayer");
            var visibleTop = visibleSurface.PointToScreen(new Point(0, 0));
            var visibleBottom = visibleSurface.PointToScreen(new Point(visibleSurface.ActualWidth, visibleSurface.ActualHeight));
            var visibleBounds = System.Drawing.Rectangle.FromLTRB((int)visibleTop.X, (int)visibleTop.Y, (int)visibleBottom.X, (int)visibleBottom.Y);
            var withoutLayered = DynamicIslandWin.Services.GlassBackdropService.Capture(visibleBounds, false)!;
            var testPixel = new byte[4]; withoutLayered.Image.CopyPixels(new Int32Rect(withoutLayered.Image.PixelWidth / 2, withoutLayered.Image.PixelHeight / 2, 1, 1), testPixel, 4, 0);
            if (Math.Abs(testPixel[0] - 102) < 4 && Math.Abs(testPixel[1] - 184) < 4 && Math.Abs(testPixel[2] - 79) < 4)
                throw new Exception("Island unexpectedly disappeared from normal screenshots");
            Call("ApplyExcludeFromCapture", true);
            if (!WaitFor(() => backdrop.Source != null)) throw new Exception("Lens did not resume after restoring exclusion");
            // Every independent capsule uses the same live source. Show one at
            // a time to keep the synthetic fixture beneath its entire bounds.
            foreach (string name in new[] { "CompactProgressPanel", "LevelHudBorder", "DiscordCallBorder", "SideToastBorder" })
            {
                var panel = Element<Border>(name); panel.Visibility = Visibility.Visible; panel.Opacity = 1;
                window.UpdateLayout();
                var imageLayer = (Grid)panel.Child;
                var panelBackdrop = (Image)((Canvas)imageLayer.Children[0]).Children[0];
                if (!WaitFor(() => panelBackdrop.Source != null && panelBackdrop.Visibility == Visibility.Visible))
                    throw new Exception(name + " did not receive the shared lens backdrop");
                if (panelBackdrop.Effect is not DynamicIslandWin.Services.LiquidGlassEffect || panel.Background != Brushes.Transparent)
                    throw new Exception(name + " is still using the old opaque capsule material");
                panel.Visibility = Visibility.Collapsed;
                window.UpdateLayout();
            }
            foreach (string name in new[] { "CompactProgressPanel", "LevelHudBorder", "DiscordCallBorder", "SideToastBorder" })
                Element<Border>(name).Visibility = Visibility.Visible;
            window.UpdateLayout();
            var simultaneousSource = backdrop.Source;
            if (!WaitFor(() => !ReferenceEquals(backdrop.Source, simultaneousSource))) throw new Exception("Simultaneous capsules stalled capture");
            foreach (string name in new[] { "CompactProgressPanel", "LevelHudBorder", "DiscordCallBorder", "SideToastBorder" })
                Element<Border>(name).Visibility = Visibility.Collapsed;
            window.UpdateLayout();
            // Popup HWNDs must be excluded from the lens source as well.
            settings.ExcludeFromCapture = false; Call("ApplyExcludeFromCapture", false);
            island.ContextMenu.IsOpen = true; window.UpdateLayout();
            if (!WaitFor(() => VisualTreeHelper.GetChildrenCount(island.ContextMenu) > 0)) throw new Exception("Context menu did not open");
            var menuBorder = (Border)VisualTreeHelper.GetChild(island.ContextMenu, 0);
            var menuBackdrop = (Image)((Canvas)((Grid)menuBorder.Child).Children[0]).Children[0];
            if (!WaitFor(() => menuBackdrop.Source != null)) throw new Exception("Context menu did not receive lens glass");
            island.ContextMenu.IsOpen = false;
            island.ContextMenu.IsOpen = true;
            if (!WaitFor(() => menuBackdrop.Source != null)) throw new Exception("Reopened menu lost its lens");
            island.ContextMenu.IsOpen = false;
            settings.LiquidGlass = false; Call("ConfigureLensGlass");
            if (backdrop.Source != null || Field<DispatcherTimer>("_glassTimer").IsEnabled) throw new Exception("Disabling glass did not stop capture and release the frame");
            var report = new { success = true, sourceWasSyntheticPattern = true, selfCaptureExcluded = true,
                feedbackAbsent = true, backdropChanged = true, resized = true, exclusionToggleChecked = true,
                allCapsulesChecked = true, popupChecked = true, visibleInScreenshots = true, stoppedAndReleased = true,
                captureAndDispatchAverageMs = durations.Average(), captureAndDispatchMaxMs = durations.Max(), largeMapMs,
                nativeCaptureAverageMs = nativeDurations.Average(), nativeCaptureMaxMs = nativeDurations.Max(),
                pixelWidth = image.PixelWidth, pixelHeight = image.PixelHeight };
            File.WriteAllText(Path.Combine(output, "live-probe.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine("PASS: live backdrop, both screenshot modes, no self-feedback, all capsules, popup, resize and shutdown.");
            return 0;
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(output, "live-probe-error.txt"), ex.ToString());
            Console.WriteLine(ex.ToString());
            return 1;
        }
        finally { Call("DisposeLensGlass"); window.Close(); fixture.Close(); }
    }
}
