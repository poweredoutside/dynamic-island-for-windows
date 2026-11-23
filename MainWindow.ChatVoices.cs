using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using DynamicIslandWin.Services;
using ComboBox = System.Windows.Controls.ComboBox;
using Panel = System.Windows.Controls.Panel;

namespace DynamicIslandWin;

public partial class MainWindow
{
    public sealed record GeminiVoicePreset(Guid Id, string Label, string Voice, string Style);

    private (string Voice, string Style) SelectedGeminiVoice()
    {
        if (_settings.GeminiVoicePresetId is { } id && _settings.GeminiVoicePresets?.FirstOrDefault(preset => preset?.Id == id) is { Voice: { } voice } saved
            && GeminiVoiceService.IsVoiceName(voice)) return (voice, saved.Style ?? "");
        return (GeminiVoiceService.BuiltInVoices.FirstOrDefault(voice => voice.Equals(_settings.GeminiVoice, StringComparison.OrdinalIgnoreCase))
            ?? GeminiVoiceService.DefaultVoice, "");
    }

    private void BuildChatVoiceControls(Panel card)
    {
        var custom = _settings.GeminiVoicePresets ??= [];
        custom.RemoveAll(preset => preset == null || string.IsNullOrWhiteSpace(preset.Label) || !GeminiVoiceService.IsVoiceName(preset.Voice ?? ""));
        card.Children.Add(FeatureText("Голос", 11, "ThemeMutedBrush"));
        var choices = new ComboBox
        {
            Style = (Style)FindResource("FeatureCombo"), MinHeight = 36,
            Margin = new Thickness(0, 0, 0, 8), FontSize = 12
        };
        AutomationProperties.SetName(choices, L("Голос"));
        card.Children.Add(choices);
        bool filling = false;
        var remove = FeatureButton("Удалить свой голос");
        remove.Click += (_, e) =>
        {
            e.Handled = true;
            if (_settings.GeminiVoicePresetId is not { } id) return;
            custom.RemoveAll(preset => preset.Id == id);
            Select(null);
            FillChoices();
        };
        card.Children.Add(remove);

        var add = new Expander { Header = L("Добавить голос"), Margin = new Thickness(0, 0, 0, 12) };
        add.SetResourceReference(Expander.ForegroundProperty, "ThemeTextBrush");
        var form = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        add.Content = form;
        card.Children.Add(add);
        var label = FeatureInput(form, "Название голоса в островке", "");
        var voiceName = FeatureInput(form, "Имя голоса в Gemini", "");
        var style = FeatureInput(form, "Манера речи (необязательно)", "");
        label.MaxLength = 60;
        voiceName.MaxLength = 80;
        style.MaxLength = 160;
        AutomationProperties.SetName(label, L("Название голоса в островке"));
        AutomationProperties.SetName(voiceName, L("Имя голоса в Gemini"));
        AutomationProperties.SetName(style, L("Манера речи (необязательно)"));
        add.Expanded += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(voiceName.Text)) voiceName.Text = SelectedGeminiVoice().Voice;
        };
        var status = FeatureText("", 11, "ThemeDangerBrush");
        form.Children.Add(FeatureButton("Сохранить голос", () =>
        {
            string name = voiceName.Text.Trim();
            string display = label.Text.Trim();
            if (!GeminiVoiceService.IsVoiceName(name) || display.Length == 0)
            {
                status.Text = L("Укажите название и имя голоса из Gemini Live.");
                return;
            }
            if (custom.Any(item => item.Label.Equals(display, StringComparison.OrdinalIgnoreCase)))
            {
                status.Text = L("Голос с таким названием уже добавлен.");
                return;
            }
            name = GeminiVoiceService.BuiltInVoices.FirstOrDefault(voice => voice.Equals(name, StringComparison.OrdinalIgnoreCase)) ?? name;
            var saved = new GeminiVoicePreset(Guid.NewGuid(), display, name, style.Text.Trim());
            custom.Add(saved);
            Select(saved.Id);
            FillChoices();
            label.Clear();
            voiceName.Clear();
            style.Clear();
            status.Text = "";
            add.IsExpanded = false;
        }));
        form.Children.Add(status);
        if (_voice != null) card.Children.Add(FeatureText("Выбранный голос применяется при следующем сеансе.", 11, "ThemeMutedBrush"));

        choices.SelectionChanged += (_, _) =>
        {
            if (!filling && choices.SelectedItem is ComboBoxItem item) Select(item.Tag);
        };
        FillChoices();

        void Select(object? key)
        {
            _settings.GeminiVoicePresetId = key is Guid id ? id : null;
            if (key is string builtIn) _settings.GeminiVoice = builtIn;
            remove.Visibility = _settings.GeminiVoicePresetId.HasValue ? Visibility.Visible : Visibility.Collapsed;
            SaveSettings();
        }

        void FillChoices()
        {
            filling = true;
            choices.Items.Clear();
            foreach (string name in GeminiVoiceService.BuiltInVoices) choices.Items.Add(new ComboBoxItem { Content = name, Tag = name });
            foreach (var preset in custom) choices.Items.Add(new ComboBoxItem { Content = $"{preset.Label} · {preset.Voice}", Tag = preset.Id });
            object key = _settings.GeminiVoicePresetId is { } id && custom.Any(preset => preset.Id == id) ? id : SelectedGeminiVoice().Voice;
            choices.SelectedItem = choices.Items.OfType<ComboBoxItem>().FirstOrDefault(item => Equals(item.Tag, key));
            remove.Visibility = key is Guid ? Visibility.Visible : Visibility.Collapsed;
            filling = false;
        }
    }
}
