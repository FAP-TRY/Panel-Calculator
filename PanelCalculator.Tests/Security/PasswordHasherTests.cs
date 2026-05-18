using PanelCalculator.Core.Security;
using Xunit;

namespace PanelCalculator.Tests.Security;

/// <summary>
/// Tests for <see cref="PasswordHasher"/> — covers BCrypt round-trip,
/// legacy SHA-256 fallback verification, and silent upgrade signalling.
/// </summary>
public class PasswordHasherTests
{
    private const string TestPassword = "S3cret!P@ssw0rd";
    private const string WrongPassword = "wrong-guess";

    [Fact]
    public void Hash_ReturnsBcryptPrefixedString()
    {
        var hash = PasswordHasher.Hash(TestPassword);

        Assert.NotNull(hash);
        Assert.StartsWith("bcrypt$", hash);
        // BCrypt hashes (after the prefix) are 60 chars: "$2a$..", "$2b$..", "$2y$.."
        Assert.True(hash.Length > "bcrypt$".Length + 50,
            $"BCrypt hash unexpectedly short: '{hash}'");
    }

    [Fact]
    public void Hash_GeneratesDifferentSaltEachTime()
    {
        // BCrypt embeds a random 16-byte salt into the hash, so two calls
        // with the same password must produce two different stored strings.
        var hash1 = PasswordHasher.Hash(TestPassword);
        var hash2 = PasswordHasher.Hash(TestPassword);

        Assert.NotEqual(hash1, hash2);
        // But both must verify against the original password.
        Assert.True(PasswordHasher.Verify(TestPassword, hash1, out _));
        Assert.True(PasswordHasher.Verify(TestPassword, hash2, out _));
    }

    [Fact]
    public void Verify_Bcrypt_Correct_ReturnsTrue_NoUpgradeNeeded()
    {
        var hash = PasswordHasher.Hash(TestPassword);

        var ok = PasswordHasher.Verify(TestPassword, hash, out var needsUpgrade);

        Assert.True(ok);
        Assert.False(needsUpgrade);
    }

    [Fact]
    public void Verify_Bcrypt_Wrong_ReturnsFalse()
    {
        var hash = PasswordHasher.Hash(TestPassword);

        var ok = PasswordHasher.Verify(WrongPassword, hash, out var needsUpgrade);

        Assert.False(ok);
        Assert.False(needsUpgrade);
    }

    [Fact]
    public void Verify_LegacySha256_Correct_ReturnsTrueAndNeedsUpgrade()
    {
        // Simulate a hash produced by the pre-v1.2.4 code path.
        var legacyHash = PasswordHasher.LegacySha256(TestPassword);

        var ok = PasswordHasher.Verify(TestPassword, legacyHash, out var needsUpgrade);

        Assert.True(ok);
        Assert.True(needsUpgrade);
    }

    [Fact]
    public void Verify_LegacySha256_Wrong_ReturnsFalse()
    {
        var legacyHash = PasswordHasher.LegacySha256(TestPassword);

        var ok = PasswordHasher.Verify(WrongPassword, legacyHash, out var needsUpgrade);

        Assert.False(ok);
        Assert.False(needsUpgrade);
    }

    [Fact]
    public void Verify_LegacySha256_KnownVector_MatchesProductionFormat()
    {
        // Hard-coded SHA-256 hex of "admin" — must match the format that
        // existing customer DBs already have on disk (lower-case hex, no
        // separators, 64 chars). If this test ever drifts, every legacy
        // admin install will fail to log in after the upgrade.
        const string adminSha256 = "8c6976e5b5410415bde908bd4dee15dfb167a9c873fc4bb8a81f6f2ab448a918";

        Assert.Equal(adminSha256, PasswordHasher.LegacySha256("admin"));

        var ok = PasswordHasher.Verify("admin", adminSha256, out var needsUpgrade);
        Assert.True(ok);
        Assert.True(needsUpgrade);
    }

    [Fact]
    public void Verify_EmptyStoredHash_ReturnsFalse()
    {
        var ok = PasswordHasher.Verify(TestPassword, "", out var needsUpgrade);

        Assert.False(ok);
        Assert.False(needsUpgrade);
    }

    [Fact]
    public void Verify_MalformedBcryptHash_ReturnsFalse_DoesNotThrow()
    {
        // bcrypt$<garbage> — must not crash login.
        var ok = PasswordHasher.Verify(TestPassword, "bcrypt$this-is-not-a-bcrypt-hash", out var needsUpgrade);

        Assert.False(ok);
        Assert.False(needsUpgrade);
    }

    [Fact]
    public void RoundTrip_UpgradeFlow_NewHashVerifiesWithoutUpgrade()
    {
        // Simulate the LoginForm upgrade path:
        //  1. user has legacy SHA-256 hash in DB
        //  2. login succeeds, Verify reports needsUpgrade=true
        //  3. we re-hash with PasswordHasher.Hash and persist
        //  4. next login should now succeed with needsUpgrade=false
        var legacyHash = PasswordHasher.LegacySha256(TestPassword);
        Assert.True(PasswordHasher.Verify(TestPassword, legacyHash, out var firstUpgrade));
        Assert.True(firstUpgrade);

        var migratedHash = PasswordHasher.Hash(TestPassword);
        Assert.StartsWith("bcrypt$", migratedHash);

        Assert.True(PasswordHasher.Verify(TestPassword, migratedHash, out var secondUpgrade));
        Assert.False(secondUpgrade);
    }
}
