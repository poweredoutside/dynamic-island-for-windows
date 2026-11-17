using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Text.RegularExpressions;
using Button = System.Windows.Controls.Button;
using Clipboard = System.Windows.Clipboard;
using Control = System.Windows.Controls.Control;
using Image = System.Windows.Controls.Image;
using Color = System.Windows.Media.Color;

namespace DynamicIslandWin;

public partial class MainWindow
{
    private const long ClipboardImageLimit = 16 * 1024 * 1024;
    private const long ClipboardHistoryImageLimit = 64 * 1024 * 1024;
    private int _clipboardCaptureVersion;
    private string ClipboardImageDirectory => System.IO.Path.Combine(System.IO.Path.GetDirectoryName(_clipboardHistoryPath)!, "clipboard-images");
    private string ClipboardEmptyHint => Tr("Copy text or a screenshot to see it here", "Скопируйте текст или скриншот, и он появится здесь", "Скопіюйте текст або знімок екрана", "Text oder Screenshot kopieren", "Copia texto o una captura para verla aquí");
    private string ScreenshotLabel => Tr("Screenshot", "Скриншот", "Знімок екрана", "Screenshot", "Captura de pantalla");
    private string? ClipboardImagePath(ClipboardHistoryItem item) => item.ImageFile is { } file
        && file.EndsWith(".png", StringComparison.Ordinal) && Guid.TryParseExact(file[..^4], "N", out _)
        ? System.IO.Path.Combine(ClipboardImageDirectory, file) : null;

    private void LoadClipboardHistory()
    {
        try
        {
            if (File.Exists(_clipboardHistoryPath))
                _clipboardHistory = JsonSerializer.Deserialize<List<ClipboardHistoryItem>>(File.ReadAllText(_clipboardHistoryPath, Encoding.UTF8)) ?? [];
        }
        catch { _clipboardHistory = []; }
        _clipboardHistory.RemoveAll(item => item == null || (item.ImageFile == null ? string.IsNullOrEmpty(item.Text) : !File.Exists(ClipboardImagePath(item))));
        PruneExpiredClipboardItems();
        SaveClipboardHistory(); // Also removes orphaned image files after an interrupted save.
        RenderClipboardHistoryUi();
    }

    private void SaveClipboardHistory()
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_clipboardHistoryPath)!);
            string temporary = _clipboardHistoryPath + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(_clipboardHistory), Encoding.UTF8);
            File.Move(temporary, _clipboardHistoryPath, true);
            if (!Directory.Exists(ClipboardImageDirectory)) return;
            var keep = _clipboardHistory.Select(i => i.ImageFile).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (string path in Directory.EnumerateFiles(ClipboardImageDirectory, "*.png"))
                if (Guid.TryParseExact(System.IO.Path.GetFileNameWithoutExtension(path), "N", out _) && !keep.Contains(System.IO.Path.GetFileName(path)))
                    File.Delete(path);
        }
        catch { }
    }

    private void PruneExpiredClipboardItems(bool saveIfChanged = true)
    {
        int before = _clipboardHistory.Count;
        _clipboardHistory.RemoveAll(i => i.CopiedAt < DateTime.Now.AddHours(-24));
        if (_clipboardHistory.Count > 100) _clipboardHistory.RemoveRange(100, _clipboardHistory.Count - 100);
        long bytes = 0;
        _clipboardHistory.RemoveAll(item =>
        {
            if (item.ImageFile == null) return false;
            try
            {
                string? path = ClipboardImagePath(item);
                if (!File.Exists(path)) return true;
                bytes += new FileInfo(path!).Length;
                return bytes > ClipboardHistoryImageLimit;
            }
            catch { return true; }
        });
        if (before == _clipboardHistory.Count) return;
        if (saveIfChanged) SaveClipboardHistory();
        RenderClipboardHistoryUi();
    }

    private async void OnClipboardChanged()
    {
        if (TglClipboardWatch.IsChecked != true) return;
        int version = ++_clipboardCaptureVersion;
        for (int attempt = 0; attempt < 4; attempt++)
        {
            try
            {
                if (version != _clipboardCaptureVersion || TglClipboardWatch.IsChecked != true || _windowClosed) return;
                ClipboardHistoryItem item;
                string fingerprint;
                if (Clipboard.ContainsImage())
                {
                    var bitmap = Clipboard.GetImage();
                    if (bitmap == null || (long)bitmap.PixelWidth * bitmap.PixelHeight > 32_000_000) return;
                    bitmap.Freeze();
                    byte[] png = await Task.Run(() => EncodeClipboardImage(bitmap));
                    if (png.LongLength > ClipboardImageLimit || version != _clipboardCaptureVersion || TglClipboardWatch.IsChecked != true || _windowClosed) return;
                    fingerprint = "image:" + Convert.ToHexString(SHA256.HashData(png));
                    if (_lastClipboardHash == fingerprint) return;
                    // Identical screenshots move to the top without another file copy.
                    var existing = _clipboardHistory.FirstOrDefault(i => i.Text == fingerprint && i.ImageFile != null);
                    string file = existing?.ImageFile ?? Guid.NewGuid().ToString("N") + ".png";
                    Directory.CreateDirectory(ClipboardImageDirectory);
                    if (existing == null) await File.WriteAllBytesAsync(System.IO.Path.Combine(ClipboardImageDirectory, file), png);
                    item = new(fingerprint, DateTime.Now, file);
                }
                else if (Clipboard.ContainsText())
                {
                    string text = Clipboard.GetText();
                    if (string.IsNullOrWhiteSpace(text) || text.Length > 1_000_000) return;
                    if (LooksLikePasswordOrSecret(text))
                    {
                        ShowSideBubble(Tr("Password hidden", "Пароль скрыт", "Пароль приховано", "Passwort ausgeblendet", "Contraseña oculta"), ToastCategory.Clipboard);
                        return;
                    }
                    fingerprint = "text:" + text;
                    if (_lastClipboardHash == fingerprint) return;
                    item = new(text, DateTime.Now);
                }
                else return;
                if (version != _clipboardCaptureVersion || TglClipboardWatch.IsChecked != true || _windowClosed)
                { SaveClipboardHistory(); return; }
                _lastClipboardHash = fingerprint;
                _clipboardHistory.RemoveAll(i => i.ImageFile == item.ImageFile && i.Text == item.Text);
                _clipboardHistory.Insert(0, item);
                PruneExpiredClipboardItems(false);
                SaveClipboardHistory(); RenderClipboardHistoryUi();
                string preview = ClipboardPreview(item);
                if (preview.Length > 24) preview = preview[..24] + "…";
                ShowSideBubble(Tr("Copied", "Скопировано", "Скопійовано", "Kopiert", "Copiado") + ": " + preview, ToastCategory.Clipboard);
                return;
            }
            catch (ExternalException) { await Task.Delay(60); } // Clipboard may be held briefly by Snipping Tool.
            catch { return; }
        }
    }

    private static byte[] EncodeClipboardImage(BitmapSource bitmap)
    {
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream(); encoder.Save(stream); return stream.ToArray();
    }
    private BitmapSource? LoadClipboardImage(ClipboardHistoryItem item, int decodeWidth = 0)
    {
        try
        {
            string? path = ClipboardImagePath(item);
            if (path == null || !File.Exists(path)) return null;
            var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = decodeWidth; image.UriSource = new Uri(path); image.EndInit(); image.Freeze(); return image;
        }
        catch { return null; }
    }
    private string ClipboardPreview(ClipboardHistoryItem item) => item.ImageFile != null ? ScreenshotLabel : Regex.Replace(item.Text, @"\s+", " ");

    private void CopyClipboardHistoryItem(ClipboardHistoryItem item)
    {
        if (item.ImageFile != null)
        {
            if (LoadClipboardImage(item) is not { } bitmap) return;
            byte[] png = File.ReadAllBytes(ClipboardImagePath(item)!);
            var data = new System.Windows.DataObject();
            data.SetImage(bitmap); data.SetData("PNG", new MemoryStream(png));
            Clipboard.SetDataObject(data, true);
            _lastClipboardHash = item.Text;
        }
        else { Clipboard.SetText(item.Text); _lastClipboardHash = "text:" + item.Text; }
        ShowSideBubble(Tr("Copied from history", "Скопировано из истории", "Скопійовано з історії", "Aus Verlauf kopiert", "Copiado del historial"), ToastCategory.Clipboard);
    }

    private void RenderClipboardHistoryUi()
    {
        if (LblClipboardHeader == null || HomeClipboardPreview == null) return;
        string title = Tr("Clipboard", "Буфер обмена", "Буфер обміну", "Zwischenablage", "Portapapeles");
        LblClipboardHeader.Text = _clipboardHistory.Count > 0 ? $"{title} ({_clipboardHistory.Count})" : title;
        HomeClipboardPreview.Text = _clipboardHistory.Count > 0 ? ClipboardPreview(_clipboardHistory[0]) : ClipboardEmptyHint;
        ClipboardHistoryListStack.Children.Clear();
        ClipboardEmptyStateText.Visibility = _clipboardHistory.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var item in _clipboardHistory)
        {
            var row = new Border { CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), Padding = new Thickness(12, 8, 10, 8), Margin = new Thickness(0, 0, 0, 6), Cursor = System.Windows.Input.Cursors.Hand };
            row.SetResourceReference(Border.BackgroundProperty, "ThemeSurfaceBrush"); row.SetResourceReference(Border.BorderBrushProperty, "ThemeBorderBrush");
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var content = new StackPanel();
            if (item.ImageFile != null && LoadClipboardImage(item, 480) is { } preview)
                content.Children.Add(new Image { Source = preview, MaxHeight = 130, Stretch = Stretch.Uniform, HorizontalAlignment = System.Windows.HorizontalAlignment.Left, Margin = new Thickness(0, 0, 10, 6) });
            var text = new TextBlock { Text = ClipboardPreview(item), FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = item.ImageFile == null ? item.Text : ScreenshotLabel };
            text.SetResourceReference(TextBlock.ForegroundProperty, "ThemeTextBrush"); content.Children.Add(text); grid.Children.Add(content);
            var time = new TextBlock { Text = item.CopiedAt.ToString("HH:mm"), FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 8, 0) };
            time.SetResourceReference(TextBlock.ForegroundProperty, "ThemeMutedBrush"); Grid.SetColumn(time, 1); grid.Children.Add(time);
            var delete = new Button { Content = "✕", Style = (Style)FindResource("GlassPillBtn"), Padding = new Thickness(7, 2, 7, 2), VerticalAlignment = VerticalAlignment.Center,
                ToolTip = Tr("Delete", "Удалить", "Видалити", "Löschen", "Eliminar") };
            delete.Click += (_, e) => { e.Handled = true; _clipboardHistory.Remove(item); _lastClipboardHash = ""; SaveClipboardHistory(); RenderClipboardHistoryUi(); };
            Grid.SetColumn(delete, 2); grid.Children.Add(delete); row.Child = grid;
            row.MouseLeftButtonUp += (_, e) => { e.Handled = true; try { CopyClipboardHistoryItem(item); } catch { } };
            ClipboardHistoryListStack.Children.Add(row);
        }
    }
    private void OpenClipboardDashboard_Click(object sender, MouseButtonEventArgs e)
    { e.Handled = true; MorphToState(IslandState.Clipboard); }
    private void BtnClearClipboardHistory_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true; ++_clipboardCaptureVersion; _clipboardHistory.Clear(); _lastClipboardHash = "";
        SaveClipboardHistory(); RenderClipboardHistoryUi();
    }
}
