using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BrimDeck.Core;

// One authenticator account. Secret is the normalized base32 key. It is kept only in memory here; OtpStore encrypts it
// before writing otp.dat.
public sealed record OtpAccount(Guid Id, string Issuer, string Name, string Secret, int Digits = 6, int Period = 30, string Algorithm = "SHA1");

public enum OtpImportError { None, NotOtpAuth, Hotp, NoSecret, BadSecret, UnsupportedAlgorithm, BadDigits, BadPeriod }

public readonly record struct OtpImport(OtpAccount? Account, OtpImportError Error)
{
    public bool Ok => Account is not null && Error == OtpImportError.None;
    public static OtpImport Fail(OtpImportError error) => new(null, error);
}

// RFC 6238 TOTP. The usual authenticator is HMAC-SHA1, a 30 second period and 6 digits. SHA-256 and SHA-512 are accepted
// when an otpauth URI asks for them. HOTP is not.
public static class Totp
{
    public static int Remaining(DateTimeOffset now, int period = 30)
    {
        if (period <= 0) period = 30;
        int used = (int)(now.ToUnixTimeSeconds() % period);
        return used == 0 ? period : period - used;
    }

    public static long Counter(DateTimeOffset now, int period = 30) => now.ToUnixTimeSeconds() / (period <= 0 ? 30 : period);

    public static string Code(string base32Secret, DateTimeOffset now, int digits = 6, int period = 30, string algorithm = "SHA1")
    {
        if (!TryDecodeBase32(base32Secret, out var key)) return new string('-', Math.Clamp(digits, 6, 8));
        try { return Code(key, Counter(now, period), digits, algorithm); }
        finally { CryptographicOperations.ZeroMemory(key); }
    }

    public static string Code(ReadOnlySpan<byte> key, long counter, int digits = 6, string algorithm = "SHA1")
    {
        digits = digits is >= 6 and <= 8 ? digits : 6;
        Span<byte> message = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(message, counter);
        byte[] mac = algorithm.ToUpperInvariant() switch
        {
            "SHA256" => HMACSHA256.HashData(key, message),
            "SHA512" => HMACSHA512.HashData(key, message),
            _ => HMACSHA1.HashData(key, message)
        };
        int offset = mac[^1] & 0x0F;
        int binary = ((mac[offset] & 0x7F) << 24) | (mac[offset + 1] << 16) | (mac[offset + 2] << 8) | mac[offset + 3];
        int mod = 1;
        for (int i = 0; i < digits; i++) mod *= 10;
        return (binary % mod).ToString(new string('0', digits), CultureInfo.InvariantCulture);
    }

    // "123456" is shown as "123 456". Other lengths stay as they are.
    public static string Group(string code) => code.Length == 6 ? code[..3] + " " + code[3..] : code;

    public static bool TryNormalizeBase32(string? secret, out string normalized)
    {
        normalized = "";
        if (!TryDecodeBase32(secret, out var key)) return false;
        CryptographicOperations.ZeroMemory(key);
        var built = new StringBuilder();
        foreach (char c in secret!)
        {
            if (char.IsWhiteSpace(c) || c is '-' or '=') continue;
            built.Append(char.ToUpperInvariant(c));
        }
        normalized = built.ToString();
        return normalized.Length > 0;
    }

    public static bool TryDecodeBase32(string? secret, out byte[] key)
    {
        key = [];
        if (string.IsNullOrWhiteSpace(secret)) return false;
        int buffer = 0, bits = 0;
        var bytes = new List<byte>();
        foreach (char raw in secret)
        {
            if (char.IsWhiteSpace(raw) || raw is '-' or '=') continue;
            char c = char.ToUpperInvariant(raw);
            int value = c is >= 'A' and <= 'Z' ? c - 'A' : c is >= '2' and <= '7' ? c - '2' + 26 : -1;
            if (value < 0) return false;
            buffer = (buffer << 5) | value;
            bits += 5;
            if (bits >= 8)
            {
                bits -= 8;
                bytes.Add((byte)((buffer >> bits) & 0xFF));
            }
        }
        if (bytes.Count == 0) return false;
        key = bytes.ToArray();
        return true;
    }
}

public static class OtpAuth
{
    public static OtpImport Parse(string? text, Guid? id = null)
    {
        text = (text ?? "").Trim();
        const string Prefix = "otpauth://";
        if (!text.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)) return OtpImport.Fail(OtpImportError.NotOtpAuth);
        string rest = text[Prefix.Length..];
        int slash = rest.IndexOf('/');
        if (slash <= 0) return OtpImport.Fail(OtpImportError.NotOtpAuth);
        string kind = rest[..slash];
        if (kind.Equals("hotp", StringComparison.OrdinalIgnoreCase)) return OtpImport.Fail(OtpImportError.Hotp);
        if (!kind.Equals("totp", StringComparison.OrdinalIgnoreCase)) return OtpImport.Fail(OtpImportError.NotOtpAuth);
        string after = rest[(slash + 1)..];
        int queryAt = after.IndexOf('?');
        string label = Uri.UnescapeDataString(queryAt >= 0 ? after[..queryAt] : after).Trim();
        string query = queryAt >= 0 ? after[(queryAt + 1)..] : "";
        int hash = query.IndexOf('#');
        if (hash >= 0) query = query[..hash];
        var fields = Query(query);
        if (!fields.TryGetValue("secret", out var secret) || secret.Length == 0) return OtpImport.Fail(OtpImportError.NoSecret);
        if (!Totp.TryNormalizeBase32(secret, out var normalized)) return OtpImport.Fail(OtpImportError.BadSecret);
        string algorithm = fields.TryGetValue("algorithm", out var asked) && asked.Length > 0 ? asked.ToUpperInvariant() : "SHA1";
        if (algorithm is not ("SHA1" or "SHA256" or "SHA512")) return OtpImport.Fail(OtpImportError.UnsupportedAlgorithm);
        int digits = 6;
        if (fields.TryGetValue("digits", out var digitText) && digitText.Length > 0 && (!int.TryParse(digitText, NumberStyles.Integer, CultureInfo.InvariantCulture, out digits) || digits is < 6 or > 8))
            return OtpImport.Fail(OtpImportError.BadDigits);
        int period = 30;
        if (fields.TryGetValue("period", out var periodText) && periodText.Length > 0 && (!int.TryParse(periodText, NumberStyles.Integer, CultureInfo.InvariantCulture, out period) || period is < 15 or > 120))
            return OtpImport.Fail(OtpImportError.BadPeriod);
        string issuer = fields.TryGetValue("issuer", out var issued) ? issued.Trim() : "";
        string name = label;
        int colon = label.IndexOf(':');
        if (colon >= 0)
        {
            if (issuer.Length == 0) issuer = label[..colon].Trim();
            name = label[(colon + 1)..].Trim();
        }
        return new OtpImport(new OtpAccount(id ?? Guid.NewGuid(), issuer, name, normalized, digits, period, algorithm), OtpImportError.None);
    }

    public static OtpImport FromSecret(string? secret, string? issuer, string? name, Guid? id = null)
    {
        if (!Totp.TryNormalizeBase32(secret, out var normalized)) return OtpImport.Fail(OtpImportError.BadSecret);
        return new OtpImport(new OtpAccount(id ?? Guid.NewGuid(), (issuer ?? "").Trim(), (name ?? "").Trim(), normalized), OtpImportError.None);
    }

    private static Dictionary<string, string> Query(string query)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = part.IndexOf('=');
            string key = Uri.UnescapeDataString(eq < 0 ? part : part[..eq]).Trim();
            string value = Uri.UnescapeDataString((eq < 0 ? "" : part[(eq + 1)..]).Replace("+", " ", StringComparison.Ordinal));
            if (key.Length > 0) map[key] = value;
        }
        return map;
    }
}

public static class OtpAccounts
{
    public static string Label(OtpAccount account)
    {
        string issuer = account.Issuer.Trim(), name = account.Name.Trim();
        if (issuer.Length > 0 && name.Length > 0) return issuer + " · " + name;
        if (issuer.Length > 0) return issuer;
        if (name.Length > 0) return name;
        return Loc.T("未命名账户", "Untitled account");
    }

    // The JSON that OtpStore encrypts. It is not the file on disk.
    public static string Export(IReadOnlyList<OtpAccount> accounts)
        => JsonSerializer.Serialize(accounts.Select(account => new Row(account)).ToList());

    public static IReadOnlyList<OtpAccount> Import(string json)
    {
        var rows = JsonSerializer.Deserialize<List<Row>>(json) ?? throw new JsonException("OTP vault is empty.");
        var seen = new HashSet<Guid>();
        var accounts = new List<OtpAccount>();
        foreach (var row in rows)
        {
            if (row is null || !Totp.TryNormalizeBase32(row.Secret, out var secret)) continue;
            var id = row.Id == Guid.Empty || !seen.Add(row.Id) ? Guid.NewGuid() : row.Id;
            seen.Add(id);
            int digits = row.Digits is >= 6 and <= 8 ? row.Digits : 6;
            int period = row.Period is >= 15 and <= 120 ? row.Period : 30;
            string algorithm = (row.Algorithm ?? "").ToUpperInvariant() is "SHA256" or "SHA512" ? row.Algorithm!.ToUpperInvariant() : "SHA1";
            accounts.Add(new OtpAccount(id, row.Issuer ?? "", row.Name ?? "", secret, digits, period, algorithm));
        }
        return accounts;
    }

    private sealed class Row
    {
        public Row() { }
        public Row(OtpAccount account)
        {
            Id = account.Id; Issuer = account.Issuer; Name = account.Name; Secret = account.Secret;
            Digits = account.Digits; Period = account.Period; Algorithm = account.Algorithm;
        }
        public Guid Id { get; set; }
        public string? Issuer { get; set; }
        public string? Name { get; set; }
        public string? Secret { get; set; }
        public int Digits { get; set; }
        public int Period { get; set; }
        public string? Algorithm { get; set; }
    }
}

// Protects the UTF-8 JSON of the accounts. The Windows app uses DPAPI (DpapiProtector).
public interface ISecretProtector
{
    byte[] Protect(byte[] plaintext);
    byte[] Unprotect(byte[] ciphertext);
}

// otp.dat in the settings folder. The bytes on disk are whatever ISecretProtector returns, never the JSON itself.
public sealed class OtpStore
{
    private readonly string _directory;
    private readonly ISecretProtector _protector;
    private readonly object _gate = new();
    private string FilePath => Path.Combine(_directory, "otp.dat");

    public OtpStore(string directory, ISecretProtector protector)
    {
        _directory = directory;
        _protector = protector;
    }

    public IReadOnlyList<OtpAccount> Load()
    {
        lock (_gate)
        {
            if (!File.Exists(FilePath)) return [];
            byte[] cipher = File.ReadAllBytes(FilePath);
            if (cipher.Length == 0) return [];
            byte[] plain = _protector.Unprotect(cipher);
            try { return OtpAccounts.Import(Encoding.UTF8.GetString(plain)); }
            finally { CryptographicOperations.ZeroMemory(plain); }
        }
    }

    public void Save(IReadOnlyList<OtpAccount> accounts)
    {
        lock (_gate)
        {
            byte[] plain = Encoding.UTF8.GetBytes(OtpAccounts.Export(accounts));
            try
            {
                byte[] cipher = _protector.Protect(plain);
                Directory.CreateDirectory(_directory);
                string temporary = FilePath + ".tmp";
                File.WriteAllBytes(temporary, cipher);
                File.Move(temporary, FilePath, true);
            }
            finally { CryptographicOperations.ZeroMemory(plain); }
        }
    }
}
