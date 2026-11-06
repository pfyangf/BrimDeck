using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BrimDeck.Core;

namespace BrimDeck;

public partial class MainWindow
{
    internal bool IsMessagesPage => CurrentPage == DeckPage.Messages;
    private string _messagesForm = "";

    private void WatchMessages()
    {
        _app.Messages.Changed += () => Dispatcher.BeginInvoke(() => { if (IsMessagesPage && _expanded) RenderMessages(); });
    }

    private void RenderMessages()
    {
        if (MessagesContent is null) return;
        RefreshStatus();
        PrepareUtilityPage();
        ShowOnlyPage(MessagesContent);
        var items = _app.Messages.Snapshot().Where(item => MessageNotices.Allows(Settings, item.App)).ToList();
        string form = $"{ContentCanvas.Width:0}|{ContentCanvas.Height:0}|{items.Count}|{PanelTheme.Generation}|{Loc.Language}|{Settings.NoticeQq}|{Settings.NoticeWeChat}|{Settings.NoticeMail}|{Settings.NoticeLark}|{Settings.NoticeDingTalk}|{Settings.NoticeWeCom}";
        if (form == _messagesForm && MessagesContent.Children.Count > 0) return;
        _messagesForm = form;
        MessagesContent.Children.Clear();
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var head = new DockPanel { Margin = new Thickness(4, 0, 4, 6) };
        head.Children.Add(DeckChip(Loc.T("清空", "Clear"), primary: false, () => { _app.Messages.Clear(); _messagesForm = ""; RenderMessages(); }));
        var title = UI.Centered(Loc.T("最近消息", "Recent messages"), 13, UI.Secondary, FontWeights.SemiBold);
        title.HorizontalAlignment = HorizontalAlignment.Left;
        title.Margin = new Thickness(4, 0, 0, 0);
        head.Children.Add(title);
        root.Children.Add(head);
        if (items.Count == 0)
        {
            var empty = DeckEmpty(Loc.T(
                "这里只展示 Windows 通知中心里的横幅（微信 / QQ / 飞书 / 钉钉 / 企微 / 邮件）。需要在设置里打开消息提示，并允许 BrimDeck 访问通知。",
                "Only Windows notification banners (WeChat / QQ / Lark / DingTalk / WeCom / mail) appear here. Turn on message notices in settings and allow BrimDeck notification access."));
            Grid.SetRow(empty, 1);
            root.Children.Add(empty);
        }
        else
        {
            var list = new StackPanel();
            foreach (var item in items.Take(40))
            {
                var row = new Border
                {
                    Background = UI.Brush(PanelTheme.Hex("fill")),
                    CornerRadius = new CornerRadius(10),
                    Margin = new Thickness(0, 0, 0, 6),
                    Padding = new Thickness(12, 8, 12, 8)
                };
                var col = new StackPanel();
                var top = new DockPanel();
                var when = UI.Centered(item.Created.ToLocalTime().ToString("HH:mm", CultureInfo.CurrentCulture), 11, UI.Tertiary);
                when.HorizontalAlignment = HorizontalAlignment.Right;
                DockPanel.SetDock(when, System.Windows.Controls.Dock.Right);
                top.Children.Add(when);
                top.Children.Add(UI.Centered(MessageNotices.AppName(item.App), 12, UI.Accent, FontWeights.SemiBold));
                col.Children.Add(top);
                string preview = MessageNotices.Preview(item.Title, item.Body);
                var body = UI.Line(preview, 12.5, UI.Primary);
                body.Margin = new Thickness(0, 4, 0, 0);
                col.Children.Add(body);
                row.Child = col;
                list.Children.Add(row);
            }
            var scroll = DeckScroll(list);
            Grid.SetRow(scroll, 1);
            root.Children.Add(scroll);
        }
        MessagesContent.Children.Add(root);
    }

    private void MessagesPage_Click(object sender, RoutedEventArgs e) => SelectPage(DeckPage.Messages);
}
