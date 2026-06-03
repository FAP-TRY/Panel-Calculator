using System.Linq;
using RabKit.Branding;
using Xunit;

namespace PanelCalculator.Tests.Security;

/// <summary>
/// Sanity test that the Panel.Branding assembly actually embeds the signed
/// <c>edition.manifest.bundle</c> AND it loads + verifies correctly via the
/// public Ed25519 key configured in PanelBrandConfig.LicensePublicKeyBase64.
///
/// <para>
/// If this test fails, the brand pack is broken — App startup will throw
/// <see cref="InvalidManifestException"/> and customers can't boot. The
/// test gives us a fast local signal before we cut a release.
/// </para>
/// </summary>
[Collection("EditionContextSerial")]
public class BrandManifestBundleTests
{
    [Fact]
    public void PanelBrandConfig_GetEditionManifestBundle_ReturnsNonEmptyBytes()
    {
        var config = new PanelBranding.PanelBrandConfig();
        var bundle = config.GetEditionManifestBundle();

        Assert.NotNull(bundle);
        Assert.True(bundle!.Length > 100, $"Bundle suspiciously small: {bundle.Length} bytes");
    }

    [Fact]
    public void PanelBrandConfig_EditionManifestBundle_VerifiesAndDeserializes()
    {
        var config = new PanelBranding.PanelBrandConfig();
        var bundle = config.GetEditionManifestBundle();
        Assert.NotNull(bundle);

        // Verify signature with the same pubkey that production runtime uses.
        var manifest = SignedManifestLoader.LoadAndVerifyFromBundle(
            bundle!, config.LicensePublicKeyBase64);

        Assert.NotNull(manifest);
        Assert.Equal("custom-tts-panel-v1", manifest.EditionId);
        Assert.Equal("custom", manifest.EditionTier);
        Assert.Equal("panel-electrical", manifest.Industry);
        Assert.Equal("tts-panel-listrik-2026.Q1", manifest.BundledIndustryPackId);
        Assert.NotNull(manifest.Features);
        Assert.False(manifest.Features.WatermarkOutput);
        Assert.Equal(999, manifest.Features.MaxConcurrentSeats);
    }
}
