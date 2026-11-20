using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DynamicIslandWin.Services;
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;
using Point = System.Windows.Point;
using Size = System.Windows.Size;
using Shape = System.Windows.Shapes.Shape;

namespace DynamicIslandWin;

public partial class MainWindow
{
    private readonly DashboardTimer _dashboardTimer = new(TimeProvider.System);
    private TextBlock? _timerDisplay, _timerPhase;
    private System.Windows.Shapes.Path? _timerArc;
    private Button? _timerStart;
    private TextBox? _timerMinutesInput;
    private double _timerMinutes = 5;

    private void BuildTimerPageControls()
    {
        var modes = new WrapPanel { Margin = new Thickness(5, 5, 0, 0) };
        var modeBar = new Border
        {
            CornerRadius = new CornerRadius(20),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 10),
            Child = modes
        };
        modeBar.SetResourceReference(Border.BackgroundProperty, "ThemeInputBrush");
        foreach (var (mode, label) in new[] { (DashboardTimer.Mode.Countdown, "Таймер"), (DashboardTimer.Mode.Pomodoro, "Помодоро"), (DashboardTimer.Mode.Stopwatch, "Секундомер") })
        {
            var button = FeatureButton(label, () =>
            {
                if (_dashboardTimer.Kind == mode) return;
                _dashboardTimer.Select(mode);
                if (mode == DashboardTimer.Mode.Countdown) _dashboardTimer.SetMinutes(_timerMinutes);
                OpenFeaturePage("timers");
            });
            if (mode == _dashboardTimer.Kind) button.SetResourceReference(Button.BackgroundProperty, "ThemeSelectionBrush");
            modes.Children.Add(button);
        }
        DashboardSectionContent.Children.Add(modeBar);
        var card = FeatureCard("");
        var layout = new DockPanel();
        card.Children.Add(layout);
        var ring = new Grid { Width = 138, Height = 138, Margin = new Thickness(0, 0, 18, 0), VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(ring, Dock.Left);
        layout.Children.Add(ring);
        var outline = new System.Windows.Shapes.Ellipse { StrokeThickness = 7, Margin = new Thickness(4) };
        outline.SetResourceReference(Shape.StrokeProperty, "ThemeBorderBrush");
        ring.Children.Add(outline);
        _timerArc = new System.Windows.Shapes.Path { StrokeThickness = 7, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
        _timerArc.SetResourceReference(Shape.StrokeProperty, "ThemeAccentBrush");
        ring.Children.Add(_timerArc);
        _timerDisplay = FeatureText(_dashboardTimer.Display, 28);
        _timerDisplay.Margin = new Thickness(0);
        _timerDisplay.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
        _timerDisplay.VerticalAlignment = VerticalAlignment.Center;
        ring.Children.Add(_timerDisplay);
        var controls = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        layout.Children.Add(controls);
        _timerMinutesInput = null;
        var error = FeatureText("", 11, "ThemeDangerBrush");
        error.Visibility = Visibility.Collapsed;
        if (_dashboardTimer.Kind == DashboardTimer.Mode.Countdown)
        {
            var presets = new WrapPanel();
            foreach (int minutes in new[] { 1, 3, 5, 10, 15, 25, 45, 60 })
                presets.Children.Add(FeatureButton(minutes == 60 ? L("1 ч") : string.Format(L("{0} мин"), minutes), () =>
                {
                    if (_dashboardTimer.Running) return;
                    _timerMinutes = minutes;
                    _dashboardTimer.SetMinutes(minutes);
                    if (_timerMinutesInput != null) _timerMinutesInput.Text = minutes.ToString(CultureInfo.CurrentCulture);
                    RenderDashboardTimer();
                }));
            controls.Children.Add(presets);
            _timerMinutesInput = FeatureInput(controls, "Минуты", _timerMinutes.ToString(CultureInfo.CurrentCulture));
            _timerMinutesInput.Style = (Style)FindResource("RoundedFeatureTextBox");
            _timerMinutesInput.Width = 92;
            _timerMinutesInput.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
            _timerMinutesInput.MaxLength = 8;
        }
        _timerPhase = FeatureText("", 12, "ThemeMutedBrush");
        _timerPhase.Visibility = _dashboardTimer.Kind == DashboardTimer.Mode.Pomodoro ? Visibility.Visible : Visibility.Collapsed;
        controls.Children.Add(_timerPhase);
        var actions = new WrapPanel();
        _timerStart = FeatureButton("Старт", () =>
        {
            error.Visibility = Visibility.Collapsed;
            if (!_dashboardTimer.Running && _timerMinutesInput is { } input)
            {
                if (!double.TryParse(input.Text.Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out double minutes)
                    || minutes is < .05 or > 1440)
                {
                    error.Text = L("Введите от 0,05 до 1440 минут.");
                    error.Visibility = Visibility.Visible;
                    return;
                }
                if (minutes != _timerMinutes) { _timerMinutes = minutes; _dashboardTimer.SetMinutes(minutes); }
            }
            _dashboardTimer.Toggle();
            RenderDashboardTimer();
        });
        _timerStart.SetResourceReference(Button.BackgroundProperty, "ThemeAccentBrush");
        _timerStart.SetResourceReference(Button.ForegroundProperty, "ThemeOnAccentBrush");
        actions.Children.Add(_timerStart);
        actions.Children.Add(FeatureButton("Сброс", () => { _dashboardTimer.Reset(); RenderDashboardTimer(); }));
        controls.Children.Add(actions);
        controls.Children.Add(error);
        RenderDashboardTimer();
    }

    private void RefreshLiveTimer()
    {
        if (_dashboardTimer.Tick())
        {
            string message = _dashboardTimer.Kind != DashboardTimer.Mode.Pomodoro ? "Таймер завершён" : _dashboardTimer.Break ? "Время отдохнуть" : "За работу";
            FeatureNotice(L(message), () => OpenFeaturePage("timers"));
            if (_settings.ReminderSound && !_isDndActive) System.Media.SystemSounds.Exclamation.Play();
        }
        if (_currentState == IslandState.Section && _featurePage == "timers") RenderDashboardTimer();
    }

    private void RenderDashboardTimer()
    {
        if (_timerDisplay == null || _timerArc == null || _timerStart == null || _timerPhase == null) return;
        string display = _dashboardTimer.Display;
        if (_timerDisplay.Text != display)
            _timerDisplay.Text = display;
        _timerStart.Content = L(_dashboardTimer.Running ? "Пауза" : _dashboardTimer.Paused ? "Продолжить" : "Старт");
        if (_timerMinutesInput != null) _timerMinutesInput.IsReadOnly = _dashboardTimer.Running;
        _timerPhase.Text = _dashboardTimer.Kind == DashboardTimer.Mode.Pomodoro
            ? (_dashboardTimer.Break ? L("Перерыв") : string.Format(L("Работа · {0}/4"), _dashboardTimer.Round)) : "";
        double progress = _dashboardTimer.Progress;
        if (Equals(_timerArc.Tag, progress)) return;
        _timerArc.Tag = progress;
        if (progress <= 0) { _timerArc.Data = null; return; }
        if (progress >= 1) { _timerArc.Data = new EllipseGeometry(new Point(69, 69), 61.5, 61.5); return; }
        double angle = progress * Math.Tau;
        var geometry = new StreamGeometry();
        using (var pen = geometry.Open())
        {
            pen.BeginFigure(new Point(69, 7.5), false, false);
            pen.ArcTo(new Point(69 + Math.Sin(angle) * 61.5, 69 - Math.Cos(angle) * 61.5), new Size(61.5, 61.5),
                0, progress > .5, SweepDirection.Clockwise, true, false);
        }
        geometry.Freeze();
        _timerArc.Data = geometry;
    }
}
