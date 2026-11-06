using System.Globalization;

namespace BrimDeck.Core;

// Which messaging app a Windows toast belongs to. QQ Music and other Tencent apps are not QQ.
public enum MessageApp { Qq, WeChat, Mail, Lark, DingTalk, WeCom }

// One toast as Windows notification center lists it. Key is the listener's identity for this text, so a later
// read of the same toast is not shown again. The feed never keeps the text after it has decided.
public readonly record struct ToastSnapshot(string Key, string AppId, string DisplayName, string Title, string Body, DateTimeOffset Created);

// A toast the collapsed island may show. Title and body are the banner's own lines, not a chat history.
public sealed record MessageNotice(MessageApp App, string Title, string Body);

// Classifies toasts from the public Windows notification list (UserNotificationListener) and decides which
// newly arrived ones to show. Nothing here talks to QQ, WeChat or a mail client.
public static class MessageNotices
{
    public static readonly TimeSpan VisibleFor = TimeSpan.FromSeconds(6);

    public static bool AnyEnabled(DeckSettings settings)
        => settings.MessageNotices && (settings.NoticeQq || settings.NoticeWeChat || settings.NoticeMail
            || settings.NoticeLark || settings.NoticeDingTalk || settings.NoticeWeCom);

    public static bool Allows(DeckSettings settings, MessageApp app) => settings.MessageNotices && app switch
    {
        MessageApp.Qq => settings.NoticeQq,
        MessageApp.WeChat => settings.NoticeWeChat,
        MessageApp.Mail => settings.NoticeMail,
        MessageApp.Lark => settings.NoticeLark,
        MessageApp.DingTalk => settings.NoticeDingTalk,
        MessageApp.WeCom => settings.NoticeWeCom,
        _ => false
    };

    public static string AppName(MessageApp app) => app switch
    {
        MessageApp.Qq => "QQ",
        MessageApp.WeChat => Loc.T("微信", "WeChat"),
        MessageApp.Mail => Loc.T("邮件", "Mail"),
        MessageApp.Lark => Loc.T("飞书", "Lark"),
        MessageApp.DingTalk => Loc.T("钉钉", "DingTalk"),
        MessageApp.WeCom => Loc.T("企业微信", "WeCom"),
        _ => ""
    };

    // One line for the island: the banner title, then its body. Repeated lines and line breaks collapse.
    public static string Preview(string? title, string? body)
    {
        string head = Collapse(title), text = Collapse(body);
        if (head.Length == 0) return text;
        if (text.Length == 0 || string.Equals(head, text, StringComparison.Ordinal)) return head;
        return head + " · " + text;
    }

    public static string Key(string? appId, string? notificationId, DateTimeOffset created, string? title, string? body)
        => string.Join('\u001f', (appId ?? "").Trim(), (notificationId ?? "").Trim(),
            created.UtcTicks.ToString(CultureInfo.InvariantCulture), title ?? "", body ?? "");

    // AppUserModelId plus the name Windows shows on the toast. Matching is by those public identities
    // (executable, package id, display name), never by a private protocol.
    public static MessageApp? Classify(string? appId, string? displayName)
    {
        string name = (displayName ?? "").Trim();
        string id = (appId ?? "").Trim().ToLowerInvariant();
        if (id.Length == 0 && name.Length == 0) return null;
        if (Excluded(id, name)) return null;
        string tail = Tail(appId);
        if (IsWeChat(id, tail, name)) return MessageApp.WeChat;
        if (IsWeCom(id, tail, name)) return MessageApp.WeCom;
        if (IsQq(id, tail, name)) return MessageApp.Qq;
        if (IsLark(id, tail, name)) return MessageApp.Lark;
        if (IsDingTalk(id, tail, name)) return MessageApp.DingTalk;
        if (IsMail(id, tail, name)) return MessageApp.Mail;
        return null;
    }

    private static string Collapse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        var parts = value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return string.Join(" ", parts);
    }

    // The executable or the packaged app id at the end of an AppUserModelId.
    private static string Tail(string? appId)
    {
        string id = (appId ?? "").Trim();
        int bang = id.LastIndexOf('!');
        if (bang >= 0 && bang < id.Length - 1) id = id[(bang + 1)..];
        int slash = Math.Max(id.LastIndexOf('\\'), id.LastIndexOf('/'));
        if (slash >= 0 && slash < id.Length - 1) id = id[(slash + 1)..];
        return id.ToLowerInvariant();
    }

    private static bool Excluded(string id, string name)
    {
        if (id.Contains("qqmusic", StringComparison.Ordinal) || id.Contains("qqbrowser", StringComparison.Ordinal)
            || id.Contains("qqlive", StringComparison.Ordinal) || id.Contains("qqpinyin", StringComparison.Ordinal)
            || id.Contains("qqplayer", StringComparison.Ordinal) || id.Contains("qqgame", StringComparison.Ordinal)) return true;
        string shown = name.ToLowerInvariant();
        return shown.Contains("qq音乐", StringComparison.Ordinal) || shown.Contains("qq 音乐", StringComparison.Ordinal)
            || shown.Contains("qq music", StringComparison.Ordinal) || shown.Contains("qqbrowser", StringComparison.Ordinal)
            || shown.Contains("qq浏览器", StringComparison.Ordinal) || shown.Contains("qq browser", StringComparison.Ordinal);
    }

    private static readonly HashSet<string> QqNames = new(StringComparer.OrdinalIgnoreCase) { "QQ", "腾讯QQ", "腾讯 QQ", "QQ NT" };
    private static readonly HashSet<string> WeChatNames = new(StringComparer.OrdinalIgnoreCase) { "微信", "WeChat", "Weixin" };
    private static readonly HashSet<string> WeComNames = new(StringComparer.OrdinalIgnoreCase) { "企业微信", "WeCom", "WeChat Work", "WXWork" };
    private static readonly HashSet<string> LarkNames = new(StringComparer.OrdinalIgnoreCase) { "飞书", "Lark", "Larksuite", "Feishu" };
    private static readonly HashSet<string> DingTalkNames = new(StringComparer.OrdinalIgnoreCase) { "钉钉", "DingTalk", "Ding Talk", "阿里钉钉" };
    private static readonly HashSet<string> MailNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Outlook", "Microsoft Outlook", "New Outlook", "Outlook (new)", "Outlook for Windows",
        "新 Outlook", "新版 Outlook", "邮件", "邮件和日历", "Mail", "Windows Mail",
        "Thunderbird", "Mozilla Thunderbird", "Foxmail"
    };

    private static bool IsWeChat(string id, string tail, string name)
    {
        if (WeChatNames.Contains(name)) return true;
        if (tail is "wechat.exe" or "weixin.exe") return true;
        // Helper processes. A toast whose display name is already 微信 was accepted above.
        if (tail is "wechatappex.exe" or "wechatapp.exe") return false;
        return id is "wechat" or "weixin" || id.Contains("tencent.wechat", StringComparison.Ordinal) || id.Contains("tencent.weixin", StringComparison.Ordinal);
    }

    private static bool IsWeCom(string id, string tail, string name)
    {
        if (WeComNames.Contains(name)) return true;
        if (tail is "wxwork.exe" or "wecom.exe") return true;
        return id.Contains("wxwork", StringComparison.Ordinal) || id.Contains("wecom", StringComparison.Ordinal)
            || id.Contains("tencent.wework", StringComparison.Ordinal);
    }

    private static bool IsQq(string id, string tail, string name)
    {
        if (tail is "qq.exe" or "qqnt.exe" or "qqex.exe") return true;
        if (id is "qq" or "tencent.qq" || id.StartsWith("tencent.qq.", StringComparison.Ordinal)) return true;
        return QqNames.Contains(name);
    }

    private static bool IsLark(string id, string tail, string name)
    {
        if (LarkNames.Contains(name)) return true;
        if (tail is "lark.exe" or "feishu.exe" or "larksuite.exe") return true;
        return id.Contains("lark", StringComparison.Ordinal) || id.Contains("feishu", StringComparison.Ordinal)
            || id.Contains("bytedance.feishu", StringComparison.Ordinal) || id.Contains("bytedance.lark", StringComparison.Ordinal);
    }

    private static bool IsDingTalk(string id, string tail, string name)
    {
        if (DingTalkNames.Contains(name)) return true;
        if (tail is "dingtalk.exe" or "dingtalklauncher.exe") return true;
        return id.Contains("dingtalk", StringComparison.Ordinal) || id.Contains("alibaba.dingtalk", StringComparison.Ordinal);
    }

    private static bool IsMail(string id, string tail, string name)
    {
        if (MailNames.Contains(name)) return true;
        if (tail is "outlook.exe" or "hxoutlook.exe" or "olk.exe" or "thunderbird.exe" or "foxmail.exe") return true;
        return id.Contains("outlookforwindows", StringComparison.Ordinal) || id.Contains("windowscommunicationsapps", StringComparison.Ordinal)
            || id.Contains("windowslive.mail", StringComparison.Ordinal) || id.Contains("office.outlook", StringComparison.Ordinal)
            || id.Contains("microsoft.outlook", StringComparison.Ordinal);
    }
}

// Turns a notification-center snapshot into the toasts that should appear. The first successful snapshot is only
// a baseline, so opening BrimDeck does not replay what is already in Action Center. An empty read does not forget
// that baseline: a failed or briefly empty list must not show the same toasts again.
public sealed class MessageNoticeFeed
{
    public const int Capacity = 256;
    private bool _primed;
    private readonly Queue<string> _order = new();
    private readonly HashSet<string> _seen = new(StringComparer.Ordinal);

    public void Reset() { _primed = false; _order.Clear(); _seen.Clear(); }

    public IReadOnlyList<MessageNotice> Observe(IReadOnlyList<ToastSnapshot> current, Func<MessageApp, bool> enabled)
    {
        if (!_primed)
        {
            foreach (var item in current) Remember(item.Key);
            _primed = true;
            return [];
        }
        if (current.Count == 0) return [];
        var found = new List<MessageNotice>();
        foreach (var item in current)
        {
            bool known = _seen.Contains(item.Key);
            Remember(item.Key);
            if (known) continue;
            if (MessageNotices.Classify(item.AppId, item.DisplayName) is not { } app || !enabled(app)) continue;
            found.Add(new MessageNotice(app, item.Title, item.Body));
        }
        return found;
    }

    private void Remember(string key)
    {
        if (!_seen.Add(key)) return;
        _order.Enqueue(key);
        while (_order.Count > Capacity && _order.TryDequeue(out var oldest)) _seen.Remove(oldest);
    }
}
