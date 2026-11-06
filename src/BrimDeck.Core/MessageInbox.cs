namespace BrimDeck.Core;

// One toast kept for the Messages page (newest first). Distinct from the collapsed-island flash (MessageNotice).
public sealed record InboxMessage(MessageApp App, string Title, string Body, DateTimeOffset Created, string Key);

// Recent classified Windows toasts for the Messages page. Capacity-capped, newest first. ToastMirror writes; the page reads.
public sealed class MessageInbox
{
    public const int Capacity = 48;
    private readonly object _gate = new();
    private readonly List<InboxMessage> _items = [];

    public event Action? Changed;

    public IReadOnlyList<InboxMessage> Snapshot()
    {
        lock (_gate) return [.. _items];
    }

    public void Remember(IEnumerable<InboxMessage> messages)
    {
        bool changed = false;
        lock (_gate)
        {
            foreach (var message in messages)
            {
                if (_items.Any(item => item.Key == message.Key)) continue;
                _items.Insert(0, message);
                changed = true;
            }
            while (_items.Count > Capacity) _items.RemoveAt(_items.Count - 1);
        }
        if (changed) Changed?.Invoke();
    }

    public void Clear()
    {
        lock (_gate)
        {
            if (_items.Count == 0) return;
            _items.Clear();
        }
        Changed?.Invoke();
    }
}
