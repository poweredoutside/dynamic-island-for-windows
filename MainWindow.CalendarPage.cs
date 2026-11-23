using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using DynamicIslandWin.Services;
using Button = System.Windows.Controls.Button;
using PasswordBox = System.Windows.Controls.PasswordBox;

namespace DynamicIslandWin;

public partial class MainWindow
{
    public partial class AppSettings
    {
        public string? CalendarFeed { get; set; }
    }

    private const int CalendarLimit = 4_000_000;
    private static readonly HttpClient CalendarClient = new() { Timeout = TimeSpan.FromSeconds(20) };
    private readonly CancellationTokenSource _calendarCancellation = new();
    private DateTime _nextCalendarDownload;
    private bool _calendarBusy;
    private StackPanel? _calendarAgenda;
    private StackPanel? _calendarReminders;
    private readonly string ProductVersion = GitHubUpdateService.CurrentVersion.ToString(3);

    private bool CalendarPageVisible => _currentState == IslandState.Section && _featurePage == "calendar";

    private void BuildCalendarPage()
    {
        _calendarAgenda = FeatureCard("Ближайшее");
        var reminder = FeatureCard("Напомнить");
        var fields = new Grid();
        fields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        fields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        fields.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        reminder.Children.Add(fields);
        var labelField = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
        var timeField = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
        Grid.SetColumn(timeField, 1);
        fields.Children.Add(labelField);
        fields.Children.Add(timeField);
        var title = FeatureInput(labelField, "Название", "");
        title.Style = (Style)FindResource("RoundedFeatureTextBox");
        title.MaxLength = 120;
        var time = FeatureInput(timeField, "Время", "");
        time.Style = (Style)FindResource("RoundedFeatureTextBox");
        time.MaxLength = 36;
        var error = FeatureText("", 11, "ThemeDangerBrush");
        error.Visibility = Visibility.Collapsed;

        void AddReminder()
        {
            if (ReminderTime.Parse(time.Text, DateTimeOffset.Now) is not { } due)
            {
                error.Text = L("Проверьте время напоминания.");
                error.Visibility = Visibility.Visible;
                return;
            }
            _settings.Timers.Add(new(Guid.NewGuid(), string.IsNullOrWhiteSpace(title.Text) ? L("Напоминание") : title.Text.Trim(), due));
            SaveSettings();
            title.Clear();
            time.Clear();
            error.Visibility = Visibility.Collapsed;
            RefreshCalendarAgenda();
        }

        var add = FeatureButton("Добавить", AddReminder);
        add.VerticalAlignment = VerticalAlignment.Bottom;
        add.Margin = new Thickness(0, 0, 0, 12);
        Grid.SetColumn(add, 2);
        fields.Children.Add(add);
        time.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) { e.Handled = true; AddReminder(); } };
        reminder.Children.Add(FeatureText("18:30, завтра 9:00, через 20 мин, 15.11 14:00", 11, "ThemeMutedBrush"));
        reminder.Children.Add(error);
        _calendarReminders = new StackPanel();
        reminder.Children.Add(_calendarReminders);

        var source = FeatureCard("Google / Outlook / iCalendar", "Вставьте ссылку HTTPS на календарь в формате ICS или импортируйте файл.");
        var link = new PasswordBox { Padding = new Thickness(10), MaxLength = 8192, Margin = new Thickness(0, 0, 0, 10) };
        link.Style = (Style)FindResource("RoundedFeaturePasswordBox");
        link.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "ThemeInputBrush");
        link.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, "ThemeTextBrush");
        System.Windows.Automation.AutomationProperties.SetName(link, L("Ссылка на календарь"));
        source.Children.Add(link);
        var status = FeatureText(_settings.CalendarFeed != null ? "Подписка подключена" : File.Exists(CalendarPath) ? "Файл ICS импортирован" : "", 11, "ThemeMutedBrush");
        if (status.Text.Length == 0) status.Visibility = Visibility.Collapsed;
        var actions = new WrapPanel();
        source.Children.Add(actions);
        source.Children.Add(status);
        actions.Children.Add(FeatureButton("Подключить", async () =>
        {
            status.Visibility = Visibility.Visible;
            if (!Uri.TryCreate(link.Password.Trim(), UriKind.Absolute, out var uri)
                || uri.Scheme != Uri.UriSchemeHttps || uri.UserInfo.Length != 0)
            {
                status.Text = L("Введите ссылку HTTPS на календарь.");
                return;
            }
            if (await LoadCalendarAsync(uri, null, true, status)) link.Clear();
        }));
        actions.Children.Add(FeatureButton("Импортировать .ics", async () =>
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Filter = L("Календарь iCalendar|*.ics"), CheckFileExists = true };
            if (dialog.ShowDialog(this) == true) await LoadCalendarAsync(null, dialog.FileName, true, status);
        }));
        if (_settings.CalendarFeed != null)
            actions.Children.Add(FeatureButton("Обновить", async () => await RefreshCalendarFeedAsync(status)));
        if (_settings.CalendarFeed != null || File.Exists(CalendarPath))
            actions.Children.Add(FeatureButton("Удалить календарь", () =>
            {
                if (_calendarBusy) { status.Text = L("Календарь обновляется…"); return; }
                File.Delete(CalendarPath);
                _settings.CalendarFeed = null;
                _meetings = [];
                RefreshWidget("calendar");
                SaveSettings();
                OpenFeaturePage("calendar");
            }));
        RefreshCalendarAgenda();
    }

    private void RefreshCalendarAgenda()
    {
        if (!CalendarPageVisible || _calendarAgenda == null || _calendarReminders == null) return;
        var now = DateTimeOffset.Now;
        if (_calendarAgenda.Children.Count > 1) _calendarAgenda.Children.RemoveRange(1, _calendarAgenda.Children.Count - 1);
        var upcoming = _meetings.Where(m => m.End >= now && m.Start < now.AddDays(7))
            .Select(m => (m.Start, Title: m.Title, m.Join))
            .Concat(_settings.Timers.Where(t => t.Due > now && t.Due < now.AddDays(7))
                .Select(t => (t.Due, Title: t.Label, Join: (Uri?)null)))
            .OrderBy(item => item.Item1).Take(50).ToArray();
        if (upcoming.Length == 0)
            _calendarAgenda.Children.Add(FeatureText("На неделю ничего не запланировано", 12, "ThemeMutedBrush"));
        foreach (var (start, title, join) in upcoming)
        {
            _calendarAgenda.Children.Add(FeatureText($"{start.ToLocalTime():ddd, dd MMM · HH:mm}   {title}"));
            if (join != null) _calendarAgenda.Children.Add(FeatureButton("Присоединиться", () => OpenWeb(join)));
        }
        _calendarReminders.Children.Clear();
        foreach (var timer in _settings.Timers.OrderBy(t => t.Due).ToArray())
        {
            var row = new DockPanel { LastChildFill = true };
            Button cancel = FeatureButton("Отменить", () => { _settings.Timers.Remove(timer); SaveSettings(); RefreshCalendarAgenda(); });
            DockPanel.SetDock(cancel, Dock.Right);
            row.Children.Add(cancel);
            row.Children.Add(FeatureText($"{timer.Due.ToLocalTime():dd MMM · HH:mm}   {timer.Label}"));
            _calendarReminders.Children.Add(row);
        }
    }

    private void TickCalendar()
    {
        var now = DateTime.UtcNow;
        if (_calendarBusy || now < _nextCalendarRead) return;
        _nextCalendarRead = now.AddMinutes(5);
        if (_nextCalendarDownload == default && File.Exists(CalendarPath))
        {
            _nextCalendarDownload = now.AddSeconds(10);
            _nextCalendarRead = _nextCalendarDownload;
            _ = LoadCalendarAsync(null, CalendarPath, false);
            return;
        }
        if (_settings.CalendarFeed != null && now >= _nextCalendarDownload)
        {
            _nextCalendarDownload = now.AddHours(1);
            _ = RefreshCalendarFeedAsync();
        }
        else if (File.Exists(CalendarPath))
            _ = LoadCalendarAsync(null, CalendarPath, false);
    }

    private async Task RefreshCalendarFeedAsync(TextBlock? status = null)
    {
        Uri uri;
        try
        {
            var bytes = Convert.FromBase64String(_settings.CalendarFeed ?? "");
            uri = new Uri(Encoding.UTF8.GetString(ProtectedData.Unprotect(bytes, null, DataProtectionScope.CurrentUser)));
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
            if (status != null)
            {
                status.Visibility = Visibility.Visible;
                status.Text = L("Подключите календарь заново.");
            }
            return;
        }
        if (!await LoadCalendarAsync(uri, null, false, status) && !_calendarBusy && !_windowClosed && File.Exists(CalendarPath))
            await LoadCalendarAsync(null, CalendarPath, false);
    }

    private async Task<bool> LoadCalendarAsync(Uri? uri, string? file, bool changeSource, TextBlock? status = null)
    {
        if (status != null)
        {
            status.Visibility = Visibility.Visible;
            status.Text = L("Календарь обновляется…");
        }
        if (_calendarBusy) return false;
        _calendarBusy = true;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_calendarCancellation.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            var snapshot = await Task.Run(async () =>
            {
                string ics;
                if (uri != null)
                {
                    using var reply = await CalendarClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                    reply.EnsureSuccessStatusCode();
                    using var stream = await reply.Content.ReadAsStreamAsync(timeout.Token);
                    ics = await ReadCalendarTextAsync(stream, timeout.Token);
                }
                else
                {
                    using var stream = File.OpenRead(file!);
                    ics = await ReadCalendarTextAsync(stream, timeout.Token);
                }
                return (Ics: ics, Meetings: CalendarReader.Read(ics, DateTimeOffset.Now));
            }, timeout.Token);
            if (_windowClosed) return false;
            string? feed = changeSource && uri != null
                ? Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(uri.AbsoluteUri), null, DataProtectionScope.CurrentUser))
                : null;
            if (changeSource || uri != null) LocalFile.Write(CalendarPath, snapshot.Ics);
            _meetings = snapshot.Meetings;
            RefreshWidget("calendar");
            if (changeSource)
            {
                _settings.CalendarFeed = feed;
                _nextCalendarDownload = DateTime.UtcNow.AddHours(1);
                SaveSettings();
            }
            if (changeSource && CalendarPageVisible && status?.IsLoaded == true) OpenFeaturePage("calendar");
            else
            {
                if (status != null) status.Text = L("Календарь обновлён");
                RefreshCalendarAgenda();
            }
            return true;
        }
        catch (Exception)
        {
            if (status != null) status.Text = L("Не удалось обновить календарь. Проверьте ссылку или файл ICS.");
            return false;
        }
        finally { _calendarBusy = false; }
    }

    private static async Task<string> ReadCalendarTextAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        byte[] chunk = new byte[8192];
        while (await stream.ReadAsync(chunk, cancellationToken) is int count && count > 0)
        {
            if (buffer.Length + count > CalendarLimit) throw new FormatException();
            buffer.Write(chunk, 0, count);
        }
        buffer.Position = 0;
        using var reader = new StreamReader(buffer, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        string text = reader.ReadToEnd();
        return text.TrimStart().StartsWith("BEGIN:VCALENDAR", StringComparison.OrdinalIgnoreCase) ? text : throw new FormatException();
    }
}
