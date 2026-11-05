using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using BrimDeck.Core;
using Microsoft.Identity.Client;

namespace BrimDeck.Native;

// Optional Microsoft account bind for the email-codes inbox via MSAL + Microsoft Graph (Mail.Read).
// Requires DeckSettings.MailOtpMsClientId (Azure AD public client). The MSAL cache is DPAPI-sealed in msal-mail.dat.
internal sealed class GraphMailClient
{
    public static readonly string[] Scopes = ["User.Read", "Mail.Read", "offline_access"];
    private static readonly HttpClient Http = CreateHttp();
    private readonly string _directory;
    private readonly ISecretProtector _protector;
    private readonly Func<string> _clientId;
    private readonly Func<Window?> _owner;
    private readonly object _cacheGate = new();
    private IPublicClientApplication? _app;
    private string? _boundClientId;

    public GraphMailClient(string directory, ISecretProtector protector, Func<string> clientId, Func<Window?> owner)
    {
        _directory = directory;
        _protector = protector;
        _clientId = clientId;
        _owner = owner;
    }

    private string CachePath => Path.Combine(_directory, "msal-mail.dat");

    public bool HasClientId
    {
        get
        {
            string id = (_clientId() ?? "").Trim();
            return id.Length >= 8 && Guid.TryParse(id, out _);
        }
    }

    public async Task<BoundMailAccount> SignInAsync(CancellationToken cancel = default)
    {
        var app = App();
        var window = _owner();
        IntPtr handle = window is null ? IntPtr.Zero : new WindowInteropHelper(window).EnsureHandle();
        AuthenticationResult result;
        try
        {
            var interactive = app.AcquireTokenInteractive(Scopes).WithPrompt(Prompt.SelectAccount);
            if (handle != IntPtr.Zero) interactive = interactive.WithParentActivityOrWindow(handle);
            result = await interactive.ExecuteAsync(cancel).ConfigureAwait(false);
        }
        catch (MsalException)
        {
            result = await app.AcquireTokenWithDeviceCode(Scopes, info =>
            {
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    MessageBox.Show(
                        Loc.T($"请在浏览器打开 {info.VerificationUrl}，并输入代码：\n\n{info.UserCode}",
                            $"Open {info.VerificationUrl} in a browser and enter this code:\n\n{info.UserCode}"),
                        Loc.T("登录 Microsoft 账号", "Sign in to Microsoft"),
                        MessageBoxButton.OK, MessageBoxImage.Information);
                });
                return Task.CompletedTask;
            }).ExecuteAsync(cancel).ConfigureAwait(false);
        }

        string email = result.Account.Username?.Trim() ?? "";
        string name = result.ClaimsPrincipal?.FindFirst("name")?.Value?.Trim()
            ?? result.Account.Username?.Trim()
            ?? Loc.T("Microsoft 账号", "Microsoft account");
        return new BoundMailAccount
        {
            Id = Guid.NewGuid(),
            Kind = MailAccountKind.MicrosoftGraph,
            Email = email,
            DisplayName = name,
            GraphHomeAccountId = result.Account.HomeAccountId?.Identifier
        }.Normalize();
    }

    public async Task SignOutAsync(BoundMailAccount account, CancellationToken cancel = default)
    {
        if (!HasClientId) return;
        var app = App();
        foreach (var cached in await app.GetAccountsAsync().ConfigureAwait(false))
        {
            bool match = account.GraphHomeAccountId is { Length: > 0 } home
                && cached.HomeAccountId?.Identifier == home;
            match = match || (!string.IsNullOrEmpty(account.Email)
                && string.Equals(cached.Username, account.Email, StringComparison.OrdinalIgnoreCase));
            if (!match) continue;
            await app.RemoveAsync(cached).ConfigureAwait(false);
            return;
        }
    }

    public async Task<IReadOnlyList<EmailOtpMessage>> FetchRecentAsync(BoundMailAccount account, int top = 25, CancellationToken cancel = default)
    {
        if (account.Kind != MailAccountKind.MicrosoftGraph) return [];
        string token = await AccessTokenAsync(account, cancel).ConfigureAwait(false);
        if (token.Length == 0) return [];
        string url = "https://graph.microsoft.com/v1.0/me/mailFolders/inbox/messages"
            + $"?$top={Math.Clamp(top, 1, 50)}"
            + "&$orderby=receivedDateTime desc"
            + "&$select=id,subject,bodyPreview,body,from,receivedDateTime";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await Http.SendAsync(request, cancel).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) return [];
        await using var stream = await response.Content.ReadAsStreamAsync(cancel).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancel).ConfigureAwait(false);
        if (!doc.RootElement.TryGetProperty("value", out var values) || values.ValueKind != JsonValueKind.Array)
            return [];
        var list = new List<EmailOtpMessage>();
        foreach (var item in values.EnumerateArray())
        {
            string id = Prop(item, "id");
            string subject = Prop(item, "subject");
            string preview = Prop(item, "bodyPreview");
            string body = preview;
            if (item.TryGetProperty("body", out var bodyNode) && bodyNode.TryGetProperty("content", out var content))
            {
                string raw = content.GetString() ?? "";
                if (raw.Length > 0) body = StripHtml(raw);
            }
            string? from = null;
            if (item.TryGetProperty("from", out var fromNode) && fromNode.TryGetProperty("emailAddress", out var addr))
            {
                string address = Prop(addr, "address");
                string name = Prop(addr, "name");
                from = address.Length > 0 ? address : name.Length > 0 ? name : null;
            }
            DateTimeOffset when = DateTimeOffset.Now;
            if (item.TryGetProperty("receivedDateTime", out var whenNode)
                && whenNode.ValueKind == JsonValueKind.String
                && DateTimeOffset.TryParse(whenNode.GetString(), out var parsed))
                when = parsed;
            list.Add(EmailOtp.FromBound(account, id, from, subject, body, when));
        }
        return list;
    }

    private async Task<string> AccessTokenAsync(BoundMailAccount account, CancellationToken cancel)
    {
        var app = App();
        IAccount? cached = null;
        foreach (var entry in await app.GetAccountsAsync().ConfigureAwait(false))
        {
            if (account.GraphHomeAccountId is { Length: > 0 } home
                && entry.HomeAccountId?.Identifier == home) { cached = entry; break; }
            if (!string.IsNullOrEmpty(account.Email)
                && string.Equals(entry.Username, account.Email, StringComparison.OrdinalIgnoreCase))
            { cached = entry; break; }
        }
        if (cached is null) return "";
        try
        {
            var result = await app.AcquireTokenSilent(Scopes, cached).ExecuteAsync(cancel).ConfigureAwait(false);
            return result.AccessToken ?? "";
        }
        catch (MsalUiRequiredException) { return ""; }
    }

    private IPublicClientApplication App()
    {
        string clientId = (_clientId() ?? "").Trim();
        if (clientId.Length == 0 || !Guid.TryParse(clientId, out _))
            throw new InvalidOperationException(Loc.T(
                "请先在设置里填写 Azure 应用（公共客户端）的应用程序（客户端）ID。",
                "Enter an Azure app (public client) Application (client) ID in settings first."));
        if (_app is not null && _boundClientId == clientId) return _app;
        _app = PublicClientApplicationBuilder.Create(clientId)
            .WithRedirectUri("http://localhost")
            .WithAuthority(AzureCloudInstance.AzurePublic, AadAuthorityAudience.AzureAdAndPersonalMicrosoftAccount)
            .Build();
        _boundClientId = clientId;
        _app.UserTokenCache.SetBeforeAccess(args =>
        {
            lock (_cacheGate)
            {
                if (!File.Exists(CachePath)) return;
                try
                {
                    byte[] cipher = File.ReadAllBytes(CachePath);
                    if (cipher.Length == 0) return;
                    byte[] plain = _protector.Unprotect(cipher);
                    try { args.TokenCache.DeserializeMsalV3(plain); }
                    finally { CryptographicOperations.ZeroMemory(plain); }
                }
                catch (Exception ex) when (ex is CryptographicException or IOException or UnauthorizedAccessException) { }
            }
        });
        _app.UserTokenCache.SetAfterAccess(args =>
        {
            if (!args.HasStateChanged) return;
            lock (_cacheGate)
            {
                byte[]? plain = args.TokenCache.SerializeMsalV3();
                if (plain is null) return;
                try
                {
                    byte[] cipher = _protector.Protect(plain);
                    Directory.CreateDirectory(_directory);
                    string temporary = CachePath + ".tmp";
                    File.WriteAllBytes(temporary, cipher);
                    File.Move(temporary, CachePath, true);
                }
                finally { CryptographicOperations.ZeroMemory(plain); }
            }
        });
        return _app;
    }

    private static string Prop(JsonElement element, string name) =>
        element.TryGetProperty(name, out var node) ? node.GetString() ?? "" : "";

    private static string StripHtml(string html)
    {
        if (html.IndexOf('<') < 0) return html.Trim();
        var sb = new StringBuilder(html.Length);
        bool hide = false;
        foreach (char c in html)
        {
            if (c == '<') { hide = true; continue; }
            if (c == '>') { hide = false; sb.Append(' '); continue; }
            if (!hide) sb.Append(c);
        }
        return System.Net.WebUtility.HtmlDecode(sb.ToString()).Trim();
    }

    private static HttpClient CreateHttp()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("BrimDeck-MailOtp");
        return client;
    }
}
