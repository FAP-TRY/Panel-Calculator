using System;
using System.Security.Cryptography;
using System.Text;

namespace PanelCalculator.Core.Security;

/// <summary>
/// Centralized password hashing for the application.
///
/// History: pre-v1.2.4 the codebase hashed passwords with raw SHA-256 (no
/// salt, no work factor) and duplicated that logic in four files
/// (<c>LoginForm</c>, <c>Program</c>, <c>SettingsForm</c>,
/// <c>UserManagementForm</c>). Item #4 of the security audit replaces it
/// with BCrypt and consolidates everything here.
///
/// New hashes are stored with the <c>"bcrypt$"</c> prefix so that legacy
/// SHA-256 hashes (raw 64-char hex) and new BCrypt hashes can coexist in
/// the same <c>Users.PasswordHash</c> column without a schema change. On
/// successful login against a legacy hash, callers re-hash the just-typed
/// password with BCrypt and persist it — eventual silent migration.
/// </summary>
public static class PasswordHasher
{
    /// <summary>Prefix that identifies a BCrypt-format hash in the DB.</summary>
    public const string BcryptPrefix = "bcrypt$";

    /// <summary>
    /// BCrypt work factor (cost). 12 ≈ 250 ms per hash on a modern desktop
    /// CPU — balance between brute-force resistance and login UX.
    /// </summary>
    private const int BcryptWorkFactor = 12;

    /// <summary>
    /// Hash a plaintext password for storage. Always returns a BCrypt hash
    /// with the <c>"bcrypt$"</c> prefix. Each call produces a different
    /// output for the same input (BCrypt generates a random salt).
    /// </summary>
    public static string Hash(string password)
    {
        if (password == null) throw new ArgumentNullException(nameof(password));
        var bcrypt = BCrypt.Net.BCrypt.HashPassword(password, BcryptWorkFactor);
        return BcryptPrefix + bcrypt;
    }

    /// <summary>
    /// Verify a plaintext password against a stored hash. Supports both
    /// BCrypt hashes (with <c>"bcrypt$"</c> prefix) and legacy SHA-256
    /// hashes (raw 64-char hex). Sets <paramref name="needsUpgrade"/> to
    /// <c>true</c> when the verify succeeded against a legacy SHA-256
    /// hash — caller should re-hash with <see cref="Hash"/> and persist.
    /// </summary>
    public static bool Verify(string password, string storedHash, out bool needsUpgrade)
    {
        needsUpgrade = false;
        if (password == null) throw new ArgumentNullException(nameof(password));
        if (string.IsNullOrEmpty(storedHash)) return false;

        if (storedHash.StartsWith(BcryptPrefix, StringComparison.Ordinal))
        {
            var bcryptHash = storedHash.Substring(BcryptPrefix.Length);
            try
            {
                return BCrypt.Net.BCrypt.Verify(password, bcryptHash);
            }
            catch (BCrypt.Net.SaltParseException)
            {
                // Malformed bcrypt hash in DB — treat as auth failure rather than crash.
                return false;
            }
        }

        // Legacy fallback: raw SHA-256 hex digest (64 lowercase hex chars).
        var legacy = LegacySha256(password);
        if (FixedTimeEquals(legacy, storedHash.Trim().ToLowerInvariant()))
        {
            needsUpgrade = true;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Replicates the pre-v1.2.4 hashing logic verbatim: SHA-256 over UTF-8
    /// bytes, hex-encoded lowercase. Exposed internally only for the
    /// verify path and for unit tests; new code must never call this for
    /// storage.
    /// </summary>
    internal static string LegacySha256(string password)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(password));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>Constant-time string comparison for the SHA-256 fallback path.</summary>
    private static bool FixedTimeEquals(string a, string b)
    {
        if (a.Length != b.Length) return false;
        var aBytes = Encoding.ASCII.GetBytes(a);
        var bBytes = Encoding.ASCII.GetBytes(b);
        return CryptographicOperations.FixedTimeEquals(aBytes, bBytes);
    }
}
