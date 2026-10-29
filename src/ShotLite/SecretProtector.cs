using System.Security.Cryptography;
using System.Text;

namespace ShotLite;

/// <summary>Encrypts small secrets with Windows DPAPI, bound to the current Windows user.</summary>
internal static class SecretProtector
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("ShotLite.Settings");

    /// <returns>Base64 cipher text, or null for an empty secret (nothing is written).</returns>
    public static string? Protect(string? secret)
    {
        if (string.IsNullOrEmpty(secret))
            return null;
        var data = ProtectedData.Protect(Encoding.UTF8.GetBytes(secret), Entropy, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(data);
    }

    /// <returns>The secret, or "" when it is missing or was encrypted by another user or machine.</returns>
    public static string Unprotect(string? protectedSecret)
    {
        if (string.IsNullOrEmpty(protectedSecret))
            return "";
        try
        {
            var data = ProtectedData.Unprotect(Convert.FromBase64String(protectedSecret), Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(data);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            // Settings copied from another account: the user has to enter the header again.
            return "";
        }
    }
}
