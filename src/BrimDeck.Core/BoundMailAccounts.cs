using System.Text.Json;
using System.Text.Json.Serialization;

namespace BrimDeck.Core;

// How a mailbox was bound for the email-codes inbox. Toast notifications need no bind.
public enum MailAccountKind { MicrosoftGraph = 0, Imap = 1 }

// One bound mailbox. Secrets (IMAP password, MSAL cache) live outside this record, encrypted for the Windows user.
public sealed class BoundMailAccount
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public MailAccountKind Kind { get; set; }
    public string Email { get; set; } = "";
    public string DisplayName { get; set; } = "";
    // IMAP only. Graph uses MSAL + Graph; these stay empty.
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
        _ => Kind.ToString()
    };

    public BoundMailAccount Normalize()
    {
        Email = (Email ?? "").Trim();
        DisplayName = (DisplayName ?? "").Trim();
        ImapHost = string.IsNullOrWhiteSpace(ImapHost) ? null : ImapHost.Trim();
        Username = string.IsNullOrWhiteSpace(Username) ? null : Username.Trim();
        GraphHomeAccountId = string.IsNullOrWhiteSpace(GraphHomeAccountId) ? null : GraphHomeAccountId.Trim();
        if (ImapPort is < 1 or > 65535) ImapPort = 993;
        return this;
    }
}

// Well-known IMAP presets offered in Settings › Email codes. Custom lets the user type host/port.
public static class ImapPresets
{
    public sealed record Preset(string Id, string Title, string Host, int Port, bool Ssl, string HintZh, string HintEn);

    public static IReadOnlyList<Preset> All { get; } =
    [
        new("gmail", "Gmail", "imap.gmail.com", 993, true,
            "请使用 Google 账号的「应用专用密码」，不是平时的登录密码。",
            "Use a Google app password, not your normal sign-in password."),
        new("qq", "QQ 邮箱", "imap.qq.com", 993, true,
            "在 QQ 邮箱设置里开启 IMAP，并使用授权码。",
            "Enable IMAP in QQ Mail settings and use the authorization code."),
        new("163", "163 邮箱", "imap.163.com", 993, true,
            "在 163 邮箱设置里开启 IMAP，并使用授权码。",
            "Enable IMAP in 163 Mail settings and use the authorization code."),
        new("outlook-imap", "Outlook.com (IMAP)", "outlook.office365.com", 993, true,
            "个人 Outlook.com 更推荐用「添加 Microsoft 账号」。IMAP 可能需要应用密码或 OAuth。",
            "Prefer Add Microsoft account for personal Outlook.com. IMAP may need an app password or OAuth."),
        new("custom", "Custom IMAP", "", 993, true,
            "填写服务器、端口，并使用邮箱密码或应用专用密码。",
            "Enter the server and port, and use the mailbox password or an app password.")
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
