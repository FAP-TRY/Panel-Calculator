using System.Buffers.Binary;
using NSec.Cryptography;

namespace RabKit.Branding;

/// <summary>
/// Process-wide registry for the active <see cref="EditionManifest"/> and
/// (after license activation) the validated license claims.
///
/// <para>
/// Wired exactly once at startup — Program.Main loads the manifest from
/// the brand pack and calls <see cref="Initialize"/>. The LicenseGate then
/// calls <see cref="ValidateAndSetLicense"/> after the user activates.
/// </para>
///
/// <para>
/// API mirrors <see cref="BrandContext"/> deliberately: static accessor +
/// throw-if-not-initialized, lock-guarded init for test safety. The two
/// contexts are sibling singletons — BrandContext holds the per-tenant
/// values, EditionContext layers the multi-edition policy/manifest on top.
/// </para>
/// </summary>
public static class EditionContext
{
    private static EditionManifest? _manifest;
    private static LicenseClaims?   _license;
    private static readonly object  _gate = new();

    /// <summary>
    /// Currently-active manifest. Throws when accessed before
    /// <see cref="Initialize"/> — same contract as <see cref="BrandContext.Current"/>.
    /// </summary>
    public static EditionManifest Manifest
    {
        get
        {
            if (_manifest == null)
                throw new InvalidOperationException(
                    "EditionContext.Initialize() must be called from Program.Main " +
                    "before any other code accesses EditionContext.");
            return _manifest;
        }
    }

    /// <summary>
    /// Returns the currently-validated license claims, or null if no
    /// license has been activated yet (i.e. before LicenseGate runs, or
    /// while LicenseGate is showing the ActivationForm).
    ///
    /// <para>
    /// After <see cref="ValidateAndSetLicense"/> succeeds, this returns
    /// a <see cref="LicenseClaims"/> with edition/tier/industry values
    /// from EITHER the V2 license itself OR (for V1 fallback) the manifest.
    /// Callers should NOT distinguish — both shapes are equivalent here.
    /// </para>
    /// </summary>
    public static LicenseClaims? CurrentLicense => _license;

    /// <summary>True once <see cref="Initialize"/> has been called.</summary>
    public static bool IsInitialized
    {
        get { lock (_gate) { return _manifest != null; } }
    }

    /// <summary>
    /// Bind the manifest. Safe to call multiple times (e.g. in tests) —
    /// later calls replace the previous manifest AND clear any cached
    /// license claims (since a new manifest may have different defaults).
    /// </summary>
    public static void Initialize(EditionManifest manifest)
    {
        if (manifest == null) throw new ArgumentNullException(nameof(manifest));
        lock (_gate)
        {
            _manifest = manifest;
            _license  = null;
        }
    }

    /// <summary>
    /// Test-only: clear both manifest + license. Production code never calls.
    /// </summary>
    internal static void ResetForTests()
    {
        lock (_gate)
        {
            _manifest = null;
            _license  = null;
        }
    }

    /// <summary>
    /// Validate the decoded license against the active manifest, applying
    /// V1 fallback if the license predates the multi-edition extension.
    ///
    /// <para>
    /// On success, the resolved claims are cached in <see cref="CurrentLicense"/>
    /// and the method returns <c>EditionValidationResult.Valid</c>.
    /// </para>
    ///
    /// <para>
    /// On failure (signature bad / hardware mismatch / edition mismatch /
    /// expired), the cached license is NOT updated and a failure result is
    /// returned. The caller (LicenseGate) decides whether to surface the
    /// ActivationForm again or exit.
    /// </para>
    /// </summary>
    /// <param name="license">Decoded license payload (V1 or V2).</param>
    /// <param name="hardwareFingerprint">8-byte machine fingerprint.</param>
    public static EditionValidationResult ValidateAndSetLicense(
        DecodedLicenseInput license,
        byte[] hardwareFingerprint)
    {
        if (license == null) throw new ArgumentNullException(nameof(license));
        if (_manifest == null)
            return new EditionValidationResult(false, "EditionContextNotInitialized",
                "Manifest not loaded before license activation.");

        // ── 1. Verify Ed25519 signature ────────────────────────────────
        // The license public key comes from BrandContext (per-edition signer).
        var pubB64 = BrandContext.IsInitialized
            ? BrandContext.Current.LicensePublicKeyBase64
            : null;
        if (string.IsNullOrEmpty(pubB64))
            return new EditionValidationResult(false, "NoPublicKey",
                "BrandContext.Current.LicensePublicKeyBase64 is not configured.");

        PublicKey publicKey;
        try
        {
            var pkBytes = Convert.FromBase64String(pubB64);
            publicKey = PublicKey.Import(SignatureAlgorithm.Ed25519, pkBytes, KeyBlobFormat.RawPublicKey);
        }
        catch (Exception ex)
        {
            return new EditionValidationResult(false, "InvalidPublicKey",
                $"Embedded license public key malformed: {ex.Message}");
        }

        bool sigOk = SignatureAlgorithm.Ed25519.Verify(publicKey, license.Payload, license.Signature);
        if (!sigOk)
            return new EditionValidationResult(false, "InvalidSignature",
                "License signature does not verify against this brand's public key.");

        // ── 2. Hardware binding ────────────────────────────────────────
        if (hardwareFingerprint == null || hardwareFingerprint.Length != license.HardwareFingerprint.Length
            || !ConstantTimeEquals(hardwareFingerprint, license.HardwareFingerprint))
        {
            return new EditionValidationResult(false, "WrongHardware",
                "License is bound to a different machine fingerprint.");
        }

        // ── 3. Resolve edition claims (V1 fallback or V2 explicit) ─────
        bool isV1 = string.IsNullOrEmpty(license.EditionId);

        string resolvedEditionId  = isV1 ? _manifest.EditionId  : license.EditionId;
        string resolvedTier       = isV1 ? _manifest.EditionTier : license.Tier;
        string resolvedIndustry   = isV1 ? _manifest.Industry   : license.Industry;
        EditionFeatures resolvedFeatures = isV1 ? _manifest.Features : license.Features;

        // ── 4. V2 cross-check: license edition MUST match manifest ─────
        // (V1 licenses skip this — they were issued before multi-edition
        // existed and inherit whatever manifest is bundled.)
        if (!isV1)
        {
            if (!string.Equals(license.EditionId, _manifest.EditionId, StringComparison.Ordinal))
            {
                return new EditionValidationResult(false, "EditionMismatch",
                    $"License is for edition '{license.EditionId}' but this install bundles " +
                    $"manifest '{_manifest.EditionId}'. " +
                    "Customer must either reinstall the matching edition or contact support for a re-issued license.");
            }
        }

        // ── 5. Expiry check ────────────────────────────────────────────
        if (license.ExpiresAtUtc.HasValue && DateTime.UtcNow > license.ExpiresAtUtc.Value)
        {
            return new EditionValidationResult(false, "Expired",
                $"License expired on {license.ExpiresAtUtc.Value:yyyy-MM-dd HH:mm} UTC.");
        }

        // ── 6. Commit resolved claims ──────────────────────────────────
        var claims = new LicenseClaims(
            CustomerName:    license.CustomerName,
            IssueDateUtc:    license.IssueDateUtc,
            HardwareFingerprint: license.HardwareFingerprint,
            FormatVersion:   license.FormatVersion,
            EditionId:       resolvedEditionId,
            Tier:            resolvedTier,
            Industry:        resolvedIndustry,
            ExpiresAtUtc:    license.ExpiresAtUtc,
            Features:        resolvedFeatures);

        lock (_gate) { _license = claims; }

        return new EditionValidationResult(true, "Valid", null);
    }

    /// <summary>
    /// Human-readable tier label for the title bar. Returns "Custom" for
    /// V1 licenses (manifest decides) and a friendly name for V2 tier slugs.
    /// </summary>
    public static string GetTierDisplayName()
    {
        var tier = _license?.Tier;
        if (string.IsNullOrEmpty(tier))
            tier = _manifest?.EditionTier;
        return tier switch
        {
            "custom"            => "Custom",
            "generic-free"      => "Free Trial",
            "generic-basic"     => "Basic",
            "generic-pro"       => "Pro",
            "generic-business"  => "Business",
            "lifetime-premium"  => "Lifetime Premium",
            null or ""          => "Unknown",
            _                   => tier
        };
    }

    private static bool ConstantTimeEquals(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return false;
        int diff = 0;
        for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
        return diff == 0;
    }
}

/// <summary>
/// Outcome of <see cref="EditionContext.ValidateAndSetLicense"/>.
/// </summary>
/// <param name="IsValid">True when the license passed every check.</param>
/// <param name="Reason">Stable machine-readable token (Valid /
///   EditionMismatch / Expired / InvalidSignature / WrongHardware /
///   InvalidPublicKey / NoPublicKey / EditionContextNotInitialized).</param>
/// <param name="Detail">Human-readable description suitable for log lines
///   or fallback error dialogs. Optional — Valid case carries null.</param>
public sealed record EditionValidationResult(
    bool IsValid,
    string Reason,
    string? Detail);

/// <summary>
/// Resolved license claims cached in <see cref="EditionContext.CurrentLicense"/>.
/// Each field is fully resolved — V1 licenses get manifest fallbacks
/// already applied so callers don't need to know about format versions.
/// </summary>
public sealed record LicenseClaims(
    string CustomerName,
    DateTime IssueDateUtc,
    byte[] HardwareFingerprint,
    int FormatVersion,
    string EditionId,
    string Tier,
    string Industry,
    DateTime? ExpiresAtUtc,
    EditionFeatures Features);

/// <summary>
/// Engine-side projection of <c>LicensePayload.DecodedLicense</c>. Lives
/// here so <see cref="EditionContext"/> can validate licenses without a
/// dependency on PanelCalculator.Core (which sits one layer up in the
/// reference graph). The caller (LicenseGate) translates between the
/// two records when it crosses the boundary.
/// </summary>
public sealed record DecodedLicenseInput(
    byte[] Payload,
    byte[] Signature,
    int FormatVersion,
    byte[] HardwareFingerprint,
    string CustomerName,
    DateTime IssueDateUtc,
    string EditionId,
    string Tier,
    string Industry,
    DateTime? ExpiresAtUtc,
    EditionFeatures Features);
