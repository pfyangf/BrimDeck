using BrimDeck.Core;

internal static class EmailOtpTests
{
    public static void Run(string root, Action<string, bool> check)
    {
        check("a plain notice is not a verification mail", !EmailOtp.LooksLikeVerification("Meeting", "See you at 3"));
        check("a Chinese verification subject is recognized", EmailOtp.LooksLikeVerification("登录验证码", "您的验证码是 384291，10 分钟内有效"));
        check("an English OTP subject is recognized", EmailOtp.LooksLikeVerification("Your verification code", "Use 384291 to sign in"));

        check("digits next to 验证码 are preferred", EmailOtp.ExtractCode("安全提示", "您的验证码是 384291，请勿泄露") == "384291");
        check("an English code is extracted", EmailOtp.ExtractCode("Security code for your account", "Use this code to sign in: 847291") == "847291");
        check("a year alone is not a code", EmailOtp.ExtractCode("验证码邮件", "Sent in 2024 for your account") is null
            || EmailOtp.ExtractCode("验证码邮件", "Sent in 2024 for your account") != "2024");
        check("a non-verification mail yields no code", EmailOtp.ExtractCode("Invoice", "Amount due 1200") is null);
        check("a 6-digit code is grouped", EmailOtp.Group("384291") == "384 291");
        check("digits-only strips spaces", EmailOtp.DigitsOnly("384 291") == "384291");

        var when = new DateTimeOffset(2026, 11, 5, 12, 0, 0, TimeSpan.FromHours(8));
        var toast = new ToastSnapshot("k1", "OUTLOOK.EXE", "Outlook", "Your verification code", "Code 112233 expires soon", when);
        var message = EmailOtp.FromToast(toast);
        check("an Outlook verification toast becomes a mail code", message is not null && message!.Code == "112233" && message.Source == "Outlook");
        var chat = new ToastSnapshot("k2", "QQ.exe", "QQ", "验证码 999111", "好友消息", when);
        check("a QQ toast is not treated as mail OTP", EmailOtp.FromToast(chat) is null);

        var feed = new EmailOtpFeed();
        check("ingest keeps the newest first", feed.Ingest(message) && feed.Items[0].Code == "112233");
        check("the same toast within a minute is ignored", !feed.Ingest(message));
        var other = message! with { Id = Guid.NewGuid(), Subject = "Another code", Body = "验证码 445566", Code = "445566", ReceivedAt = when.AddMinutes(2) };
        check("a later mail is prepended", feed.Ingest(other) && feed.Items[0].Code == "445566" && feed.Items.Count == 2);
        var failed = other with { Id = Guid.NewGuid(), Subject = "Security code for your account", Body = "Use this code to sign in: ******", Code = null, ReceivedAt = when.AddMinutes(3) };
        check("an unrecognized mail is kept for preview", feed.Ingest(failed) && feed.Items[0].Code is null);
        var retried = feed.Retry(failed.Id);
        check("retry leaves a still-masked body unrecognized", retried is not null && retried.Code is null);

        var previous = Loc.Language;
        try
        {
            Loc.Use(Loc.English);
            check("relative time speaks english", EmailOtp.Relative(when, when.AddMinutes(4)).Contains("minute", StringComparison.Ordinal));
            Loc.Use(Loc.Chinese);
            check("relative time speaks chinese", EmailOtp.Relative(when, when.AddMinutes(4)).Contains("分钟", StringComparison.Ordinal));
        }
        finally { Loc.Use(previous); }


        var bound = new BoundMailAccount { Kind = MailAccountKind.Imap, Email = "a@example.com", DisplayName = "Alice", ImapHost = "imap.example.com" }.Normalize();
        var boundMail = EmailOtp.FromBound(bound, "ext-1", "noreply@bank.com", "Your verification code", "Use 556677 to continue", when);
        check("FromBound tags the account", boundMail.AccountId == bound.Id && boundMail.ExternalId == "ext-1" && boundMail.Code == "556677" && boundMail.IsVerification);
        var ordinary = EmailOtp.FromBound(bound, "ext-2", null, "Weekly digest", "Nothing special here", when.AddMinutes(1));
        check("ordinary bound mail has no code", ordinary.Code is null && !ordinary.IsVerification);
        check("feed dedupes by external id", feed.Ingest(boundMail) && !feed.Ingest(boundMail with { Id = Guid.NewGuid() }));
        check("filtered toast-only uses empty guid", feed.Filtered(Guid.Empty, false).All(item => item.AccountId is null));
        check("filtered by account returns bound mail", feed.Filtered(bound.Id, false).Any(item => item.ExternalId == "ext-1"));
        check("ordinary bound mail is kept", feed.Ingest(ordinary));
        check("verification-only filter drops digests", feed.Filtered(bound.Id, true).All(item => item.IsVerification || !string.IsNullOrEmpty(item.Code)));

        var roundtrip = BoundMailAccounts.Import(BoundMailAccounts.Export([bound]));
        check("bound accounts round-trip", roundtrip.Count == 1 && roundtrip[0].Email == "a@example.com" && roundtrip[0].ImapHost == "imap.example.com");
        check("imap presets include gmail", ImapPresets.Find("gmail")?.Host == "imap.gmail.com");

        var pop3 = new BoundMailAccount { Kind = MailAccountKind.Pop3, Email = "b@example.com", DisplayName = "Bob", ImapHost = "pop.example.com", ImapPort = 995 }.Normalize();
        check("pop3 kind label", pop3.KindLabel == "POP3");
        check("pop3 default port stays 995", pop3.ImapPort == 995);
        var badPort = new BoundMailAccount { Kind = MailAccountKind.Pop3, Email = "c@example.com", ImapHost = "pop.example.com", ImapPort = 0 }.Normalize();
        check("pop3 invalid port falls back to 995", badPort.ImapPort == 995);
        var popRound = BoundMailAccounts.Import(BoundMailAccounts.Export([bound, pop3]));
        check("imap and pop3 accounts round-trip together",
            popRound.Count == 2
            && popRound[0].Kind == MailAccountKind.Imap && popRound[0].ImapHost == "imap.example.com"
            && popRound[1].Kind == MailAccountKind.Pop3 && popRound[1].ImapHost == "pop.example.com" && popRound[1].ImapPort == 995);
        check("legacy imap json still loads as imap",
            BoundMailAccounts.Import("""[{"id":"11111111-1111-1111-1111-111111111111","kind":1,"email":"old@example.com","imapHost":"imap.old.com","imapPort":993,"imapSsl":true}]""")[0].Kind == MailAccountKind.Imap);
        check("pop3 presets include gmail", Pop3Presets.Find("gmail")?.Host == "pop.gmail.com" && Pop3Presets.Find("gmail")?.Port == 995);
        check("pop3 presets include qq", Pop3Presets.Find("qq")?.Host == "pop.qq.com");
        check("pop3 presets include aliyun mxhichina", Pop3Presets.Find("aliyun-mx")?.Host == "pop3.mxhichina.com" && Pop3Presets.Find("aliyun-mx")?.Port == 995 && Pop3Presets.Find("aliyun-mx")?.SmtpHost == "smtp.mxhichina.com");
        check("imap presets include aliyun mxhichina", ImapPresets.Find("aliyun-mx")?.Host == "imap.mxhichina.com" && ImapPresets.Find("aliyun-mx")?.SmtpHost == "smtp.mxhichina.com");
        var withSmtp = new BoundMailAccount { Kind = MailAccountKind.Pop3, Email = "y@example.com", ImapHost = "pop3.mxhichina.com", ImapPort = 995, SmtpHost = "smtp.mxhichina.com", SmtpPort = 465, SmtpSsl = true, StartTls = false }.Normalize();
        var smtpRound = BoundMailAccounts.Import(BoundMailAccounts.Export([withSmtp]));
        check("smtp fields round-trip", smtpRound.Count == 1 && smtpRound[0].SmtpHost == "smtp.mxhichina.com" && smtpRound[0].SmtpPort == 465 && smtpRound[0].SmtpSsl && !smtpRound[0].StartTls);

                check("the mail page stays after authenticator", DeckPages.Order.ToList().IndexOf(DeckPage.Mail) == DeckPages.Order.ToList().IndexOf(DeckPage.Otp) + 1 && (int)DeckPage.Mail == 4 && !new DeckSettings().MailOtpPage);
        var legacy = System.Text.Json.JsonSerializer.Deserialize<DeckSettings>("""{"PageOrder":[0,1,2,3]}""")!;
        check("an older page order gains the mail and utility pages", legacy.PageOrder.SequenceEqual([DeckPage.Usage, DeckPage.Music, DeckPage.Monitor, DeckPage.Otp, DeckPage.Mail, DeckPage.Messages, DeckPage.Alarm, DeckPage.Pomodoro, DeckPage.Memo, DeckPage.Shortcuts, DeckPage.Tools]));
    }
}
