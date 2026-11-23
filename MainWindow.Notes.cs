using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using DynamicIslandWin.Services;
using Clipboard = System.Windows.Clipboard;
using ComboBox = System.Windows.Controls.ComboBox;

namespace DynamicIslandWin;

public partial class MainWindow
{
    private const int NoteLimit = 100;
    private static readonly StringComparer NoteTopicComparer = StringComparer.OrdinalIgnoreCase;
    private List<NoteItem> _notes = [];
    private string NotesPath => Path.Combine(Path.GetDirectoryName(_settingsPath)!, "notes.json");
    private sealed record NoteItem(Guid Id, string Text, DateTimeOffset Updated, string? Topic = null);

    private void LoadNotes()
    {
        try
        {
            if (File.Exists(NotesPath))
                _notes = JsonSerializer.Deserialize<List<NoteItem>>(File.ReadAllText(NotesPath)) ?? [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            AppLog.Error("Read notes", ex);
        }
    }

    private void SaveNotes(List<NoteItem> notes)
    {
        LocalFile.Write(NotesPath, JsonSerializer.Serialize(notes));
        _notes = notes;
        RefreshWidget("notes");
    }

    private void BuildNotesPage()
    {
        var form = FeatureCard("");
        var heading = FeatureText("Новая заметка", 16);
        heading.FontWeight = FontWeights.SemiBold;
        form.Children.Add(heading);
        var editor = FeatureInput(form, "Заметка", "");
        editor.Style = (Style)FindResource("RoundedFeatureTextBox");
        editor.AcceptsReturn = true;
        editor.TextWrapping = TextWrapping.Wrap;
        editor.MaxLength = 4000;
        editor.MinHeight = 56;
        editor.MaxHeight = 120;
        editor.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        AutomationProperties.SetName(editor, L("Заметка"));
        var topicInput = FeatureInput(form, "Тематика (необязательно)", "");
        topicInput.Style = (Style)FindResource("RoundedFeatureTextBox");
        topicInput.MaxLength = 60;
        AutomationProperties.SetName(topicInput, L("Тематика (необязательно)"));
        var actions = new WrapPanel();
        form.Children.Add(actions);

        var filter = new ComboBox
        {
            Style = (Style)FindResource("FeatureCombo"), MinHeight = 36,
            Margin = new Thickness(0, 0, 0, 12)
        };
        AutomationProperties.SetName(filter, L("Тематика заметок"));
        DashboardSectionContent.Children.Add(filter);
        var saved = new StackPanel();
        DashboardSectionContent.Children.Add(saved);
        Guid? editing = null;
        var status = FeatureText("", 11, "ThemeDangerBrush");
        var save = FeatureButton("Сохранить заметку");
        save.ToolTip = "Ctrl+Enter";
        var cancel = FeatureButton("Отменить");
        cancel.Visibility = Visibility.Collapsed;
        save.Click += (_, e) => { e.Handled = true; Save(); };
        cancel.Click += (_, e) => { e.Handled = true; ResetEditor(); };
        actions.Children.Add(save);
        actions.Children.Add(cancel);
        actions.Children.Add(status);
        form.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter || Keyboard.Modifiers != ModifierKeys.Control) return;
            e.Handled = true;
            Save();
        };
        filter.SelectionChanged += (_, _) => { if (filter.SelectedIndex >= 0) RenderNotes(); };
        RefreshTopics();

        void Save()
        {
            string text = editor.Text.Trim();
            if (text.Length == 0) return;
            string topic = topicInput.Text.Trim();
            topic = _notes.Select(NoteTopic).FirstOrDefault(existing => NoteTopicComparer.Equals(topic, existing)) ?? topic;
            if (!editing.HasValue && _notes.Count >= NoteLimit)
            {
                status.Text = L("Достигнут предел: 100 заметок.");
                return;
            }
            var note = new NoteItem(editing ?? Guid.NewGuid(), text, DateTimeOffset.Now, topic.Length == 0 ? null : topic);
            List<NoteItem> next = [note, .. _notes.Where(item => item.Id != note.Id)];
            try { SaveNotes(next); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppLog.Error("Save note", ex);
                status.Text = L("Не удалось сохранить заметку.");
                return;
            }
            ResetEditor();
            RefreshTopics(topic);
        }

        void ResetEditor()
        {
            editing = null;
            editor.Clear();
            topicInput.Clear();
            status.Text = "";
            heading.Text = L("Новая заметка");
            save.Content = L("Сохранить заметку");
            cancel.Visibility = Visibility.Collapsed;
        }

        void RefreshTopics(string? changedTopic = null)
        {
            string? selected = (filter.SelectedItem as ComboBoxItem)?.Tag as string;
            if (selected != null) selected = changedTopic ?? selected;
            filter.Items.Clear();
            filter.Items.Add(new ComboBoxItem { Content = L("Все тематики") });
            foreach (var topic in _notes.Select(NoteTopic).Distinct(NoteTopicComparer)
                         .OrderBy(topic => topic.Length == 0).ThenBy(topic => topic, StringComparer.CurrentCultureIgnoreCase))
                filter.Items.Add(new ComboBoxItem { Tag = topic, Content = TopicName(topic) });
            filter.Visibility = _notes.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            filter.SelectedItem = filter.Items.OfType<ComboBoxItem>()
                .FirstOrDefault(item => NoteTopicComparer.Equals(item.Tag as string, selected))
                ?? filter.Items[0];
        }

        void RenderNotes()
        {
            saved.Children.Clear();
            if (_notes.Count == 0)
            {
                saved.Children.Add(FeatureText("Пока пусто — напишите заметку выше", 12, "ThemeMutedBrush"));
                return;
            }
            string? selected = (filter.SelectedItem as ComboBoxItem)?.Tag as string;
            var groups = _notes.Where(note => selected == null || NoteTopicComparer.Equals(NoteTopic(note), selected))
                .GroupBy(NoteTopic, NoteTopicComparer)
                .OrderBy(group => group.Key.Length == 0).ThenBy(group => group.Key, StringComparer.CurrentCultureIgnoreCase);
            foreach (var group in groups)
            {
                if (selected == null)
                {
                    var label = FeatureText("", 14);
                    label.Text = TopicName(group.Key);
                    label.FontWeight = FontWeights.SemiBold;
                    saved.Children.Add(label);
                }
                foreach (var note in group.OrderByDescending(note => note.Updated)) RenderNote(note);
            }
        }

        string TopicName(string topic) => topic.Length == 0 ? L("Без тематики") : topic;

        void RenderNote(NoteItem note)
        {
            var row = FeatureCard("", parent: saved);
            var date = FeatureText("", 11, "ThemeMutedBrush");
            date.Text = note.Updated.LocalDateTime.ToString("g");
            row.Children.Add(date);
            var body = FeatureText("");
            body.Text = note.Text;
            row.Children.Add(body);
            var buttons = new WrapPanel();
            row.Children.Add(buttons);
            buttons.Children.Add(FeatureButton("Редактировать", () =>
            {
                editing = note.Id;
                editor.Text = note.Text;
                topicInput.Text = NoteTopic(note);
                status.Text = "";
                heading.Text = L("Редактирование заметки");
                save.Content = L("Сохранить изменения");
                cancel.Visibility = Visibility.Visible;
                DashboardSectionView.ScrollToTop();
                editor.Focus();
            }));
            buttons.Children.Add(FeatureButton("Копировать", () => Clipboard.SetText(note.Text)));
            buttons.Children.Add(FeatureButton("Удалить", () =>
            {
                SaveNotes(_notes.Where(item => item.Id != note.Id).ToList());
                if (editing == note.Id) ResetEditor();
                RefreshTopics();
            }));
        }
    }

    private static string NoteTopic(NoteItem note) => note.Topic?.Trim() ?? "";
}
