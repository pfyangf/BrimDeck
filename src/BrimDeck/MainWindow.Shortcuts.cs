using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using BrimDeck.Core;
using Microsoft.Win32;

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
        head.Children.Add(DeckChip(Loc.T("添加快捷方式", "Add shortcut"), true, AddShortcut));
        root.Children.Add(head);
        if (_shortcuts.Count == 0)
        {
            var empty = DeckEmpty(Loc.T("点「添加快捷方式」选择 .exe / .lnk，或手动填写 shell URI。", "Tap Add shortcut to pick an .exe / .lnk, or type a shell URI."));
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
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or FileNotFoundException) { }
    }

    // Opens a file picker first so “Add shortcut” always shows a selection window (exe / lnk / …).
    private void AddShortcut()
    {
        string? path = PickShortcutTarget();
        if (path is null) return;
        var item = new ShortcutItem
        {
            Name = Path.GetFileNameWithoutExtension(path),
            Target = path
        };
        EditShortcut(item, picked: true);
    }

    private string? PickShortcutTarget()
    {
        var dialog = new OpenFileDialog
        {
            Title = Loc.T("选择快捷方式", "Choose a shortcut"),
            Filter = Loc.T(
                "程序与快捷方式 (*.exe;*.lnk;*.bat;*.cmd;*.url)|*.exe;*.lnk;*.bat;*.cmd;*.url|所有文件 (*.*)|*.*",
                "Programs and shortcuts (*.exe;*.lnk;*.bat;*.cmd;*.url)|*.exe;*.lnk;*.bat;*.cmd;*.url|All files (*.*)|*.*"),
            CheckFileExists = true,
            Multiselect = false
        };
        return dialog.ShowDialog(this) == true ? dialog.FileName : null;
    }

    private void EditShortcut(ShortcutItem item, bool picked = false)
    {
        bool isNew = _shortcuts.All(s => s.Id != item.Id);
        var dialog = new Window
        {
            Title = Loc.T("快捷方式", "Shortcut"),
            Width = 460, Height = 250, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this, ResizeMode = ResizeMode.NoResize, Background = UI.Brush(PanelTheme.Hex("surface"))
        };
        var root = new StackPanel { Margin = new Thickness(16) };
        var name = new TextBox { Text = item.Name, Margin = new Thickness(0, 0, 0, 8) };
        var target = new TextBox { Text = item.Target, Margin = new Thickness(0, 0, 8, 0) };
        var browse = new Button { Content = Loc.T("浏览…", "Browse…"), Padding = new Thickness(12, 6, 12, 6), MinWidth = 72 };
        browse.Click += (_, _) =>
        {
            string? path = PickShortcutTarget();
            if (path is null) return;
            target.Text = path;
            if (string.IsNullOrWhiteSpace(name.Text) || picked || isNew)
                name.Text = Path.GetFileNameWithoutExtension(path);
        };
        var targetRow = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        targetRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        targetRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(browse, 1);
        targetRow.Children.Add(target);
        targetRow.Children.Add(browse);
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
        root.Children.Add(targetRow);
        root.Children.Add(save);
        dialog.Content = root;
        dialog.ShowDialog();
        _shortcutsForm = "";
        RenderShortcuts();
    }

    private void ShortcutsPage_Click(object sender, RoutedEventArgs e) => SelectPage(DeckPage.Shortcuts);
}
