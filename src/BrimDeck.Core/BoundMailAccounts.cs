using System.Text.Json;
using System.Text.Json.Serialization;

namespace BrimDeck.Core;

// How a mailbox was bound for the email-codes inbox. Toast notifications need no bind.
public enum MailAccountKind { MicrosoftGraph = 0, Imap = 1, Pop3 = 2 }

// One bound mailbox. Secrets (IMAP/POP3 password, MSAL cache) live outside this record, encrypted for the Windows user.
public sealed class BoundMailAccount
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public MailAccountKind Kind { get; set; }
    public string Email { get; set; } = "";
    public string DisplayName { get; set; } = "";
    // IMAP / POP3 server. Graph uses MSAL + Graph; these stay empty.
    // Property names stay Imap* for JSON compatibility with already-bound IMAP accounts; POP3 reuses the same fields.
    public string? ImapHost { get; set; }
    public int ImapPort { get; set; } = 993;
    public bool ImapSsl { get; set; } = true;
    public string? Username { get; set; }
    // MSAL home account id when Kind is MicrosoftGraph; used to pick the right cached account.
    public string? GraphHomeAccountId { get; set; }

    public string Label
    {
        get
        {
            string name = (DisplayName ?? "").Trim();
            string email = (Email ?? "").Trim();
            if (name.Length > 0 && email.Length > 0 && !name.Equals(email, StringComparison.OrdinalIgnoreCase))
                return name + " · " + email;
            return email.Length > 0 ? email : name.Length > 0 ? name : Loc.T("邮箱", "Mailbox");
        }
    }

    public string KindLabel => Kind switch
    {
        MailAccountKind.MicrosoftGraph => "Microsoft",
        MailAccountKind.Imap => "IMAP",
        MailAccountKind.Pop3 => "POP3",
        _ => Kind.ToString()
    };

    public BoundMailAccount Normalize()
    {
        Email = (Email ?? "").Trim();
        DisplayName = (DisplayName ?? "").Trim();
        ImapHost = string.IsNullOrWhiteSpace(ImapHost) ? null : ImapHost.Trim();
        Username = string.IsNullOrWhiteSpace(Username) ? null : Username.Trim();
        GraphHomeAccountId = string.IsNullOrWhiteSpace(GraphHomeAccountId) ? null : GraphHomeAccountId.Trim();
        int fallback = Kind == MailAccountKind.Pop3 ? 995 : 993;
        if (ImapPort is < 1 or > 65535) ImapPort = fallback;
        return this;
    }
}

// Well-known IMAP presets offered in Settings › Email codes. Custom lets the user type host/port.
public static class ImapPresets
{
    public sealed record Preset(string Id, string Title, string Host, int Port, bool Ssl, string HintZh, string HintEn);

    public static IReadOnlyList<Preset> All { get; } =
    [
        new("qq", "QQ 邮箱", "imap.qq.com", 993, true,
            "在 QQ 邮箱设置里开启 IMAP，并使用授权码（不是登录密码）。",
            "Enable IMAP in QQ Mail settings and use the authorization code (not the login password)."),
        new("foxmail", "Foxmail", "imap.qq.com", 993, true,
            "Foxmail / QQ 邮箱共用授权码；在 QQ 邮箱设置里开启 IMAP。",
            "Foxmail shares QQ Mail’s authorization code; enable IMAP in QQ Mail settings."),
        new("163", "163 邮箱", "imap.163.com", 993, true,
            "在 163 邮箱设置里开启 IMAP，并使用授权码。",
            "Enable IMAP in 163 Mail settings and use the authorization code."),
        new("126", "126 邮箱", "imap.126.com", 993, true,
            "在 126 邮箱设置里开启 IMAP，并使用授权码。",
            "Enable IMAP in 126 Mail settings and use the authorization code."),
        new("yeah", "Yeah 邮箱", "imap.yeah.net", 993, true,
            "在 Yeah.net 邮箱设置里开启 IMAP，并使用授权码。",
            "Enable IMAP in Yeah.net settings and use the authorization code."),
        new("gmail", "Gmail", "imap.gmail.com", 993, true,
            "请使用 Google 账号的「应用专用密码」，不是平时的登录密码。",
            "Use a Google app password, not your normal sign-in password."),
        new("outlook-imap", "Outlook.com (IMAP)", "outlook.office365.com", 993, true,
            "个人 Outlook.com 更推荐用「添加 Microsoft 账号」。IMAP 可能需要应用密码或 OAuth。",
            "Prefer Add Microsoft account for personal Outlook.com. IMAP may need an app password or OAuth."),
        new("icloud", "iCloud", "imap.mail.me.com", 993, true,
            "在 appleid.apple.com 生成「App 专用密码」。",
            "Generate an app-specific password at appleid.apple.com."),
        new("yahoo", "Yahoo Mail", "imap.mail.yahoo.com", 993, true,
            "在 Yahoo 账号安全设置里生成应用专用密码。",
            "Generate an app password in Yahoo account security settings."),
        new("aliyun", "阿里云邮箱", "imap.qiye.aliyun.com", 993, true,
            "企业邮箱请用阿里云提供的 IMAP 主机；个人阿里云邮箱多为 imap.aliyun.com。",
            "Use the IMAP host from Aliyun; personal Aliyun mail is often imap.aliyun.com."),
        new("139", "139 邮箱", "imap.139.com", 993, true,
            "在 139 邮箱设置里开启 IMAP，并使用授权码。",
            "Enable IMAP in 139 Mail settings and use the authorization code."),
        new("sina", "新浪邮箱", "imap.sina.com", 993, true,
            "在新浪邮箱设置里开启 IMAP，并使用授权码或密码。",
            "Enable IMAP in Sina Mail settings and use an auth code or password."),
        new("sohu", "搜狐邮箱", "imap.sohu.com", 993, true,
            "在搜狐邮箱设置里开启 IMAP，并使用授权码或密码。",
            "Enable IMAP in Sohu Mail settings and use an auth code or password."),
        new("custom", "自定义 IMAP", "", 993, true,
            "填写服务器、端口，并使用邮箱密码或应用专用密码。",
            "Enter the server and port, and use the mailbox password or an app password.")
    ];

    public static Preset? Find(string? id) => All.FirstOrDefault(p => p.Id.Equals(id ?? "", StringComparison.OrdinalIgnoreCase));
}

// Well-known POP3 presets offered in Settings › Email codes. Custom lets the user type host/port.
public static class Pop3Presets
{
    public sealed record Preset(string Id, string Title, string Host, int Port, bool Ssl, string HintZh, string HintEn);

    public static IReadOnlyList<Preset> All { get; } =
    [
        new("qq", "QQ 邮箱", "pop.qq.com", 995, true,
            "在 QQ 邮箱设置里开启 POP3，并使用授权码（不是登录密码）。BrimDeck 只读取近期邮件，不会从服务器删除。",
            "Enable POP3 in QQ Mail settings and use the authorization code (not the login password). BrimDeck only reads recent mail and does not delete it from the server."),
        new("foxmail", "Foxmail", "pop.qq.com", 995, true,
            "Foxmail / QQ 邮箱共用授权码；在 QQ 邮箱设置里开启 POP3。BrimDeck 不会删除服务器上的邮件。",
            "Foxmail shares QQ Mail’s authorization code; enable POP3 in QQ Mail settings. BrimDeck does not delete mail on the server."),
        new("163", "163 邮箱", "pop.163.com", 995, true,
            "在 163 邮箱设置里开启 POP3，并使用授权码。BrimDeck 不会删除服务器上的邮件。",
            "Enable POP3 in 163 Mail settings and use the authorization code. BrimDeck does not delete mail on the server."),
        new("126", "126 邮箱", "pop.126.com", 995, true,
            "在 126 邮箱设置里开启 POP3，并使用授权码。BrimDeck 不会删除服务器上的邮件。",
            "Enable POP3 in 126 Mail settings and use the authorization code. BrimDeck does not delete mail on the server."),
        new("yeah", "Yeah 邮箱", "pop.yeah.net", 995, true,
            "在 Yeah.net 邮箱设置里开启 POP3，并使用授权码。BrimDeck 不会删除服务器上的邮件。",
            "Enable POP3 in Yeah.net settings and use the authorization code. BrimDeck does not delete mail on the server."),
        new("gmail", "Gmail", "pop.gmail.com", 995, true,
            "请使用 Google 账号的「应用专用密码」，并在 Gmail 设置中开启 POP。BrimDeck 不会删除服务器上的邮件。",
            "Use a Google app password and enable POP in Gmail settings. BrimDeck does not delete mail on the server."),
        new("outlook-pop3", "Outlook.com (POP3)", "outlook.office365.com", 995, true,
            "个人 Outlook.com 更推荐用「添加 Microsoft 账号」。POP3 可能需要应用密码或 OAuth。BrimDeck 不会删除服务器上的邮件。",
            "Prefer Add Microsoft account for personal Outlook.com. POP3 may need an app password or OAuth. BrimDeck does not delete mail on the server."),
        new("icloud", "iCloud", "pop.mail.me.com", 995, true,
            "在 appleid.apple.com 生成「App 专用密码」。部分 iCloud 账号可能仅支持 IMAP。BrimDeck 不会删除服务器上的邮件。",
            "Generate an app-specific password at appleid.apple.com. Some iCloud accounts may only support IMAP. BrimDeck does not delete mail on the server."),
        new("yahoo", "Yahoo Mail", "pop.mail.yahoo.com", 995, true,
            "在 Yahoo 账号安全设置里生成应用专用密码。BrimDeck 不会删除服务器上的邮件。",
            "Generate an app password in Yahoo account security settings. BrimDeck does not delete mail on the server."),
        new("aliyun", "阿里云邮箱", "pop.qiye.aliyun.com", 995, true,
            "企业邮箱请用阿里云提供的 POP3 主机；个人阿里云邮箱多为 pop.aliyun.com。BrimDeck 不会删除服务器上的邮件。",
            "Use the POP3 host from Aliyun; personal Aliyun mail is often pop.aliyun.com. BrimDeck does not delete mail on the server."),
        new("139", "139 邮箱", "pop.139.com", 995, true,
            "在 139 邮箱设置里开启 POP3，并使用授权码。BrimDeck 不会删除服务器上的邮件。",
            "Enable POP3 in 139 Mail settings and use the authorization code. BrimDeck does not delete mail on the server."),
        new("sina", "新浪邮箱", "pop3.sina.com", 995, true,
            "在新浪邮箱设置里开启 POP3，并使用授权码或密码。BrimDeck 不会删除服务器上的邮件。",
            "Enable POP3 in Sina Mail settings and use an auth code or password. BrimDeck does not delete mail on the server."),
        new("sohu", "搜狐邮箱", "pop3.sohu.com", 995, true,
            "在搜狐邮箱设置里开启 POP3，并使用授权码或密码。BrimDeck 不会删除服务器上的邮件。",
            "Enable POP3 in Sohu Mail settings and use an auth code or password. BrimDeck does not delete mail on the server."),
        new("custom", "自定义 POP3", "", 995, true,
            "填写服务器、端口，并使用邮箱密码或应用专用密码。BrimDeck 只读取近期邮件，不会从服务器删除。",
            "Enter the server and port, and use the mailbox password or an app password. BrimDeck only reads recent mail and does not delete it from the server.")
    ];

    public static Preset? Find(string? id) => All.FirstOrDefault(p => p.Id.Equals(id ?? "", StringComparison.OrdinalIgnoreCase));
}

// JSON of bound accounts (no passwords). Encrypted on disk by MailAccountStore with DPAPI.
public static class BoundMailAccounts
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    public static string Export(IReadOnlyList<BoundMailAccount> accounts) =>
        JsonSerializer.Serialize(accounts.Select(a => a.Normalize()).ToList(), Json);

    public static IReadOnlyList<BoundMailAccount> Import(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        var list = JsonSerializer.Deserialize<List<BoundMailAccount>>(json, Json) ?? [];
        var seen = new HashSet<Guid>();
        foreach (var account in list)
        {
            account.Normalize();
            if (account.Id == Guid.Empty || !seen.Add(account.Id))
            {
                do { account.Id = Guid.NewGuid(); } while (!seen.Add(account.Id));
            }
            if (!Enum.IsDefined(account.Kind)) account.Kind = MailAccountKind.Imap;
        }
        return list;
    }
}
