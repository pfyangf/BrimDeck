using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BrimDeck.Core;

namespace BrimDeck.Native;

// Bound mailboxes for the email-codes inbox. Account metadata and IMAP/POP3 passwords are DPAPI-protected for the
// current Windows user (same scope as otp.dat). MSAL tokens live in a separate cache file beside them.
public sealed class MailAccountStore
{
    private readonly string _directory;
    private readonly ISecretProtector _protector;
    private readonly object _gate = new();
    private string AccountsPath => Path.Combine(_directory, "mail-accounts.dat");
    private string SecretsPath => Path.Combine(_directory, "mail-secrets.dat");

    public MailAccountStore(string directory, ISecretProtector protector)
    {
        _directory = directory;
        _protector = protector;
    }

    public event Action? Changed;

    public IReadOnlyList<BoundMailAccount> Load()
    {
        lock (_gate)
        {
            if (!File.Exists(AccountsPath)) return [];
            byte[] cipher = File.ReadAllBytes(AccountsPath);
            if (cipher.Length == 0) return [];
            byte[] plain = _protector.Unprotect(cipher);
            try { return BoundMailAccounts.Import(Encoding.UTF8.GetString(plain)); }
            finally { CryptographicOperations.ZeroMemory(plain); }
        }
    }

    public void Save(IReadOnlyList<BoundMailAccount> accounts)
    {
        lock (_gate)
        {
            byte[] plain = Encoding.UTF8.GetBytes(BoundMailAccounts.Export(accounts));
            try
            {
                byte[] cipher = _protector.Protect(plain);
                Directory.CreateDirectory(_directory);
                string temporary = AccountsPath + ".tmp";
                File.WriteAllBytes(temporary, cipher);
                File.Move(temporary, AccountsPath, true);
            }
            finally { CryptographicOperations.ZeroMemory(plain); }
        }
        Changed?.Invoke();
    }

    public string ReadSecret(Guid accountId)
    {
        lock (_gate)
        {
            if (!LoadSecrets().TryGetValue(accountId, out var encrypted) || encrypted.Length == 0) return "";
            var plain = ProtectedData.Unprotect(Convert.FromBase64String(encrypted), null, DataProtectionScope.CurrentUser);
            try { return Encoding.UTF8.GetString(plain); }
            finally { CryptographicOperations.ZeroMemory(plain); }
        }
    }

    public void WriteSecret(Guid accountId, string secret)
    {
        lock (_gate)
        {
            var values = LoadSecrets();
            if (string.IsNullOrEmpty(secret)) values.Remove(accountId);
            else
            {
                var plain = Encoding.UTF8.GetBytes(secret);
                try { values[accountId] = Convert.ToBase64String(ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser)); }
                finally { CryptographicOperations.ZeroMemory(plain); }
            }
            Directory.CreateDirectory(_directory);
            var temp = SecretsPath + ".tmp";
            File.WriteAllBytes(temp, JsonSerializer.SerializeToUtf8Bytes(values));
            File.Move(temp, SecretsPath, true);
        }
    }

    public void DeleteSecret(Guid accountId) => WriteSecret(accountId, "");

    public void Remove(Guid accountId)
    {
        var next = Load().Where(account => account.Id != accountId).ToList();
        Save(next);
        DeleteSecret(accountId);
    }

    private Dictionary<Guid, string> LoadSecrets() => File.Exists(SecretsPath)
        ? JsonSerializer.Deserialize<Dictionary<Guid, string>>(File.ReadAllBytes(SecretsPath)) ?? []
        : [];
}
