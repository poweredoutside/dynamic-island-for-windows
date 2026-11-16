using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using DynamicIslandWin.Services;
using Color = System.Windows.Media.Color;
using Brushes = System.Windows.Media.Brushes;
using Point = System.Windows.Point;
using Path = System.Windows.Shapes.Path;
using Ellipse = System.Windows.Shapes.Ellipse;
using Brush = System.Windows.Media.Brush;
using FontFamily = System.Windows.Media.FontFamily;

namespace DynamicIslandWin;

public partial class MainWindow
{
    private readonly List<WeakReference<FrameworkElement>> _animatedMascots = [];
    private readonly TranslateTransform _attraction = new();
    private bool _attractionInitialized;
    private double _attractionX, _attractionY, _attractionVx, _attractionVy;
    private double _attractionTargetX, _attractionTargetY;
    private Color _albumAccent = Color.FromRgb(110, 231, 213);
    private bool _lyricsFetching;

    private UIElement? CreateFeatureMascot(string key, double size)
    {
        if (key == "None") return new Border { Width = size, Height = size };
        string? color = key switch
        {
            "Puppy" => "#D6AD7B", "Fox" => "#F79753", "Panda" => "#F2F2EE", "Bear" => "#BA855F",
            "Frog" => "#98D584", "Penguin" => "#515F76", "Chick" => "#FFE082", "Piggy" => "#F5A9BB",
            "Koala" => "#B0BAC7", "Mouse" => "#C4BDCD", "Bee" => "#FFD568", "Octopus" => "#CC96E8",
            "Mushroom" => "#F77E76", "Cactus" => "#7CCCA3", "Alien" => "#B3E787", "Bat" => "#A294CD",
            "Moon" => "#D2C8F5", "Sun" => "#FFCA67", "Flame" => "#FF9D60", "Droplet" => "#80CCF5",
            "Heart" => "#F68DB3", "Pumpkin" => "#F6AC59", _ => null
        };
        if (color == null) return null;
        var canvas = new Canvas { Width = 36, Height = 36, RenderTransformOrigin = new Point(.5, .8) };
        var group = new TransformGroup(); group.Children.Add(new ScaleTransform()); group.Children.Add(new RotateTransform()); group.Children.Add(new TranslateTransform()); canvas.RenderTransform = group;
        var fill = (Brush)new BrushConverter().ConvertFromString(color)!;
        void Shape(string geometry, Brush brush) => canvas.Children.Add(new Path { Data = Geometry.Parse(geometry), Fill = brush });
        void Oval(double x, double y, double w, double h, Brush brush)
        { var shape = new Ellipse { Width = w, Height = h, Fill = brush }; Canvas.SetLeft(shape, x); Canvas.SetTop(shape, y); canvas.Children.Add(shape); }
        switch (key)
        {
            case "Puppy": Oval(3, 10, 9, 20, fill); Oval(24, 10, 9, 20, fill); Oval(8, 10, 20, 22, fill); Oval(12, 22, 12, 7, Brushes.Bisque); break;
            case "Fox": Shape("M4,5 L14,12 L22,12 L32,5 L29,25 L18,33 L7,25 Z", fill); Shape("M7,22 L18,28 L29,22 L24,29 L18,33 L12,29 Z", Brushes.FloralWhite); break;
            case "Panda": Oval(4, 4, 11, 12, Brushes.DimGray); Oval(21, 4, 11, 12, Brushes.DimGray); Oval(5, 9, 26, 24, fill); Oval(9, 15, 7, 9, Brushes.DimGray); Oval(20, 15, 7, 9, Brushes.DimGray); break;
            case "Bear": Oval(4, 5, 11, 12, fill); Oval(21, 5, 11, 12, fill); Oval(5, 10, 26, 23, fill); Oval(11, 22, 14, 8, Brushes.Bisque); break;
            case "Frog": Oval(5, 7, 12, 12, fill); Oval(19, 7, 12, 12, fill); Oval(4, 14, 28, 18, fill); break;
            case "Penguin": Oval(6, 3, 24, 31, fill); Oval(10, 13, 16, 18, Brushes.FloralWhite); Shape("M14,24 L22,24 L18,28 Z", Brushes.Gold); break;
            case "Chick": Oval(5, 6, 26, 27, fill); Shape("M12,8 L14,1 L19,8 L23,3 L24,10 Z", fill); Shape("M14,24 L22,24 L18,28 Z", Brushes.Coral); break;
            case "Piggy": Shape("M6,18 L5,5 L16,13 L22,13 L31,5 L30,20 Z", fill); Oval(4, 11, 28, 22, fill); Oval(12, 23, 12, 7, Brushes.PaleVioletRed); break;
            case "Koala": Oval(0, 9, 14, 16, fill); Oval(22, 9, 14, 16, fill); Oval(8, 9, 20, 24, fill); Oval(15, 21, 6, 9, Brushes.SlateGray); break;
            case "Mouse": Oval(2, 2, 15, 17, fill); Oval(19, 2, 15, 17, fill); Oval(7, 12, 22, 21, fill); Oval(15, 26, 6, 4, Brushes.Pink); break;
            case "Bee": Oval(0, 8, 17, 13, Brushes.LightCyan); Oval(19, 8, 17, 13, Brushes.LightCyan); Oval(7, 8, 22, 26, fill); Shape("M9,25 L27,25 L25,29 L11,29 Z", Brushes.DarkSlateGray); break;
            case "Octopus": Shape("M5,25 L4,32 Q9,36 12,30 Q15,36 18,30 Q22,36 25,30 Q30,35 32,30 L30,22 Z", fill); Oval(5, 5, 26, 25, fill); break;
            case "Mushroom": Shape("M12,16 L24,16 L26,33 L10,33 Z", Brushes.Bisque); Shape("M2,18 Q4,1 18,2 Q32,2 34,18 Q18,23 2,18 Z", fill); Oval(9, 9, 5, 4, Brushes.FloralWhite); Oval(22, 8, 6, 5, Brushes.FloralWhite); break;
            case "Cactus": Shape("M12,33 L12,22 L6,22 Q2,22 2,18 L2,12 L7,12 L7,17 L12,17 L12,9 Q12,2 18,2 Q24,2 24,9 L24,21 L29,21 L29,13 L34,13 L34,24 Q34,27 24,27 L24,33 Z", fill); break;
            case "Alien": Shape("M4,15 Q3,3 18,3 Q33,3 32,15 Q28,29 18,34 Q8,29 4,15 Z", fill); break;
            case "Bat": Shape("M12,16 L1,8 L3,27 L9,24 L13,30 L23,30 L27,24 L33,27 L35,8 L24,16 L24,7 L18,13 L12,7 Z", fill); break;
            case "Moon": Shape("M26,3 A16,16 0 1 0 32,26 A14,14 0 0 1 26,3 Z", fill); break;
            case "Sun": for (int i = 0; i < 12; i++) { double a = i * Math.PI / 6; Oval(16 + Math.Cos(a) * 14, 16 + Math.Sin(a) * 14, 4, 4, fill); } Oval(6, 6, 24, 24, fill); break;
            case "Flame": Shape("M18,1 Q32,14 30,24 Q28,34 18,34 Q5,34 5,23 Q5,16 11,10 Q10,20 16,18 Q21,14 18,1 Z", fill); Shape("M18,20 Q28,33 18,33 Q9,33 18,20 Z", Brushes.Gold); break;
            case "Droplet": Shape("M18,2 Q21,11 29,20 Q35,33 18,34 Q1,33 7,20 Q15,10 18,2 Z", fill); break;
            case "Heart": Shape("M18,33 Q-2,20 4,9 Q11,-1 18,10 Q25,-1 32,9 Q38,20 18,33 Z", fill); break;
            case "Pumpkin": Shape("M16,10 L17,2 L22,2 L20,11 Z", Brushes.MediumSeaGreen); Oval(3, 10, 30, 23, fill); Oval(12, 10, 12, 23, Brushes.DarkOrange); break;
        }
        // Eye layer stays separate so pupils follow the cursor and eyelids close at rest.
        var eyes = new Canvas { Width = 36, Height = 36, Tag = "eyes", RenderTransform = new TranslateTransform() };
        foreach (double x in new[] { 12d, 21d })
        {
            var eye = new Ellipse { Width = 3.5, Height = 5, Fill = Brushes.Black, RenderTransformOrigin = new Point(.5, .5), RenderTransform = new ScaleTransform() };
            Canvas.SetLeft(eye, x); Canvas.SetTop(eye, key == "Mushroom" ? 25 : 18); eyes.Children.Add(eye);
        }
        canvas.Children.Add(eyes);
        _animatedMascots.Add(new(canvas));
        return new Viewbox { Width = size, Height = size, Child = canvas };
    }

    private void AnimateFeatureMascots(double now, double dt)
    {
        if (!IsLoaded) return;
        var pointer = System.Windows.Forms.Cursor.Position;
        for (int i = _animatedMascots.Count - 1; i >= 0; i--)
        {
            if (!_animatedMascots[i].TryGetTarget(out var element)) { _animatedMascots.RemoveAt(i); continue; }
            if (!element.IsVisible || element is not Canvas canvas) continue;
            Point mouse = canvas.PointFromScreen(new Point(pointer.X, pointer.Y));
            bool asleep = !_isMediaPlaying && DateTime.UtcNow - _lastCursorMovement > TimeSpan.FromSeconds(30);
            var transforms = (TransformGroup)canvas.RenderTransform;
            ((RotateTransform)transforms.Children[1]).Angle = _isMediaPlaying ? Math.Sin(now * 7) * 7 : Math.Sin(now * 1.7) * 1.5;
            ((TranslateTransform)transforms.Children[2]).Y = _isMediaPlaying ? -Math.Abs(Math.Sin(now * 7)) * 2 : Math.Sin(now * 1.7);
            if (canvas.Children.OfType<Canvas>().LastOrDefault() is { } eyes)
            {
                ((TranslateTransform)eyes.RenderTransform).X = Math.Clamp((mouse.X - 18) / 25, -2, 2);
                ((TranslateTransform)eyes.RenderTransform).Y = Math.Clamp((mouse.Y - 18) / 25, -1, 1);
                bool blink = asleep || now % 5 > 4.85;
                foreach (Ellipse eye in eyes.Children) ((ScaleTransform)eye.RenderTransform).ScaleY = blink ? .15 : 1;
            }
        }
        if (pointer != _lastCursor) { _lastCursorMovement = DateTime.UtcNow; _lastCursor = pointer; }
        if (!_attractionInitialized)
        {
            var group = new TransformGroup(); group.Children.Add(IslandScaleTransform); group.Children.Add(_attraction); IslandBorder.RenderTransform = group; _attractionInitialized = true;
        }
        if (_isClickCandidate || _bodyDragging) return; // Keep the hit target still throughout a gesture.
        var relative = IslandBorder.PointFromScreen(new Point(pointer.X, pointer.Y));
        double dx = relative.X - IslandBorder.ActualWidth / 2, dy = relative.Y - IslandBorder.ActualHeight / 2;
        bool near = Math.Abs(dx) < IslandBorder.ActualWidth / 2 + 70 && Math.Abs(dy) < IslandBorder.ActualHeight / 2 + 60;
        double tx = near && !_manualResize && !_isClickCandidate ? Math.Clamp(dx * .025, -5, 5) : 0;
        double ty = near && !_manualResize && !_isClickCandidate ? Math.Clamp(dy * .025, -2, 3) : 0;
        _attractionTargetX = tx; _attractionTargetY = ty;
        StepSpring(ref _attractionX, tx, ref _attractionVx, 110, 20, dt); StepSpring(ref _attractionY, ty, ref _attractionVy, 110, 20, dt);
        if (Math.Abs(_attractionX - tx) < .01 && Math.Abs(_attractionVx) < .05) { _attractionX = tx; _attractionVx = 0; }
        if (Math.Abs(_attractionY - ty) < .01 && Math.Abs(_attractionVy) < .05) { _attractionY = ty; _attractionVy = 0; }
        if (_attraction.X != _attractionX) _attraction.X = _attractionX;
        if (_attraction.Y != _attractionY) _attraction.Y = _attractionY;
    }
    private System.Drawing.Point _lastCursor;
    private DateTime _lastCursorMovement = DateTime.UtcNow;

    private void UpdateLyricsFrame()
    {
        string track = _currentMediaTitle + "\n" + _currentMediaArtist;
        if (_isMediaPlaying && _lyricsTrack != track && !_lyricsFetching)
        { _lyrics = []; _lyricIndex = -1; _lyricsTrack = track; if (_settings.OnlineLyrics) _ = FetchLyricsAsync(track); }
        bool show = _settings.Karaoke && _isMediaPlaying && _lyrics.Count > 0 && _currentState == IslandState.Compact && !_isGameModeActive && CompactProgressPanel.Visibility != Visibility.Visible;
        KaraokePanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (!show)
        {
            if (_currentState == IslandState.Compact && !_isGameModeActive)
            { _targetW = _settings.CompactWidth; _targetH = _settings.CompactHeight; _targetR = Math.Max(4, _settings.CompactHeight * .5 * _settings.CornerRounding); }
            return;
        }
        try
        {
            var session = _mediaManager?.GetCurrentSession(); if (session == null) return;
            var timeline = session.GetTimelineProperties();
            double position = timeline.Position.TotalSeconds + (DateTimeOffset.Now - timeline.LastUpdatedTime).TotalSeconds;
            int index = LyricsReader.At(_lyrics, position);
            if (index < 0) { KaraokePanel.Visibility = Visibility.Collapsed; return; }
            if (index != _lyricIndex)
            {
                _lyricIndex = index;
                KaraokeOldLine.Text = KaraokeLine.Text;
                KaraokeLine.Text = _lyrics[index].Text;
                var formatted = new FormattedText(KaraokeLine.Text, CultureInfo.CurrentCulture, System.Windows.FlowDirection.LeftToRight, new Typeface(IslandBorder.GetValue(System.Windows.Documents.TextElement.FontFamilyProperty) as FontFamily ?? new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal), 15, Brushes.White, VisualTreeHelper.GetDpi(this).PixelsPerDip);
                _targetW = Math.Clamp(formatted.WidthIncludingTrailingWhitespace + 40, _settings.CompactWidth, Math.Min(1100, SystemParameters.WorkArea.Width / .9 - 40));
                _targetH = Math.Max(42, _settings.CompactHeight);
                AnimateLyricLine(KaraokeLine, 12, 0, 0, 1); AnimateLyricLine(KaraokeOldLine, 0, -12, 1, 0);
            }
            double end = index + 1 < _lyrics.Count ? _lyrics[index + 1].Seconds : _lyrics[index].Seconds + 5;
            double progress = Math.Clamp((position - _lyrics[index].Seconds) / Math.Max(.1, end - _lyrics[index].Seconds), 0, 1);
            var brush = new LinearGradientBrush { StartPoint = new Point(0, .5), EndPoint = new Point(1, .5) };
            var textColor = ((SolidColorBrush)FindResource("ThemeTextBrush")).Color;
            var muted = ((SolidColorBrush)FindResource("ThemeMutedBrush")).Color;
            brush.GradientStops.Add(new(textColor, 0)); brush.GradientStops.Add(new(textColor, Math.Max(0, progress - .04)));
            brush.GradientStops.Add(new(_albumAccent, progress)); brush.GradientStops.Add(new(muted, Math.Min(1, progress + .025))); brush.GradientStops.Add(new(muted, 1));
            KaraokeLine.Foreground = brush;
        }
        catch (Exception ex) { AppLog.Error("Karaoke timeline", ex); KaraokePanel.Visibility = Visibility.Collapsed; }
    }
    private static void AnimateLyricLine(TextBlock line, double from, double to, double opacityFrom, double opacityTo)
    {
        line.RenderTransform = new TranslateTransform();
        ((TranslateTransform)line.RenderTransform).BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(280)) { EasingFunction = new BackEase { Amplitude = .25, EasingMode = EasingMode.EaseOut } });
        line.BeginAnimation(OpacityProperty, new DoubleAnimation(opacityFrom, opacityTo, TimeSpan.FromMilliseconds(240)));
    }
    private async Task FetchLyricsAsync(string track)
    {
        _lyricsFetching = true;
        try
        {
            var session = _mediaManager?.GetCurrentSession(); if (session == null) return;
            var props = await session.TryGetMediaPropertiesAsync();
            var timeline = session.GetTimelineProperties();
            string url = "https://lrclib.net/api/get?track_name=" + Uri.EscapeDataString(props.Title) + "&artist_name=" + Uri.EscapeDataString(props.Artist)
                + "&album_name=" + Uri.EscapeDataString(props.AlbumTitle ?? "") + "&duration=" + Math.Round((timeline.EndTime - timeline.StartTime).TotalSeconds).ToString(CultureInfo.InvariantCulture);
            using var response = await _http.GetAsync(url);
            if (!response.IsSuccessStatusCode) { _lyricsStatus = "Синхронизированный текст не найден. Можно импортировать LRC."; return; }
            if (response.Content.Headers.ContentLength > 1_000_000) return;
            string body = await response.Content.ReadAsStringAsync(); if (body.Length > 1_000_000) return;
            using var data = JsonDocument.Parse(body);
            if (_lyricsTrack != track) return;
            if (data.RootElement.TryGetProperty("syncedLyrics", out var lyrics) && lyrics.ValueKind == JsonValueKind.String)
            { _lyrics = LyricsReader.Parse(lyrics.GetString()!); _lyricIndex = -1; _lyricsStatus = $"LRCLIB · строк: {_lyrics.Count}"; }
            if (props.Thumbnail != null)
            {
                using var stream = await props.Thumbnail.OpenReadAsync();
                using var managed = stream.AsStreamForRead();
                var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.DecodePixelWidth = 24; image.StreamSource = managed; image.EndInit();
                var converted = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
                int stride = converted.PixelWidth * 4; var pixels = new byte[stride * converted.PixelHeight]; converted.CopyPixels(pixels, stride, 0);
                long r = 0, g = 0, b = 0; for (int i = 0; i < pixels.Length; i += 4) { b += pixels[i]; g += pixels[i + 1]; r += pixels[i + 2]; }
                int count = pixels.Length / 4; if (count > 0) _albumAccent = Color.FromRgb((byte)Math.Clamp(r / count + 45, 0, 255), (byte)Math.Clamp(g / count + 45, 0, 255), (byte)Math.Clamp(b / count + 45, 0, 255));
            }
        }
        catch (Exception ex) { AppLog.Error("Fetch lyrics", ex); _lyricsStatus = "Не удалось получить текст. Проверьте интернет или импортируйте LRC."; }
        finally { _lyricsFetching = false; }
    }
}
