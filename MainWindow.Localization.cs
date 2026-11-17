using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DynamicIslandWin;

public partial class MainWindow
{
    private readonly Dictionary<(DependencyObject Element, DependencyProperty Property), string> _localizedLabels = [];
    private string L(string source)
    {
        if (!AdditionalTranslations.TryGetValue(source, out var words))
        {
            foreach (string prefix in new[] { "Доступна версия ", "Загрузка · ", "Загружено строк: ", "LRCLIB · строк: " })
                if (source.StartsWith(prefix, StringComparison.Ordinal))
                {
                    string key = prefix.TrimEnd(' ', ':', '·');
                    return L(key) + prefix[key.Length..] + source[prefix.Length..];
                }
            return source;
        }
        int index = _settings.Language switch { "Русский" => 0, "Українська" => 2, "Deutsch" => 3, "Español" => 4, _ => 1 };
        return words[index];
    }

    private void CaptureAdditionalLabels(DependencyObject node)
    {
        void Capture(DependencyProperty property)
        {
            if (node.GetValue(property) is string text && AdditionalTranslations.ContainsKey(text))
                _localizedLabels[(node, property)] = text;
        }
        if (node is TextBlock) Capture(TextBlock.TextProperty);
        if (node is ContentControl) Capture(ContentControl.ContentProperty);
        if (node is HeaderedItemsControl) Capture(HeaderedItemsControl.HeaderProperty);
        if (node is FrameworkElement) Capture(FrameworkElement.ToolTipProperty);
        foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>()) CaptureAdditionalLabels(child);
    }

    private void ApplyAdditionalLocalization()
    {
        string culture = _settings.Language switch { "Русский" => "ru-RU", "Українська" => "uk-UA", "Deutsch" => "de-DE", "Español" => "es-ES", _ => "en-US" };
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
        CultureInfo.CurrentUICulture = CultureInfo.CurrentCulture;
        foreach (var label in _localizedLabels) label.Key.Element.SetValue(label.Key.Property, L(label.Value));
        LblExternalProgress.Text = L("Прогресс других программ справа");
        LblExternalProgressSub.Text = L("Отдельная капсула, не закрывающая часы и погоду");
        LblClipboardWatch.Text = L("Текст и скриншоты в буфере обмена");
        ClipboardEmptyStateText.Text = ClipboardEmptyHint;
        System.Windows.Automation.AutomationProperties.SetName(VolumeHudSlider, L("Громкость"));
        if (LevelHudBorder.Visibility == Visibility.Visible && VolumeHudSlider.Visibility == Visibility.Visible)
            ShowVolumeSnapshot(_displayedVolume);
        RenderClipboardHistoryUi();
        BuildFeatureNavigation();
        if (_currentState == IslandState.Features) OpenFeaturePage(_featurePage);
    }
}
