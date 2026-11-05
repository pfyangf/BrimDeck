using BrimDeck.Core;

internal static class MessageNoticeTests
{
    public static void Run(string root, Action<string, bool> check)
    {
        check("QQ's own executable is QQ", MessageNotices.Classify(@"C:\Program Files\Tencent\QQNT\QQ.exe", "QQ") == MessageApp.Qq);
        check("a packaged-style QQ id is QQ", MessageNotices.Classify(@"{6D809377-6AF0-444B-8957-A3773F02200E}\QQ.exe", "QQ") == MessageApp.Qq);
        check("QQ's short app id is QQ", MessageNotices.Classify("QQ", "QQ") == MessageApp.Qq);
        check("Tencent.QQ.QQ is QQ", MessageNotices.Classify("Tencent.QQ.QQ", "") == MessageApp.Qq);
        check("QQ Music is not a QQ message", MessageNotices.Classify(@"C:\Program Files\Tencent\QQMusic\QQMusic.exe", "QQ 音乐") is null);
        check("QQ Music's display name is not a QQ message", MessageNotices.Classify("QQ", "QQ Music") is null);
        check("QQ Browser is not a QQ message", MessageNotices.Classify("QQBrowser.exe", "QQ浏览器") is null);
        check("WeChat is WeChat", MessageNotices.Classify(@"D:\WeChat\WeChat.exe", "微信") == MessageApp.WeChat);
        check("Weixin.exe is WeChat", MessageNotices.Classify("Weixin.exe", "微信") == MessageApp.WeChat);
        check("the WeChat display name is enough", MessageNotices.Classify("", "微信") == MessageApp.WeChat);
        check("a WeChat helper without that name is ignored", MessageNotices.Classify("WeChatAppEx.exe", "WeChatAppEx") is null);
        check("Outlook is mail", MessageNotices.Classify(@"C:\Program Files\Microsoft Office\root\Office16\OUTLOOK.EXE", "Outlook") == MessageApp.Mail);
        check("new Outlook's package is mail", MessageNotices.Classify("Microsoft.OutlookForWindows_8wekyb3d8bbwe!Microsoft.OutlookforWindows", "Outlook") == MessageApp.Mail);
        check("Windows Mail's package is mail", MessageNotices.Classify("microsoft.windowscommunicationsapps_8wekyb3d8bbwe!microsoft.windowslive.mail", "邮件") == MessageApp.Mail);
        check("Thunderbird is mail", MessageNotices.Classify("thunderbird.exe", "Thunderbird") == MessageApp.Mail);
        check("Foxmail is mail", MessageNotices.Classify("Foxmail.exe", "Foxmail") == MessageApp.Mail);
        check("the Mail display name is mail", MessageNotices.Classify("Mail", "邮件") == MessageApp.Mail);
        check("Chrome is not mail, even with a Gmail title", MessageNotices.Classify("chrome.exe", "Google Chrome") is null);
        check("an empty toast is not a message", MessageNotices.Classify("", "  ") is null);

        string preview = MessageNotices.Preview("  Alice\n", "hello\r\nthere  ");
        check("a banner preview is one line", preview == "Alice · hello there");
        check("a repeated banner line is not doubled", MessageNotices.Preview("Alice", "Alice") == "Alice");

        var previous = Loc.Language;
        try
        {
            Loc.Use(Loc.English);
            check("english names the apps in english", MessageNotices.AppName(MessageApp.WeChat) == "WeChat" && MessageNotices.AppName(MessageApp.Mail) == "Mail" && MessageNotices.AppName(MessageApp.Qq) == "QQ");
            Loc.Use(Loc.Chinese);
            check("chinese names the apps in simplified chinese", MessageNotices.AppName(MessageApp.WeChat) == "微信" && MessageNotices.AppName(MessageApp.Mail) == "邮件");
        }
        finally { Loc.Use(previous); }

        var settings = new DeckSettings { MessageNotices = true, NoticeQq = true, NoticeWeChat = false, NoticeMail = true };
        check("each app can be left out", MessageNotices.Allows(settings, MessageApp.Qq) && !MessageNotices.Allows(settings, MessageApp.WeChat) && MessageNotices.Allows(settings, MessageApp.Mail));
        settings.MessageNotices = false;
        check("the master switch stops every app", !MessageNotices.AnyEnabled(settings) && !MessageNotices.Allows(settings, MessageApp.Mail));

        var when = new DateTimeOffset(2026, 11, 5, 8, 0, 0, TimeSpan.Zero);
        ToastSnapshot Toast(string id, string name, string title, string body, int minute, string note = "1")
            => new(MessageNotices.Key(id, note, when.AddMinutes(minute), title, body), id, name, title, body, when.AddMinutes(minute));
        var qq = Toast("QQ.exe", "QQ", "Bob", "在吗", 1);
        var music = Toast("QQMusic.exe", "QQ 音乐", "歌曲", "正在播放", 2);
        var feed = new MessageNoticeFeed();
        check("the first snapshot is only a baseline", feed.Observe([qq, music], app => app == MessageApp.Qq).Count == 0);
        var again = feed.Observe([qq, music], app => app == MessageApp.Qq);
        check("a toast already in the center is not shown again", again.Count == 0);
        var next = Toast("QQ.exe", "QQ", "Bob", "到了", 3, "2");
        var arrived = feed.Observe([qq, music, next], app => app == MessageApp.Qq);
        check("a new QQ toast is shown and QQ Music is not", arrived.Count == 1 && arrived[0].App == MessageApp.Qq && arrived[0].Body == "到了");
        check("an empty read does not replay the center", feed.Observe([], app => true).Count == 0);
        check("the same toasts stay quiet after an empty read", feed.Observe([qq, music, next], app => true).Count == 0);
        var hidden = Toast("Weixin.exe", "微信", "群", "新消息", 4, "9");
        check("a disabled app is remembered but not shown", feed.Observe([qq, hidden], app => app == MessageApp.Qq).Count == 0);
        feed.Reset();
        check("turning sources on starts from a new baseline", feed.Observe([qq, hidden], app => true).Count == 0);
        var later = Toast("Weixin.exe", "微信", "群", "下一条", 5, "10");
        var shown = feed.Observe([qq, hidden, later], app => true);
        check("after the new baseline only a later toast is shown", shown.Count == 1 && shown[0].App == MessageApp.WeChat && shown[0].Title == "群");

        var folder = Path.Combine(root, "message-notices");
        var store = new SettingsStore(folder, "1.2.4");
        Directory.CreateDirectory(store.DirectoryPath);
        File.WriteAllText(store.FilePath, """{"AppVersion":"1.2.4","Language":"zh-CN"}""");
        var loaded = store.Load();
        check("older settings keep message notices on", loaded.MessageNotices && loaded.NoticeQq && loaded.NoticeWeChat && loaded.NoticeMail && loaded.Language == Loc.Chinese);
        File.WriteAllText(store.FilePath, """{"AppVersion":"1.2.4","MessageNotices":false,"NoticeQq":false,"NoticeWeChat":true,"NoticeMail":false}""");
        var saved = new SettingsStore(folder, "1.2.4").Load();
        check("message notice switches load as they were saved", !saved.MessageNotices && !saved.NoticeQq && saved.NoticeWeChat && !saved.NoticeMail);
    }
}
