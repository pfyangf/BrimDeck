using System.Windows.Threading;
using BrimDeck.Core;

namespace BrimDeck.Native;

// Advances a running pomodoro on the UI dispatcher. The timer is off while idle or paused so an unused page
// does not wake the island every second; the open page paints the countdown itself (MainWindow.Pomodoro).
internal sealed class PomodoroService : IDisposable
{
    private readonly DispatcherTimer _timer;
    private readonly Func<DeckSettings> _settings;
    private bool _disposed;

    public PomodoroService(Dispatcher dispatcher, Func<DeckSettings> settings)
    {
        _settings = settings;
        Clock = new PomodoroClock();
        _timer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => Tick(), dispatcher);
        _timer.Stop();
    }

    public PomodoroClock Clock { get; }
    public event Action? Changed;

    public void Dispose()
    {
        _disposed = true;
        _timer.Stop();
    }

    public void Notify()
    {
        ApplyTimer();
        Changed?.Invoke();
    }

    private void ApplyTimer()
    {
        if (_disposed) return;
        if (Clock.Running) { if (!_timer.IsEnabled) _timer.Start(); }
        else _timer.Stop();
    }

    private void Tick()
    {
        if (_disposed) return;
        try
        {
            var settings = _settings();
            if (Clock.Tick(settings, DateTimeOffset.Now, out var finished))
            {
                if (settings.PomodoroNotify && finished != PomodoroPhase.Idle)
                    LocalNotice.Show(PomodoroClock.PhaseName(finished), Loc.T("阶段结束", "Phase ended"));
                Changed?.Invoke();
            }
            ApplyTimer();
        }
        catch (Exception)
        {
            // A toast or settings read must not take down the dispatcher tick.
            ApplyTimer();
        }
    }
}
