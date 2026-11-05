using System.Text;
using BrimDeck.Core;
using ZXing;
using ZXing.Common;

internal static class TotpTests
{
    private const string RfcSecret = "12345678901234567890";
    private const string RfcBase32 = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ";

    public static void Run(string root, Action<string, bool> check)
    {
        var key = Encoding.ASCII.GetBytes(RfcSecret);
        check("RFC 6238 SHA1 at 59 seconds is 94287082", Totp.Code(key, 59 / 30, 8) == "94287082");
        check("RFC 6238 SHA1 at 1111111109 is 07081804", Totp.Code(key, 1111111109L / 30, 8) == "07081804");
        check("RFC 6238 SHA1 at 1111111111 is 14050471", Totp.Code(key, 1111111111L / 30, 8) == "14050471");
        check("RFC 6238 SHA1 at 1234567890 is 89005924", Totp.Code(key, 1234567890L / 30, 8) == "89005924");
        check("RFC 6238 SHA1 at 2000000000 is 69279037", Totp.Code(key, 2000000000L / 30, 8) == "69279037");
        var sha256 = Encoding.ASCII.GetBytes(RfcSecret + "123456789012");
        check("RFC 6238 SHA256 at 59 seconds is 46119246", Totp.Code(sha256, 59 / 30, 8, "SHA256") == "46119246");

        check("the RFC secret's base32 decodes to those bytes", Totp.TryDecodeBase32(RfcBase32, out var decoded) && decoded.AsSpan().SequenceEqual(key));
        var at59 = DateTimeOffset.FromUnixTimeSeconds(59);
        check("a base32 secret matches the raw key for 6 digits", Totp.Code(RfcBase32, at59) == "287082");
        check("spaces and lowercase base32 are the same secret", Totp.Code("gezd gnbv gy3t qojq gezd gnbv gy3t qojq", at59) == "287082");
        check("the 6 digit code is grouped", Totp.Group("287082") == "287 082");
        check("a step just started has the full period left", Totp.Remaining(DateTimeOffset.FromUnixTimeSeconds(60)) == 30 && Totp.Counter(at59) == Totp.Counter(DateTimeOffset.FromUnixTimeSeconds(30)));
        check("the last second of a step has one second left", Totp.Remaining(at59) == 1);
        check("a digit outside the base32 alphabet is rejected", !Totp.TryNormalizeBase32("1234", out _));
        check("one base32 character is too short", !Totp.TryDecodeBase32("A", out _));

        string uri = "otpauth://totp/GitHub:pengfei?secret=" + RfcBase32 + "&issuer=GitHub&algorithm=SHA1&digits=6&period=30";
        var parsed = OtpAuth.Parse(uri);
        check("an otpauth URI keeps the issuer, account and secret", parsed.Ok && parsed.Account!.Issuer == "GitHub" && parsed.Account.Name == "pengfei" && parsed.Account.Secret == RfcBase32);
        check("that URI's code is the RFC code", parsed.Ok && Totp.Code(parsed.Account!.Secret, at59, parsed.Account.Digits, parsed.Account.Period, parsed.Account.Algorithm) == "287082");
        var mail = OtpAuth.Parse("otpauth://totp/Example:alice@google.com?secret=" + RfcBase32 + "&issuer=Example");
        check("an address in the label stays the account name", mail.Ok && mail.Account!.Issuer == "Example" && mail.Account.Name == "alice@google.com");
        check("the issuer query wins over the label prefix", OtpAuth.Parse("otpauth://totp/Other:name?secret=" + RfcBase32 + "&issuer=GitHub").Account!.Issuer == "GitHub");
        check("HOTP is refused", OtpAuth.Parse("otpauth://hotp/GitHub:me?secret=" + RfcBase32 + "&counter=1").Error == OtpImportError.Hotp);
        check("a missing secret is refused", OtpAuth.Parse("otpauth://totp/GitHub:me").Error == OtpImportError.NoSecret);
        check("plain text is not an otpauth URI", OtpAuth.Parse("hello").Error == OtpImportError.NotOtpAuth);
        var typed = OtpAuth.FromSecret("gezd-gnbv", "GitHub", " me ");
        check("a typed secret is normalized and the name is trimmed", typed.Ok && typed.Account!.Secret == "GEZDGNBV" && typed.Account.Issuer == "GitHub" && typed.Account.Name == "me");
        check("an empty typed secret is refused", OtpAuth.FromSecret("   ", "", "").Error == OtpImportError.BadSecret);

        var account = parsed.Account! with { Id = Guid.NewGuid() };
        string json = OtpAccounts.Export([account]);
        var imported = OtpAccounts.Import(json);
        check("the vault JSON round-trips an account", imported.Count == 1 && imported[0].Id == account.Id && imported[0].Secret == account.Secret && imported[0].Name == "pengfei");
        check("a Chinese label is kept", OtpAccounts.Label(account with { Issuer = "公司", Name = "" }) == "公司");

        var folder = Path.Combine(root, "otp");
        var store = new OtpStore(folder, new FlipProtector());
        store.Save([account]);
        byte[] file = File.ReadAllBytes(storePath(folder));
        check("otp.dat does not contain the plaintext secret", Encoding.UTF8.GetString(file).Contains(RfcBase32, StringComparison.Ordinal) == false);
        var loaded = store.Load();
        check("the encrypted vault loads the same account", loaded.Count == 1 && loaded[0].Secret == account.Secret && loaded[0].Issuer == "GitHub");
        store.Save([]);
        check("deleting the last account leaves an empty vault", store.Load().Count == 0);

        var writer = new BarcodeWriterPixelData { Format = BarcodeFormat.QR_CODE, Options = new EncodingOptions { Width = 240, Height = 240, Margin = 1 } };
        var pixels = writer.Write(uri);
        check("a QR image of an otpauth URI decodes back to that URI", QrText.Read(pixels.Pixels, pixels.Width, pixels.Height) == uri);
        check("pixels that are not a QR code decode to nothing", QrText.Read(new byte[16], 2, 2) is null);
        check("the authenticator page is last in the switch and keeps the saved number", DeckPages.Order[^1] == DeckPage.Otp && (int)DeckPage.Otp == 3 && new DeckSettings().OtpPage);
        var legacy = System.Text.Json.JsonSerializer.Deserialize<DeckSettings>("""{"PageOrder":[0,1,2]}""")!;
        check("an older page order gains the authenticator page", legacy.PageOrder.SequenceEqual([DeckPage.Usage, DeckPage.Music, DeckPage.Monitor, DeckPage.Otp]));
        check("the carousel stays on one account and stops at the ends", OtpCarousel.Step(0, 3, -1) == 0 && OtpCarousel.Step(1, 3, 1) == 2 && OtpCarousel.Step(2, 3, 1) == 2 && OtpCarousel.Step(5, 0, 1) == 0);
    }

    private static string storePath(string folder) => Path.Combine(folder, "otp.dat");

    private sealed class FlipProtector : ISecretProtector
    {
        public byte[] Protect(byte[] plaintext) => Flip(plaintext);
        public byte[] Unprotect(byte[] ciphertext) => Flip(ciphertext);
        private static byte[] Flip(byte[] value)
        {
            var copy = (byte[])value.Clone();
            for (int i = 0; i < copy.Length; i++) copy[i] ^= 0xA5;
            return copy;
        }
    }
}
