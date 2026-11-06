using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using BrimDeck.Core;

namespace BrimDeck;

public partial class MainWindow
{
    private FrameworkElement[] PageHosts() =>
    [
        UsageContent, MusicContent, MonitorContent, OtpContent, MailContent,
        MessagesContent, AlarmContent, PomodoroContent, MemoContent, ShortcutsContent, ToolsContent
    ];

    private void ShowOnlyPage(FrameworkElement shown)
    {
        foreach (var host in PageHosts())
            host.Visibility = ReferenceEquals(host, shown) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void PrepareUtilityPage()
    {
        ModelToolbar.Visibility = Visibility.Collapsed;
        ApplicationPages.Visibility = Visibility.Collapsed;
        ClockLabel.Visibility = Visibility.Visible;
        ApplyHeader(PanelHeader.For(36, 20));
    }

    private void StopUtilityPageTimers()
    {
        _otpTimer?.Stop();
        _mailTimer?.Stop();
        _pomodoroUiTimer?.Stop();
    }


    private Border DeckChip(string title, bool primary, Action click, string? tag = null)
    {
        var label = UI.Centered(title, 12.5, primary ? "#041018" : UI.Primary, FontWeights.SemiBold);
        label.Margin = new Thickness(12, 8, 12, 8);
        var border = new Border
        {
            Background = UI.Brush(primary ? UI.Accent : PanelTheme.Hex("fill")),
            CornerRadius = new CornerRadius(10),
            Cursor = Cursors.Hand,
            Margin = new Thickness(0, 0, 8, 0),
            Child = label,
            Tag = tag
        };
        border.PreviewMouseLeftButtonDown += (_, e) => e.Handled = true;
        border.MouseLeftButtonUp += (_, e) => { click(); e.Handled = true; };
        AutomationProperties.SetName(border, title);
        return border;
    }

    private static ScrollViewer DeckScroll(UIElement child) => new()
    {
        Content = child,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        Background = Brushes.Transparent,
        Padding = new Thickness(2, 0, 2, 0)
    };

    private TextBlock DeckEmpty(string text)
    {
        var block = UI.Centered(text, 13, UI.Tertiary);
        block.TextWrapping = TextWrapping.Wrap;
        block.TextAlignment = TextAlignment.Center;
        block.Margin = new Thickness(24, 20, 24, 20);
        block.HorizontalAlignment = HorizontalAlignment.Center;
        block.VerticalAlignment = VerticalAlignment.Center;
        return block;
    }
}
