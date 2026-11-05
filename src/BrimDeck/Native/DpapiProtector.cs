using System.Security.Cryptography;
using BrimDeck.Core;

namespace BrimDeck.Native;

// The same Windows user scope ProviderSecrets uses for API keys. Other Windows accounts cannot decrypt otp.dat.
public sealed class DpapiProtector : ISecretProtector
{
    public byte[] Protect(byte[] plaintext) => ProtectedData.Protect(plaintext, null, DataProtectionScope.CurrentUser);
    public byte[] Unprotect(byte[] ciphertext) => ProtectedData.Unprotect(ciphertext, null, DataProtectionScope.CurrentUser);
}
