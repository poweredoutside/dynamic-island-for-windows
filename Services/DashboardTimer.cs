namespace DynamicIslandWin.Services;

internal sealed class DashboardTimer(TimeProvider clock)
{
    internal enum Mode { Countdown, Pomodoro, Stopwatch }

    private long? _started;
    private TimeSpan _accumulated;
    private TimeSpan _duration = TimeSpan.FromMinutes(5);
    internal Mode Kind { get; private set; }
    internal bool Running => _started.HasValue;
    internal bool Paused => !Running && _accumulated > TimeSpan.Zero && (Kind == Mode.Stopwatch || _accumulated < _duration);
    internal bool Break { get; private set; }
    internal int Round { get; private set; } = 1;
    private TimeSpan Elapsed => _accumulated + (_started is long start ? clock.GetElapsedTime(start) : TimeSpan.Zero);
    internal TimeSpan Value => Kind == Mode.Stopwatch ? Elapsed : TimeSpan.FromTicks(Math.Max(0, (_duration - Elapsed).Ticks));
    internal double Progress => Kind == Mode.Stopwatch ? Value.TotalSeconds % 60 / 60 : Math.Clamp(1 - Value.TotalSeconds / _duration.TotalSeconds, 0, 1);

    internal void Select(Mode mode)
    {
        if (Kind == mode) return;
        Kind = mode;
        if (mode == Mode.Countdown) _duration = TimeSpan.FromMinutes(5);
        Reset();
    }

    internal void SetMinutes(double minutes)
    {
        _duration = TimeSpan.FromMinutes(minutes);
        Reset();
    }

    internal void Toggle()
    {
        if (Running)
        {
            _accumulated = Elapsed;
            _started = null;
        }
        else
        {
            if (Kind != Mode.Stopwatch && Value == TimeSpan.Zero) Reset();
            _started = clock.GetTimestamp();
        }
    }

    internal void Reset()
    {
        _started = null;
        _accumulated = TimeSpan.Zero;
        Break = false;
        Round = 1;
        if (Kind == Mode.Pomodoro) _duration = TimeSpan.FromMinutes(25);
    }

    internal bool Tick()
    {
        if (!Running || Kind == Mode.Stopwatch || Value > TimeSpan.Zero) return false;
        if (Kind == Mode.Countdown)
        {
            _started = null;
            _accumulated = _duration;
        }
        else
        {
            if (Break) Round = Round % 4 + 1;
            Break = !Break;
            _duration = TimeSpan.FromMinutes(Break ? Round == 4 ? 15 : 5 : 25);
            _accumulated = TimeSpan.Zero;
            _started = clock.GetTimestamp();
        }
        return true;
    }

    internal string Display
    {
        get
        {
            var value = Value;
            var seconds = (long)(Kind == Mode.Stopwatch ? Math.Floor(value.TotalSeconds) : Math.Ceiling(value.TotalSeconds));
            return seconds >= 3600 ? $"{seconds / 3600}:{seconds / 60 % 60:00}:{seconds % 60:00}" : $"{seconds / 60:00}:{seconds % 60:00}";
        }
    }
}
