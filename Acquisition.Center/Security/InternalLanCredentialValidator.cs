using System.Security.Cryptography;
using System.Text;

namespace Acquisition.Center.Security;

public static class InternalLanCredentialValidator
{
    public static bool IsValid(string? configuredKey, string? suppliedKey)
    {
        if (string.IsNullOrWhiteSpace(configuredKey) || string.IsNullOrWhiteSpace(suppliedKey)) return false;
        var expected = SHA256.HashData(Encoding.UTF8.GetBytes(configuredKey));
        var actual = SHA256.HashData(Encoding.UTF8.GetBytes(suppliedKey));
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }
}
