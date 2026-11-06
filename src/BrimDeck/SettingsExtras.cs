using System.Windows;
using System.Windows.Controls;
using BrimDeck.Core;

namespace BrimDeck;

public partial class SettingsWindow
{
    private void PageSizeControls(DeckPage page, Func<DeckSettings, double> widthOf, Action<DeckSettings, double> setWidth,
        Func<DeckSettings, double> heightOf, Action<DeckSettings, double> setHeight)
    {
        var dimensions = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        dimensions.ColumnDefinitions.Add(new ColumnDefinition());
        dimensions.ColumnDefinitions.Add(new ColumnDefinition());
        var width = Dimension(Loc.T("宽度", "Width"), widthOf(S), 440, 1200, value => Resize(s => setWidth(s, value), page));
        width.Margin = new Thickness(0, 0, 20, 0);
        var height = Dimension(Loc.T("高度", "Height"), heightOf(S), 140, 400, value => Resize(s => setHeight(s, value), page));
        height.Margin = new Thickness(20, 0, 0, 0);
        Grid.SetColumn(height, 1);
        dimensions.Children.Add(width);
        dimensions.Children.Add(height);
        Target.Children.Add(dimensions);
    }

    private void MessagesSizeControls() => PageSizeControls(DeckPage.Messages, s => s.MessagesWidth, (s, v) => s.MessagesWidth = v, s => s.MessagesHeight, (s, v) => s.MessagesHeight = v);
    private void AlarmSizeControls() => PageSizeControls(DeckPage.Alarm, s => s.AlarmWidth, (s, v) => s.AlarmWidth = v, s => s.AlarmHeight, (s, v) => s.AlarmHeight = v);
    private void PomodoroSizeControls() => PageSizeControls(DeckPage.Pomodoro, s => s.PomodoroWidth, (s, v) => s.PomodoroWidth = v, s => s.PomodoroHeight, (s, v) => s.PomodoroHeight = v);
    private void MemoSizeControls() => PageSizeControls(DeckPage.Memo, s => s.MemoWidth, (s, v) => s.MemoWidth = v, s => s.MemoHeight, (s, v) => s.MemoHeight = v);
    private void ShortcutsSizeControls() => PageSizeControls(DeckPage.Shortcuts, s => s.ShortcutsWidth, (s, v) => s.ShortcutsWidth = v, s => s.ShortcutsHeight, (s, v) => s.ShortcutsHeight = v);
    private void ToolsSizeControls() => PageSizeControls(DeckPage.Tools, s => s.ToolsWidth, (s, v) => s.ToolsWidth = v, s => s.ToolsHeight, (s, v) => s.ToolsHeight = v);
}
