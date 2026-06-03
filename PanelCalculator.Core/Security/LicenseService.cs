using System;
using NSec.Cryptography;
using RabKit.Branding;

namespace PanelCalculator.Core.Security;

/// <summary>
/// Public projection of <c>LicensePayload.DecodedLicense</c> for callers
/// outside PanelCalculator.Core (e.g. LicenseGate in WinForms project)
/// that need the V2 edition claims without InternalsVisibleTo.
///
/// <para>
/// Field-for-field equivalent of the internal record; carries enough
/// bytes for <see cref="RabKit.Branding.EditionContext.ValidateAndSetLicense"/>
/// to re-verify the signature and apply cross-checks.
/// </para>
/// </summary>
public sealed record PublicDecodedLicense(
    byte[]   Payload,
    byte[]   Signature,
    int      Version,
    byte[]   HardwareFingerprint,
    string   CustomerName,
    DateTime IssueDateUtc,
    string   EditionId,
    string   Tier,
    string   Industry,
    DateTime? ExpiresAtUtc,
    EditionFeatures Features);

/// <summary>
/// Public decoder facade — exposes <see cref="LicensePayload.Decode"/> to
/// callers outside the assembly without leaking the internal record.
/// </summary>
public static class LicenseDecoder
{
    /// <summary>
    /// Decode a Base32-grouped license key into its component fields.
    /// Auto-detects V1 vs V2 format; V1 returns empty edition strings
    /// (caller applies manifest fallback via EditionContext).
    /// </summary>
    public static PublicDecodedLicense Decode(string licenseKey)
    {
        var raw = LicensePayload.Decode(licenseKey);
        return new PublicDecodedLicense(
            Payload: raw.Payload,
            Signature: raw.Signature,
            Version: raw.Version,
            HardwareFingerprint: raw.HardwareFingerprint,
            CustomerName: raw.CustomerName,
            IssueDateUtc: raw.IssueDateUtc,
            EditionId: raw.EditionId,
            Tier: raw.Tier,
            Industry: raw.Industry,
            ExpiresAtUtc: raw.ExpiresAtUtc,
            Features: raw.Features);
    }
}

/// <summary>
/// License validation entry-point. The app calls
/// <see cref="ValidateLicense(string, byte[])"/> at startup; the admin /
/// support workflow uses the same code via the keygen tool to verify a
/// freshly-issued license before shipping it to the customer.
///
/// The signing private key lives ONLY on PT TTS's developer machine
/// (file: <c>license-private.key</c>, kept out of git). The matching
/// public key is embedded here as <see cref="PublicKeyBase64"/>; after
/// Obfuscar's HideStrings pass it is encrypted inside the EXE.
/// </summary>
public static class LicenseService
{
    /// <summary>
    /// Ed25519 public key, base64-encoded (32 raw bytes → 44 chars).
    ///
    /// <para>
    /// Source-of-truth moved to the brand pack
    /// (<see cref="IBrandConfig.LicensePublicKeyBase64"/>) in the v1.3.0
    /// refactor — each edition (Panel.Branding, RAB Cepat Branding,
    /// Carrosserie.Branding, etc.) ships its own signing keypair. The
    /// matching private key lives offline at the brand owner (issuer);
    /// it is NEVER committed to git.
    /// </para>
    ///
    /// <para>
    /// <strong>API surface note</strong> — this used to be a
    /// <c>const string</c>. It is now a <c>static</c> property. The only
    /// consumer in this codebase is the line below
    /// (<see cref="ValidateLicense"/>), and external tools
    /// (Tools/LicenseKeyGen) just print the value at codegen time —
    /// neither requires compile-time constness.
    /// </para>
    /// </summary>
    public static string PublicKeyBase64 => BrandContext.Current.LicensePublicKeyBase64;

    /// <summary>
    /// Validates a license key string against the given hardware fingerprint.
    /// </summary>
    /// <param name="licenseKey">The Base32-grouped license string the customer pasted.</param>
    /// <param name="hardwareFingerprint">8-byte fingerprint from <c>MachineKeyProvider.GetHardwareFingerprintBytes()</c>.</param>
    public static LicenseValidationResult ValidateLicense(string licenseKey, byte[] hardwareFingerprint)
        => ValidateLicenseWithKey(licenseKey, hardwareFingerprint, PublicKeyBase64);

    /// <summary>
    /// Test-only overload that takes an explicit public key (base64). Lets
    /// unit tests round-trip with a freshly-generated keypair without
    /// touching the embedded production constant.
    /// </summary>
    internal static LicenseValidationResult ValidateLicenseWithKey(
        string licenseKey, byte[] hardwareFingerprint, string publicKeyBase64)
    {
        if (hardwareFingerprint == null || hardwareFingerprint.Length != LicensePayload.FingerprintLength)
            return LicenseValidationResult.Malformed("Hardware fingerprint internal size mismatch.");

        LicensePayload.DecodedLicense decoded;
        try
        {
            decoded = LicensePayload.Decode(licenseKey ?? string.Empty);
        }
        catch (FormatException fx)
        {
            return LicenseValidationResult.Malformed(fx.Message);
        }
        catch (Exception ex)
        {
            return LicenseValidationResult.Malformed($"License could not be parsed: {ex.Message}");
        }

        // 1. Verify the Ed25519 signature first (cheap, ~200 µs). If it fails
        //    we don't reveal hardware mismatch (avoids a side channel for
        //    attackers crafting payloads).
        PublicKey publicKey;
        try
        {
            var pkBytes = Convert.FromBase64String(publicKeyBase64);
            publicKey = PublicKey.Import(SignatureAlgorithm.Ed25519, pkBytes, KeyBlobFormat.RawPublicKey);
        }
        catch (Exception ex)
        {
            // This is a developer error — the embedded constant is malformed.
            // We surface it as InvalidSignature so production users still see
            // a clear "license invalid" message instead of a crash.
            return LicenseValidationResult.InvalidSignature(
                $"Embedded license public key is invalid: {ex.Message}");
        }

        bool sigOk = SignatureAlgorithm.Ed25519.Verify(publicKey, decoded.Payload, decoded.Signature);
        if (!sigOk)
            return LicenseValidationResult.InvalidSignature(
                "Tanda tangan license tidak valid. Kemungkinan license palsu atau diubah.");

        // 2. Hardware binding check
        if (!ConstantTimeEquals(decoded.HardwareFingerprint, hardwareFingerprint))
            return LicenseValidationResult.WrongHardware(
                "License ini diterbitkan untuk komputer lain. Hubungi support untuk reaktivasi.");

        // (No expiry check for now — license is perpetual.)

        return LicenseValidationResult.Valid(decoded.CustomerName, decoded.IssueDateUtc);
    }

    /// <summary>
    /// Convenience overload: pull the fingerprint from the configured
    /// provider. The provider delegate keeps Core decoupled from Data —
    /// the caller (Program.cs) wires <c>MachineKeyProvider.GetHardwareFingerprintBytes</c>.
    /// </summary>
    public static LicenseValidationResult ValidateLicense(string licenseKey, Func<byte[]> fingerprintProvider)
    {
        if (fingerprintProvider == null) throw new ArgumentNullException(nameof(fingerprintProvider));
        return ValidateLicense(licenseKey, fingerprintProvider());
    }

    /// <summary>Length-bounded constant-time byte equality.</summary>
    private static bool ConstantTimeEquals(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return false;
        int diff = 0;
        for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
        return diff == 0;
    }
}

/// <summary>
/// Result of <see cref="LicenseService.ValidateLicense(string, byte[])"/>.
/// </summary>
public sealed class LicenseValidationResult
{
    public LicenseValidationStatus Status { get; }
    public string  Reason       { get; }
    public string? CustomerName { get; }
    public DateTime? IssueDate  { get; }
    public bool IsValid => Status == LicenseValidationStatus.Valid;

    private LicenseValidationResult(
        LicenseValidationStatus status,
        string reason,
        string? customerName = null,
        DateTime? issueDate = null)
    {
        Status       = status;
        Reason       = reason;
        CustomerName = customerName;
        IssueDate    = issueDate;
    }

    internal static LicenseValidationResult Valid(string customerName, DateTime issueDate)
        => new(LicenseValidationStatus.Valid, "License valid.", customerName, issueDate);

    internal static LicenseValidationResult InvalidSignature(string reason)
        => new(LicenseValidationStatus.InvalidSignature, reason);

    internal static LicenseValidationResult WrongHardware(string reason)
        => new(LicenseValidationStatus.WrongHardware, reason);

    internal static LicenseValidationResult Malformed(string reason)
        => new(LicenseValidationStatus.Malformed, reason);

    internal static LicenseValidationResult Expired(string reason)
        => new(LicenseValidationStatus.Expired, reason);
}

public enum LicenseValidationStatus
{
    Valid,
    InvalidSignature,
    WrongHardware,
    Malformed,
    Expired
}
