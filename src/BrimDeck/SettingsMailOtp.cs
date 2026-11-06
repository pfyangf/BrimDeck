using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using BrimDeck.Core;
using BrimDeck.Native;

namespace BrimDeck;

// "验证 › 邮箱验证码": toast listening + optional Microsoft Graph / IMAP / POP3 mailbox bind for the island inbox.
public sealed partial class SettingsWindow
{
    internal const int MailOtpSettingsPage = 12;

    private void MailOtpSettings()
    {
        var intro = TextLine(Loc.T(
            "两种来源可同时使用：① Windows 邮件通知（Outlook 等 toast，无需登录）；② 绑定 Microsoft、IMAP 或 POP3 邮箱后拉取近期收件箱。验证码在展开面板的邮箱页显示，密钥只保存在本机当前用户下。",
            "Two sources can run together: (1) Windows mail notifications (Outlook and similar toasts, no sign-in); (2) bound Microsoft, IMAP, or POP3 mailboxes for a recent inbox. Codes show on the expanded email page. Secrets stay on this PC for the current Windows user."), 12.5, TextSecondary);
        intro.TextWrapping = TextWrapping.Wrap; intro.Margin = new Thickness(0, 0, 0, 18);
        _content.Children.Add(intro);

        SectionTitle(Loc.T("绑定邮箱", "Bound mailboxes"), help: () => Loc.T(
            "绑定后可在岛上按账号筛选并预览近期邮件；看起来像验证码的邮件会抽出数字供一键复制。不绑定也可以只用下面的通知开关。",
            "After binding, the island can filter and preview recent mail by account; verification-looking mails expose a code to copy. The notification toggle below still works without any bind."));

        IReadOnlyList<BoundMailAccount> accounts;
        try { accounts = _app.MailAccounts.Load(); }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException or System.Text.Json.JsonException)
        {
            accounts = [];
            var err = TextLine(Loc.T("无法读取已绑定的邮箱。", "Bound mailboxes could not be read."), 12.5, TextTertiary);
            err.Margin = new Thickness(0, 0, 0, 12); _content.Children.Add(err);
        }

        if (accounts.Count == 0)
        {
            var empty = TextLine(Loc.T("还没有绑定邮箱。通知路径仍可单独使用。", "No mailbox bound yet. The notification path still works on its own."), 12.5, TextTertiary);
            empty.Margin = new Thickness(0, 0, 0, 12); _content.Children.Add(empty);
        }
        else Group(accounts.Select(BoundAccountRow).ToArray());

        var addRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        var addMs = ActionButton(Loc.T("添加 Microsoft 账号", "Add Microsoft account"), AddMicrosoftAccount, primary: true);
        var addImap = ActionButton(Loc.T("添加 IMAP 邮箱", "Add IMAP mailbox"), () => AddImapAccount());
        var addPop3 = ActionButton(Loc.T("添加 POP3 邮箱", "Add POP3 mailbox"), () => AddPop3Account());
        foreach (var button in new[] { addMs, addImap, addPop3 })
        { button.Margin = new Thickness(0, 0, 8, 0); button.Padding = new Thickness(12, 6, 12, 6); }
        addRow.Children.Add(addMs); addRow.Children.Add(addImap); addRow.Children.Add(addPop3);
        Group(addRow);

        SectionTitle(Loc.T("Microsoft Graph", "Microsoft Graph"), help: () => Loc.T(
            "在 Azure 门户注册「公共客户端 / 桌面」应用，添加委派权限 User.Read 与 Mail.Read，并启用公共客户端流。把应用程序（客户端）ID 粘贴到下面。",
            "In Azure Portal register a public client / desktop app, add delegated User.Read and Mail.Read, enable public client flows, then paste the Application (client) ID below."));
        var clientId = Field(); clientId.Text = S.MailOtpMsClientId; clientId.MaxLength = 64;
        AutomationProperties.SetName(clientId, Loc.T("Azure 客户端 ID", "Azure client ID"));
        clientId.LostKeyboardFocus += (_, _) =>
        {
            string value = (clientId.Text ?? "").Trim();
            if (value == S.MailOtpMsClientId) return;
            Change(s => s.MailOtpMsClientId = value);
        };
        var clientHint = TextLine(Loc.T("未填写客户端 ID 时无法添加 Microsoft 账号。", "Microsoft sign-in needs a client ID first."), 12, TextTertiary);
        clientHint.Margin = new Thickness(0, 4, 0, 0);
        Group(Labeled(Loc.T("应用程序（客户端）ID", "Application (client) ID"), clientId), clientHint);

        SectionTitle(Loc.T("邮件通知", "Mail notifications"));
        Group(
            Toggle(Loc.T("Outlook / 邮件通知", "Outlook / Mail notifications"), S.MailOtpOutlook, value => Change(s => s.MailOtpOutlook = value),
                help: Loc.T("打开后，即使消息横幅关闭，也会读取邮件通知以提取验证码。无需绑定邮箱。需要 Windows 通知访问权限。",
                    "When on, mail notifications are still read for codes even if message banners are off. No mailbox bind required. Windows notification access is required.")),
            TextLine(Loc.T("仅扫「验证码 / code / OTP」类邮件通知", "Only verification / code / OTP mail notifications"), 12, TextTertiary));

        SectionTitle(Loc.T("通知访问", "Notification access"));
        var access = TextLine(_app.Toasts?.Status ?? Loc.T("通知监听尚未启动。", "Notification listening has not started."), 12, TextSecondary);
        access.TextWrapping = TextWrapping.Wrap; access.Margin = new Thickness(0, 0, 0, 10);
        var check = ActionButton(Loc.T("检查通知访问", "Check notification access"),
            () => _app.Toasts?.RequestAccess(() => { if (_page == MailOtpSettingsPage) ShowPage(MailOtpSettingsPage); }));
        check.Padding = new Thickness(12, 6, 12, 6);
        var refresh = ActionButton(Loc.T("立即同步收件箱", "Sync inbox now"), () => _ = SyncInboxNowAsync());
        refresh.Padding = new Thickness(12, 6, 12, 6); refresh.Margin = new Thickness(8, 0, 0, 0);
        var accessButtons = new StackPanel { Orientation = Orientation.Horizontal };
        accessButtons.Children.Add(check); accessButtons.Children.Add(refresh);
        Group(access, accessButtons);

        SectionTitle(Loc.T("展开面板", "Expanded panel"));
        Group(Toggle(Loc.T("显示邮箱验证码页", "Show email codes page"), S.MailOtpPage, value =>
        {
            Change(s => s.MailOtpPage = value);
        }, help: Loc.T("也可在「展开外观 › 显示的页面」中开关并拖动排序。岛上可按账号筛选近期邮件并复制验证码。",
            "You can also turn this on or off and reorder it under Expanded view › Pages shown. The island filters recent mail by account and copies codes.")));
    }

    private FrameworkElement BoundAccountRow(BoundMailAccount account)
    {
        var row = new Grid { Margin = new Thickness(0, 8, 0, 8) };
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(TextLine(account.Label, 13, TextPrimary, FontWeights.SemiBold));
        text.Children.Add(TextLine(account.KindLabel, 12, TextSecondary));
        var remove = ActionButton(Loc.T("解除绑定", "Disconnect"), () => DisconnectAccount(account));
        AutomationProperties.SetName(remove, Loc.T("解除绑定 ", "Disconnect ") + account.Label);
        remove.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(remove, 1); row.Children.Add(text); row.Children.Add(remove);
        return row;
    }

    private async void AddMicrosoftAccount()
    {
        if (_app.GraphMail is null) return;
        if (!_app.GraphMail.HasClientId)
        {
            MessageBox.Show(this,
                Loc.T("请先填写上方的 Azure 应用程序（客户端）ID，并在 Azure 门户为该应用启用公共客户端与 Mail.Read。",
                    "Enter the Azure Application (client) ID above first, and enable public client flows plus Mail.Read for that app in Azure Portal."),
                Loc.T("需要客户端 ID", "Client ID required"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        try
        {
            var account = await _app.GraphMail.SignInAsync().ConfigureAwait(true);
            var list = _app.MailAccounts.Load().ToList();
            if (list.Any(a => a.Kind == MailAccountKind.MicrosoftGraph
                && string.Equals(a.Email, account.Email, StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show(this, Loc.T("这个 Microsoft 账号已经绑定过了。", "That Microsoft account is already bound."),
                    Loc.T("绑定邮箱", "Bound mailboxes"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            list.Add(account);
            _app.MailAccounts.Save(list);
            _app.MailInbox?.Apply(S);
            _ = _app.MailInbox?.RefreshNowAsync();
            ShowPage(MailOtpSettingsPage);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            MessageBox.Show(this, ex.Message, Loc.T("无法登录 Microsoft", "Microsoft sign-in failed"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void AddImapAccount()
    {
        try { OpenServerBindDialog(MailAccountKind.Imap); }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, Loc.T("无法打开 IMAP 绑定", "Could not open IMAP bind"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void AddPop3Account()
    {
        try { OpenServerBindDialog(MailAccountKind.Pop3); }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, Loc.T("无法打开 POP3 绑定", "Could not open POP3 bind"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OpenServerBindDialog(MailAccountKind kind)
    {
        bool pop3 = kind == MailAccountKind.Pop3;
        string protocol = pop3 ? "POP3" : "IMAP";
        var dialog = new Window
        {
            Title = Loc.T($"添加 {protocol} 邮箱", $"Add {protocol} mailbox"),
            Owner = this, Width = 440, SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false,
            FontFamily = UI.PanelFont, Background = UI.Brush(Surface), Foreground = UI.Brush(TextPrimary),
            ResizeMode = ResizeMode.NoResize
        };
        dialog.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/BrimDeck;component/SettingsResources.xaml", UriKind.Relative) });
        _palette.Apply(dialog.Resources); Native.SettingsChrome.Apply(dialog);
        dialog.SourceInitialized += (_, _) => Native.SettingsChrome.SetTheme(dialog, _palette.Dark);

        var panel = new StackPanel { Margin = new Thickness(24, 20, 24, 20) };
        var presetBox = new ComboBox { Margin = new Thickness(0, 4, 0, 12), MinHeight = 32 };
        if (pop3)
        {
            foreach (var preset in Pop3Presets.All)
            {
                string title = preset.Id == "custom" ? Loc.T("自定义 POP3", "Custom POP3") : preset.Title;
                presetBox.Items.Add(new ComboBoxItem { Content = title, Tag = preset.Id });
            }
        }
        else
        {
            foreach (var preset in ImapPresets.All)
            {
                string title = preset.Id == "custom" ? Loc.T("自定义 IMAP", "Custom IMAP") : preset.Title;
                presetBox.Items.Add(new ComboBoxItem { Content = title, Tag = preset.Id });
            }
        }
        presetBox.SelectedIndex = 0;

        var email = Field(); AutomationProperties.SetName(email, Loc.T("邮箱地址", "Email address"));
        var user = Field(); AutomationProperties.SetName(user, Loc.T("用户名", "Username"));
        var password = new PasswordBox { Style = (Style)FindResource("SettingsPasswordField"), Margin = new Thickness(0, 4, 0, 12), MaxLength = 256 };
        AutomationProperties.SetName(password, Loc.T("密码或授权码", "Password or app password"));
        var host = Field(); AutomationProperties.SetName(host, Loc.T($"{protocol} 服务器", $"{protocol} server"));
        var port = Field(); port.Text = pop3 ? "995" : "993"; AutomationProperties.SetName(port, Loc.T("端口", "Port"));
        var ssl = new CheckBox { Content = Loc.T("SSL/TLS", "SSL/TLS"), IsChecked = true, Margin = new Thickness(0, 0, 0, 12), Foreground = UI.Brush(TextPrimary) };
        var hint = TextLine("", 12, TextTertiary); hint.TextWrapping = TextWrapping.Wrap; hint.Margin = new Thickness(0, 0, 0, 8);

        void ApplyPreset()
        {
            string id = (presetBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "custom";
            if (pop3)
            {
                var preset = Pop3Presets.Find(id);
                if (preset is null) return;
                hint.Text = Loc.T(preset.HintZh, preset.HintEn);
                if (preset.Id != "custom")
                {
                    host.Text = preset.Host;
                    port.Text = preset.Port.ToString();
                    ssl.IsChecked = preset.Ssl;
                }
            }
            else
            {
                var preset = ImapPresets.Find(id);
                if (preset is null) return;
                hint.Text = Loc.T(preset.HintZh, preset.HintEn);
                if (preset.Id != "custom")
                {
                    host.Text = preset.Host;
                    port.Text = preset.Port.ToString();
                    ssl.IsChecked = preset.Ssl;
                }
            }
        }
        presetBox.SelectionChanged += (_, _) => ApplyPreset();
        ApplyPreset();

        panel.Children.Add(TextLine(Loc.T("邮箱服务", "Mail service"), 12, TextSecondary));
        panel.Children.Add(presetBox);
        panel.Children.Add(Labeled(Loc.T("邮箱地址", "Email address"), email));
        panel.Children.Add(Labeled(Loc.T("用户名（可空，默认邮箱）", "Username (optional, defaults to email)"), user));
        panel.Children.Add(TextLine(Loc.T("密码 / 授权码 / 应用专用密码", "Password / auth code / app password"), 12, TextSecondary));
        panel.Children.Add(password);
        panel.Children.Add(Labeled(Loc.T($"{protocol} 服务器", $"{protocol} server"), host));
        panel.Children.Add(Labeled(Loc.T("端口", "Port"), port));
        panel.Children.Add(ssl);
        panel.Children.Add(hint);

        bool saved = false;
        Button? save = null;
        var cancel = ActionButton(Loc.T("取消", "Cancel"), dialog.Close);
        cancel.Style = (Style)dialog.FindResource("SettingsOutlineButton"); cancel.IsCancel = true;
        save = ActionButton(Loc.T("绑定", "Bind"), async () =>
        {
            int fallbackPort = pop3 ? 995 : 993;
            var account = new BoundMailAccount
            {
                Kind = kind,
                Email = (email.Text ?? "").Trim(),
                DisplayName = (email.Text ?? "").Trim(),
                Username = string.IsNullOrWhiteSpace(user.Text) ? null : user.Text.Trim(),
                ImapHost = (host.Text ?? "").Trim(),
                ImapPort = int.TryParse(port.Text, out int parsed) ? parsed : fallbackPort,
                ImapSsl = ssl.IsChecked != false
            }.Normalize();
            if (account.Email.Length == 0 || account.ImapHost is null || account.ImapHost.Length == 0)
            {
                MessageBox.Show(dialog, Loc.T($"请填写邮箱地址和 {protocol} 服务器。", $"Enter the email address and {protocol} server."),
                    dialog.Title, MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            string secret = password.Password ?? "";
            try
            {
                if (save is not null) save.IsEnabled = false;
                if (pop3) await Pop3MailClient.TestAsync(account, secret).ConfigureAwait(true);
                else await ImapMailClient.TestAsync(account, secret).ConfigureAwait(true);
                var list = _app.MailAccounts.Load().ToList();
                list.Add(account);
                _app.MailAccounts.Save(list);
                _app.MailAccounts.WriteSecret(account.Id, secret);
                saved = true;
                dialog.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(dialog, ex.Message, Loc.T($"无法连接 {protocol}", $"{protocol} connection failed"),
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally { if (save is not null) save.IsEnabled = true; }
        }, primary: true);
        save.Style = (Style)dialog.FindResource("SettingsPrimaryButton");
        foreach (var button in new[] { cancel, save }) { button.MinWidth = 84; button.Padding = new Thickness(14, 6, 14, 6); }
        save.Margin = new Thickness(8, 0, 0, 0);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        buttons.Children.Add(cancel); buttons.Children.Add(save); panel.Children.Add(buttons);

        var frame = new Border { BorderThickness = new Thickness(1), Child = panel };
        frame.SetResourceReference(Border.BorderBrushProperty, "SettingsBorder");
        frame.SetResourceReference(Border.BackgroundProperty, "SettingsSurface");
        dialog.Content = frame;
        dialog.ShowDialog();
        if (!saved) return;
        _app.MailInbox?.Apply(S);
        _ = _app.MailInbox?.RefreshNowAsync();
        ShowPage(MailOtpSettingsPage);
    }

    private async void DisconnectAccount(BoundMailAccount account)
    {
        if (!Confirm(Loc.T("解除绑定", "Disconnect"),
            Loc.T($"确定解除「{account.Label}」吗？本机保存的登录信息会删除。",
                $"Disconnect “{account.Label}”? Sign-in data stored on this PC will be removed."),
            Loc.T("解除绑定", "Disconnect"))) return;
        try
        {
            if (account.Kind == MailAccountKind.MicrosoftGraph && _app.GraphMail is not null)
                await _app.GraphMail.SignOutAsync(account).ConfigureAwait(true);
        }
        catch (Exception) { }
        _app.MailAccounts.Remove(account.Id);
        _app.MailInbox?.Apply(S);
        ShowPage(MailOtpSettingsPage);
    }

    private async System.Threading.Tasks.Task SyncInboxNowAsync()
    {
        if (_app.MailInbox is null) return;
        await _app.MailInbox.RefreshNowAsync().ConfigureAwait(true);
        SaveStatus(Loc.T("已请求同步收件箱。", "Inbox sync requested."));
    }

    private void MailOtpSizeControls()
    {
        var dimensions = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        dimensions.ColumnDefinitions.Add(new ColumnDefinition());
        dimensions.ColumnDefinitions.Add(new ColumnDefinition());
        var width = Dimension(Loc.T("宽度", "Width"), S.MailOtpWidth, 440, 1200, value => Resize(s => s.MailOtpWidth = value, DeckPage.Mail));
        width.Margin = new Thickness(0, 0, 20, 0);
        var height = Dimension(Loc.T("高度", "Height"), S.MailOtpHeight, 140, 400, value => Resize(s => s.MailOtpHeight = value, DeckPage.Mail));
        height.Margin = new Thickness(20, 0, 0, 0);
        Grid.SetColumn(height, 1);
        dimensions.Children.Add(width);
        dimensions.Children.Add(height);
        Target.Children.Add(dimensions);
    }
}
