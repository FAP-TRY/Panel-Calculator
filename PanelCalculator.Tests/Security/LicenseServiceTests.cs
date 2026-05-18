using System;
using System.Text;
using NSec.Cryptography;
using PanelCalculator.Core.Security;
using Xunit;

namespace PanelCalculator.Tests.Security;

/// <summary>
/// Round-trip + tampering tests for LicenseService / LicensePayload.
/// Each test generates a fresh Ed25519 keypair in-memory and feeds the
/// public key into the test-only validator overload — so no fixture state
/// leaks between tests and the production-embedded constant is irrelevant.
/// </summary>
public class LicenseServiceTests
{
    private static readonly byte[] SampleFingerprint = new byte[]
    {
        0xA3, 0xF7, 0x9C, 0x2B, 0x1E, 0x4D, 0x8F, 0x60
    };

    private static (Key key, string publicB64) MakeKeypair()
    {
        var algo = SignatureAlgorithm.Ed25519;
        var key = Key.Create(algo, new KeyCreationParameters
        {
            ExportPolicy = KeyExportPolicies.AllowPlaintextExport
        });
        var pubB64 = Convert.ToBase64String(key.PublicKey.Export(KeyBlobFormat.RawPublicKey));
        return (key, pubB64);
    }

    private static string IssueLicense(Key key, byte[] fp, string customerName, DateTime? issueDate = null)
    {
        var payload   = LicensePayload.BuildSignablePayload(fp, customerName, issueDate ?? DateTime.UtcNow);
        var signature = SignatureAlgorithm.Ed25519.Sign(key, payload);
        return LicensePayload.Encode(payload, signature);
    }

    // ─────────────────────────────────────────────────────────────────────
    [Fact]
    public void ValidateLicense_ValidKey_ReturnsValid()
    {
        var (key, pub) = MakeKeypair();
        var license = IssueLicense(key, SampleFingerprint, "PT Sample Customer");

        var result = LicenseService.ValidateLicenseWithKey(license, SampleFingerprint, pub);

        Assert.True(result.IsValid, $"Expected valid, got {result.Status}: {result.Reason}");
        Assert.Equal(LicenseValidationStatus.Valid, result.Status);
        Assert.Equal("PT Sample Customer", result.CustomerName);
        Assert.NotNull(result.IssueDate);
    }

    [Fact]
    public void ValidateLicense_TamperedSignature_ReturnsInvalidSignature()
    {
        var (key, pub) = MakeKeypair();
        var license = IssueLicense(key, SampleFingerprint, "PT Sample");

        // Flip one character near the end (where signature bytes live).
        // We have to flip to a different VALID Base32 char or we'd just hit Malformed.
        var stripped = LicensePayload.StripFormatting(license);
        var sb = new StringBuilder(stripped);
        char last = sb[sb.Length - 1];
        sb[sb.Length - 1] = last == 'A' ? 'B' : 'A';
        var tampered = sb.ToString();

        var result = LicenseService.ValidateLicenseWithKey(tampered, SampleFingerprint, pub);

        Assert.Equal(LicenseValidationStatus.InvalidSignature, result.Status);
    }

    [Fact]
    public void ValidateLicense_WrongHardware_ReturnsWrongHardware()
    {
        var (key, pub) = MakeKeypair();
        var license = IssueLicense(key, SampleFingerprint, "PT Sample");

        var differentMachine = new byte[8] { 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88 };
        var result = LicenseService.ValidateLicenseWithKey(license, differentMachine, pub);

        Assert.Equal(LicenseValidationStatus.WrongHardware, result.Status);
    }

    [Fact]
    public void ValidateLicense_DifferentSigner_ReturnsInvalidSignature()
    {
        // Issue with signer A but verify with signer B's public key — this
        // is the realistic "attacker forged a license with their own key"
        // scenario.
        var (signerA, _) = MakeKeypair();
        var (_, pubB)    = MakeKeypair();
        var license = IssueLicense(signerA, SampleFingerprint, "PT Attacker");

        var result = LicenseService.ValidateLicenseWithKey(license, SampleFingerprint, pubB);

        Assert.Equal(LicenseValidationStatus.InvalidSignature, result.Status);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-license")]
    [InlineData("AAAA-BBBB-CCCC")]   // valid base32 chars but far too short
    public void ValidateLicense_Malformed_ReturnsMalformed(string input)
    {
        var (_, pub) = MakeKeypair();
        var result = LicenseService.ValidateLicenseWithKey(input, SampleFingerprint, pub);
        Assert.Equal(LicenseValidationStatus.Malformed, result.Status);
    }

    [Fact]
    public void ValidateLicense_BadBase32Char_ReturnsMalformed()
    {
        // 'I' / 'L' / 'O' / 'U' are intentionally NOT in the alphabet.
        var (_, pub) = MakeKeypair();
        var result = LicenseService.ValidateLicenseWithKey("ILOU-ILOU-ILOU", SampleFingerprint, pub);
        Assert.Equal(LicenseValidationStatus.Malformed, result.Status);
    }

    [Fact]
    public void EncodeDecode_Roundtrip_Identical()
    {
        var payload   = LicensePayload.BuildSignablePayload(SampleFingerprint, "PT Roundtrip", DateTime.UtcNow);
        // Fake signature (64 zero bytes) — we only care about byte layout, not validity.
        var fakeSig   = new byte[LicensePayload.SignatureLength];
        var encoded   = LicensePayload.Encode(payload, fakeSig);
        var decoded   = LicensePayload.Decode(encoded);

        Assert.Equal(payload, decoded.Payload);
        Assert.Equal(fakeSig, decoded.Signature);
        Assert.Equal(LicensePayload.FormatVersion, decoded.Version);
        Assert.Equal(SampleFingerprint, decoded.HardwareFingerprint);
        Assert.Equal("PT Roundtrip", decoded.CustomerName);
    }

    [Fact]
    public void EncodeDecode_ToleratesDashesWhitespaceAndCase()
    {
        var payload = LicensePayload.BuildSignablePayload(SampleFingerprint, "PT X", DateTime.UtcNow);
        var fakeSig = new byte[LicensePayload.SignatureLength];
        var encoded = LicensePayload.Encode(payload, fakeSig);

        // Mangle: lowercase + remove dashes + add spaces + add extra dashes.
        var messy = "  " + encoded.ToLowerInvariant().Replace("-", " ") + "  -- ";

        var decoded = LicensePayload.Decode(messy);
        Assert.Equal("PT X", decoded.CustomerName);
    }

    [Fact]
    public void Base32_Roundtrip_AllByteValues()
    {
        // Exhaustively test that every byte value survives the base32 hop.
        var input = new byte[256];
        for (int i = 0; i < 256; i++) input[i] = (byte)i;

        var encoded = LicensePayload.ToBase32(input);
        var decoded = LicensePayload.FromBase32(encoded);

        Assert.Equal(input, decoded);
    }

    [Fact]
    public void Group_FormatsEvery5Chars()
    {
        var formatted = LicensePayload.Group("ABCDEFGHIJ", 5, '-');
        Assert.Equal("ABCDE-FGHIJ", formatted);
    }
}
