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
    private static readonly TimeSpan RetryEvery = TimeSpan.FromSeconds(15);
    private static readonly string[] Templates =
    [
        KnownNotificationBindings.ToastGeneric,
        "ToastText02", "ToastText01", "ToastText03", "ToastText04",
        "ToastImageAndText02", "ToastImageAndText01", "ToastImageAndText03", "ToastImageAndText04"
    ];

    private readonly DispatcherTimer _timer;
    private readonly MessageNoticeFeed _feed = new();
    private Flags _flags;
    private bool _disposed, _polling, _asked, _forceRequest;
    private Action? _pending;

    public ToastMirror(Dispatcher dispatcher)
    {
        _timer = new DispatcherTimer(PollEvery, DispatcherPriority.Background, (_, _) => _ = PollAsync(null), dispatcher);
        _timer.Stop();
        Status = Idle();
    }

    public event Action<MessageNotice>? Arrived;
    public string Status { get; private set; }

    public void Apply(DeckSettings settings)
    {
        var flags = Flags.From(settings);
        bool changed = flags != _flags;
        _flags = flags;
        if (changed) _feed.Reset();
        if (!flags.Any)
        {
            _timer.Stop();
            Status = Idle();
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
            if (!flags.Any && !force) { Status = Idle(); return; }
            var (access, toasts) = await ReadAsync(force);
            if (_disposed) return;
            Status = Describe(access);
            _timer.Interval = access == Access.Allowed ? PollEvery : RetryEvery;
            if (toasts is null || !flags.Any) return;
            toasts.Sort((a, b) => a.Created.CompareTo(b.Created));
            var fresh = _feed.Observe(toasts, flags.Allows);
            if (fresh.Count > 0) Arrived?.Invoke(fresh[^1]);
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

    private static string Idle() => Loc.T("消息提示已关闭，不会读取通知。", "Message notices are off, so notifications are not read.");

    private static string Describe(Access access) => access switch
    {
        Access.Allowed => Loc.T("已允许读取 Windows 通知。新的 QQ、微信和邮件横幅会显示在刘海或胶囊上。",
            "Windows notification access is allowed. New QQ, WeChat and mail banners show on the notch or capsule."),
        Access.Denied => Loc.T("系统拒绝了通知访问。请打开“Windows 设置 › 隐私和安全性 › 通知”，在“通知访问”中允许 BrimDeck，然后点“检查通知访问”。",
            "Windows denied notification access. Open Settings › Privacy & security › Notifications, allow BrimDeck under Notification access, then select Check notification access."),
        Access.Unspecified => Loc.T("尚未授予通知访问。点“检查通知访问”。若没有系统提示，请到“Windows 设置 › 隐私和安全性 › 通知”中允许 BrimDeck。",
            "Notification access has not been granted. Select Check notification access. If Windows shows no prompt, allow BrimDeck under Settings › Privacy & security › Notifications."),
        Access.Failed => Loc.T("暂时无法读取 Windows 通知。请确认系统为 Windows 10 2004 或更高，并在“隐私和安全性 › 通知”中允许 BrimDeck 访问通知。",
            "Windows notifications cannot be read right now. Windows 10 version 2004 or later is required, and BrimDeck must be allowed under Privacy & security › Notifications."),
        _ => Loc.T("尚未检查 Windows 通知访问。", "Windows notification access has not been checked.")
    };

    private readonly record struct Flags(bool Master, bool Qq, bool WeChat, bool Mail)
    {
        public bool Any => Master && (Qq || WeChat || Mail);
        public bool Allows(MessageApp app) => Master && app switch
        {
            MessageApp.Qq => Qq,
            MessageApp.WeChat => WeChat,
            MessageApp.Mail => Mail,
            _ => false
        };
        public static Flags From(DeckSettings settings) => new(settings.MessageNotices, settings.NoticeQq, settings.NoticeWeChat, settings.NoticeMail);
    }
}
