using System;
using System.IO;
using NSec.Cryptography;
using PanelCalculator.Core.Security;

namespace PanelCalculator.Tools.LicenseKeyGen;

/// <summary>
/// Reusable license issuance logic shared between the CLI (Program.cs) and
/// the WinForms GUI (LicenseKeyGenGui project). Keeping all crypto + format
/// rules in one place means CLI output and GUI output are guaranteed
/// byte-identical for the same inputs.
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

    public sealed record IssueResult(
        string LicenseKey,
        string CustomerName,
        string HardwareFingerprintDisplay,
        DateTime IssueDateUtc,
        int LicenseLength);

    public sealed record VerifyResult(
        bool SignatureOk,
        bool FingerprintMatches,
        string CustomerName,
        string HardwareFingerprintDisplay,
        DateTime IssueDateUtc);

    public sealed record KeyPairResult(
        string PrivateKeyPath,
        string PublicKeyBase64);

    // ─────────────────────────────────────────────────────────────────────
    //  Issue a license for one customer machine
    // ─────────────────────────────────────────────────────────────────────
    public static IssueResult Issue(string fingerprintRaw, string customerName, string privateKeyPath)
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
            LicenseLength: licenseStr.Length);
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
            IssueDateUtc: decoded.IssueDateUtc);
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

    private static bool ConstantTimeEquals(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return false;
        int diff = 0;
        for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
        return diff == 0;
    }
}
