using System;
using NSec.Cryptography;
using PanelCalculator.Core.Security;
using RabKit.Branding;
using Xunit;

namespace PanelCalculator.Tests.Security;

/// <summary>
/// Tests for <see cref="EditionContext.ValidateAndSetLicense"/> covering:
/// <list type="bullet">
///   <item>V1 backward compat — legacy PT TTS license validates and
///         inherits edition fields from the bundled manifest.</item>
///   <item>V2 explicit claims — license carries its own edition/tier and
///         the validator cross-checks against the manifest.</item>
///   <item>EditionMismatch rejection — V2 license issued for a different
///         editionId than the install bundles.</item>
///   <item>Expiry rejection — V2 license past <c>ExpiresAtUtc</c>.</item>
/// </list>
///
/// <para>
/// Tests bypass <see cref="LicenseService"/> entirely so they can wire
/// fresh keypairs without touching the production
/// <c>BrandContext.LicensePublicKeyBase64</c>. Each test rebinds
/// <see cref="BrandContext"/> with a test brand carrying the test pubkey,
/// then resets afterwards via the <see cref="IDisposable"/> pattern.
/// </para>
/// </summary>
[Collection("EditionContextSerial")]  // serialise — these mutate static singletons
public class EditionContextTests : IDisposable
{
    private readonly Key   _signerKey;
    private readonly string _signerPubB64;
    private readonly byte[] _fingerprint = new byte[]
    {
        0xA1, 0xB2, 0xC3, 0xD4, 0xE5, 0xF6, 0x07, 0x08
    };

    public EditionContextTests()
    {
        _signerKey = Key.Create(SignatureAlgorithm.Ed25519, new KeyCreationParameters
        {
            ExportPolicy = KeyExportPolicies.AllowPlaintextExport
        });
        _signerPubB64 = Convert.ToBase64String(_signerKey.PublicKey.Export(KeyBlobFormat.RawPublicKey));

        // Stub BrandContext with a test pubkey + EditionContext with a baseline manifest.
        BrandContext.Initialize(
            new TestBrandConfig(_signerPubB64),
            new PanelBranding.PanelIndustryProfile());
        EditionContext.Initialize(MakeManifest(editionId: "custom-tts-panel-v1", tier: "custom"));
    }

    public void Dispose()
    {
        _signerKey.Dispose();
        EditionContext.ResetForTests();
        BrandContext.Initialize(
            new PanelBranding.PanelBrandConfig(),
            new PanelBranding.PanelIndustryProfile());
    }

    // ── V1 backward compat — KRITIS ─────────────────────────────────────

    [Fact]
    public void ValidateAndSetLicense_V1License_FallsBackToManifestEditionFields()
    {
        // Issue a V1 license (no edition claims in payload).
        var payload = LicensePayload.BuildSignablePayload(_fingerprint, "PT Legacy TTS", DateTime.UtcNow);
        var sig = SignatureAlgorithm.Ed25519.Sign(_signerKey, payload);
        var input = MakeInput(payload, sig, isV2: false, customerName: "PT Legacy TTS");

        var result = EditionContext.ValidateAndSetLicense(input, _fingerprint);

        Assert.True(result.IsValid, $"Expected Valid, got {result.Reason}: {result.Detail}");
        Assert.Equal("Valid", result.Reason);

        var claims = EditionContext.CurrentLicense;
        Assert.NotNull(claims);

        // KRITIS: V1 license + TTS manifest → tier resolves to manifest's "custom".
        Assert.Equal(1, claims!.FormatVersion);
        Assert.Equal("custom-tts-panel-v1", claims.EditionId);
        Assert.Equal("custom", claims.Tier);
        Assert.Equal("panel-electrical", claims.Industry);
        Assert.Equal("PT Legacy TTS", claims.CustomerName);
    }

    // ── V2 happy path ───────────────────────────────────────────────────

    [Fact]
    public void ValidateAndSetLicense_V2License_MatchingManifest_Succeeds()
    {
        var features = new EditionFeatures(false, false, false, 0, 999, false);
        var payload = LicensePayload.BuildSignablePayload(
            _fingerprint, "PT Custom TTS", DateTime.UtcNow,
            editionId: "custom-tts-panel-v1",
            tier: "custom",
            industry: "panel-electrical",
            expiresAtUtc: null,
            features: features);
        var sig = SignatureAlgorithm.Ed25519.Sign(_signerKey, payload);
        var input = MakeInput(payload, sig, isV2: true, customerName: "PT Custom TTS",
            editionId: "custom-tts-panel-v1", tier: "custom", industry: "panel-electrical",
            features: features);

        var result = EditionContext.ValidateAndSetLicense(input, _fingerprint);

        Assert.True(result.IsValid);
        Assert.Equal("Valid", result.Reason);

        var claims = EditionContext.CurrentLicense;
        Assert.Equal(2, claims!.FormatVersion);
        Assert.Equal("custom-tts-panel-v1", claims.EditionId);
    }

    // ── V2 edition mismatch ─────────────────────────────────────────────

    [Fact]
    public void ValidateAndSetLicense_V2License_DifferentEdition_Rejects()
    {
        // Manifest says custom-tts-panel-v1, license says generic-rab-v1.
        var features = new EditionFeatures();
        var payload = LicensePayload.BuildSignablePayload(
            _fingerprint, "PT Wrong Edition", DateTime.UtcNow,
            editionId: "generic-rab-v1",
            tier: "generic-pro",
            industry: "panel-electrical",
            expiresAtUtc: null,
            features: features);
        var sig = SignatureAlgorithm.Ed25519.Sign(_signerKey, payload);
        var input = MakeInput(payload, sig, isV2: true, customerName: "PT Wrong Edition",
            editionId: "generic-rab-v1", tier: "generic-pro", industry: "panel-electrical",
            features: features);

        var result = EditionContext.ValidateAndSetLicense(input, _fingerprint);

        Assert.False(result.IsValid);
        Assert.Equal("EditionMismatch", result.Reason);
        Assert.Null(EditionContext.CurrentLicense);  // failed → no claims cached
    }

    // ── V2 expired ──────────────────────────────────────────────────────

    [Fact]
    public void ValidateAndSetLicense_V2License_Expired_Rejects()
    {
        var pastExpiry = DateTime.UtcNow.AddDays(-1);  // yesterday
        var features = new EditionFeatures();
        var payload = LicensePayload.BuildSignablePayload(
            _fingerprint, "PT Expired", DateTime.UtcNow.AddYears(-1),
            editionId: "custom-tts-panel-v1",
            tier: "custom",
            industry: "panel-electrical",
            expiresAtUtc: pastExpiry,
            features: features);
        var sig = SignatureAlgorithm.Ed25519.Sign(_signerKey, payload);
        var input = MakeInput(payload, sig, isV2: true, customerName: "PT Expired",
            editionId: "custom-tts-panel-v1", tier: "custom", industry: "panel-electrical",
            expiresAtUtc: pastExpiry, features: features);

        var result = EditionContext.ValidateAndSetLicense(input, _fingerprint);

        Assert.False(result.IsValid);
        Assert.Equal("Expired", result.Reason);
    }

    // ── Wrong hardware fingerprint ──────────────────────────────────────

    [Fact]
    public void ValidateAndSetLicense_WrongFingerprint_Rejects()
    {
        var payload = LicensePayload.BuildSignablePayload(_fingerprint, "PT Hardware Locked", DateTime.UtcNow);
        var sig = SignatureAlgorithm.Ed25519.Sign(_signerKey, payload);
        var input = MakeInput(payload, sig, isV2: false, customerName: "PT Hardware Locked");

        var otherMachine = new byte[8] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF };
        var result = EditionContext.ValidateAndSetLicense(input, otherMachine);

        Assert.False(result.IsValid);
        Assert.Equal("WrongHardware", result.Reason);
    }

    // ── Tier display name ───────────────────────────────────────────────

    [Fact]
    public void GetTierDisplayName_AfterV1License_ShowsCustomFromManifest()
    {
        var payload = LicensePayload.BuildSignablePayload(_fingerprint, "PT Display", DateTime.UtcNow);
        var sig = SignatureAlgorithm.Ed25519.Sign(_signerKey, payload);
        var input = MakeInput(payload, sig, isV2: false, customerName: "PT Display");

        EditionContext.ValidateAndSetLicense(input, _fingerprint);
        Assert.Equal("Custom", EditionContext.GetTierDisplayName());
    }

    [Fact]
    public void GetTierDisplayName_BeforeActivation_ShowsCustomFromManifestStillAvailable()
    {
        // No license activated yet — should still show the manifest's tier.
        Assert.Equal("Custom", EditionContext.GetTierDisplayName());
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static EditionManifest MakeManifest(string editionId, string tier)
        => new(
            EditionId: editionId,
            EditionTier: tier,
            Industry: "panel-electrical",
            BundledIndustryPackId: "test-pack-2026.Q1",
            Features: new EditionFeatures(MaxConcurrentSeats: 999),
            IssuedAt: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            IssuerKey: "test-issuer");

    private static DecodedLicenseInput MakeInput(
        byte[] payload, byte[] sig, bool isV2, string customerName,
        string editionId = "",
        string tier = "",
        string industry = "",
        DateTime? expiresAtUtc = null,
        EditionFeatures? features = null)
    {
        return new DecodedLicenseInput(
            Payload: payload,
            Signature: sig,
            FormatVersion: isV2 ? 2 : 1,
            HardwareFingerprint: new byte[] { 0xA1, 0xB2, 0xC3, 0xD4, 0xE5, 0xF6, 0x07, 0x08 },
            CustomerName: customerName,
            IssueDateUtc: DateTime.UtcNow,
            EditionId: editionId,
            Tier: tier,
            Industry: industry,
            ExpiresAtUtc: expiresAtUtc,
            Features: features ?? new EditionFeatures());
    }

    /// <summary>
    /// Test stub of <see cref="IBrandConfig"/> — only LicensePublicKeyBase64
    /// is used by EditionContext; everything else returns dummy values.
    /// </summary>
    private sealed class TestBrandConfig : IBrandConfig
    {
        public TestBrandConfig(string licensePubB64) { LicensePublicKeyBase64 = licensePubB64; }

        public string CompanyName => "TEST";
        public string CompanyShortName => "T";
        public string CompanyNpwp => "";
        public string CompanyAddress => "";
        public string CompanyPhone => "";
        public string CompanyEmail => "";
        public string CompanyWebsite => "";
        public string SupportWhatsAppNumber => "";
        public string DefaultSignerName => "TEST";
        public string DefaultSignerTitle => "TEST";
        public string DefaultOfferLocation => "TEST";
        public byte[]? GetLogoBytes() => null;
        public byte[]? GetLetterheadBytes() => null;
        public byte[]? GetSignatureBytes() => null;
        public byte[]? GetStampBytes() => null;
        public byte[]? GetEditionManifestBundle() => null;
        public string LegacyMachineKeyAppTag => "TEST";
        public byte[] MachineKeyPepperBytes => new byte[16];
        public string LicensePublicKeyBase64 { get; }
        public string UpdateGitHubOwner => "TEST";
        public string UpdateGitHubRepo => "TEST";
        public string UpdateAssetName => "TEST";
        public string AppDisplayName => "TEST";
        public string AppDataFolderName => "TEST";
        public string AppVersion => "1.3.0";
        public string EstimationNumberPattern => "TEST-{0:yyyyMMdd}-{1:D3}";
    }
}
