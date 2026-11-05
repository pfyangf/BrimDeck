using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using BrimDeck.Core;

namespace BrimDeck;

// "验证 › 邮箱验证码": Outlook / Windows Mail toast listening for verification codes (EmailOtp).
public sealed partial class SettingsWindow
{
    internal const int MailOtpSettingsPage = 12;

    private void MailOtpSettings()
    {
        var intro = TextLine(Loc.T("从 Outlook、Windows 邮件等通知中识别验证码，并在展开面板的邮箱验证码页显示。不登录邮箱，也不上传邮件内容。",
            "Reads verification codes from Outlook, Windows Mail and similar notifications, and shows them on the expanded email-codes page. It does not sign in to a mailbox or upload mail."), 12.5, TextSecondary);
        intro.TextWrapping = TextWrapping.Wrap; intro.Margin = new Thickness(0, 0, 0, 18);
        _content.Children.Add(intro);

        SectionTitle(Loc.T("绑定邮箱", "Bind mailbox"), help: () => Loc.T("仅扫描带「验证码 / code / OTP」字样的邮件通知。Gmail 等其它客户端若以系统邮件通知发出，也会被识别。",
            "Only scans mail notifications that look like a verification code / OTP. Other clients such as Gmail are included when they raise a system mail notification."));
        Group(
            Toggle(Loc.T("Outlook / 邮件通知", "Outlook / Mail notifications"), S.MailOtpOutlook, value => Change(s => s.MailOtpOutlook = value),
                help: Loc.T("打开后，即使消息横幅关闭，也会读取邮件通知以提取验证码。需要 Windows 通知访问权限。",
                    "When on, mail notifications are still read for codes even if message banners are off. Windows notification access is required.")),
            TextLine(Loc.T("仅扫「验证码 / code / OTP」类邮件", "Only verification / code / OTP mails"), 12, TextTertiary));

        SectionTitle(Loc.T("通知访问", "Notification access"));
        var access = TextLine(_app.Toasts?.Status ?? Loc.T("通知监听尚未启动。", "Notification listening has not started."), 12, TextSecondary);
        access.TextWrapping = TextWrapping.Wrap; access.Margin = new Thickness(0, 0, 0, 10);
        var check = ActionButton(Loc.T("检查通知访问", "Check notification access"),
            () => _app.Toasts?.RequestAccess(() => { if (_page == MailOtpSettingsPage) ShowPage(MailOtpSettingsPage); }));
        check.Padding = new Thickness(12, 6, 12, 6);
        Group(access, check);

        SectionTitle(Loc.T("展开面板", "Expanded panel"));
        Group(Toggle(Loc.T("显示邮箱验证码页", "Show email codes page"), S.MailOtpPage, value =>
        {
            Change(s => s.MailOtpPage = value);
            // Mirror the Pages list so turning it on here also shows the size row's page.
        }, help: Loc.T("也可在「展开外观 › 显示的页面」中开关并拖动排序。",
            "You can also turn this on or off and reorder it under Expanded view › Pages shown.")));
    }

    private void MailOtpSizeControls()
    {
        var dimensions = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        dimensions.ColumnDefinitions.Add(new ColumnDefinition());
        dimensions.ColumnDefinitions.Add(new ColumnDefinition());
        var width = Dimension(Loc.T("宽度", "Width"), S.MailOtpWidth, 440, 1200, value => Resize(s => s.MailOtpWidth = value, DeckPage.Mail));
        width.Margin = new Thickness(0, 0, 20, 0);
        var height = Dimension(Loc.T("高度", "Height"), S.MailOtpHeight, 140, 400, value => Resize(s => s.MailOtpHeight = value, DeckPage.Mail));
        height.Margin = new Thickness(20, 0, 0, 0);
        Grid.SetColumn(height, 1);
        dimensions.Children.Add(width);
        dimensions.Children.Add(height);
        Target.Children.Add(dimensions);
    }
}
