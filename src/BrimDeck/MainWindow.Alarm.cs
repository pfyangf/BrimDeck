using System.Windows;
using System.Windows.Controls;
using BrimDeck.Core;

namespace BrimDeck;

public partial class MainWindow
{
    internal bool IsAlarmPage => CurrentPage == DeckPage.Alarm;
    private string _alarmForm = "";
    private List<AlarmItem> _alarms = [];

    private void WatchAlarm()
    {
        void Reload()
        {
            _alarms = [.. _app.Alarms.Load()];
            Dispatcher.BeginInvoke(() => { if (IsAlarmPage && _expanded) { _alarmForm = ""; RenderAlarm(); } });
        }
        _app.Alarms.Changed += Reload;
        if (_app.AlarmClock is { } clock)
            clock.Fired += _ => Dispatcher.BeginInvoke(() => { if (IsAlarmPage && _expanded) { _alarmForm = ""; RenderAlarm(); } });
        _alarms = [.. _app.Alarms.Load()];
    }

    private void RenderAlarm()
    {
        if (AlarmContent is null) return;
        string form = $"{ContentCanvas.Width:0}|{ContentCanvas.Height:0}|{AlarmItems.Export(_alarms)}|{PanelTheme.Generation}|{Loc.Language}";
        if (form == _alarmForm && AlarmContent.Children.Count > 0)
        {
            ShowOnlyPage(AlarmContent);
            return;
        }
        RefreshStatus();
        PrepareUtilityPage();
        ShowOnlyPage(AlarmContent);
        _alarmForm = form;
        AlarmContent.Children.Clear();
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 0, 4, 6) };
        head.Children.Add(DeckChip(Loc.T("添加闹钟", "Add alarm"), primary: true, AddAlarm));
        root.Children.Add(head);
        if (_alarms.Count == 0)
        {
            var empty = DeckEmpty(Loc.T("还没有闹钟。添加后会在设定时间弹出 Windows 通知并播放提示音。", "No alarms yet. When due, a Windows toast and system sound fire locally."));
            Grid.SetRow(empty, 1);
            root.Children.Add(empty);
        }
        else
        {
            var list = new StackPanel();
            foreach (var alarm in _alarms)
            {
                var item = alarm;
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
                var left = new StackPanel();
                left.Children.Add(UI.Centered(AlarmItems.TimeLabel(item), 18, UI.Primary, FontWeights.SemiBold));
                string meta = (string.IsNullOrWhiteSpace(item.Label) ? Loc.T("闹钟", "Alarm") : item.Label) + " · " + AlarmItems.DaysLabel(item);
                left.Children.Add(UI.Centered(meta, 11.5, UI.Tertiary));
                grid.Children.Add(left);
                var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                actions.Children.Add(DeckChip(item.Enabled ? Loc.T("开", "On") : Loc.T("关", "Off"), item.Enabled, () => ToggleAlarm(item)));
                actions.Children.Add(DeckChip(Loc.T("编辑", "Edit"), false, () => EditAlarm(item)));
                actions.Children.Add(DeckChip(Loc.T("删除", "Delete"), false, () => DeleteAlarm(item)));
                Grid.SetColumn(actions, 1);
                grid.Children.Add(actions);
                row.Child = grid;
                list.Children.Add(row);
            }
            var scroll = DeckScroll(list);
            Grid.SetRow(scroll, 1);
            root.Children.Add(scroll);
        }
        AlarmContent.Children.Add(root);
    }

    private void AddAlarm() => EditAlarm(new AlarmItem { Minutes = DateTime.Now.Hour * 60 + DateTime.Now.Minute + 1 });

    private void ToggleAlarm(AlarmItem item)
    {
        item.Enabled = !item.Enabled;
        item.FiredDate = null;
        _app.Alarms.Save(_alarms);
        _alarmForm = "";
        RenderAlarm();
    }

    private void DeleteAlarm(AlarmItem item)
    {
        _alarms.RemoveAll(a => a.Id == item.Id);
        _app.Alarms.Save(_alarms);
        _alarmForm = "";
        RenderAlarm();
    }

    private void EditAlarm(AlarmItem item)
    {
        bool isNew = _alarms.All(a => a.Id != item.Id);
        var dialog = new Window
        {
            Title = Loc.T("闹钟", "Alarm"),
            Width = 360, Height = 280, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this, ResizeMode = ResizeMode.NoResize, Background = UI.Brush(PanelTheme.Hex("surface"))
        };
        var root = new StackPanel { Margin = new Thickness(16) };
        var labelBox = new TextBox { Text = item.Label, Margin = new Thickness(0, 0, 0, 8) };
        var hour = new TextBox { Text = item.Hour.ToString("00"), Width = 48, Margin = new Thickness(0, 0, 8, 0) };
        var minute = new TextBox { Text = item.Minute.ToString("00"), Width = 48 };
        var timeRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        timeRow.Children.Add(hour); timeRow.Children.Add(new TextBlock { Text = ":", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) }); timeRow.Children.Add(minute);
        var days = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        string[] names = Loc.Language == Loc.Chinese ? ["日", "一", "二", "三", "四", "五", "六"] : ["Su", "Mo", "Tu", "We", "Th", "Fr", "Sa"];
        var checks = new CheckBox[7];
        for (int i = 0; i < 7; i++)
        {
            checks[i] = new CheckBox { Content = names[i], IsChecked = item.Days == 0 || (item.Days & (1 << i)) != 0, Margin = new Thickness(0, 0, 6, 0) };
            days.Children.Add(checks[i]);
        }
        var save = new Button { Content = Loc.T("保存", "Save"), Margin = new Thickness(0, 8, 0, 0), Padding = new Thickness(12, 6, 12, 6) };
        save.Click += (_, _) =>
        {
            if (!int.TryParse(hour.Text, out int h) || !int.TryParse(minute.Text, out int m)) return;
            h = Math.Clamp(h, 0, 23); m = Math.Clamp(m, 0, 59);
            item.Label = labelBox.Text.Trim();
            item.Minutes = h * 60 + m;
            int mask = 0;
            for (int i = 0; i < 7; i++) if (checks[i].IsChecked == true) mask |= 1 << i;
            item.Days = mask == 0b1111111 ? 0 : mask;
            item.FiredDate = null;
            item.Enabled = true;
            if (isNew) _alarms.Add(item);
            _app.Alarms.Save(_alarms);
            dialog.DialogResult = true;
        };
        root.Children.Add(new TextBlock { Text = Loc.T("名称", "Label"), Margin = new Thickness(0, 0, 0, 4) });
        root.Children.Add(labelBox);
        root.Children.Add(new TextBlock { Text = Loc.T("时间", "Time"), Margin = new Thickness(0, 0, 0, 4) });
        root.Children.Add(timeRow);
        root.Children.Add(new TextBlock { Text = Loc.T("重复（全选=每天）", "Repeat (all = every day)"), Margin = new Thickness(0, 0, 0, 4) });
        root.Children.Add(days);
        root.Children.Add(save);
        dialog.Content = root;
        dialog.ShowDialog();
        _alarmForm = "";
        RenderAlarm();
    }

    private void AlarmPage_Click(object sender, RoutedEventArgs e) => SelectPage(DeckPage.Alarm);
}
