using System.Security.Cryptography;
using System.Text;

namespace Acquisition.Center.Security;

public static class DeviceCredentialHasher
{
    public static string CreateSecret() => Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    public static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    public static bool IsValid(string? expectedHash, string? suppliedValue)
    {
        if (string.IsNullOrWhiteSpace(expectedHash) || string.IsNullOrWhiteSpace(suppliedValue)) return false;
        byte[] expected;
        try { expected = Convert.FromHexString(expectedHash); }
        catch (FormatException) { return false; }
        var actual = SHA256.HashData(Encoding.UTF8.GetBytes(suppliedValue));
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
