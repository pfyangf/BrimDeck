using System.Text.RegularExpressions;

namespace BrimDeck.Core;

// One verification-looking mail the island may show. Code is null when digits could not be extracted; the page then
// offers a preview of Subject and Body. Source is a short label for the client (Outlook, Mail, …).
public sealed record EmailOtpMessage(
    Guid Id,
    string Source,
    string Account,
    string? From,
    string Subject,
    string Body,
    string? Code,
    DateTimeOffset ReceivedAt,
    Guid? AccountId = null,
    string? ExternalId = null,
    bool IsVerification = true);

// Pulls a one-time code out of a mail toast or a pasted preview. Only looks at text that already looks like a
// verification mail, so ordinary notifications are not treated as codes.
public static partial class EmailOtp
{
    public const int Capacity = 48;

    public static bool LooksLikeVerification(string? title, string? body)
    {
        string text = ((title ?? "") + "\n" + (body ?? "")).Trim();
        if (text.Length == 0) return false;
        return Keyword().IsMatch(text);
    }

    // Digits when a verification mail carries a clear code; null when it does not. Prefers a code next to a keyword,
    // then a lone 4–8 digit run that is not a year.
    public static string? ExtractCode(string? title, string? body)
    {
        string text = ((title ?? "") + "\n" + (body ?? "")).Trim();
        if (text.Length == 0 || !LooksLikeVerification(title, body)) return null;
        foreach (Match match in NearKeyword().Matches(text))
            if (Usable(match.Groups[1].Value) is { } code) return code;
        foreach (Match match in Digits().Matches(text))
            if (Usable(match.Value) is { } code) return code;
        return null;
    }

    public static EmailOtpMessage? FromToast(ToastSnapshot toast, string? account = null)
    {
        if (MessageNotices.Classify(toast.AppId, toast.DisplayName) != MessageApp.Mail) return null;
        if (!LooksLikeVerification(toast.Title, toast.Body)) return null;
        string source = SourceLabel(toast.DisplayName);
        string subject = (toast.Title ?? "").Trim();
        string body = (toast.Body ?? "").Trim();
        string? from = GuessFrom(subject, body);
        return new EmailOtpMessage(Guid.NewGuid(), source, account ?? source, from, subject, body,
            ExtractCode(subject, body), toast.Created);
    }

    public static EmailOtpMessage? FromNotice(MessageNotice notice, DateTimeOffset when, string? account = null)
    {
        if (notice.App != MessageApp.Mail || !LooksLikeVerification(notice.Title, notice.Body)) return null;
        string subject = (notice.Title ?? "").Trim();
        string body = (notice.Body ?? "").Trim();
        return new EmailOtpMessage(Guid.NewGuid(), Loc.T("邮件", "Mail"), account ?? Loc.T("邮件", "Mail"),
            GuessFrom(subject, body), subject, body, ExtractCode(subject, body), when);
    }

    // Groups a 6-digit code as "384 291"; other lengths stay unbroken.
    public static string Group(string? code)
    {
        string digits = DigitsOnly(code);
        if (digits.Length == 6) return digits[..3] + " " + digits[3..];
        return digits;
    }

    public static string DigitsOnly(string? code) => string.Concat((code ?? "").Where(char.IsDigit));

    public static string Relative(DateTimeOffset when, DateTimeOffset now)
    {
        var span = now - when;
        if (span < TimeSpan.Zero) span = TimeSpan.Zero;
        if (span.TotalSeconds < 45) return Loc.T("刚刚", "just now");
        if (span.TotalMinutes < 60)
        {
            int m = Math.Max(1, (int)Math.Round(span.TotalMinutes));
            return Loc.T($"约 {m} 分钟前", m == 1 ? "about 1 minute ago" : $"about {m} minutes ago");
        }
        if (span.TotalHours < 24)
        {
            int h = Math.Max(1, (int)Math.Round(span.TotalHours));
            return Loc.T($"约 {h} 小时前", h == 1 ? "about 1 hour ago" : $"about {h} hours ago");
        }
        int d = Math.Max(1, (int)Math.Round(span.TotalDays));
        return Loc.T($"约 {d} 天前", d == 1 ? "about 1 day ago" : $"about {d} days ago");
    }

    private static string? Usable(string digits)
    {
        if (digits.Length is < 4 or > 8) return null;
        if (digits.Length == 4 && int.TryParse(digits, out int year) && year is >= 1990 and <= 2099) return null;
        return digits;
    }

    private static string SourceLabel(string? displayName)
    {
        string name = (displayName ?? "").Trim();
        if (name.Length == 0) return Loc.T("邮件", "Mail");
        if (name.Contains("Outlook", StringComparison.OrdinalIgnoreCase) || name.Contains("Outlook", StringComparison.Ordinal)
            || name.Contains("新 Outlook", StringComparison.Ordinal) || name.Contains("新版 Outlook", StringComparison.Ordinal))
            return "Outlook";
        if (name.Contains("Gmail", StringComparison.OrdinalIgnoreCase)) return "Gmail";
        if (name.Contains("Thunderbird", StringComparison.OrdinalIgnoreCase)) return "Thunderbird";
        if (name.Contains("Foxmail", StringComparison.OrdinalIgnoreCase)) return "Foxmail";
        return name;
    }

    private static string? GuessFrom(string subject, string body)
    {
        var match = FromAddress().Match(subject + " " + body);
        return match.Success ? match.Value : null;
    }

    [GeneratedRegex(@"验证码|校验码|登录码|动态码|安全码|verification\s*code|security\s*code|one[-\s]?time(?:\s+pass(?:word|code)?|\s+code)|\bOTP\b|\b2FA\b|auth(?:entication)?\s*code",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Keyword();

    [GeneratedRegex(@"(?:验证码|校验码|登录码|动态码|security\s*code|verification\s*code|one[-\s]?time(?:\s+pass(?:word|code)?|\s+code)|OTP|code|码)[^\d]{0,24}(\d{4,8})",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NearKeyword();

    [GeneratedRegex(@"\b(\d{4,8})\b", RegexOptions.CultureInvariant)]
    private static partial Regex Digits();

    [GeneratedRegex(@"[A-Z0-9._%+\-]+@[A-Z0-9.\-]+\.[A-Z]{2,}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FromAddress();

    // A message fetched from a bound Microsoft Graph, IMAP, or POP3 mailbox. Code is filled only when the mail looks like a verification.
    public static EmailOtpMessage FromBound(
        BoundMailAccount account,
        string externalId,
        string? from,
        string subject,
        string body,
        DateTimeOffset receivedAt)
    {
        subject = (subject ?? "").Trim();
        body = (body ?? "").Trim();
        bool verification = LooksLikeVerification(subject, body);
        return new EmailOtpMessage(
            Guid.NewGuid(),
            account.KindLabel,
            account.Label,
            string.IsNullOrWhiteSpace(from) ? GuessFrom(subject, body) : from.Trim(),
            subject,
            body,
            verification ? ExtractCode(subject, body) : null,
            receivedAt,
            account.Id,
            string.IsNullOrWhiteSpace(externalId) ? null : externalId.Trim(),
            verification);
    }
}

// Newest mails from toasts and bound mailboxes (Graph / IMAP / POP3). Keeps only Capacity entries.
public sealed class EmailOtpFeed
{
    private readonly List<EmailOtpMessage> _items = [];
    private readonly object _gate = new();

    public event Action? Changed;

    public IReadOnlyList<EmailOtpMessage> Items
    {
        get { lock (_gate) return _items.ToList(); }
    }

    public void Clear()
    {
        lock (_gate) _items.Clear();
        Changed?.Invoke();
    }

    public bool Ingest(EmailOtpMessage? message)
    {
        if (message is null) return false;
        lock (_gate)
        {
            if (!string.IsNullOrEmpty(message.ExternalId)
                && _items.Any(item => item.ExternalId == message.ExternalId
                    && item.AccountId == message.AccountId)) return false;
            // Same subject+body within a minute is the same toast or poll replayed.
            if (_items.Any(item => item.Subject == message.Subject && item.Body == message.Body
                && Math.Abs((item.ReceivedAt - message.ReceivedAt).TotalSeconds) < 60)) return false;
            _items.Insert(0, message);
            while (_items.Count > EmailOtp.Capacity) _items.RemoveAt(_items.Count - 1);
        }
        Changed?.Invoke();
        return true;
    }

    public IReadOnlyList<EmailOtpMessage> Filtered(Guid? accountId, bool verificationOnly)
    {
        lock (_gate)
        {
            IEnumerable<EmailOtpMessage> query = _items;
            if (accountId is { } id)
                query = query.Where(item => item.AccountId == id
                    || (id == Guid.Empty && item.AccountId is null));
            if (verificationOnly) query = query.Where(item => item.IsVerification || !string.IsNullOrEmpty(item.Code));
            return query.ToList();
        }
    }

    // Re-runs extraction on the message at index (preview "retry"). Returns the updated list index message, or null.
    public EmailOtpMessage? Retry(Guid id)
    {
        EmailOtpMessage? next;
        lock (_gate)
        {
            int at = _items.FindIndex(item => item.Id == id);
            if (at < 0) return null;
            var current = _items[at];
            next = current with { Code = EmailOtp.ExtractCode(current.Subject, current.Body) };
            _items[at] = next;
        }
        Changed?.Invoke();
        return next;
    }
}
