using System.Windows.Threading;
using BrimDeck.Core;

namespace BrimDeck.Native;

// Checks alarms once a second on the UI dispatcher. Marks FiredDate and raises Fired so decks can refresh.
internal sealed class AlarmService : IDisposable
{
    private readonly DispatcherTimer _timer;
    private readonly AlarmStore _store;
    private readonly Func<DeckSettings> _settings;
    private IReadOnlyList<AlarmItem> _items = [];
    private bool _disposed;

    public AlarmService(Dispatcher dispatcher, AlarmStore store, Func<DeckSettings> settings)
    {
        _store = store;
        _settings = settings;
        _timer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => Tick(), dispatcher);
        _timer.Stop();
        Reload();
        store.Changed += Reload;
    }

    public event Action<AlarmItem>? Fired;

    public void Apply()
    {
        if (_disposed) return;
        bool any = _settings().AlarmPage && _items.Any(item => item.Enabled);
        if (any) { if (!_timer.IsEnabled) _timer.Start(); }
        else _timer.Stop();
    }

    public void Dispose()
    {
        _disposed = true;
        _timer.Stop();
        _store.Changed -= Reload;
    }

    private void Reload()
    {
        _items = _store.Load();
        Apply();
    }

    private void Tick()
    {
        if (_disposed || !_settings().AlarmPage) return;
        var now = DateTimeOffset.Now;
        bool dirty = false;
        foreach (var item in _items)
        {
            if (!item.IsDue(now)) continue;
            item.FiredDate = now.ToString("yyyy-MM-dd");
            dirty = true;
            string label = string.IsNullOrWhiteSpace(item.Label) ? Loc.T("闹钟", "Alarm") : item.Label;
            LocalNotice.Show(label, AlarmItems.TimeLabel(item));
            Fired?.Invoke(item);
        }
        if (dirty) _store.Save(_items);
    }
}
