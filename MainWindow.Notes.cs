using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DynamicIslandWin.Services;
using Button = System.Windows.Controls.Button;
using Clipboard = System.Windows.Clipboard;

namespace DynamicIslandWin;

public partial class MainWindow
{
    private const int NoteLimit = 100;
    private List<NoteItem> _notes = [];
    private string NotesPath => Path.Combine(Path.GetDirectoryName(_settingsPath)!, "notes.json");
    private sealed record NoteItem(Guid Id, string Text, DateTimeOffset Updated);

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
        RefreshDesktopWidgets();
    }

    private void BuildNotesPage()
    {
        var editor = FeatureInput(DashboardSectionContent, "Заметка", "");
        editor.Style = (Style)FindResource("RoundedFeatureTextBox");
        editor.AcceptsReturn = true;
        editor.TextWrapping = TextWrapping.Wrap;
        editor.MaxLength = 4000;
        editor.MaxHeight = 120;
        editor.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        var actions = new WrapPanel();
        DashboardSectionContent.Children.Add(actions);
        var saved = new StackPanel();
        DashboardSectionContent.Children.Add(saved);
        Guid? editing = null;
        var status = FeatureText("", 11, "ThemeDangerBrush");
        Button? cancel = null;
        cancel = FeatureButton("Отменить", ResetEditor);
        actions.Children.Add(FeatureButton("Сохранить заметку", Save));
        cancel.Visibility = Visibility.Collapsed;
        actions.Children.Add(cancel);
        actions.Children.Add(status);
        editor.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter || Keyboard.Modifiers != ModifierKeys.None) return;
            e.Handled = true;
            try { Save(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { status.Text = L("Не удалось сохранить заметку."); }
        };
        RenderNotes();

        void Save()
        {
            string text = editor.Text.Trim();
            if (text.Length == 0) return;
            if (!editing.HasValue && _notes.Count >= NoteLimit)
            {
                status.Text = L("Достигнут предел: 100 заметок.");
                return;
            }
            var note = new NoteItem(editing ?? Guid.NewGuid(), text, DateTimeOffset.Now);
            List<NoteItem> next = [note, .. _notes.Where(item => item.Id != note.Id)];
            SaveNotes(next);
            ResetEditor();
            RenderNotes();
        }

        void ResetEditor()
        {
            editing = null;
            editor.Clear();
            status.Text = "";
            if (cancel != null) cancel.Visibility = Visibility.Collapsed;
        }

        void RenderNotes()
        {
            if (_currentState == IslandState.Section && _featurePage == "notes")
            {
                _targetH = DashboardPageHeight;
                StartIslandFrames();
            }
            saved.Children.Clear();
            if (_notes.Count == 0)
                saved.Children.Add(FeatureText("Пока пусто — напишите заметку выше", 12, "ThemeMutedBrush"));
            foreach (var note in _notes.OrderByDescending(note => note.Updated))
            {
                var row = FeatureCard(note.Updated.LocalDateTime.ToString("g"), parent: saved);
                var body = FeatureText("");
                body.Text = note.Text;
                row.Children.Add(body);
                var buttons = new WrapPanel();
                row.Children.Add(buttons);
                buttons.Children.Add(FeatureButton("Редактировать", () =>
                {
                    editing = note.Id;
                    editor.Text = note.Text;
                    status.Text = "";
                    cancel.Visibility = Visibility.Visible;
                    DashboardSectionView.ScrollToTop();
                    editor.Focus();
                }));
                buttons.Children.Add(FeatureButton("Копировать", () => Clipboard.SetText(note.Text)));
                buttons.Children.Add(FeatureButton("Удалить", () =>
                {
                    SaveNotes(_notes.Where(item => item.Id != note.Id).ToList());
                    if (editing == note.Id) ResetEditor();
                    RenderNotes();
                }));
            }
        }
    }
}
