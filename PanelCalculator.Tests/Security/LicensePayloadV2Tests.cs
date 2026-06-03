using System;
using PanelCalculator.Core.Security;
using RabKit.Branding;
using Xunit;

namespace PanelCalculator.Tests.Security;

/// <summary>
/// Round-trip tests for the V2 license payload format introduced in v1.3.0.
///
/// <para>
/// The CRITICAL property under test is backward compat: a V1 license issued
/// by the pre-v1.3.0 keygen tool must keep decoding correctly under the new
/// V2-aware decoder, with empty edition/tier/industry strings and default
/// (all-false) features. <see cref="LicenseService"/> applies fallback
/// values from the loaded <see cref="EditionManifest"/> for V1 licenses.
/// </para>
/// </summary>
public class LicensePayloadV2Tests
{
    private static readonly byte[] SampleFingerprint = new byte[]
    {
        0xA3, 0xF7, 0x9C, 0x2B, 0x1E, 0x4D, 0x8F, 0x60
    };

    // ── V1 backward compat ──────────────────────────────────────────────

    [Fact]
    public void V1Payload_DecodesAsVersion1_WithEmptyEditionFields()
    {
        // The simple (V1) BuildSignablePayload overload must still emit a
        // V1 payload that round-trips through Decode as Version=1.
        var payload = LicensePayload.BuildSignablePayload(SampleFingerprint, "PT Legacy TTS", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var fakeSig = new byte[LicensePayload.SignatureLength];
        var encoded = LicensePayload.Encode(payload, fakeSig);

        var decoded = LicensePayload.Decode(encoded);

        Assert.Equal(LicensePayload.FormatVersionV1, decoded.Version);
        Assert.Equal("PT Legacy TTS", decoded.CustomerName);
        Assert.Equal(SampleFingerprint, decoded.HardwareFingerprint);
        Assert.Equal("", decoded.EditionId);
        Assert.Equal("", decoded.Tier);
        Assert.Equal("", decoded.Industry);
        Assert.Null(decoded.ExpiresAtUtc);
        Assert.False(decoded.Features.WatermarkOutput);
        Assert.False(decoded.Features.AllowBrandingOverride);
        Assert.False(decoded.Features.AllowMarketplacePacks);
        Assert.False(decoded.Features.RequiresOnlineActivation);
    }

    [Fact]
    public void V1Payload_FormatVersionConstant_StaysAt1()
    {
        // KRITIS guard: a future PR must not silently bump FormatVersion to 2
        // and break every existing PT TTS license (their signature was over
        // version byte = 1, so the verify would fail).
        Assert.Equal((byte)1, LicensePayload.FormatVersion);
    }

    // ── V2 round-trip ───────────────────────────────────────────────────

    [Fact]
    public void V2Payload_RoundTrip_PreservesAllFields()
    {
        var issued = new DateTime(2026, 6, 1, 10, 0, 0, DateTimeKind.Utc);
        var expires = new DateTime(2027, 6, 1, 10, 0, 0, DateTimeKind.Utc);
        var features = new EditionFeatures(
            WatermarkOutput: true,
            AllowBrandingOverride: true,
            AllowMarketplacePacks: false,
            MaxEstimationsPerMonth: 100,
            MaxConcurrentSeats: 5,
            RequiresOnlineActivation: true);

        var payload = LicensePayload.BuildSignablePayload(
            SampleFingerprint, "PT Generic Pro", issued,
            editionId: "generic-rab-v1",
            tier: "generic-pro",
            industry: "panel-electrical",
            expiresAtUtc: expires,
            features: features);

        var fakeSig = new byte[LicensePayload.SignatureLength];
        var encoded = LicensePayload.Encode(payload, fakeSig);

        var decoded = LicensePayload.Decode(encoded);

        Assert.Equal(LicensePayload.FormatVersionV2, decoded.Version);
        Assert.Equal("PT Generic Pro", decoded.CustomerName);
        Assert.Equal(SampleFingerprint, decoded.HardwareFingerprint);
        Assert.Equal("generic-rab-v1", decoded.EditionId);
        Assert.Equal("generic-pro", decoded.Tier);
        Assert.Equal("panel-electrical", decoded.Industry);
        Assert.Equal(expires, decoded.ExpiresAtUtc);
        Assert.True(decoded.Features.WatermarkOutput);
        Assert.True(decoded.Features.AllowBrandingOverride);
        Assert.False(decoded.Features.AllowMarketplacePacks);
        Assert.True(decoded.Features.RequiresOnlineActivation);
    }

    [Fact]
    public void V2Payload_NeverExpires_RoundTripsAsNull()
    {
        var payload = LicensePayload.BuildSignablePayload(
            SampleFingerprint, "PT Lifetime", DateTime.UtcNow,
            editionId: "custom-tts-panel-v1",
            tier: "custom",
            industry: "panel-electrical",
            expiresAtUtc: null,    // perpetual
            features: new EditionFeatures());

        var fakeSig = new byte[LicensePayload.SignatureLength];
        var encoded = LicensePayload.Encode(payload, fakeSig);

        var decoded = LicensePayload.Decode(encoded);

        Assert.Equal(LicensePayload.FormatVersionV2, decoded.Version);
        Assert.Null(decoded.ExpiresAtUtc);
    }

    [Fact]
    public void V2Payload_EmptyEditionStrings_StillRoundTrip()
    {
        // Edge case: a partially-filled V2 license still encodes/decodes
        // cleanly. EditionContext is what enforces semantic completeness.
        var payload = LicensePayload.BuildSignablePayload(
            SampleFingerprint, "PT Partial", DateTime.UtcNow,
            editionId: "",
            tier: "",
            industry: "",
            expiresAtUtc: null,
            features: new EditionFeatures());

        var fakeSig = new byte[LicensePayload.SignatureLength];
        var encoded = LicensePayload.Encode(payload, fakeSig);

        var decoded = LicensePayload.Decode(encoded);
        Assert.Equal(LicensePayload.FormatVersionV2, decoded.Version);
        Assert.Equal("", decoded.EditionId);
        Assert.Equal("", decoded.Tier);
        Assert.Equal("", decoded.Industry);
    }

    // ── Features bitmask packing ────────────────────────────────────────

    [Fact]
    public void Features_BitmaskRoundTrip_PreservesAllBoolFlags()
    {
        var combos = new[]
        {
            new EditionFeatures(false, false, false, 0, 0, false),
            new EditionFeatures(true,  false, false, 0, 0, false),
            new EditionFeatures(false, true,  false, 0, 0, false),
            new EditionFeatures(false, false, true,  0, 0, false),
            new EditionFeatures(false, false, false, 0, 0, true),
            new EditionFeatures(true,  true,  true,  0, 0, true),
        };

        foreach (var input in combos)
        {
            byte packed = LicensePayload.PackFeaturesBitmask(input);
            var unpacked = LicensePayload.UnpackFeaturesBitmask(packed);

            Assert.Equal(input.WatermarkOutput,          unpacked.WatermarkOutput);
            Assert.Equal(input.AllowBrandingOverride,    unpacked.AllowBrandingOverride);
            Assert.Equal(input.AllowMarketplacePacks,    unpacked.AllowMarketplacePacks);
            Assert.Equal(input.RequiresOnlineActivation, unpacked.RequiresOnlineActivation);
            // Numeric quotas are not encoded in bitmask — caller must apply manifest fallback.
            Assert.Equal(0, unpacked.MaxEstimationsPerMonth);
            Assert.Equal(1, unpacked.MaxConcurrentSeats);
        }
    }

    [Fact]
    public void Features_BitmaskReservedBits_AreZero()
    {
        // bits 4-7 are reserved — packing should never set them.
        var input = new EditionFeatures(true, true, true, 99, 99, true);
        byte packed = LicensePayload.PackFeaturesBitmask(input);
        Assert.Equal(0, packed & 0b1111_0000);
        Assert.Equal(0b0000_1111, packed);
    }
}
