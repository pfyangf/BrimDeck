using System.Windows.Threading;
using BrimDeck.Core;

namespace BrimDeck.Native;

// Polls bound Microsoft Graph, IMAP, and POP3 mailboxes into EmailOtpFeed. Toast OTP arrives separately via ToastMirror.
internal sealed class MailInboxService : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly MailAccountStore _accounts;
    private readonly GraphMailClient _graph;
    private readonly EmailOtpFeed _feed;
    private readonly Func<DeckSettings> _settings;
    private readonly DispatcherTimer _timer;
    private CancellationTokenSource? _round;
    private bool _busy;

    public MailInboxService(
        Dispatcher dispatcher,
        MailAccountStore accounts,
        GraphMailClient graph,
        EmailOtpFeed feed,
        Func<DeckSettings> settings)
    {
        _dispatcher = dispatcher;
        _accounts = accounts;
        _graph = graph;
        _feed = feed;
        _settings = settings;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
        _timer.Tick += (_, _) => _ = PollAsync();
        _accounts.Changed += OnAccountsChanged;
    }

    public event Action? Polled;

    public void Apply(DeckSettings settings)
    {
        bool on = settings.MailOtpPage && _accounts.Load().Count > 0;
        if (on) { if (!_timer.IsEnabled) { _timer.Start(); _ = PollAsync(); } }
        else _timer.Stop();
    }

    public Task RefreshNowAsync() => PollAsync();

    private void OnAccountsChanged() => _dispatcher.BeginInvoke(() =>
    {
        Apply(_settings());
        _ = PollAsync();
    });

    private async Task PollAsync()
    {
        if (_busy) return;
        var settings = _settings();
        if (!settings.MailOtpPage) return;
        var accounts = _accounts.Load();
        if (accounts.Count == 0) return;
        _busy = true;
        _round?.Cancel();
        _round = new CancellationTokenSource();
        var cancel = _round.Token;
        try
        {
            bool any = false;
            foreach (var account in accounts)
            {
                cancel.ThrowIfCancellationRequested();
                IReadOnlyList<EmailOtpMessage> messages;
                try
                {
                    if (account.Kind == MailAccountKind.MicrosoftGraph)
                        messages = await _graph.FetchRecentAsync(account, cancel: cancel).ConfigureAwait(false);
                    else
                    {
                        string secret = _accounts.ReadSecret(account.Id);
                        messages = account.Kind == MailAccountKind.Pop3
                            ? await Pop3MailClient.FetchRecentAsync(account, secret, cancel: cancel).ConfigureAwait(false)
                            : await ImapMailClient.FetchRecentAsync(account, secret, cancel: cancel).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception)
                {
                    continue;
                }
                await _dispatcher.InvokeAsync(() =>
                {
                    foreach (var message in messages)
                        if (_feed.Ingest(message)) any = true;
                });
            }
            if (any) Polled?.Invoke();
        }
        catch (OperationCanceledException) { }
        finally { _busy = false; }
    }

    public void Dispose()
    {
        _timer.Stop();
        _accounts.Changed -= OnAccountsChanged;
        _round?.Cancel();
        _round?.Dispose();
    }
}
