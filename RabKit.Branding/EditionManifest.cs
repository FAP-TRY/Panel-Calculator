using System.Text.Json.Serialization;

namespace RabKit.Branding;

/// <summary>
/// Signed JSON manifest that locks a build to a specific edition (Custom /
/// Generic-Free / Generic-Pro / Lifetime / etc.) and a specific industry
/// (panel-electrical / carrosserie / sipil / mep). Bundled at compile time
/// as <c>edition.manifest.bundle</c> in the brand pack assembly, signed
/// offline by the brand owner's Ed25519 private key.
///
/// <para>
/// The manifest is the SINGLE SOURCE OF TRUTH for an edition's identity —
/// the license <em>claims</em> a tier (V2 license format, see
/// <c>PanelCalculator.Core.Security.LicensePayload</c>), but the manifest
/// determines what tier the install is ALLOWED to operate at. A V1
/// (legacy) license without explicit edition claims inherits the manifest
/// defaults (backward compat for existing PT TTS installs).
/// </para>
///
/// <para>
/// JSON ordering matches the order the spec was issued in (W4 plan) so
/// signature bytes stay deterministic across SerializerOptions tweaks.
/// </para>
/// </summary>
public sealed record EditionManifest(
    [property: JsonPropertyName("editionId")]              string EditionId,
    [property: JsonPropertyName("editionTier")]            string EditionTier,
    [property: JsonPropertyName("industry")]               string Industry,
    [property: JsonPropertyName("bundledIndustryPackId")]  string BundledIndustryPackId,
    [property: JsonPropertyName("features")]               EditionFeatures Features,
    [property: JsonPropertyName("issuedAt")]               DateTime IssuedAt,
    [property: JsonPropertyName("issuerKey")]              string IssuerKey
);

/// <summary>
/// Feature flag set bundled in the manifest. License V2 claims can also
/// carry features (see <c>LicensePayload</c>'s features bitmask) — the
/// effective feature set is computed in <see cref="EditionContext"/> by
/// taking the manifest features as the floor (Custom edition installs
/// without a license fall back to manifest-only).
/// </summary>
public sealed record EditionFeatures(
    [property: JsonPropertyName("watermarkOutput")]        bool WatermarkOutput = false,
    [property: JsonPropertyName("allowBrandingOverride")]  bool AllowBrandingOverride = false,
    [property: JsonPropertyName("allowMarketplacePacks")]  bool AllowMarketplacePacks = false,
    [property: JsonPropertyName("maxEstimationsPerMonth")] int MaxEstimationsPerMonth = 0,
    [property: JsonPropertyName("maxConcurrentSeats")]     int MaxConcurrentSeats = 1,
    [property: JsonPropertyName("requiresOnlineActivation")] bool RequiresOnlineActivation = false
);

/// <summary>
/// Thrown by <see cref="SignedManifestLoader"/> when the manifest bytes
/// fail Ed25519 signature verification, the JSON is malformed, or the
/// bundle layout is truncated. Caller (Program.Main wiring) should treat
/// this as a fatal startup error — never silently fall back to a default
/// manifest, otherwise an attacker can swap the manifest to escalate tier.
/// </summary>
public sealed class InvalidManifestException : Exception
{
    public InvalidManifestException(string message) : base(message) { }
    public InvalidManifestException(string message, Exception innerException)
        : base(message, innerException) { }
}
