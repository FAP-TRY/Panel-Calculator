using System;
using NSec.Cryptography;

namespace PanelCalculator.Core.Security;

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
    /// === DEVELOPER NOTE ===
    /// This is a PLACEHOLDER public key generated for development.
    /// Before the v1.2.4 public release, PT TTS MUST:
    ///   1) Run: dotnet run --project Tools/LicenseKeyGen -- generate-keypair
    ///   2) Save the printed private key file securely (NOT in git!)
    ///   3) Paste the printed public key value below, replacing this constant.
    ///   4) Re-build the release EXE so the new key is embedded + obfuscated.
    /// Failing to do this means anyone with the source can forge licenses.
    /// =====================
    /// </summary>
    // Production public key (Ed25519, raw, base64) — generated 2026-05-18.
    // Matching private key lives offline at PT TTS (issuer) — NEVER commit it.
    public const string PublicKeyBase64 = "D5Bk2OC+FFZdZqqtI86iFCiy1/pFRQLbkMBpVQ+ia6w=";

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
