using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using BrimDeck.Core;
using BrimDeck.Native;
using Microsoft.Win32;

namespace BrimDeck;

// "验证 › 验证码": TOTP codes for accounts the user adds. Secrets live in otp.dat, encrypted for the Windows user
// (OtpStore, DpapiProtector), and the page never writes them into settings.json.
public sealed partial class SettingsWindow
{
    internal const int OtpPage = 11;
    private DispatcherTimer? _otpTimer;
    private readonly List<OtpRow> _otpRows = [];
    private bool _otpUnreadable;

    private sealed record OtpRow(OtpAccount Account, TextBlock Code, TextBlock Left, Border Bar);

    private void StopOtpClock()
    {
        _otpTimer?.Stop();
        _otpTimer = null;
        _otpRows.Clear();
    }

    private void Authenticator()
    {
        StopOtpClock();
        var intro = TextLine(Loc.T("显示一次性验证码。密钥只保存在这台电脑的当前用户下，不写入设置，也不上传。",
            "Shows one-time codes. The secret is kept for the current Windows user on this PC, not in settings, and is not uploaded."), 12.5, TextSecondary);
        intro.TextWrapping = TextWrapping.Wrap; intro.Margin = new Thickness(0, 0, 0, 18);
        _content.Children.Add(intro);

        SectionTitle(Loc.T("当前验证码", "Current codes"), help: () => Loc.T("标准 TOTP：HMAC-SHA1，30 秒更换，6 位数字。二维码若指定 SHA-256、SHA-512、位数或周期，按二维码来。不支持 HOTP。",
            "Standard TOTP: HMAC-SHA1, a new code every 30 seconds, 6 digits. A QR code that asks for SHA-256, SHA-512, other digits or another period is followed. HOTP is not supported."));
        var accounts = ReadOtp();
        if (accounts.Count == 0)
        {
            var empty = TextLine(Loc.T("还没有账户。在下面输入密钥，或从二维码图片导入。", "No accounts yet. Enter a secret below, or import a QR code image."), 12.5, TextTertiary);
            empty.Margin = new Thickness(0, 0, 0, 22); _content.Children.Add(empty);
        }
        else Group(accounts.Select(AccountRow).ToArray());
        if (accounts.Count > 0)
        {
            _otpTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _otpTimer.Tick += (_, _) => RefreshOtpCodes();
            _otpTimer.Start();
            RefreshOtpCodes();
        }

        SectionTitle(Loc.T("添加账户", "Add an account"));
        var issuer = Field(); AutomationProperties.SetName(issuer, Loc.T("发行方", "Issuer"));
        var name = Field(); AutomationProperties.SetName(name, Loc.T("账户名", "Account name"));
        var secret = Field(); secret.FontFamily = new FontFamily("Cascadia Mono, Consolas, Segoe UI");
        AutomationProperties.SetName(secret, Loc.T("密钥", "Secret"));
        var form = new StackPanel();
        form.Children.Add(Labeled(Loc.T("发行方（可选）", "Issuer (optional)"), issuer));
        form.Children.Add(Labeled(Loc.T("账户名（可选）", "Account name (optional)"), name));
        form.Children.Add(Labeled(Loc.T("密钥（Base32）", "Secret (Base32)"), secret));
        var add = ActionButton(Loc.T("添加", "Add"), () => AddOtp(issuer.Text, name.Text, secret.Text), primary: true);
        var file = ActionButton(Loc.T("从图片导入", "Import from image"), ImportOtpImage);
        var paste = ActionButton(Loc.T("从剪贴板导入", "Paste from clipboard"), ImportOtpClipboard);
        foreach (var button in new[] { add, file, paste }) { button.Margin = new Thickness(0, 0, 8, 0); button.Padding = new Thickness(12, 6, 12, 6); }
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        buttons.Children.Add(add); buttons.Children.Add(file); buttons.Children.Add(paste);
        form.Children.Add(buttons);
        Group(form);
    }

    private TextBox Field() => new() { Style = (Style)FindResource("SettingsField"), MaxLength = 256, Margin = new Thickness(0, 4, 0, 12) };

    private StackPanel Labeled(string title, TextBox box)
    {
        var stack = new StackPanel();
        stack.Children.Add(TextLine(title, 12, TextSecondary));
        stack.Children.Add(box);
        return stack;
    }

    private FrameworkElement AccountRow(OtpAccount account)
    {
        var row = new Grid { Margin = new Thickness(0, 8, 0, 8) };
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var title = TextLine(OtpAccounts.Label(account), 13, TextPrimary, FontWeights.SemiBold);
        var code = TextLine("------", 22, TextPrimary, FontWeights.SemiBold);
        code.FontFamily = new FontFamily("Cascadia Mono, Consolas, Segoe UI"); code.Margin = new Thickness(0, 2, 0, 4);
        var meta = new StackPanel { Orientation = Orientation.Horizontal };
        var bar = new Border { Height = 3, Width = 0, HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(1.5), Background = UI.Brush("#0A84FF") };
        var track = new Border { Width = 72, Height = 3, CornerRadius = new CornerRadius(1.5), Background = UI.Brush(Divider), Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center, ClipToBounds = true, Child = bar };
        var left = TextLine("", 12, TextSecondary); left.VerticalAlignment = VerticalAlignment.Center;
        meta.Children.Add(track); meta.Children.Add(left);
        text.Children.Add(title); text.Children.Add(code); text.Children.Add(meta);
        var remove = ActionButton(Loc.T("删除", "Delete"), () => DeleteOtp(account));
        AutomationProperties.SetName(remove, Loc.T("删除 ", "Delete ") + OtpAccounts.Label(account));
        remove.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(remove, 1); row.Children.Add(text); row.Children.Add(remove);
        _otpRows.Add(new OtpRow(account, code, left, bar));
        return row;
    }

    private void RefreshOtpCodes()
    {
        var now = DateTimeOffset.Now;
        foreach (var row in _otpRows)
        {
            string code = Totp.Code(row.Account.Secret, now, row.Account.Digits, row.Account.Period, row.Account.Algorithm);
            row.Code.Text = Totp.Group(code);
            AutomationProperties.SetName(row.Code, Loc.T("验证码 ", "Code ") + code);
            int left = Totp.Remaining(now, row.Account.Period);
            row.Left.Text = Loc.T($"{left} 秒", left == 1 ? "1 second" : $"{left} seconds");
            row.Bar.Width = 72d * left / row.Account.Period;
            row.Bar.Background = UI.Brush(left <= 5 ? "#E0A050" : "#0A84FF");
        }
    }

    private IReadOnlyList<OtpAccount> ReadOtp()
    {
        try
        {
            var accounts = _app.Otp.Load();
            _otpUnreadable = false;
            return accounts;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException or JsonException)
        {
            _otpUnreadable = true;
            SaveStatus(Loc.T("无法读取已保存的验证码。otp.dat 未修改。", "Saved codes could not be read. otp.dat was not changed."), true);
            return [];
        }
    }

    private void AddOtp(string issuer, string name, string secret)
    {
        var imported = OtpAuth.FromSecret(secret, issuer, name);
        if (!imported.Ok || imported.Account is null) { SaveStatus(ImportError(imported.Error), true); return; }
        if (!StoreOtp(ReadOtp().Append(imported.Account).ToList())) return;
        ShowPage(OtpPage);
    }

    private void DeleteOtp(OtpAccount account)
    {
        string label = OtpAccounts.Label(account);
        if (!Confirm(Loc.T("删除验证码", "Delete code"), Loc.T($"将删除“{label}”。此操作无法撤销。", $"This deletes {label}. It cannot be undone."), Loc.T("删除", "Delete"))) return;
        if (!StoreOtp(ReadOtp().Where(item => item.Id != account.Id).ToList())) return;
        ShowPage(OtpPage);
    }

    private bool StoreOtp(IReadOnlyList<OtpAccount> accounts)
    {
        if (_otpUnreadable)
        {
            SaveStatus(Loc.T("无法读取已保存的验证码，因此没有写入，以免覆盖原来的文件。", "Saved codes could not be read, so nothing was written, to avoid replacing the file."), true);
            return false;
        }
        try { _app.Otp.Save(accounts); return true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
        {
            SaveStatus(Loc.T("无法保存验证码。", "The codes could not be saved."), true);
            return false;
        }
    }

    private void ImportOtpImage()
    {
        var dialog = new OpenFileDialog
        {
            Title = Loc.T("选择二维码图片", "Choose a QR code image"),
            Filter = Loc.T("图片 (*.png;*.jpg;*.jpeg;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif", "Images (*.png;*.jpg;*.jpeg;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif")
        };
        if (dialog.ShowDialog(this) != true) return;
        try { ImportOtpText(QrImage.Read(QrImage.Open(dialog.FileName))); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or InvalidOperationException)
        {
            SaveStatus(Loc.T("无法打开这张图片。", "This image could not be opened."), true);
        }
    }

    private void ImportOtpClipboard()
    {
        try
        {
            if (!Clipboard.ContainsImage()) { SaveStatus(Loc.T("剪贴板里没有图片。", "The clipboard has no image."), true); return; }
            ImportOtpText(QrImage.Read(Clipboard.GetImage()));
        }
        catch (Exception ex) when (ex is ExternalException or InvalidOperationException)
        {
            SaveStatus(Loc.T("无法读取剪贴板里的图片。", "The image on the clipboard could not be read."), true);
        }
    }

    private void ImportOtpText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) { SaveStatus(Loc.T("这张图片里没有二维码。", "No QR code was found in this image."), true); return; }
        var imported = OtpAuth.Parse(text);
        if (!imported.Ok || imported.Account is null) { SaveStatus(ImportError(imported.Error), true); return; }
        if (!StoreOtp(ReadOtp().Append(imported.Account).ToList())) return;
        ShowPage(OtpPage);
    }

    private static string ImportError(OtpImportError error) => error switch
    {
        OtpImportError.Hotp => Loc.T("这是 HOTP 计数验证码，目前不支持。请改用 TOTP。", "This is an HOTP counter code, which is not supported. Use TOTP."),
        OtpImportError.NoSecret => Loc.T("二维码里没有密钥。", "The QR code has no secret."),
        OtpImportError.BadSecret => Loc.T("密钥不是有效的 Base32。", "The secret is not valid Base32."),
        OtpImportError.UnsupportedAlgorithm => Loc.T("不支持这个哈希算法。请使用 SHA-1、SHA-256 或 SHA-512。", "This hash is not supported. Use SHA-1, SHA-256 or SHA-512."),
        OtpImportError.BadDigits => Loc.T("位数需要在 6 到 8 之间。", "Digits must be from 6 to 8."),
        OtpImportError.BadPeriod => Loc.T("更换周期需要在 15 到 120 秒之间。", "The period must be from 15 to 120 seconds."),
        _ => Loc.T("这不是 otpauth 验证码二维码。可以改为手动输入密钥。", "This is not an otpauth code. You can type the secret instead.")
    };
}
