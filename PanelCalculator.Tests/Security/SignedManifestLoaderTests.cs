using System;
using System.Text;
using System.Text.Json;
using NSec.Cryptography;
using RabKit.Branding;
using Xunit;

namespace PanelCalculator.Tests.Security;

/// <summary>
/// Round-trip + tampering tests for <see cref="SignedManifestLoader"/>.
/// Each test generates a fresh Ed25519 keypair in-memory; production
/// constants are irrelevant — what matters is that the verifier accepts
/// what the signer emits and rejects anything else.
/// </summary>
public class SignedManifestLoaderTests
{
    private static EditionManifest SampleManifest() => new(
        EditionId: "custom-tts-panel-v1",
        EditionTier: "custom",
        Industry: "panel-electrical",
        BundledIndustryPackId: "tts-panel-listrik-2026.Q1",
        Features: new EditionFeatures(
            WatermarkOutput: false,
            AllowBrandingOverride: false,
            AllowMarketplacePacks: false,
            MaxEstimationsPerMonth: 0,
            MaxConcurrentSeats: 999,
            RequiresOnlineActivation: false),
        IssuedAt: new DateTime(2026, 6, 2, 0, 0, 0, DateTimeKind.Utc),
        IssuerKey: "test-issuer-key");

    private static (byte[] jsonBytes, byte[] sig, string pubB64) SignSample(EditionManifest? overrideManifest = null)
    {
        var manifest = overrideManifest ?? SampleManifest();
        var jsonBytes = SignedManifestLoader.SerializeManifestForSigning(manifest);

        var algo = SignatureAlgorithm.Ed25519;
        using var key = Key.Create(algo, new KeyCreationParameters
        {
            ExportPolicy = KeyExportPolicies.AllowPlaintextExport
        });
        var pubB64 = Convert.ToBase64String(key.PublicKey.Export(KeyBlobFormat.RawPublicKey));
        var sig = algo.Sign(key, jsonBytes);
        return (jsonBytes, sig, pubB64);
    }

    [Fact]
    public void LoadAndVerify_ValidSignature_ReturnsManifest()
    {
        var (jsonBytes, sig, pub) = SignSample();

        var manifest = SignedManifestLoader.LoadAndVerify(jsonBytes, sig, pub);

        Assert.NotNull(manifest);
        Assert.Equal("custom-tts-panel-v1", manifest.EditionId);
        Assert.Equal("custom", manifest.EditionTier);
        Assert.Equal("panel-electrical", manifest.Industry);
        Assert.Equal("tts-panel-listrik-2026.Q1", manifest.BundledIndustryPackId);
        Assert.NotNull(manifest.Features);
        Assert.Equal(999, manifest.Features.MaxConcurrentSeats);
        Assert.False(manifest.Features.WatermarkOutput);
    }

    [Fact]
    public void LoadAndVerify_TamperedJson_ThrowsInvalidManifest()
    {
        var (jsonBytes, sig, pub) = SignSample();

        // Flip one byte in the JSON — signature now mismatches
        var tampered = (byte[])jsonBytes.Clone();
        // Find first occurrence of "custom" and flip a byte inside it
        var custom = Encoding.UTF8.GetBytes("custom");
        int idx = IndexOf(tampered, custom);
        Assert.True(idx >= 0, "Sample JSON should contain 'custom' literal.");
        tampered[idx] = (byte)'X';

        var ex = Assert.Throws<InvalidManifestException>(
            () => SignedManifestLoader.LoadAndVerify(tampered, sig, pub));
        Assert.Contains("signature", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LoadAndVerify_WrongPublicKey_ThrowsInvalidManifest()
    {
        var (jsonBytes, sig, _) = SignSample();

        // Use a DIFFERENT keypair's public key
        using var otherKey = Key.Create(SignatureAlgorithm.Ed25519, new KeyCreationParameters
        {
            ExportPolicy = KeyExportPolicies.AllowPlaintextExport
        });
        var otherPub = Convert.ToBase64String(otherKey.PublicKey.Export(KeyBlobFormat.RawPublicKey));

        var ex = Assert.Throws<InvalidManifestException>(
            () => SignedManifestLoader.LoadAndVerify(jsonBytes, sig, otherPub));
        Assert.Contains("signature", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LoadAndVerifyFromBundle_ValidBundle_ReturnsManifest()
    {
        var (jsonBytes, sig, pub) = SignSample();
        var bundle = SignedManifestLoader.PackBundle(jsonBytes, sig);

        var manifest = SignedManifestLoader.LoadAndVerifyFromBundle(bundle, pub);

        Assert.NotNull(manifest);
        Assert.Equal("custom-tts-panel-v1", manifest.EditionId);
    }

    [Fact]
    public void LoadAndVerifyFromBundle_TruncatedBundle_Throws()
    {
        var (jsonBytes, sig, pub) = SignSample();
        var bundle = SignedManifestLoader.PackBundle(jsonBytes, sig);

        // Lop off the last 10 bytes of the signature
        var truncated = new byte[bundle.Length - 10];
        Buffer.BlockCopy(bundle, 0, truncated, 0, truncated.Length);

        var ex = Assert.Throws<InvalidManifestException>(
            () => SignedManifestLoader.LoadAndVerifyFromBundle(truncated, pub));
        Assert.Contains("size mismatch", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ── Helpers ──

    private static int IndexOf(byte[] haystack, byte[] needle)
    {
        for (int i = 0; i <= haystack.Length - needle.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < needle.Length; j++)
                if (haystack[i + j] != needle[j]) { match = false; break; }
            if (match) return i;
        }
        return -1;
    }
}
