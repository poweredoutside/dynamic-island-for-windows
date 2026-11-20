using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DynamicIslandWin;

public partial class MainWindow
{
    private const string SystemLanguage = "system";
    private static readonly string WindowsLanguage = LanguageForCulture(ReadWindowsUiLanguage());
    private string InterfaceLanguage => _settings.Language is "English" or "Русский" or "Українська" or "Deutsch" or "Español"
        ? _settings.Language : WindowsLanguage;

    internal static string LanguageForCulture(string culture) => culture.Split('-', '_')[0].ToLowerInvariant() switch
    {
        "ru" => "Русский", "uk" => "Українська", "de" => "Deutsch", "es" => "Español", _ => "English"
    };

    private static string ReadWindowsUiLanguage()
    {
        const uint languageName = 8;
        uint length = 0;
        if (GetUserPreferredUILanguages(languageName, out _, null, ref length) && length is > 1 and <= 8192)
        {
            var names = new char[length];
            if (GetUserPreferredUILanguages(languageName, out _, names, ref length) && Array.IndexOf(names, '\0') is > 0 and var end)
                return new string(names, 0, end);
        }
        return CultureInfo.CurrentUICulture.Name;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern bool GetUserPreferredUILanguages(uint flags, out uint count, [Out] char[]? buffer, ref uint length);

    private string LanguageChoiceLabel(string language) => language == SystemLanguage
        ? Tr("Same as Windows", "Как в Windows", "Як у Windows", "Wie in Windows", "Igual que Windows") : language;

    private readonly Dictionary<(DependencyObject Element, DependencyProperty Property), string> _localizedLabels = [];
    private string L(string source)
    {
        if (!AdditionalTranslations.TryGetValue(source, out var words))
        {
            foreach (string prefix in new[] { "Версия приложения · ", "Доступна версия ", "Загрузка · " })
                if (source.StartsWith(prefix, StringComparison.Ordinal))
                {
                    string key = prefix.TrimEnd(' ', ':', '·');
                    return L(key) + prefix[key.Length..] + source[prefix.Length..];
                }
            return source;
        }
        int index = InterfaceLanguage switch { "Русский" => 0, "Українська" => 2, "Deutsch" => 3, "Español" => 4, _ => 1 };
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
        string culture = InterfaceLanguage switch { "Русский" => "ru-RU", "Українська" => "uk-UA", "Deutsch" => "de-DE", "Español" => "es-ES", _ => "en-US" };
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
        CultureInfo.CurrentUICulture = CultureInfo.CurrentCulture;
        SelectedLangLabel.Text = LanguageChoiceLabel(_settings.Language);
        BuildLanguageDropdown();
        foreach (var label in _localizedLabels) label.Key.Element.SetValue(label.Key.Property, L(label.Value));
        System.Windows.Automation.AutomationProperties.SetName(HiddenIslandHandle, L("Показать островок"));
        LblExternalProgress.Text = L("Прогресс других программ справа");
        LblExternalProgressSub.Text = L("Отдельная капсула, не закрывающая часы и погоду");
        LblClipboardWatch.Text = L("Текст и скриншоты в буфере обмена");
        ClipboardEmptyStateText.Text = ClipboardEmptyHint;
        UpdateLensGlassStatus();
        System.Windows.Automation.AutomationProperties.SetName(VolumeHudSlider, L("Громкость"));
        if (LevelHudBorder.Visibility == Visibility.Visible && VolumeHudSlider.Visibility == Visibility.Visible)
            ShowVolumeSnapshot(_displayedVolume);
        if (_currentState == IslandState.Section) OpenFeaturePage(_featurePage);
        else
        {
            BuildDashboardNavigation();
            if (_currentState == IslandState.Settings && _settingsFeaturePage is { } page) BuildSettingsFeaturePage(page);
        }
    }
}
