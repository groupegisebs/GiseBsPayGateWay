using System.Security.Cryptography;
using System.Text;

namespace GiseBsPayGateway.Services;

public class ApiKeyService : IApiKeyService
{
    private const string KeyPrefixLabel = "gbsk";
    private const int KeyLength = 32;

    public (string RawKey, string Prefix, string Hash) GenerateApiKey()
    {
        var bytes = RandomNumberGenerator.GetBytes(KeyLength);
        var rawKey = $"{KeyPrefixLabel}_{Convert.ToBase64String(bytes).Replace("+", "").Replace("/", "").TrimEnd('=')[..32]}";
        var prefix = rawKey[..Math.Min(12, rawKey.Length)];
        var hash = HashApiKey(rawKey);
        return (rawKey, prefix, hash);
    }

    public string HashApiKey(string rawKey)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawKey));
        return Convert.ToHexString(bytes);
    }

    public bool VerifyApiKey(string rawKey, string hash) =>
        CryptographicOperations.FixedTimeEquals(
            Convert.FromHexString(hash),
            SHA256.HashData(Encoding.UTF8.GetBytes(rawKey)));
}
