using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using PanelCalculator.Data.Security;
using Xunit;

namespace PanelCalculator.Tests.Security;

public class UpdateVerifierTests
{
    // ──────────────────────────────────────────────────────────────────────
    // ComputeSha256
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void ComputeSha256_OnKnownBytes_ReturnsKnownDigest()
    {
        // Empty input → standard SHA-256 of zero-length is well-known.
        var empty = UpdateVerifier.ComputeSha256(Array.Empty<byte>());
        Assert.Equal(
            "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
            empty);

        // ASCII "abc" → another well-known reference value.
        var abc = UpdateVerifier.ComputeSha256(Encoding.ASCII.GetBytes("abc"));
        Assert.Equal(
            "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
            abc);
    }

    [Fact]
    public void ComputeSha256_OnFile_MatchesComputeOnBytes()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"upd-verify-test-{Guid.NewGuid():N}.bin");
        try
        {
            var bytes = new byte[1024];
            new Random(42).NextBytes(bytes);
            File.WriteAllBytes(tmp, bytes);

            var viaFile  = UpdateVerifier.ComputeSha256(tmp);
            var viaBytes = UpdateVerifier.ComputeSha256(bytes);
            Assert.Equal(viaBytes, viaFile);
            Assert.Equal(64, viaFile.Length);
            Assert.Matches("^[0-9a-f]{64}$", viaFile);
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }

    [Fact]
    public void ComputeSha256_OnMissingFile_Throws()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"definitely-not-here-{Guid.NewGuid():N}.bin");
        Assert.Throws<FileNotFoundException>(() => UpdateVerifier.ComputeSha256(missing));
    }

    // ──────────────────────────────────────────────────────────────────────
    // ParseManifest
    // ──────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad")]
    [InlineData("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad  PanelCalculator.exe")]
    [InlineData("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad *PanelCalculator.exe")]
    [InlineData("SHA256: ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad")]
    [InlineData("BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD  PanelCalculator.exe")]
    [InlineData("# Comment line\nba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad\n")]
    public void ParseManifest_AcceptsKnownFormats(string manifest)
    {
        var parsed = UpdateVerifier.ParseManifest(manifest);
        Assert.Equal(
            "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
            parsed);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a hash at all")]
    [InlineData("too short abc123")]
    [InlineData("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f200")] // 60 chars
    [InlineData("zzz816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad")] // bad char in front
    public void ParseManifest_RejectsInvalidContent(string manifest)
    {
        Assert.Throws<FormatException>(() => UpdateVerifier.ParseManifest(manifest));
    }

    // ──────────────────────────────────────────────────────────────────────
    // HashesMatch
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void HashesMatch_IsCaseInsensitive_AndTrimsWhitespace()
    {
        const string lower = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";
        var upper = lower.ToUpperInvariant();
        Assert.True(UpdateVerifier.HashesMatch(lower, upper));
        Assert.True(UpdateVerifier.HashesMatch($"  {lower}  ", upper));
    }

    [Fact]
    public void HashesMatch_DifferentHashes_ReturnsFalse()
    {
        Assert.False(UpdateVerifier.HashesMatch(
            "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
            "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"));
    }

    [Fact]
    public void HashesMatch_EmptyOrNull_ReturnsFalse()
    {
        Assert.False(UpdateVerifier.HashesMatch(null!, "abc"));
        Assert.False(UpdateVerifier.HashesMatch("abc", null!));
        Assert.False(UpdateVerifier.HashesMatch("", "abc"));
        Assert.False(UpdateVerifier.HashesMatch("abc", "   "));
    }

    // ──────────────────────────────────────────────────────────────────────
    // VerifyOrThrow — round-trip with fake EXE bytes
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void VerifyOrThrow_MatchingHash_DoesNotThrow()
    {
        var tmp = WriteRandomFile(out var expectedHash);
        try
        {
            var manifest = $"{expectedHash}  PanelCalculator.exe\n";
            UpdateVerifier.VerifyOrThrow(tmp, manifest); // must not throw
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }

    [Fact]
    public void VerifyOrThrow_NonMatchingHash_ThrowsUpdateVerificationException()
    {
        var tmp = WriteRandomFile(out _);
        try
        {
            // Manifest claims a hash for empty input — file is 4 KB random, won't match.
            var wrongHash = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";
            var manifest  = $"{wrongHash}  PanelCalculator.exe";

            var ex = Assert.Throws<UpdateVerificationException>(() =>
                UpdateVerifier.VerifyOrThrow(tmp, manifest));

            Assert.Contains("TIDAK SAMA", ex.Message);
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }

    [Fact]
    public void VerifyOrThrow_InvalidManifest_ThrowsFormatException()
    {
        var tmp = WriteRandomFile(out _);
        try
        {
            Assert.Throws<FormatException>(() =>
                UpdateVerifier.VerifyOrThrow(tmp, "this is not a hash manifest"));
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    // IsAllowedDownloadHost
    // ──────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("https://github.com/FAP-TRY/Panel-Calculator/releases/download/v1.2.4/PanelCalculator.exe", true)]
    [InlineData("https://objects.githubusercontent.com/path/to/asset", true)]
    [InlineData("https://api.github.com/repos/FAP-TRY/Panel-Calculator/releases/latest", true)] // subdomain of github.com
    [InlineData("https://evil.com/PanelCalculator.exe", false)]
    [InlineData("http://github.com/asset", false)]               // not HTTPS
    [InlineData("https://github.evil.com/asset", false)]         // typosquat
    [InlineData("https://githubXcom/asset", false)]              // garbled, not a real host
    [InlineData("https://notgithub.com/asset", false)]
    [InlineData("https://random.githubusercontent.com/asset", false)] // only objects.* is allowed under githubusercontent.com (exact match enforced)
    public void IsAllowedDownloadHost_KnownCases(string url, bool expected)
    {
        Assert.Equal(expected,
            UpdateVerifier.IsAllowedDownloadHost(new Uri(url)));
    }

    [Fact]
    public void IsAllowedDownloadHost_RelativeUri_ReturnsFalse()
    {
        var uri = new Uri("/relative/path", UriKind.Relative);
        Assert.False(UpdateVerifier.IsAllowedDownloadHost(uri));
    }

    // ──────────────────────────────────────────────────────────────────────
    // helpers
    // ──────────────────────────────────────────────────────────────────────

    private static string WriteRandomFile(out string sha256Hex)
    {
        var path  = Path.Combine(Path.GetTempPath(), $"upd-verify-{Guid.NewGuid():N}.bin");
        var bytes = new byte[4096];
        RandomNumberGenerator.Fill(bytes);
        File.WriteAllBytes(path, bytes);
        sha256Hex = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        return path;
    }
}
