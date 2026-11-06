using System.Globalization;
using System.Windows.Threading;
using BrimDeck.Core;
using Windows.ApplicationModel;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace BrimDeck.Native;

// Mirrors QQ, WeChat and mail banners from Windows notification center onto the island.
// UserNotificationListener is the public listener. On an unpackaged desktop app, RequestAccessAsync and
// GetNotificationsAsync work after the user allows notification access, but subscribing to NotificationChanged
// fails (0x80070490). The list is therefore read on a timer. Toasts are not clicked, removed or written to disk.
internal sealed class ToastMirror : IDisposable
{
    private static readonly TimeSpan PollEvery = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan MailOtpOnlyEvery = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan RetryEvery = TimeSpan.FromSeconds(15);
    private static readonly string[] Templates =
    [
        KnownNotificationBindings.ToastGeneric,
        "ToastText02", "ToastText01", "ToastText03", "ToastText04",
        "ToastImageAndText02", "ToastImageAndText01", "ToastImageAndText03", "ToastImageAndText04"
    ];

    private readonly DispatcherTimer _timer;
    private readonly MessageNoticeFeed _feed = new();
    private readonly MessageInbox? _inbox;
    private readonly HashSet<string> _mailSeen = new(StringComparer.Ordinal);
    private readonly Queue<string> _mailOrder = new();
    private Flags _flags;
    private bool _disposed, _polling, _asked, _forceRequest, _mailPrimed;
    private Action? _pending;

    public ToastMirror(Dispatcher dispatcher, MessageInbox? inbox = null)
    {
        _inbox = inbox;
        _timer = new DispatcherTimer(PollEvery, DispatcherPriority.Background, (_, _) => _ = PollAsync(null), dispatcher);
        _timer.Stop();
        Status = Idle(default);
    }

    public event Action<MessageNotice>? Arrived;
    // Verification-looking mail toasts for the Email OTP page (EmailOtpFeed), even when message banners are off.
    public event Action<EmailOtpMessage>? MailOtpArrived;
    public string Status { get; private set; }

    public void Apply(DeckSettings settings)
    {
        var flags = Flags.From(settings);
        bool changed = flags != _flags;
        bool mailChanged = flags.MailOtp != _flags.MailOtp;
        _flags = flags;
        if (changed) _feed.Reset();
        if (mailChanged || !flags.MailOtp) { _mailSeen.Clear(); _mailOrder.Clear(); _mailPrimed = false; }
        if (!flags.Any)
        {
            _timer.Stop();
            Status = Idle(flags);
            return;
        }
        bool start = !_timer.IsEnabled;
        if (start) _timer.Start();
        if (changed || start) _ = PollAsync(null);
    }

    // Asks Windows again. The settings page calls this from the button; the first poll also asks once
    // while access is still unspecified.
    public void RequestAccess(Action? completed)
    {
        _forceRequest = true;
        if (_flags.Any && !_timer.IsEnabled) _timer.Start();
        _ = PollAsync(completed);
    }

    public void Dispose()
    {
        _disposed = true;
        _timer.Stop();
    }

    private async Task PollAsync(Action? completed)
    {
        if (_disposed) { completed?.Invoke(); return; }
        if (_polling) { if (completed is not null) _pending = completed; return; }
        _polling = true;
        try
        {
            var flags = _flags;
            bool force = _forceRequest;
            _forceRequest = false;
            if (!flags.Any && !force) { Status = Idle(flags); return; }
            var (access, toasts) = await ReadAsync(force);
            if (_disposed) return;
            Status = Describe(access, flags);
            _timer.Interval = access == Access.Allowed
                ? (flags.Banners ? PollEvery : MailOtpOnlyEvery)
                : RetryEvery;
            if (toasts is null || !flags.Any) return;
            toasts.Sort((a, b) => a.Created.CompareTo(b.Created));
            var fresh = _feed.Observe(toasts, flags.Allows);
            if (fresh.Count > 0) Arrived?.Invoke(fresh[^1]);
            FeedInbox(toasts, flags);
            ObserveMailOtp(toasts, flags);
        }
        finally
        {
            _polling = false;
            var done = completed;
            var pending = _pending;
            _pending = null;
            done?.Invoke();
            pending?.Invoke();
            if (_forceRequest && !_disposed) _ = PollAsync(null);
        }
    }

    private async Task<(Access Access, List<ToastSnapshot>? Toasts)> ReadAsync(bool request)
    {
        try
        {
            var listener = UserNotificationListener.Current;
            var access = listener.GetAccessStatus();
            // Ask once while access is still open, and again when the settings button asks. An allowed
            // listener is not asked again; a denial stays until the user changes it in Windows Settings.
            if (access != UserNotificationListenerAccessStatus.Allowed && (request || !_asked))
            {
                _asked = true;
                access = await listener.RequestAccessAsync();
            }
            if (access != UserNotificationListenerAccessStatus.Allowed) return (Map(access), null);
            var list = await listener.GetNotificationsAsync(NotificationKinds.Toast);
            return (Access.Allowed, Project(list));
        }
        catch (Exception)
        {
            // The exception text is not kept: it is not needed, and a listener failure must not surface banner text.
            return (Access.Failed, null);
        }
    }

    private static List<ToastSnapshot> Project(IReadOnlyList<UserNotification> list)
    {
        var result = new List<ToastSnapshot>(list.Count);
        foreach (var item in list)
        {
            try
            {
                AppInfo? info = item.AppInfo;
                if (info is null) continue;
                string appId = info.AppUserModelId ?? "";
                string display = "";
                try { display = info.DisplayInfo?.DisplayName ?? ""; } catch (Exception) { }
                var (title, body) = Texts(item);
                string id = item.Id.ToString(CultureInfo.InvariantCulture);
                result.Add(new ToastSnapshot(MessageNotices.Key(appId, id, item.CreationTime, title, body), appId, display, title, body, item.CreationTime));
            }
            catch (Exception) { }
        }
        return result;
    }

    private static (string Title, string Body) Texts(UserNotification item)
    {
        var visual = item.Notification?.Visual;
        if (visual is null) return ("", "");
        NotificationBinding? binding = null;
        foreach (var name in Templates)
        {
            try { binding = visual.GetBinding(name); } catch (Exception) { binding = null; }
            if (binding is not null) break;
        }
        if (binding is null) return ("", "");
        System.Collections.IEnumerable texts;
        try { texts = binding.GetTextElements(); } catch (Exception) { return ("", ""); }
        var lines = new List<string>();
        foreach (AdaptiveNotificationText text in texts) lines.Add(text.Text ?? "");
        if (lines.Count == 0 || lines.All(string.IsNullOrEmpty)) return ("", "");
        return (lines[0], string.Join("\n", lines.Skip(1)));
    }

    private enum Access { Unknown, Allowed, Denied, Unspecified, Failed }

    private static Access Map(UserNotificationListenerAccessStatus status) => status switch
    {
        UserNotificationListenerAccessStatus.Allowed => Access.Allowed,
        UserNotificationListenerAccessStatus.Denied => Access.Denied,
        UserNotificationListenerAccessStatus.Unspecified => Access.Unspecified,
        _ => Access.Unknown
    };

    private void ObserveMailOtp(IReadOnlyList<ToastSnapshot> toasts, Flags flags)
    {
        if (!flags.MailOtp) return;
        if (!_mailPrimed)
        {
            foreach (var item in toasts) _mailSeen.Add(item.Key);
            _mailPrimed = true;
            return;
        }
        foreach (var item in toasts)
        {
            if (!_mailSeen.Add(item.Key)) continue;
            _mailOrder.Enqueue(item.Key);
            while (_mailOrder.Count > MessageNoticeFeed.Capacity && _mailOrder.TryDequeue(out var oldest))
                _mailSeen.Remove(oldest);
            if (EmailOtp.FromToast(item) is not { } message) continue;
            MailOtpArrived?.Invoke(message);
        }
    }

    private static string Idle(Flags flags) => flags.MailOtp
        ? Loc.T("消息横幅已关闭；仍会读取邮件通知以提取验证码。", "Message banners are off; mail notifications are still read for verification codes.")
        : Loc.T("消息提示已关闭，不会读取通知。", "Message notices are off, so notifications are not read.");

    private static string Describe(Access access, Flags flags) => access switch
    {
        Access.Allowed => flags.Banners
            ? Loc.T("已允许读取 Windows 通知。新的消息横幅会显示在刘海或胶囊上。",
                "Windows notification access is allowed. New message banners show on the notch or capsule.")
            : Loc.T("已允许读取 Windows 通知。新的验证码邮件会进入邮箱验证码页。",
                "Windows notification access is allowed. New verification mails appear on the email codes page."),
        Access.Denied => Loc.T("系统拒绝了通知访问。请打开“Windows 设置 › 隐私和安全性 › 通知”，在“通知访问”中允许 BrimDeck，然后点“检查通知访问”。",
            "Windows denied notification access. Open Settings › Privacy & security › Notifications, allow BrimDeck under Notification access, then select Check notification access."),
        Access.Unspecified => Loc.T("尚未授予通知访问。点“检查通知访问”。若没有系统提示，请到“Windows 设置 › 隐私和安全性 › 通知”中允许 BrimDeck。",
            "Notification access has not been granted. Select Check notification access. If Windows shows no prompt, allow BrimDeck under Settings › Privacy & security › Notifications."),
        Access.Failed => Loc.T("暂时无法读取 Windows 通知。请确认系统为 Windows 10 2004 或更高，并在“隐私和安全性 › 通知”中允许 BrimDeck 访问通知。",
            "Windows notifications cannot be read right now. Windows 10 version 2004 or later is required, and BrimDeck must be allowed under Privacy & security › Notifications."),
        _ => Loc.T("尚未检查 Windows 通知访问。", "Windows notification access has not been checked.")
    };

    private void FeedInbox(IReadOnlyList<ToastSnapshot> toasts, Flags flags)
    {
        // Inbox is only for the Messages page. Collapsed banners still use Observe/Arrived without rebuilding that list.
        if (_inbox is null || !flags.Banners || !flags.MessagesPage) return;
        var batch = new List<InboxMessage>();
        foreach (var item in toasts)
        {
            if (MessageNotices.Classify(item.AppId, item.DisplayName) is not { } app || !flags.Allows(app)) continue;
            batch.Add(new InboxMessage(app, item.Title, item.Body, item.Created, item.Key));
        }
        if (batch.Count > 0) _inbox.Remember(batch);
    }

    private readonly record struct Flags(bool Master, bool Qq, bool WeChat, bool Mail, bool Lark, bool DingTalk, bool WeCom, bool MailOtp, bool MessagesPage)
    {
        public bool Banners => Master && (Qq || WeChat || Mail || Lark || DingTalk || WeCom);
        public bool Any => Banners || MailOtp;
        public bool Allows(MessageApp app) => Master && app switch
        {
            MessageApp.Qq => Qq,
            MessageApp.WeChat => WeChat,
            MessageApp.Mail => Mail,
            MessageApp.Lark => Lark,
            MessageApp.DingTalk => DingTalk,
            MessageApp.WeCom => WeCom,
            _ => false
        };
        public static Flags From(DeckSettings settings) => new(settings.MessageNotices, settings.NoticeQq, settings.NoticeWeChat, settings.NoticeMail,
            settings.NoticeLark, settings.NoticeDingTalk, settings.NoticeWeCom,
            settings.MailOtpPage && settings.MailOtpOutlook, settings.MessagesPage);
    }
}
