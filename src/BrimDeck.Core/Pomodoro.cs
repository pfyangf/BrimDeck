namespace BrimDeck.Core;

public enum PomodoroPhase { Idle, Focus, ShortBreak, LongBreak }

// Classic pomodoro clock. Durations come from DeckSettings; the running phase lives here.
public sealed class PomodoroClock
{
    public PomodoroPhase Phase { get; private set; } = PomodoroPhase.Idle;
    public DateTimeOffset? EndsAt { get; private set; }
    public int CompletedFocus { get; private set; }
    public bool Running { get; private set; }

    public TimeSpan Remaining(DateTimeOffset now)
    {
        if (EndsAt is not { } end) return TimeSpan.Zero;
        var left = end - now;
        return left < TimeSpan.Zero ? TimeSpan.Zero : left;
    }

    public void StartFocus(DeckSettings settings, DateTimeOffset now)
    {
        Phase = PomodoroPhase.Focus;
        Running = true;
        EndsAt = now.AddMinutes(Math.Clamp(settings.PomodoroFocusMinutes, 1, 180));
    }

    public void StartBreak(DeckSettings settings, DateTimeOffset now, bool longBreak)
    {
        Phase = longBreak ? PomodoroPhase.LongBreak : PomodoroPhase.ShortBreak;
        Running = true;
        int minutes = longBreak
            ? Math.Clamp(settings.PomodoroLongBreakMinutes, 1, 60)
            : Math.Clamp(settings.PomodoroBreakMinutes, 1, 60);
        EndsAt = now.AddMinutes(minutes);
    }

    public void Pause(DateTimeOffset now)
    {
        if (!Running || EndsAt is null) return;
        // Store remaining as a synthetic end far in the past marker via CompletedFocus? Keep Remaining frozen by stopping.
        _pausedLeft = Remaining(now);
        Running = false;
        EndsAt = null;
    }

    private TimeSpan _pausedLeft;

    public void Resume(DateTimeOffset now)
    {
        if (Running || Phase == PomodoroPhase.Idle) return;
        if (_pausedLeft <= TimeSpan.Zero) _pausedLeft = TimeSpan.FromMinutes(1);
        EndsAt = now + _pausedLeft;
        Running = true;
    }

    public void Reset()
    {
        Phase = PomodoroPhase.Idle;
        Running = false;
        EndsAt = null;
        _pausedLeft = TimeSpan.Zero;
    }

    // Advances when a phase ends. Returns true when a phase just completed (caller may notify).
    public bool Tick(DeckSettings settings, DateTimeOffset now, out PomodoroPhase finished)
    {
        finished = PomodoroPhase.Idle;
        if (!Running || EndsAt is null || now < EndsAt) return false;
        finished = Phase;
        Running = false;
        EndsAt = null;
        if (finished == PomodoroPhase.Focus)
        {
            CompletedFocus++;
            bool longBreak = settings.PomodoroLongEvery > 0 && CompletedFocus % settings.PomodoroLongEvery == 0;
            StartBreak(settings, now, longBreak);
        }
        else if (finished is PomodoroPhase.ShortBreak or PomodoroPhase.LongBreak)
        {
            StartFocus(settings, now);
        }
        return true;
    }

    public static string PhaseName(PomodoroPhase phase) => phase switch
    {
        PomodoroPhase.Focus => Loc.T("专注", "Focus"),
        PomodoroPhase.ShortBreak => Loc.T("短休息", "Short break"),
        PomodoroPhase.LongBreak => Loc.T("长休息", "Long break"),
        _ => Loc.T("未开始", "Idle")
    };
}
