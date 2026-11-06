using BrimDeck.Core;
using MailKit.Net.Pop3;
using MimeKit;

namespace BrimDeck.Native;

// Fetches recent inbox messages over POP3 for Gmail / QQ / 163 / custom servers using an app password or authorization code.
// Messages are read only — never deleted from the server (no DELE).
internal static class Pop3MailClient
{
    public static async Task<IReadOnlyList<EmailOtpMessage>> FetchRecentAsync(
        BoundMailAccount account,
        string password,
        int top = 25,
        CancellationToken cancel = default)
    {
        if (account.Kind != MailAccountKind.Pop3) return [];
        string host = (account.ImapHost ?? "").Trim();
        if (host.Length == 0 || string.IsNullOrEmpty(password)) return [];
        string user = string.IsNullOrWhiteSpace(account.Username) ? account.Email : account.Username!.Trim();
        using var client = new Pop3Client { Timeout = 30_000 };
        await client.ConnectAsync(host, account.ImapPort <= 0 ? 995 : account.ImapPort,
            account.ImapSsl ? MailKit.Security.SecureSocketOptions.SslOnConnect : MailKit.Security.SecureSocketOptions.StartTlsWhenAvailable,
            cancel).ConfigureAwait(false);
        try
        {
            await client.AuthenticateAsync(user, password, cancel).ConfigureAwait(false);
            int count = client.Count;
            if (count == 0) return [];
            int take = Math.Clamp(top, 1, 50);
            int start = Math.Max(0, count - take);
            var list = new List<EmailOtpMessage>();
            for (int i = start; i < count; i++)
            {
                cancel.ThrowIfCancellationRequested();
                var message = await client.GetMessageAsync(i, cancel).ConfigureAwait(false);
                string subject = message.Subject ?? "";
                string body = BodyText(message);
                string? from = message.From?.Mailboxes.FirstOrDefault()?.Address
                    ?? message.From?.Mailboxes.FirstOrDefault()?.Name;
                DateTimeOffset when = message.Date.ToUniversalTime();
                string external = ExternalId(account, client, i, message);
                list.Add(EmailOtp.FromBound(account, external, from, subject, body, when));
            }
            // Newest first to match IMAP / Graph ingest order.
            list.Reverse();
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
        if (host.Length == 0) throw new InvalidOperationException(Loc.T("请填写 POP3 服务器。", "Enter a POP3 server."));
        if (string.IsNullOrEmpty(password)) throw new InvalidOperationException(Loc.T("请填写密码或授权码。", "Enter a password or authorization code."));
        string user = string.IsNullOrWhiteSpace(account.Username) ? account.Email : account.Username!.Trim();
        using var client = new Pop3Client { Timeout = 20_000 };
        await client.ConnectAsync(host, account.ImapPort <= 0 ? 995 : account.ImapPort,
            account.ImapSsl ? MailKit.Security.SecureSocketOptions.SslOnConnect : MailKit.Security.SecureSocketOptions.StartTlsWhenAvailable,
            cancel).ConfigureAwait(false);
        try
        {
            await client.AuthenticateAsync(user, password, cancel).ConfigureAwait(false);
            _ = client.Count;
        }
        finally
        {
            if (client.IsConnected) await client.DisconnectAsync(true, cancel).ConfigureAwait(false);
        }
    }

    private static string ExternalId(BoundMailAccount account, Pop3Client client, int index, MimeMessage message)
    {
        string? messageId = (message.MessageId ?? "").Trim();
        if (messageId.Length > 0) return $"{account.Id:N}:mid:{messageId}";
        try
        {
            // GetMessageUid is sync on MailKit Pop3Client; prefer UIDL when the server supports it.
            string uid = client.GetMessageUid(index);
            if (!string.IsNullOrWhiteSpace(uid)) return $"{account.Id:N}:uid:{uid.Trim()}";
        }
        catch (Exception)
        {
            // Some servers omit UIDL; fall through to index + date.
        }
        return $"{account.Id:N}:idx:{index}:{message.Date.UtcTicks}";
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
}
