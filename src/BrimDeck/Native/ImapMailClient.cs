using BrimDeck.Core;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MimeKit;

namespace BrimDeck.Native;

// Fetches recent inbox messages over IMAP for Gmail / QQ / 163 / custom servers using an app password or authorization code.
internal static class ImapMailClient
{
    public static async Task<IReadOnlyList<EmailOtpMessage>> FetchRecentAsync(
        BoundMailAccount account,
        string password,
        int top = 25,
        CancellationToken cancel = default)
    {
        if (account.Kind != MailAccountKind.Imap) return [];
        string host = (account.ImapHost ?? "").Trim();
        if (host.Length == 0 || string.IsNullOrEmpty(password)) return [];
        string user = string.IsNullOrWhiteSpace(account.Username) ? account.Email : account.Username!.Trim();
        using var client = new ImapClient { Timeout = 30_000 };
        await client.ConnectAsync(host, account.ImapPort <= 0 ? 993 : account.ImapPort,
            SocketOptions(account),
            cancel).ConfigureAwait(false);
        try
        {
            await client.AuthenticateAsync(user, password, cancel).ConfigureAwait(false);
            var inbox = client.Inbox;
            await inbox.OpenAsync(FolderAccess.ReadOnly, cancel).ConfigureAwait(false);
            if (inbox.Count == 0) return [];
            int take = Math.Clamp(top, 1, 50);
            IList<UniqueId> uids = await inbox.SearchAsync(SearchQuery.All, cancel).ConfigureAwait(false);
            uids = uids.Reverse().Take(take).ToList();
            var list = new List<EmailOtpMessage>();
            foreach (var uid in uids)
            {
                cancel.ThrowIfCancellationRequested();
                var message = await inbox.GetMessageAsync(uid, cancel).ConfigureAwait(false);
                string subject = message.Subject ?? "";
                string body = BodyText(message);
                string? from = message.From?.Mailboxes.FirstOrDefault()?.Address
                    ?? message.From?.Mailboxes.FirstOrDefault()?.Name;
                DateTimeOffset when = message.Date.ToUniversalTime();
                list.Add(EmailOtp.FromBound(account, $"{account.Id:N}:{uid}", from, subject, body, when));
            }
            return list;
        }
        finally
        {
            if (client.IsConnected) await client.DisconnectAsync(true, cancel).ConfigureAwait(false);
        }
    }

    public static async Task TestAsync(BoundMailAccount account, string password, CancellationToken cancel = default)
    {
        string host = (account.ImapHost ?? "").Trim();
        if (host.Length == 0) throw new InvalidOperationException(Loc.T("请填写 IMAP 服务器。", "Enter an IMAP server."));
        if (string.IsNullOrEmpty(password)) throw new InvalidOperationException(Loc.T("请填写密码或授权码。", "Enter a password or authorization code."));
        string user = string.IsNullOrWhiteSpace(account.Username) ? account.Email : account.Username!.Trim();
        using var client = new ImapClient { Timeout = 20_000 };
        await client.ConnectAsync(host, account.ImapPort <= 0 ? 993 : account.ImapPort,
            SocketOptions(account),
            cancel).ConfigureAwait(false);
        try
        {
            await client.AuthenticateAsync(user, password, cancel).ConfigureAwait(false);
            await client.Inbox.OpenAsync(FolderAccess.ReadOnly, cancel).ConfigureAwait(false);
        }
        finally
        {
            if (client.IsConnected) await client.DisconnectAsync(true, cancel).ConfigureAwait(false);
        }
    }

    private static string BodyText(MimeMessage message)
    {
        if (!string.IsNullOrWhiteSpace(message.TextBody)) return message.TextBody.Trim();
        if (!string.IsNullOrWhiteSpace(message.HtmlBody))
        {
            string html = message.HtmlBody;
            var sb = new System.Text.StringBuilder(html.Length);
            bool hide = false;
            foreach (char c in html)
            {
                if (c == '<') { hide = true; continue; }
                if (c == '>') { hide = false; sb.Append(' '); continue; }
                if (!hide) sb.Append(c);
            }
            return System.Net.WebUtility.HtmlDecode(sb.ToString()).Trim();
        }
        return "";
    }
    // SSL → SslOnConnect; otherwise StartTlsWhenAvailable (Foxmail STARTTLS-when-supported / legacy non-SSL).
    private static MailKit.Security.SecureSocketOptions SocketOptions(BoundMailAccount account) =>
        account.ImapSsl ? MailKit.Security.SecureSocketOptions.SslOnConnect
        : MailKit.Security.SecureSocketOptions.StartTlsWhenAvailable;


}
