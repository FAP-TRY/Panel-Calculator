using System;
using System.IO;
using NSec.Cryptography;
using PanelCalculator.Core.Security;
using RabKit.Branding;

namespace PanelCalculator.Tools.LicenseKeyGen;

/// <summary>
/// Reusable license issuance logic shared between the CLI (Program.cs) and
/// the WinForms GUI (LicenseKeyGenGui project). Keeping all crypto + format
/// rules in one place means CLI output and GUI output are guaranteed
/// byte-identical for the same inputs.
///
/// <para>
/// Two issuance modes coexist:
/// <list type="bullet">
///   <item><see cref="Issue(string, string, string)"/> — V1 (legacy) payload,
///         byte-identical with the pre-v1.3.0 keygen. Used for existing PT
///         TTS workflow that doesn't need edition claims.</item>
///   <item><see cref="Issue(string, string, EditionTierSpec, string)"/> —
///         V2 payload with editionId/tier/industry/expiry/features claims.
///         Used for multi-edition (Custom + Generic + Lifetime) launches.</item>
/// </list>
/// </para>
/// </summary>
public static class LicenseIssuer
{
    public const string DefaultKeyFileName = "license-private.key";

    /// <summary>
    /// Default place we suggest saving the private key. Hidden file under
    /// the issuer's user profile, outside any repo, survives Windows
    /// updates. PT TTS should also keep an offline backup.
    /// </summary>
    public static string DefaultKeyPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".panel-calculator-secrets",
            DefaultKeyFileName);

    /// <summary>
    /// Edition-specific claims bundled into a V2 license. Used as the
    /// argument bag for <see cref="Issue(string, string, EditionTierSpec, string)"/>.
    /// </summary>
    public sealed record EditionTierSpec(
        string EditionId,
        string Tier,
        string Industry,
        DateTime? ExpiresAtUtc,
        EditionFeatures Features);

    public sealed record IssueResult(
        string LicenseKey,
        string CustomerName,
        string HardwareFingerprintDisplay,
        DateTime IssueDateUtc,
        int LicenseLength,
        int FormatVersion);

    public sealed record VerifyResult(
        bool SignatureOk,
        bool FingerprintMatches,
        string CustomerName,
        string HardwareFingerprintDisplay,
        DateTime IssueDateUtc,
        int FormatVersion,
        string EditionId,
        string Tier,
        string Industry,
        DateTime? ExpiresAtUtc);

    public sealed record KeyPairResult(
        string PrivateKeyPath,
        string PublicKeyBase64);

    // ─────────────────────────────────────────────────────────────────────
    //  Issue a V1 license (LEGACY — keep for PT TTS existing workflow)
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// V1 legacy issuance. Emits a license payload byte-identical with the
    /// pre-v1.3.0 keygen. PT TTS admin keeps using this for Custom-edition
    /// installs that inherit edition claims from the bundled manifest.
    /// </summary>
    public static IssueResult Issue(string fingerprintRaw, string customerName, string privateKeyPath)
    {
        ValidateIssueInputs(fingerprintRaw, customerName, privateKeyPath);

        byte[] fingerprint   = ParseFingerprintLoose(fingerprintRaw);
        byte[] privateBytes  = File.ReadAllBytes(privateKeyPath);

        var algo = SignatureAlgorithm.Ed25519;
        using var key = Key.Import(algo, privateBytes, KeyBlobFormat.RawPrivateKey,
            new KeyCreationParameters { ExportPolicy = KeyExportPolicies.AllowPlaintextExport });

        var issueDate  = DateTime.UtcNow;
        var payload    = LicensePayload.BuildSignablePayload(fingerprint, customerName.Trim(), issueDate);
        var signature  = algo.Sign(key, payload);
        var licenseStr = LicensePayload.Encode(payload, signature);

        return new IssueResult(
            LicenseKey: licenseStr,
            CustomerName: customerName.Trim(),
            HardwareFingerprintDisplay: BytesToFingerprintDisplay(fingerprint),
            IssueDateUtc: issueDate,
            LicenseLength: licenseStr.Length,
            FormatVersion: LicensePayload.FormatVersionV1);
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Issue a V2 license (NEW — edition/tier/industry/expiry/features)
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// V2 issuance — emits a license with edition claims baked into the
    /// signed payload. EditionContext on the customer machine cross-checks
    /// these claims against the installed manifest before granting access.
    /// </summary>
    public static IssueResult Issue(
        string fingerprintRaw,
        string customerName,
        EditionTierSpec spec,
        string privateKeyPath)
    {
        ValidateIssueInputs(fingerprintRaw, customerName, privateKeyPath);
        if (spec == null) throw new ArgumentNullException(nameof(spec));
        if (string.IsNullOrWhiteSpace(spec.EditionId))
            throw new ArgumentException("EditionTierSpec.EditionId is required.", nameof(spec));
        if (string.IsNullOrWhiteSpace(spec.Tier))
            throw new ArgumentException("EditionTierSpec.Tier is required.", nameof(spec));
        if (string.IsNullOrWhiteSpace(spec.Industry))
            throw new ArgumentException("EditionTierSpec.Industry is required.", nameof(spec));

        byte[] fingerprint   = ParseFingerprintLoose(fingerprintRaw);
        byte[] privateBytes  = File.ReadAllBytes(privateKeyPath);

        var algo = SignatureAlgorithm.Ed25519;
        using var key = Key.Import(algo, privateBytes, KeyBlobFormat.RawPrivateKey,
            new KeyCreationParameters { ExportPolicy = KeyExportPolicies.AllowPlaintextExport });

        var issueDate = DateTime.UtcNow;
        var payload   = LicensePayload.BuildSignablePayload(
            fingerprint, customerName.Trim(), issueDate,
            spec.EditionId, spec.Tier, spec.Industry,
            spec.ExpiresAtUtc, spec.Features ?? new EditionFeatures());
        var signature  = algo.Sign(key, payload);
        var licenseStr = LicensePayload.Encode(payload, signature);

        return new IssueResult(
            LicenseKey: licenseStr,
            CustomerName: customerName.Trim(),
            HardwareFingerprintDisplay: BytesToFingerprintDisplay(fingerprint),
            IssueDateUtc: issueDate,
            LicenseLength: licenseStr.Length,
            FormatVersion: LicensePayload.FormatVersionV2);
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Verify a license (sanity check before sending to customer)
    // ─────────────────────────────────────────────────────────────────────
    public static VerifyResult Verify(string fingerprintRaw, string licenseKey, string privateKeyPath)
    {
        byte[] fingerprint  = ParseFingerprintLoose(fingerprintRaw);
        byte[] privateBytes = File.ReadAllBytes(privateKeyPath);
        using var key = Key.Import(SignatureAlgorithm.Ed25519, privateBytes, KeyBlobFormat.RawPrivateKey,
            new KeyCreationParameters { ExportPolicy = KeyExportPolicies.AllowPlaintextExport });
        var publicBytes = key.PublicKey.Export(KeyBlobFormat.RawPublicKey);

        var decoded   = LicensePayload.Decode(licenseKey);
        var publicKey = PublicKey.Import(SignatureAlgorithm.Ed25519, publicBytes, KeyBlobFormat.RawPublicKey);
        bool sigOk    = SignatureAlgorithm.Ed25519.Verify(publicKey, decoded.Payload, decoded.Signature);
        bool fpMatch  = ConstantTimeEquals(decoded.HardwareFingerprint, fingerprint);

        return new VerifyResult(
            SignatureOk: sigOk,
            FingerprintMatches: fpMatch,
            CustomerName: decoded.CustomerName,
            HardwareFingerprintDisplay: BytesToFingerprintDisplay(decoded.HardwareFingerprint),
            IssueDateUtc: decoded.IssueDateUtc,
            FormatVersion: decoded.Version,
            EditionId: decoded.EditionId,
            Tier: decoded.Tier,
            Industry: decoded.Industry,
            ExpiresAtUtc: decoded.ExpiresAtUtc);
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Sign an EditionManifest record into the [length][json][sig] bundle
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Sign a manifest record using the issuer's private key and emit the
    /// canonical bundle layout (4-byte BE length || JSON || 64-byte sig)
    /// that <see cref="SignedManifestLoader.LoadAndVerifyFromBundle"/>
    /// accepts. Used by the brand pack build pipeline to produce
    /// <c>Panel.Branding/Assets/edition.manifest.bundle</c>.
    /// </summary>
    public static byte[] SignManifestBundle(EditionManifest manifest, string privateKeyPath)
    {
        if (manifest == null) throw new ArgumentNullException(nameof(manifest));
        if (string.IsNullOrWhiteSpace(privateKeyPath))
            throw new ArgumentException("privateKeyPath is empty", nameof(privateKeyPath));
        if (!File.Exists(privateKeyPath))
            throw new FileNotFoundException(
                $"Private key file not found:\n{privateKeyPath}\n\n" +
                "Generate sekali dulu via 'generate-keypair', atau cek path.",
                privateKeyPath);

        var jsonBytes = SignedManifestLoader.SerializeManifestForSigning(manifest);

        var privateBytes = File.ReadAllBytes(privateKeyPath);
        using var key = Key.Import(SignatureAlgorithm.Ed25519, privateBytes,
            KeyBlobFormat.RawPrivateKey,
            new KeyCreationParameters { ExportPolicy = KeyExportPolicies.AllowPlaintextExport });

        var sig = SignatureAlgorithm.Ed25519.Sign(key, jsonBytes);
        return SignedManifestLoader.PackBundle(jsonBytes, sig);
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Generate a fresh Ed25519 keypair
    // ─────────────────────────────────────────────────────────────────────
    public static KeyPairResult GenerateKeyPair(string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        var keyPath = Path.Combine(outputDirectory, DefaultKeyFileName);
        if (File.Exists(keyPath))
            throw new IOException(
                $"File private key SUDAH ADA di:\n{keyPath}\n\n" +
                "Pindahkan / rename dulu kalau benar-benar mau bikin keypair baru.\n" +
                "JANGAN overwrite kecuali Anda siap re-release semua app customer.");

        var algo = SignatureAlgorithm.Ed25519;
        using var key = Key.Create(algo, new KeyCreationParameters
        {
            ExportPolicy = KeyExportPolicies.AllowPlaintextExport
        });

        var privateBytes = key.Export(KeyBlobFormat.RawPrivateKey);
        var publicBytes  = key.PublicKey.Export(KeyBlobFormat.RawPublicKey);

        File.WriteAllBytes(keyPath, privateBytes);
        try { File.SetAttributes(keyPath, File.GetAttributes(keyPath) | FileAttributes.Hidden); } catch { }

        return new KeyPairResult(
            PrivateKeyPath: keyPath,
            PublicKeyBase64: Convert.ToBase64String(publicBytes));
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Fingerprint formatting helpers
    // ─────────────────────────────────────────────────────────────────────
    public static byte[] ParseFingerprintLoose(string raw)
    {
        var hex = raw.Replace("-", "").Replace(" ", "").Trim().ToUpperInvariant();
        if (hex.Length != 16)
            throw new FormatException(
                $"Hardware fingerprint harus 16 huruf hex (contoh: A3F7-9C2B-1E4D-8F60).\n" +
                $"Yang diterima: '{raw}' ({hex.Length} huruf setelah dibersihkan).");
        return Convert.FromHexString(hex);
    }

    public static string BytesToFingerprintDisplay(byte[] bytes)
    {
        var hex = Convert.ToHexString(bytes).ToUpperInvariant();
        return $"{hex.Substring(0, 4)}-{hex.Substring(4, 4)}-{hex.Substring(8, 4)}-{hex.Substring(12, 4)}";
    }

    // ── Private ──────────────────────────────────────────────────────────

    private static void ValidateIssueInputs(string fingerprintRaw, string customerName, string privateKeyPath)
    {
        if (string.IsNullOrWhiteSpace(fingerprintRaw))
            throw new ArgumentException("Hardware fingerprint kosong.", nameof(fingerprintRaw));
        if (string.IsNullOrWhiteSpace(customerName))
            throw new ArgumentException("Nama customer kosong.", nameof(customerName));
        if (string.IsNullOrWhiteSpace(privateKeyPath))
            throw new ArgumentException("Path private key kosong.", nameof(privateKeyPath));
        if (!File.Exists(privateKeyPath))
            throw new FileNotFoundException(
                $"File private key tidak ditemukan:\n{privateKeyPath}\n\n" +
                $"Generate sekali dulu via 'Generate Keypair', atau cek path.",
                privateKeyPath);
    }

    private static bool ConstantTimeEquals(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return false;
        int diff = 0;
        for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
        return diff == 0;
    }
}
