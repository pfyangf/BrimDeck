using System.Windows;
using System.Windows.Controls;
using BrimDeck.Core;

namespace BrimDeck;

public partial class MainWindow
{
    internal bool IsMemoPage => CurrentPage == DeckPage.Memo;
    private string _memoForm = "";
    private List<MemoItem> _memos = [];

    private void WatchMemo()
    {
        _app.Memos.Changed += () => Dispatcher.BeginInvoke(() => { if (IsMemoPage && _expanded) { _memoForm = ""; RenderMemo(); } });
    }

    private void RenderMemo()
    {
        if (MemoContent is null) return;
        RefreshStatus();
        PrepareUtilityPage();
        ShowOnlyPage(MemoContent);
        _memos = [.. _app.Memos.Load()];
        string form = $"{ContentCanvas.Width:0}|{MemoItems.Export(_memos)}|{PanelTheme.Generation}|{Loc.Language}";
        if (form == _memoForm && MemoContent.Children.Count > 0) return;
        _memoForm = form;
        MemoContent.Children.Clear();
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 0, 4, 6) };
        head.Children.Add(DeckChip(Loc.T("新建备忘", "New memo"), true, () => EditMemo(new MemoItem())));
        root.Children.Add(head);
        if (_memos.Count == 0)
        {
            var empty = DeckEmpty(Loc.T("还没有备忘录。内容保存在本机，重启后仍在。", "No memos yet. Notes stay on this PC across restarts."));
            Grid.SetRow(empty, 1);
            root.Children.Add(empty);
        }
        else
        {
            var list = new StackPanel();
            foreach (var memo in _memos)
            {
                var item = memo;
                var row = new Border
                {
                    Background = UI.Brush(PanelTheme.Hex("fill")),
                    CornerRadius = new CornerRadius(10),
                    Margin = new Thickness(0, 0, 0, 6),
                    Padding = new Thickness(12, 8, 12, 8),
                    Cursor = System.Windows.Input.Cursors.Hand
                };
                var col = new StackPanel();
                string title = string.IsNullOrWhiteSpace(item.Title) ? Loc.T("未命名", "Untitled") : item.Title;
                col.Children.Add(UI.Centered(title, 13, UI.Primary, FontWeights.SemiBold));
                if (!string.IsNullOrWhiteSpace(item.Body))
                {
                    var body = UI.Line(item.Body.Replace('\n', ' '), 12, UI.Secondary);
                    body.Margin = new Thickness(0, 4, 0, 0);
                    col.Children.Add(body);
                }
                row.Child = col;
                row.MouseLeftButtonUp += (_, e) => { EditMemo(item); e.Handled = true; };
                var bar = new DockPanel();
                var del = DeckChip(Loc.T("删除", "Delete"), false, () =>
                {
                    _memos.RemoveAll(m => m.Id == item.Id);
                    _app.Memos.Save(_memos);
                    _memoForm = "";
                    RenderMemo();
                });
                DockPanel.SetDock(del, Dock.Right);
                bar.Children.Add(del);
                bar.Children.Add(row);
                list.Children.Add(bar);
            }
            var scroll = DeckScroll(list);
            Grid.SetRow(scroll, 1);
            root.Children.Add(scroll);
        }
        MemoContent.Children.Add(root);
    }

    private void EditMemo(MemoItem item)
    {
        bool isNew = _memos.All(m => m.Id != item.Id);
        var dialog = new Window
        {
            Title = Loc.T("备忘录", "Memo"),
            Width = 420, Height = 320, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this, Background = UI.Brush(PanelTheme.Hex("surface"))
        };
        var root = new DockPanel { Margin = new Thickness(16) };
        var save = new Button { Content = Loc.T("保存", "Save"), Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 8, 0, 0) };
        DockPanel.SetDock(save, Dock.Bottom);
        var title = new TextBox { Text = item.Title, Margin = new Thickness(0, 0, 0, 8) };
        var body = new TextBox { Text = item.Body, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        save.Click += (_, _) =>
        {
            item.Title = title.Text.Trim();
            item.Body = body.Text ?? "";
            item.Updated = DateTimeOffset.Now;
            if (isNew) _memos.Insert(0, item);
            _app.Memos.Save(_memos);
            dialog.DialogResult = true;
        };
        var stack = new DockPanel();
        DockPanel.SetDock(title, Dock.Top);
        stack.Children.Add(title);
        stack.Children.Add(body);
        root.Children.Add(save);
        root.Children.Add(stack);
        dialog.Content = root;
        dialog.ShowDialog();
        _memoForm = "";
        RenderMemo();
    }

    private void MemoPage_Click(object sender, RoutedEventArgs e) => SelectPage(DeckPage.Memo);
}
