using System.Windows.Threading;
using BrimDeck.Core;

namespace BrimDeck.Native;

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
        _timer.Start();
    }

    public PomodoroClock Clock { get; }
    public event Action? Changed;

    public void Dispose()
    {
        _disposed = true;
        _timer.Stop();
    }

    private void Tick()
    {
        if (_disposed) return;
        var settings = _settings();
        if (Clock.Tick(settings, DateTimeOffset.Now, out var finished))
        {
            if (settings.PomodoroNotify && finished != PomodoroPhase.Idle)
                LocalNotice.Show(PomodoroClock.PhaseName(finished), Loc.T("阶段结束", "Phase ended"));
            Changed?.Invoke();
        }
        else if (Clock.Running) Changed?.Invoke();
    }

    public void Notify() => Changed?.Invoke();
}
