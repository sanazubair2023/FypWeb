using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;

namespace FypWeb.Services;

/// <summary>Hashes new passwords and upgrades legacy plaintext values after a successful login.</summary>
public sealed class PasswordService
{
    private readonly PasswordHasher<object> _hasher = new();

    public string Hash(string password) => _hasher.HashPassword(new object(), password);

    public bool Verify(string? stored, string supplied, out bool needsRehash)
    {
        needsRehash = false;
        if (string.IsNullOrEmpty(stored)) return false;

        try
        {
            var result = _hasher.VerifyHashedPassword(new object(), stored, supplied);
            if (result != PasswordVerificationResult.Failed)
            {
                needsRehash = result == PasswordVerificationResult.SuccessRehashNeeded;
                return true;
            }
        }
        catch (FormatException)
        {
            // Legacy rows are plaintext. The compatibility check below upgrades
            // them only after the exact legacy password has been supplied.
        }

        if (!string.Equals(stored, supplied, StringComparison.Ordinal)) return false;
        needsRehash = true;
        return true;
    }

    public static bool IsValidNewPassword(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length is >= 8 and <= 100;

    /// <summary>A non-secret value compared with the cookie to revoke old sessions after a password change.</summary>
    public static string Stamp(string? storedPassword) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(storedPassword ?? "")));
}
