using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using BrimDeck.Core;

namespace BrimDeck;

public partial class MainWindow
{
    internal bool IsShortcutsPage => CurrentPage == DeckPage.Shortcuts;
    private string _shortcutsForm = "";
    private List<ShortcutItem> _shortcuts = [];

    private void WatchShortcuts()
    {
        void Reload()
        {
            _shortcuts = [.. _app.Shortcuts.Load()];
            Dispatcher.BeginInvoke(() => { if (IsShortcutsPage && _expanded) { _shortcutsForm = ""; RenderShortcuts(); } });
        }
        _app.Shortcuts.Changed += Reload;
        _shortcuts = [.. _app.Shortcuts.Load()];
    }

    private void RenderShortcuts()
    {
        if (ShortcutsContent is null) return;
        string form = $"{ContentCanvas.Width:0}|{ContentCanvas.Height:0}|{ShortcutItems.Export(_shortcuts)}|{PanelTheme.Generation}|{Loc.Language}";
        if (form == _shortcutsForm && ShortcutsContent.Children.Count > 0)
        {
            ShowOnlyPage(ShortcutsContent);
            return;
        }
        RefreshStatus();
        PrepareUtilityPage();
        ShowOnlyPage(ShortcutsContent);
        _shortcutsForm = form;
        ShortcutsContent.Children.Clear();
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 0, 4, 6) };
        head.Children.Add(DeckChip(Loc.T("添加快捷方式", "Add shortcut"), true, () => EditShortcut(new ShortcutItem())));
        root.Children.Add(head);
        if (_shortcuts.Count == 0)
        {
            var empty = DeckEmpty(Loc.T("添加 .exe / .lnk 或 shell URI，点击即可打开。", "Add an .exe / .lnk or shell URI, then click to launch."));
            Grid.SetRow(empty, 1);
            root.Children.Add(empty);
        }
        else
        {
            var list = new StackPanel();
            foreach (var shortcut in _shortcuts)
            {
                var item = shortcut;
                var row = new Border
                {
                    Background = UI.Brush(PanelTheme.Hex("fill")),
                    CornerRadius = new CornerRadius(10),
                    Margin = new Thickness(0, 0, 0, 6),
                    Padding = new Thickness(12, 8, 12, 8)
                };
                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var left = new StackPanel { Cursor = System.Windows.Input.Cursors.Hand };
                string name = string.IsNullOrWhiteSpace(item.Name) ? item.Target : item.Name;
                left.Children.Add(UI.Centered(name, 13, UI.Primary, FontWeights.SemiBold));
                left.Children.Add(UI.Line(item.Target, 11, UI.Tertiary));
                left.MouseLeftButtonUp += (_, e) => { LaunchShortcut(item); e.Handled = true; };
                grid.Children.Add(left);
                var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                actions.Children.Add(DeckChip(Loc.T("打开", "Open"), true, () => LaunchShortcut(item)));
                actions.Children.Add(DeckChip(Loc.T("编辑", "Edit"), false, () => EditShortcut(item)));
                actions.Children.Add(DeckChip(Loc.T("删除", "Delete"), false, () =>
                {
                    _shortcuts.RemoveAll(s => s.Id == item.Id);
                    _app.Shortcuts.Save(_shortcuts);
                    _shortcutsForm = "";
                    RenderShortcuts();
                }));
                Grid.SetColumn(actions, 1);
                grid.Children.Add(actions);
                row.Child = grid;
                list.Children.Add(row);
            }
            var scroll = DeckScroll(list);
            Grid.SetRow(scroll, 1);
            root.Children.Add(scroll);
        }
        ShortcutsContent.Children.Add(root);
    }

    private static void LaunchShortcut(ShortcutItem item)
    {
        if (!ShortcutItems.IsSafeTarget(item.Target)) return;
        try { Process.Start(new ProcessStartInfo(item.Target) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or System.IO.FileNotFoundException) { }
    }

    private void EditShortcut(ShortcutItem item)
    {
        bool isNew = _shortcuts.All(s => s.Id != item.Id);
        var dialog = new Window
        {
            Title = Loc.T("快捷方式", "Shortcut"),
            Width = 420, Height = 220, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this, ResizeMode = ResizeMode.NoResize, Background = UI.Brush(PanelTheme.Hex("surface"))
        };
        var root = new StackPanel { Margin = new Thickness(16) };
        var name = new TextBox { Text = item.Name, Margin = new Thickness(0, 0, 0, 8) };
        var target = new TextBox { Text = item.Target, Margin = new Thickness(0, 0, 0, 8) };
        var save = new Button { Content = Loc.T("保存", "Save"), Padding = new Thickness(12, 6, 12, 6) };
        save.Click += (_, _) =>
        {
            string path = target.Text.Trim();
            if (!ShortcutItems.IsSafeTarget(path)) return;
            item.Name = name.Text.Trim();
            item.Target = path;
            if (isNew) _shortcuts.Add(item);
            _app.Shortcuts.Save(_shortcuts);
            dialog.DialogResult = true;
        };
        root.Children.Add(new TextBlock { Text = Loc.T("名称", "Name"), Margin = new Thickness(0, 0, 0, 4) });
        root.Children.Add(name);
        root.Children.Add(new TextBlock { Text = Loc.T("路径或 URI", "Path or URI"), Margin = new Thickness(0, 0, 0, 4) });
        root.Children.Add(target);
        root.Children.Add(save);
        dialog.Content = root;
        dialog.ShowDialog();
        _shortcutsForm = "";
        RenderShortcuts();
    }

    private void ShortcutsPage_Click(object sender, RoutedEventArgs e) => SelectPage(DeckPage.Shortcuts);
}
