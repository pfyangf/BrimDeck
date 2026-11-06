using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using BrimDeck.Core;

namespace BrimDeck;

public partial class MainWindow
{
    internal bool IsPomodoroPage => CurrentPage == DeckPage.Pomodoro;
    private string _pomodoroForm = "";
    private TextBlock? _pomodoroTime;
    private DispatcherTimer? _pomodoroUiTimer;

    private void WatchPomodoro()
    {
        if (_app.Pomodoro is { } service)
            service.Changed += () => Dispatcher.BeginInvoke(() =>
            {
                if (IsPomodoroPage && _expanded) { _pomodoroForm = ""; RenderPomodoro(); }
                else EnsurePomodoroUiTimer();
            });
        _pomodoroUiTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _pomodoroUiTimer.Tick += (_, _) =>
        {
            if (!IsPomodoroPage || !_expanded) { _pomodoroUiTimer!.Stop(); return; }
            PaintPomodoroLive();
        };
        EnsurePomodoroUiTimer();
    }

    private void RenderPomodoro()
    {
        if (PomodoroContent is null) return;
        var clock = _app.Pomodoro?.Clock ?? new PomodoroClock();
        // Seconds stay out of the form key: the countdown paints in place so expand/clock ticks do not rebuild the tree.
        string form = $"{ContentCanvas.Width:0}|{ContentCanvas.Height:0}|{clock.Phase}|{clock.Running}|{Settings.PomodoroFocusMinutes}|{Settings.PomodoroBreakMinutes}|{Settings.PomodoroLongBreakMinutes}|{Settings.PomodoroLongEvery}|{clock.CompletedFocus}|{PanelTheme.Generation}|{Loc.Language}";
        if (form == _pomodoroForm && PomodoroContent.Children.Count > 0)
        {
            ShowOnlyPage(PomodoroContent);
            PaintPomodoroLive();
            EnsurePomodoroUiTimer();
            return;
        }
        RefreshStatus();
        PrepareUtilityPage();
        ShowOnlyPage(PomodoroContent);
        _pomodoroForm = form;
        PomodoroContent.Children.Clear();
        _pomodoroTime = null;
        var root = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        root.Children.Add(UI.Centered(PomodoroClock.PhaseName(clock.Phase), 13, UI.Secondary, FontWeights.SemiBold));
        _pomodoroTime = UI.Centered(PomodoroTimeText(clock), 42, UI.Primary, FontWeights.SemiBold);
        _pomodoroTime.Margin = new Thickness(0, 8, 0, 12);
        root.Children.Add(_pomodoroTime);
        root.Children.Add(UI.Centered(Loc.T($"已完成 {clock.CompletedFocus} 个专注", $"{clock.CompletedFocus} focus sessions done"), 11.5, UI.Tertiary));
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 14, 0, 0) };
        if (clock.Phase == PomodoroPhase.Idle)
            actions.Children.Add(DeckChip(Loc.T("开始专注", "Start focus"), true, () => { _app.Pomodoro?.Clock.StartFocus(Settings, DateTimeOffset.Now); _app.Pomodoro?.Notify(); }));
        else if (clock.Running)
            actions.Children.Add(DeckChip(Loc.T("暂停", "Pause"), true, () => { clock.Pause(DateTimeOffset.Now); _app.Pomodoro?.Notify(); }));
        else
            actions.Children.Add(DeckChip(Loc.T("继续", "Resume"), true, () => { clock.Resume(DateTimeOffset.Now); _app.Pomodoro?.Notify(); }));
        actions.Children.Add(DeckChip(Loc.T("重置", "Reset"), false, () => { clock.Reset(); _app.Pomodoro?.Notify(); }));
        actions.Children.Add(DeckChip(Loc.T("时长", "Durations"), false, EditPomodoroDurations));
        root.Children.Add(actions);
        PomodoroContent.Children.Add(root);
        EnsurePomodoroUiTimer();
    }

    private void PaintPomodoroLive()
    {
        if (_pomodoroTime is null) return;
        var clock = _app.Pomodoro?.Clock ?? new PomodoroClock();
        string next = PomodoroTimeText(clock);
        if (_pomodoroTime.Text != next) _pomodoroTime.Text = next;
    }

    private string PomodoroTimeText(PomodoroClock clock)
    {
        if (clock.Phase == PomodoroPhase.Idle)
            return $"{Settings.PomodoroFocusMinutes:00}:00";
        var left = clock.Remaining(DateTimeOffset.Now);
        return $"{(int)left.TotalMinutes:00}:{left.Seconds:00}";
    }

    private void EnsurePomodoroUiTimer()
    {
        if (_pomodoroUiTimer is null) return;
        bool need = IsPomodoroPage && _expanded && _app.Pomodoro?.Clock.Running == true;
        if (need) { if (!_pomodoroUiTimer.IsEnabled) _pomodoroUiTimer.Start(); }
        else _pomodoroUiTimer.Stop();
    }

    private void EditPomodoroDurations()
    {
        var dialog = new Window
        {
            Title = Loc.T("番茄钟时长", "Pomodoro durations"),
            Width = 340, Height = 260, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this, ResizeMode = ResizeMode.NoResize, Background = UI.Brush(PanelTheme.Hex("surface"))
        };
        var root = new StackPanel { Margin = new Thickness(16) };
        TextBox Field(string label, int value)
        {
            root.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 4) });
            var box = new TextBox { Text = value.ToString(), Margin = new Thickness(0, 0, 0, 8) };
            root.Children.Add(box);
            return box;
        }
        var focus = Field(Loc.T("专注（分钟）", "Focus (minutes)"), Settings.PomodoroFocusMinutes);
        var brief = Field(Loc.T("短休息", "Short break"), Settings.PomodoroBreakMinutes);
        var longBreak = Field(Loc.T("长休息", "Long break"), Settings.PomodoroLongBreakMinutes);
        var every = Field(Loc.T("每 N 次专注后长休息（0=关闭）", "Long break every N focus (0=off)"), Settings.PomodoroLongEvery);
        var save = new Button { Content = Loc.T("保存", "Save"), Padding = new Thickness(12, 6, 12, 6) };
        save.Click += (_, _) =>
        {
            if (!int.TryParse(focus.Text, out int f) || !int.TryParse(brief.Text, out int b)
                || !int.TryParse(longBreak.Text, out int l) || !int.TryParse(every.Text, out int e)) return;
            var next = Settings.Copy();
            next.PomodoroFocusMinutes = f;
            next.PomodoroBreakMinutes = b;
            next.PomodoroLongBreakMinutes = l;
            next.PomodoroLongEvery = e;
            _app.UpdateSettings(next);
            dialog.DialogResult = true;
        };
        root.Children.Add(save);
        dialog.Content = root;
        dialog.ShowDialog();
        _pomodoroForm = "";
        RenderPomodoro();
    }

    private void PomodoroPage_Click(object sender, RoutedEventArgs e) => SelectPage(DeckPage.Pomodoro);
}
