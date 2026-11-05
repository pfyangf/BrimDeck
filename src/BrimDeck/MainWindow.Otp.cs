using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using BrimDeck.Core;

namespace BrimDeck;

// The authenticator page of the open panel. It only reads otp.dat (the same store the settings page writes) and shows
// one code at a time, with the neighbouring accounts peeking above and below.
public partial class MainWindow
{
    internal bool IsOtpPage => CurrentPage == DeckPage.Otp;
    private readonly List<OtpAccount> _otpAccounts = [];
    private int _otpIndex, _otpShown = -1;
    private bool _otpUnreadable, _otpDragged;
    private Point _otpDragStart;
    private string _otpForm = "";
    private string _otpPlain = "";
    private Guid _otpCopied;
    private DateTimeOffset _otpCopiedUntil;
    private TextBlock? _otpCode, _otpRemain, _otpHint;
    private Grid? _otpRing;
    private DispatcherTimer? _otpTimer;

    private void WatchOtp()
    {
        _app.Otp.Changed += () => Dispatcher.BeginInvoke(() => { if (IsOtpPage && _expanded) RenderOtp(); });
        _otpTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _otpTimer.Tick += (_, _) =>
        {
            if (!IsOtpPage || !_expanded) { _otpTimer!.Stop(); return; }
            if (DateTimeOffset.Now >= _otpCopiedUntil && _otpCopied != Guid.Empty) { _otpCopied = Guid.Empty; RenderOtp(); return; }
            PaintOtpLive();
        };
        if (IsOtpPage && _expanded) _otpTimer.Start();
    }

    private void RenderOtp()
    {
        if (OtpContent is null) return;
        bool shown = OtpContent.IsVisible && _expanded && !_preview;
        RefreshStatus();
        UsageContent.Visibility = Visibility.Collapsed;
        MonitorContent.Visibility = Visibility.Collapsed;
        MusicContent.Visibility = Visibility.Collapsed;
        MailContent.Visibility = Visibility.Collapsed;
        OtpContent.Visibility = Visibility.Visible;
        ModelToolbar.Visibility = Visibility.Collapsed;
        ApplicationPages.Visibility = Visibility.Collapsed;
        ClockLabel.Visibility = Visibility.Visible;
        ApplyHeader(PanelHeader.For(36, 20));
        LoadOtpAccounts();
        double width = ContentCanvas.Width, height = ContentCanvas.Height;
        string ids = string.Join(",", _otpAccounts.Select(account => account.Id.ToString("N") + ":" + account.Issuer + ":" + account.Name + ":" + account.Digits + ":" + account.Period + ":" + account.Algorithm));
        bool copied = _otpAccounts.Count > 0 && _otpAccounts[_otpIndex].Id == _otpCopied && DateTimeOffset.Now < _otpCopiedUntil;
        string form = $"{width:0}|{height:0}|{_otpIndex}|{_otpUnreadable}|{ids}|{PanelTheme.Generation}|{Loc.Language}|{copied}|{_otpShown}";
        if (form == _otpForm && OtpContent.Children.Count > 0) { PaintOtpLive(); EnsureOtpTimer(); return; }
        int previous = _otpShown;
        _otpShown = _otpIndex;
        _otpForm = form;
        OtpContent.Children.Clear();
        var root = new Grid { Background = Brushes.Transparent };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        if (_otpAccounts.Count == 0)
        {
            root.Children.Add(OtpEmpty());
            Grid.SetRowSpan(root.Children[0], 3);
        }
        else
        {
            bool room = height >= 200;
            if (room && _otpIndex > 0) root.Children.Add(OtpPeek(_otpAccounts[_otpIndex - 1], above: true));
            var focus = OtpFocus(_otpAccounts[_otpIndex]);
            Grid.SetRow(focus, 1);
            root.Children.Add(focus);
            if (room && _otpIndex < _otpAccounts.Count - 1)
            {
                var next = OtpPeek(_otpAccounts[_otpIndex + 1], above: false);
                Grid.SetRow(next, 2);
                root.Children.Add(next);
            }
        }
        root.MouseLeftButtonDown += OtpPress;
        root.MouseMove += OtpDrag;
        root.MouseLeftButtonUp += OtpRelease;
        root.PreviewMouseWheel += OtpWheel;
        OtpContent.Children.Add(root);
        if (shown && previous >= 0 && previous != _otpIndex && Soft.Enabled && root.Children.Count > 0)
        {
            var shift = new TranslateTransform(0, _otpIndex > previous ? 14 : -14);
            root.RenderTransform = shift;
            shift.BeginAnimation(TranslateTransform.YProperty, Soft.Fade(shift.Y, 0, Soft.Quick, Soft.Follow));
            root.BeginAnimation(OpacityProperty, Soft.Fade(.55, 1, Soft.Quick, Soft.Follow));
        }
        PaintOtpLive();
        EnsureOtpTimer();
    }

    private void EnsureOtpTimer()
    {
        if (_otpTimer is null) return;
        if (IsOtpPage && _expanded) { if (!_otpTimer.IsEnabled) _otpTimer.Start(); }
        else _otpTimer.Stop();
    }

    private void LoadOtpAccounts()
    {
        try
        {
            var next = _app.Otp.Load();
            _otpUnreadable = false;
            if (!_otpAccounts.Select(account => account.Id).SequenceEqual(next.Select(account => account.Id)))
                _otpIndex = OtpCarousel.Step(_otpIndex, next.Count, 0);
            _otpAccounts.Clear();
            _otpAccounts.AddRange(next);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException or System.Text.Json.JsonException)
        {
            _otpUnreadable = true;
            _otpAccounts.Clear();
        }
        _otpIndex = OtpCarousel.Step(_otpIndex, _otpAccounts.Count, 0);
    }

    private FrameworkElement OtpEmpty()
    {
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, MaxWidth = 360 };
        var title = UI.Centered(_otpUnreadable
            ? Loc.T("无法读取验证码", "Codes could not be read")
            : Loc.T("还没有验证码", "No codes yet"), 16, UI.Primary, FontWeights.SemiBold);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        var hint = UI.Centered(_otpUnreadable
            ? Loc.T("设置里的验证码文件无法打开。", "The authenticator file in settings could not be opened.")
            : Loc.T("在设置 › 验证 › 验证码中添加账户", "Add an account in Settings › Verification › Authenticator"), 12.5, UI.Secondary);
        hint.HorizontalAlignment = HorizontalAlignment.Center;
        hint.TextAlignment = TextAlignment.Center;
        hint.TextWrapping = TextWrapping.Wrap;
        hint.Margin = new Thickness(0, 8, 0, 0);
        stack.Children.Add(title);
        stack.Children.Add(hint);
        return stack;
    }

    private FrameworkElement OtpFocus(OtpAccount account)
    {
        double height = ContentCanvas.Height;
        double codeSize = height < 170 ? 28 : height < 230 ? 36 : 44;
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        if (_otpAccounts.Count > 1)
        {
            var place = UI.Centered($"{_otpIndex + 1} / {_otpAccounts.Count}", 11, UI.Tertiary);
            place.HorizontalAlignment = HorizontalAlignment.Center;
            place.Margin = new Thickness(0, 0, 0, 8);
            stack.Children.Add(place);
            if (_otpAccounts.Count <= 8)
            {
                var dots = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 10) };
                for (int i = 0; i < _otpAccounts.Count; i++)
                    dots.Children.Add(new Ellipse { Width = 5, Height = 5, Margin = new Thickness(3, 0, 3, 0), Fill = UI.Brush(i == _otpIndex ? UI.Primary : UI.Tertiary), Opacity = i == _otpIndex ? 1 : .45 });
                stack.Children.Add(dots);
            }
        }
        var label = UI.Centered(OtpAccounts.Label(account), 13, UI.Secondary);
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.TextTrimming = TextTrimming.CharacterEllipsis;
        label.MaxWidth = Math.Max(120, ContentCanvas.Width - 48);
        stack.Children.Add(label);
        _otpCode = UI.Centered("000 000", codeSize, UI.Primary, FontWeights.SemiBold, UI.DisplayFont);
        _otpCode.HorizontalAlignment = HorizontalAlignment.Center;
        _otpCode.Margin = new Thickness(0, 6, 0, 8);
        _otpCode.Cursor = Cursors.Hand;
        stack.Children.Add(_otpCode);
        var meta = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        _otpRing = new Grid { Width = 22, Height = 22, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        meta.Children.Add(_otpRing);
        _otpRemain = UI.Centered("", 13, UI.Secondary);
        _otpRemain.VerticalAlignment = VerticalAlignment.Center;
        meta.Children.Add(_otpRemain);
        stack.Children.Add(meta);
        _otpHint = UI.Centered("", 11, UI.Tertiary);
        _otpHint.HorizontalAlignment = HorizontalAlignment.Center;
        _otpHint.Margin = new Thickness(0, 8, 0, 0);
        stack.Children.Add(_otpHint);
        return stack;
    }

    private static FrameworkElement OtpPeek(OtpAccount account, bool above)
    {
        var text = UI.Centered(OtpAccounts.Label(account), 12, UI.Tertiary);
        text.HorizontalAlignment = HorizontalAlignment.Center;
        text.TextTrimming = TextTrimming.CharacterEllipsis;
        text.Opacity = .72;
        text.Margin = new Thickness(24, above ? 0 : 6, 24, above ? 6 : 2);
        return text;
    }

    private void PaintOtpLive()
    {
        if (_otpCode is null || _otpRemain is null || _otpHint is null || _otpRing is null || _otpAccounts.Count == 0) return;
        var account = _otpAccounts[_otpIndex];
        var now = DateTimeOffset.Now;
        _otpPlain = Totp.Code(account.Secret, now, account.Digits, account.Period, account.Algorithm);
        _otpCode.Text = Totp.Group(_otpPlain);
        AutomationProperties.SetName(_otpCode, Loc.T("验证码 ", "Code ") + _otpPlain);
        int left = Totp.Remaining(now, account.Period);
        _otpRemain.Text = Loc.T($"{left} 秒", left == 1 ? "1 second" : $"{left} seconds");
        _otpRing.Children.Clear();
        _otpRing.Children.Add(UI.Ring(left * 100d / account.Period, left <= 5 ? "#E0A050" : UI.Accent, 22, 2.2));
        bool copied = account.Id == _otpCopied && now < _otpCopiedUntil;
        _otpHint.Text = copied ? Loc.T("已复制", "Copied") : Loc.T("点击复制 · 上下滑动切换", "Tap to copy · swipe to switch");
        _otpHint.Foreground = UI.Brush(copied ? UI.Accent : UI.Tertiary);
    }

    private void OtpPress(object sender, MouseButtonEventArgs e)
    {
        if (sender is not UIElement surface || _otpAccounts.Count < 2) return;
        _otpDragStart = e.GetPosition(surface);
        _otpDragged = false;
        surface.CaptureMouse();
    }

    private void OtpDrag(object sender, MouseEventArgs e)
    {
        if (sender is not UIElement surface || !surface.IsMouseCaptured) return;
        double dy = e.GetPosition(surface).Y - _otpDragStart.Y;
        if (Math.Abs(dy) < 42) return;
        _otpDragged = true;
        MoveOtp(dy < 0 ? 1 : -1);
        _otpDragStart = e.GetPosition(surface);
    }

    private void OtpRelease(object sender, MouseButtonEventArgs e)
    {
        if (sender is UIElement surface && surface.IsMouseCaptured) surface.ReleaseMouseCapture();
        if (_otpDragged || _otpAccounts.Count == 0) return;
        CopyOtp();
    }

    private void OtpWheel(object sender, MouseWheelEventArgs e)
    {
        if (_otpAccounts.Count < 2) return;
        MoveOtp(e.Delta > 0 ? -1 : 1);
        e.Handled = true;
    }

    private void MoveOtp(int delta)
    {
        int next = OtpCarousel.Step(_otpIndex, _otpAccounts.Count, delta);
        if (next == _otpIndex) return;
        _otpIndex = next;
        _otpCopied = Guid.Empty;
        RenderOtp();
    }

    private void OtpPage_Click(object sender, RoutedEventArgs e) => SelectPage(DeckPage.Otp);

    private void CopyOtp()
    {
        if (_otpPlain.Length == 0 || _otpAccounts.Count == 0) return;
        try { Clipboard.SetText(_otpPlain); }
        catch (ExternalException) { return; }
        _otpCopied = _otpAccounts[_otpIndex].Id;
        _otpCopiedUntil = DateTimeOffset.Now.AddSeconds(1.4);
        _otpForm = "";
        RenderOtp();
    }
}
