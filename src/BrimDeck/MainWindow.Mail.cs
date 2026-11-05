using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using BrimDeck.Core;

namespace BrimDeck;

// Email verification-code page: one mail at a time from Outlook / Windows Mail toasts (EmailOtpFeed).
public partial class MainWindow
{
    internal bool IsMailPage => CurrentPage == DeckPage.Mail;
    private readonly List<EmailOtpMessage> _mailItems = [];
    private int _mailIndex, _mailShown = -1;
    private bool _mailPreview, _mailDragged;
    private Point _mailDragStart;
    private string _mailForm = "";
    private Guid _mailCopied;
    private DateTimeOffset _mailCopiedUntil;
    private TextBox? _mailSelect;
    private DispatcherTimer? _mailTimer;

    internal void NotifyMailOtp()
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (!IsMailPage || !_expanded) return;
            _mailForm = "";
            RenderMail();
        });
    }

    private void WatchMail()
    {
        _app.MailCodes.Changed += () => Dispatcher.BeginInvoke(() =>
        {
            if (!IsMailPage || !_expanded) return;
            _mailForm = "";
            RenderMail();
        });
        _mailTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _mailTimer.Tick += (_, _) =>
        {
            if (!IsMailPage || !_expanded) { _mailTimer!.Stop(); return; }
            if (DateTimeOffset.Now >= _mailCopiedUntil && _mailCopied != Guid.Empty)
            {
                _mailCopied = Guid.Empty;
                _mailForm = "";
                RenderMail();
                return;
            }
            // Relative times ("about 4 minutes ago") refresh occasionally.
            _mailForm = "";
            RenderMail();
        };
        if (IsMailPage && _expanded) _mailTimer.Start();
    }

    private void RenderMail()
    {
        if (MailContent is null) return;
        bool shown = MailContent.IsVisible && _expanded && !_preview;
        RefreshStatus();
        UsageContent.Visibility = Visibility.Collapsed;
        MonitorContent.Visibility = Visibility.Collapsed;
        MusicContent.Visibility = Visibility.Collapsed;
        OtpContent.Visibility = Visibility.Collapsed;
        MailContent.Visibility = Visibility.Visible;
        ModelToolbar.Visibility = Visibility.Collapsed;
        ApplicationPages.Visibility = Visibility.Collapsed;
        ClockLabel.Visibility = Visibility.Visible;
        ApplyHeader(PanelHeader.For(36, 20));
        LoadMailItems();
        double width = ContentCanvas.Width, height = ContentCanvas.Height;
        string ids = string.Join(",", _mailItems.Select(item => item.Id.ToString("N") + ":" + (item.Code ?? "") + ":" + item.Subject));
        bool copied = _mailItems.Count > 0 && _mailItems[_mailIndex].Id == _mailCopied && DateTimeOffset.Now < _mailCopiedUntil;
        string form = $"{width:0}|{height:0}|{_mailIndex}|{_mailPreview}|{ids}|{PanelTheme.Generation}|{Loc.Language}|{copied}|{_mailShown}";
        if (form == _mailForm && MailContent.Children.Count > 0) { EnsureMailTimer(); return; }
        int previous = _mailShown;
        _mailShown = _mailIndex;
        _mailForm = form;
        MailContent.Children.Clear();
        _mailSelect = null;
        var root = new Grid { Background = Brushes.Transparent };
        if (_mailItems.Count == 0) root.Children.Add(MailEmpty());
        else if (_mailPreview || string.IsNullOrEmpty(_mailItems[_mailIndex].Code))
            root.Children.Add(MailPreviewPane(_mailItems[_mailIndex]));
        else root.Children.Add(MailRecognized(_mailItems[_mailIndex]));
        root.MouseLeftButtonDown += MailPress;
        root.MouseMove += MailDrag;
        root.MouseLeftButtonUp += MailRelease;
        root.PreviewMouseWheel += MailWheel;
        MailContent.Children.Add(root);
        if (shown && previous >= 0 && previous != _mailIndex && Soft.Enabled && root.Children.Count > 0)
        {
            var shift = new TranslateTransform(0, _mailIndex > previous ? 14 : -14);
            root.RenderTransform = shift;
            shift.BeginAnimation(TranslateTransform.YProperty, Soft.Fade(shift.Y, 0, Soft.Quick, Soft.Follow));
            root.BeginAnimation(OpacityProperty, Soft.Fade(.55, 1, Soft.Quick, Soft.Follow));
        }
        EnsureMailTimer();
    }

    private void EnsureMailTimer()
    {
        if (_mailTimer is null) return;
        if (IsMailPage && _expanded) { if (!_mailTimer.IsEnabled) _mailTimer.Start(); }
        else _mailTimer.Stop();
    }

    private void LoadMailItems()
    {
        var next = _app.MailCodes.Items;
        if (!_mailItems.Select(item => item.Id).SequenceEqual(next.Select(item => item.Id)))
            _mailIndex = OtpCarousel.Step(_mailIndex, next.Count, 0);
        _mailItems.Clear();
        _mailItems.AddRange(next);
        _mailIndex = OtpCarousel.Step(_mailIndex, _mailItems.Count, 0);
        if (_mailItems.Count == 0) _mailPreview = false;
        else if (!string.IsNullOrEmpty(_mailItems[_mailIndex].Code) && !_mailPreview) { }
        else if (string.IsNullOrEmpty(_mailItems[_mailIndex].Code)) _mailPreview = true;
    }

    private FrameworkElement MailEmpty()
    {
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, MaxWidth = 380 };
        var title = UI.Centered(Loc.T("还没有邮件验证码", "No email codes yet"), 16, UI.Primary, FontWeights.SemiBold);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        var hint = UI.Centered(Settings.MailOtpOutlook
            ? Loc.T("Outlook / 邮件的验证码通知会出现在这里。也可在设置 › 验证 › 邮箱验证码中检查通知访问。",
                "Verification notices from Outlook / Mail appear here. Check notification access in Settings › Verification › Email codes.")
            : Loc.T("在设置 › 验证 › 邮箱验证码中打开 Outlook 通知监听。",
                "Turn on Outlook notification listening in Settings › Verification › Email codes."), 12.5, UI.Secondary);
        hint.HorizontalAlignment = HorizontalAlignment.Center;
        hint.TextAlignment = TextAlignment.Center;
        hint.TextWrapping = TextWrapping.Wrap;
        hint.Margin = new Thickness(0, 8, 0, 0);
        stack.Children.Add(title);
        stack.Children.Add(hint);
        return stack;
    }

    private FrameworkElement MailRecognized(EmailOtpMessage message)
    {
        double height = ContentCanvas.Height;
        double codeSize = height < 170 ? 28 : height < 230 ? 36 : 44;
        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.35, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 12, 0) };
        var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        head.Children.Add(MailEnvelope());
        var headTitle = UI.Centered(Loc.T("邮箱验证码", "Email code"), 13, UI.Secondary, FontWeights.SemiBold);
        headTitle.Margin = new Thickness(6, 0, 0, 0);
        headTitle.VerticalAlignment = VerticalAlignment.Center;
        head.Children.Add(headTitle);
        left.Children.Add(head);

        var account = UI.Centered(MailAccountLine(message), 12.5, UI.Tertiary);
        account.HorizontalAlignment = HorizontalAlignment.Left;
        account.TextTrimming = TextTrimming.CharacterEllipsis;
        account.Margin = new Thickness(0, 0, 0, 10);
        left.Children.Add(account);

        var code = UI.Centered(EmailOtp.Group(message.Code), codeSize, UI.Primary, FontWeights.SemiBold, UI.DisplayFont);
        code.HorizontalAlignment = HorizontalAlignment.Left;
        code.Cursor = Cursors.Hand;
        code.MouseLeftButtonUp += (_, e) => { if (!_mailDragged) { CopyMailCode(message); e.Handled = true; } };
        AutomationProperties.SetName(code, Loc.T("验证码 ", "Code ") + EmailOtp.DigitsOnly(message.Code));
        left.Children.Add(code);

        var meta = UI.Centered(MailMetaLine(message), 11.5, UI.Tertiary);
        meta.HorizontalAlignment = HorizontalAlignment.Left;
        meta.TextTrimming = TextTrimming.CharacterEllipsis;
        meta.Margin = new Thickness(0, 10, 0, 4);
        left.Children.Add(meta);

        if (!string.IsNullOrWhiteSpace(message.Subject))
        {
            var subject = UI.Centered(message.Subject.Trim(), 12, UI.Secondary);
            subject.HorizontalAlignment = HorizontalAlignment.Left;
            subject.TextTrimming = TextTrimming.CharacterEllipsis;
            left.Children.Add(subject);
        }

        if (_mailItems.Count > 1)
        {
            var place = UI.Centered($"{_mailIndex + 1} / {_mailItems.Count} · " + Loc.T("上下滑动切换", "swipe to switch"), 11, UI.Tertiary);
            place.HorizontalAlignment = HorizontalAlignment.Left;
            place.Margin = new Thickness(0, 10, 0, 0);
            left.Children.Add(place);
        }

        bool copied = message.Id == _mailCopied && DateTimeOffset.Now < _mailCopiedUntil;
        var actions = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0) };
        actions.Children.Add(MailAction(copied ? Loc.T("已复制", "Copied") : Loc.T("复制验证码", "Copy code"),
            Loc.T("点击复制到剪贴板", "Tap to copy to the clipboard"), primary: true, () => CopyMailCode(message)));
        actions.Children.Add(MailAction(Loc.T("预览原文", "Preview text"),
            Loc.T("查看主题与正文", "See the subject and body"), primary: false, () => { _mailPreview = true; _mailForm = ""; RenderMail(); }));

        Grid.SetColumn(actions, 1);
        root.Children.Add(left);
        root.Children.Add(actions);
        return root;
    }

    private FrameworkElement MailPreviewPane(EmailOtpMessage message)
    {
        var stack = new StackPanel { Margin = new Thickness(8, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        var title = UI.Centered(string.IsNullOrEmpty(message.Code)
            ? Loc.T("无法自动识别验证码", "Could not read a code automatically")
            : Loc.T("邮件原文预览", "Mail text preview"), 15, UI.Primary, FontWeights.SemiBold);
        title.HorizontalAlignment = HorizontalAlignment.Left;
        title.Margin = new Thickness(0, 0, 0, 10);
        stack.Children.Add(title);

        var subject = UI.Centered(Loc.T("主题：", "Subject: ") + (string.IsNullOrWhiteSpace(message.Subject) ? "—" : message.Subject.Trim()), 12.5, UI.Secondary);
        subject.HorizontalAlignment = HorizontalAlignment.Left;
        subject.TextWrapping = TextWrapping.Wrap;
        subject.Margin = new Thickness(0, 0, 0, 6);
        stack.Children.Add(subject);

        string body = string.IsNullOrWhiteSpace(message.Body) ? "—" : message.Body.Trim();
        _mailSelect = new TextBox
        {
            Text = body,
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Foreground = UI.Brush(UI.Secondary),
            FontSize = 12.5,
            MaxHeight = Math.Max(48, ContentCanvas.Height - 130),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(0)
        };
        AutomationProperties.SetName(_mailSelect, Loc.T("正文预览", "Body preview"));
        stack.Children.Add(_mailSelect);

        var tip = UI.Centered(Loc.T("可手动选中数字后点「复制选中」", "Select digits, then tap Copy selection"), 11.5, UI.Accent);
        tip.HorizontalAlignment = HorizontalAlignment.Left;
        tip.Margin = new Thickness(0, 8, 0, 12);
        stack.Children.Add(tip);

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(MailChip(Loc.T("重试识别", "Retry"), primary: true, RetryMail));
        row.Children.Add(MailChip(Loc.T("复制选中", "Copy selection"), primary: false, CopyMailSelection));
        row.Children.Add(MailChip(Loc.T("打开邮箱", "Open mailbox"), primary: false, OpenMailbox));
        if (!string.IsNullOrEmpty(message.Code))
            row.Children.Add(MailChip(Loc.T("返回", "Back"), primary: false, () => { _mailPreview = false; _mailForm = ""; RenderMail(); }));
        stack.Children.Add(row);
        return stack;
    }

    private static FrameworkElement MailEnvelope()
    {
        var box = new Viewbox { Width = 14, Height = 14, VerticalAlignment = VerticalAlignment.Center };
        var canvas = new Canvas { Width = 24, Height = 24 };
        canvas.Children.Add(new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("M 3,6 H 21 V 18 H 3 Z M 3,6 L 12,13 L 21,6"),
            Stroke = UI.Brush(UI.Secondary),
            StrokeThickness = 1.65,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            Fill = Brushes.Transparent
        });
        box.Child = canvas;
        return box;
    }

    private static string MailAccountLine(EmailOtpMessage message)
    {
        string account = (message.Account ?? "").Trim();
        if (account.Length == 0 || account == message.Source) return message.Source;
        return message.Source + " · " + account;
    }

    private static string MailMetaLine(EmailOtpMessage message)
    {
        string when = EmailOtp.Relative(message.ReceivedAt, DateTimeOffset.Now);
        string from = (message.From ?? "").Trim();
        return from.Length == 0 ? when : when + " · " + from;
    }

    private Border MailAction(string title, string subtitle, bool primary, Action click)
    {
        var stack = new StackPanel { Margin = new Thickness(14, 12, 14, 12) };
        stack.Children.Add(UI.Centered(title, 13.5, primary ? "#041018" : UI.Primary, FontWeights.SemiBold));
        var sub = UI.Centered(subtitle, 11, primary ? "#041018" : UI.Tertiary);
        sub.Opacity = primary ? .72 : 1;
        sub.Margin = new Thickness(0, 4, 0, 0);
        stack.Children.Add(sub);
        var border = new Border
        {
            Background = UI.Brush(primary ? UI.Accent : PanelTheme.Hex("fill")),
            CornerRadius = new CornerRadius(14),
            Cursor = Cursors.Hand,
            Margin = new Thickness(0, 0, 0, 8),
            Child = stack
        };
        border.MouseLeftButtonUp += (_, e) => { if (!_mailDragged) { click(); e.Handled = true; } };
        AutomationProperties.SetName(border, title);
        return border;
    }

    private Border MailChip(string title, bool primary, Action click)
    {
        var label = UI.Centered(title, 12.5, primary ? "#041018" : UI.Primary, FontWeights.SemiBold);
        label.Margin = new Thickness(12, 8, 12, 8);
        var border = new Border
        {
            Background = UI.Brush(primary ? UI.Accent : PanelTheme.Hex("fill")),
            CornerRadius = new CornerRadius(10),
            Cursor = Cursors.Hand,
            Margin = new Thickness(0, 0, 8, 0),
            Child = label
        };
        border.MouseLeftButtonUp += (_, e) => { click(); e.Handled = true; };
        AutomationProperties.SetName(border, title);
        return border;
    }

    private void RetryMail()
    {
        if (_mailItems.Count == 0) return;
        var current = _mailItems[_mailIndex];
        if (_app.MailCodes.Retry(current.Id) is { } next && !string.IsNullOrEmpty(next.Code))
            _mailPreview = false;
        _mailForm = "";
        RenderMail();
    }

    private void CopyMailSelection()
    {
        string text = _mailSelect?.SelectedText ?? "";
        if (text.Length == 0) text = _mailSelect?.Text ?? "";
        string digits = EmailOtp.DigitsOnly(text);
        if (digits.Length == 0) return;
        try { Clipboard.SetText(digits); }
        catch (ExternalException) { return; }
        if (_mailItems.Count > 0)
        {
            _mailCopied = _mailItems[_mailIndex].Id;
            _mailCopiedUntil = DateTimeOffset.Now.AddSeconds(1.4);
        }
    }

    private void CopyMailCode(EmailOtpMessage message)
    {
        string digits = EmailOtp.DigitsOnly(message.Code);
        if (digits.Length == 0) return;
        try { Clipboard.SetText(digits); }
        catch (ExternalException) { return; }
        _mailCopied = message.Id;
        _mailCopiedUntil = DateTimeOffset.Now.AddSeconds(1.4);
        _mailForm = "";
        RenderMail();
    }

    private static void OpenMailbox()
    {
        foreach (var target in new[] { "outlookmail:", "ms-outlook:", "mailto:" })
        {
            try
            {
                Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
                return;
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or System.IO.FileNotFoundException) { }
        }
    }

    private void MailPress(object sender, MouseButtonEventArgs e)
    {
        if (sender is not UIElement surface || _mailItems.Count < 2) return;
        _mailDragStart = e.GetPosition(surface);
        _mailDragged = false;
        surface.CaptureMouse();
    }

    private void MailDrag(object sender, MouseEventArgs e)
    {
        if (sender is not UIElement surface || !surface.IsMouseCaptured) return;
        double dy = e.GetPosition(surface).Y - _mailDragStart.Y;
        if (Math.Abs(dy) < 42) return;
        _mailDragged = true;
        MoveMail(dy < 0 ? 1 : -1);
        _mailDragStart = e.GetPosition(surface);
    }

    private void MailRelease(object sender, MouseButtonEventArgs e)
    {
        if (sender is UIElement surface && surface.IsMouseCaptured) surface.ReleaseMouseCapture();
    }

    private void MailWheel(object sender, MouseWheelEventArgs e)
    {
        if (_mailItems.Count < 2) return;
        MoveMail(e.Delta > 0 ? -1 : 1);
        e.Handled = true;
    }

    private void MoveMail(int delta)
    {
        int next = OtpCarousel.Step(_mailIndex, _mailItems.Count, delta);
        if (next == _mailIndex) return;
        _mailIndex = next;
        _mailCopied = Guid.Empty;
        _mailPreview = string.IsNullOrEmpty(_mailItems[_mailIndex].Code);
        _mailForm = "";
        RenderMail();
    }

    private void MailPage_Click(object sender, RoutedEventArgs e) => SelectPage(DeckPage.Mail);
}
