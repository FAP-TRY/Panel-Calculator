using System;
using System.IO;
using NSec.Cryptography;
using PanelCalculator.Core.Security;

namespace PanelCalculator.Tools.LicenseKeyGen;

/// <summary>
/// PT TTS internal CLI — DO NOT distribute to customers.
///
/// Usage:
///   dotnet run --project Tools/LicenseKeyGen -- generate-keypair [outputDir]
///   dotnet run --project Tools/LicenseKeyGen -- issue --fp "A3F7-9C2B-1E4D-8F60" --name "PT Customer XYZ"
///   dotnet run --project Tools/LicenseKeyGen -- issue --fp "A3F7-9C2B-1E4D-8F60" --name "PT Customer XYZ" --key "C:\path\to\license-private.key"
/// </summary>
internal static class Program
{
    private const string DefaultKeyFileName = "license-private.key";

    static int Main(string[] args)
    {
        try
        {
            if (args.Length == 0) { PrintUsage(); return 1; }

            return args[0].ToLowerInvariant() switch
            {
                "generate-keypair" => GenerateKeyPair(args),
                "issue"            => IssueLicense(args),
                "verify"           => VerifyLicense(args),
                "fingerprint-info" => FingerprintInfo(args),
                "help" or "-h" or "--help" => PrintUsageOk(),
                _ => PrintUsageErr($"Unknown command: {args[0]}")
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"ERROR: {ex.Message}");
            return 2;
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    //  generate-keypair
    // ─────────────────────────────────────────────────────────────────────
    private static int GenerateKeyPair(string[] args)
    {
        // Optional output directory (defaults to current working directory).
        var outDir = args.Length >= 2 ? args[1] : Environment.CurrentDirectory;
        Directory.CreateDirectory(outDir);

        var algo = SignatureAlgorithm.Ed25519;
        var creationParams = new KeyCreationParameters
        {
            ExportPolicy = KeyExportPolicies.AllowPlaintextExport
        };
        using var key = Key.Create(algo, creationParams);

        var privateBytes = key.Export(KeyBlobFormat.RawPrivateKey);
        var publicBytes  = key.PublicKey.Export(KeyBlobFormat.RawPublicKey);

        var keyPath = Path.Combine(outDir, DefaultKeyFileName);
        if (File.Exists(keyPath))
        {
            Console.Error.WriteLine($"REFUSING TO OVERWRITE existing key file: {keyPath}");
            Console.Error.WriteLine("Move/rename it first if you really want a new keypair.");
            return 3;
        }
        File.WriteAllBytes(keyPath, privateBytes);

        // Hard-lock permissions on Windows (best-effort).
        try { File.SetAttributes(keyPath, File.GetAttributes(keyPath) | FileAttributes.Hidden); } catch { }

        var publicB64 = Convert.ToBase64String(publicBytes);

        Console.WriteLine("========================================================");
        Console.WriteLine("  Ed25519 keypair generated successfully");
        Console.WriteLine("========================================================");
        Console.WriteLine();
        Console.WriteLine($"  Private key file : {keyPath}");
        Console.WriteLine($"  Public key (b64) : {publicB64}");
        Console.WriteLine();
        Console.WriteLine("NEXT STEPS:");
        Console.WriteLine("  1. BACKUP the private key file to a safe offline location");
        Console.WriteLine("     (e.g. encrypted USB stored in a locked drawer).");
        Console.WriteLine("     If you lose it, you can no longer issue licenses and");
        Console.WriteLine("     all installed copies of the app must be re-released.");
        Console.WriteLine("  2. NEVER commit the private key file to git. It must not");
        Console.WriteLine("     leave the issuer machine in any form.");
        Console.WriteLine("  3. Open file:");
        Console.WriteLine("       PanelCalculator.Core/Security/LicenseService.cs");
        Console.WriteLine("     and replace the value of the PublicKeyBase64 constant");
        Console.WriteLine("     with the base64 string printed above.");
        Console.WriteLine("  4. Rebuild & re-release the app so customer machines pick");
        Console.WriteLine("     up the new public key.");
        Console.WriteLine();
        return 0;
    }

    // ─────────────────────────────────────────────────────────────────────
    //  issue
    // ─────────────────────────────────────────────────────────────────────
    private static int IssueLicense(string[] args)
    {
        var (fp, name, keyPath) = ParseIssueArgs(args);

        if (!File.Exists(keyPath))
            throw new FileNotFoundException(
                $"Private key not found: {keyPath}\n" +
                $"Run `generate-keypair` first, or pass --key <path>.");

        // Hardware fingerprint parsing — same routine the app uses to display.
        // Accept either dashed ("A3F7-9C2B-1E4D-8F60") or raw 16 hex chars.
        byte[] fingerprint = ParseFingerprintLoose(fp);

        var privateBytes = File.ReadAllBytes(keyPath);
        var algo = SignatureAlgorithm.Ed25519;
        using var key = Key.Import(algo, privateBytes, KeyBlobFormat.RawPrivateKey,
            new KeyCreationParameters { ExportPolicy = KeyExportPolicies.AllowPlaintextExport });

        var issueDate = DateTime.UtcNow;
        var payload   = LicensePayload.BuildSignablePayload(fingerprint, name, issueDate);
        var signature = algo.Sign(key, payload);
        var licenseStr = LicensePayload.Encode(payload, signature);

        Console.WriteLine("========================================================");
        Console.WriteLine("  License issued successfully");
        Console.WriteLine("========================================================");
        Console.WriteLine();
        Console.WriteLine($"  Customer name        : {name}");
        Console.WriteLine($"  Hardware fingerprint : {BytesToFingerprintDisplay(fingerprint)}");
        Console.WriteLine($"  Issue date (UTC)     : {issueDate:yyyy-MM-dd HH:mm:ss}");
        Console.WriteLine();
        Console.WriteLine("LICENSE KEY (send this string to the customer via WhatsApp):");
        Console.WriteLine();
        Console.WriteLine(licenseStr);
        Console.WriteLine();
        Console.WriteLine("  Total length: " + licenseStr.Length + " chars");
        Console.WriteLine();
        return 0;
    }

    // ─────────────────────────────────────────────────────────────────────
    //  verify — sanity-check a freshly issued license before sending it.
    //   Uses the SAME validator the running app uses, but with the public
    //   key derived from the private key file (so we don't need the embedded
    //   constant to be set yet).
    // ─────────────────────────────────────────────────────────────────────
    private static int VerifyLicense(string[] args)
    {
        var (fp, _, keyPath) = ParseIssueArgs(args, requireName: false);
        string? license = GetArg(args, "--license") ?? throw new ArgumentException("--license <key> required");

        byte[] fingerprint = ParseFingerprintLoose(fp);
        var privateBytes = File.ReadAllBytes(keyPath);
        using var key = Key.Import(SignatureAlgorithm.Ed25519, privateBytes, KeyBlobFormat.RawPrivateKey,
            new KeyCreationParameters { ExportPolicy = KeyExportPolicies.AllowPlaintextExport });
        var publicBytes = key.PublicKey.Export(KeyBlobFormat.RawPublicKey);

        var decoded = LicensePayload.Decode(license);
        var publicKey = PublicKey.Import(SignatureAlgorithm.Ed25519, publicBytes, KeyBlobFormat.RawPublicKey);
        bool sigOk = SignatureAlgorithm.Ed25519.Verify(publicKey, decoded.Payload, decoded.Signature);

        Console.WriteLine($"  Signature OK         : {sigOk}");
        Console.WriteLine($"  Customer name        : {decoded.CustomerName}");
        Console.WriteLine($"  Hardware fingerprint : {BytesToFingerprintDisplay(decoded.HardwareFingerprint)}");
        Console.WriteLine($"  Issue date (UTC)     : {decoded.IssueDateUtc:yyyy-MM-dd HH:mm:ss}");
        bool fpMatch = ConstantTimeEquals(decoded.HardwareFingerprint, fingerprint);
        Console.WriteLine($"  Fingerprint matches  : {fpMatch}");

        return (sigOk && fpMatch) ? 0 : 4;
    }

    // ─────────────────────────────────────────────────────────────────────
    //  fingerprint-info — small helper to show what a customer's
    //  fingerprint string parses to (sanity check before issuing).
    // ─────────────────────────────────────────────────────────────────────
    private static int FingerprintInfo(string[] args)
    {
        string? fp = GetArg(args, "--fp") ?? throw new ArgumentException("--fp <fingerprint> required");
        var bytes = ParseFingerprintLoose(fp);
        Console.WriteLine($"Parsed bytes (hex)   : {Convert.ToHexString(bytes)}");
        Console.WriteLine($"Display form         : {BytesToFingerprintDisplay(bytes)}");
        Console.WriteLine($"Length               : {bytes.Length} bytes");
        return 0;
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Argument parsing helpers
    // ─────────────────────────────────────────────────────────────────────
    private static (string fp, string name, string keyPath) ParseIssueArgs(
        string[] args, bool requireName = true)
    {
        string? fp   = GetArg(args, "--fp")   ?? throw new ArgumentException("--fp <fingerprint> required");
        string? name = GetArg(args, "--name");
        if (requireName && string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("--name <customer name> required");

        string keyPath = GetArg(args, "--key") ?? Path.Combine(Environment.CurrentDirectory, DefaultKeyFileName);
        return (fp, name ?? "", keyPath);
    }

    private static string? GetArg(string[] args, string flag)
    {
        for (int i = 0; i < args.Length - 1; i++)
            if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        return null;
    }

    private static byte[] ParseFingerprintLoose(string raw)
    {
        var hex = raw.Replace("-", "").Replace(" ", "").Trim().ToUpperInvariant();
        if (hex.Length != 16)
            throw new FormatException(
                $"Fingerprint must be 16 hex characters (e.g. A3F7-9C2B-1E4D-8F60). Got {hex.Length} chars.");
        return Convert.FromHexString(hex);
    }

    private static string BytesToFingerprintDisplay(byte[] bytes)
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

    private static int PrintUsage()    { PrintUsageBody(Console.Out); return 1; }
    private static int PrintUsageOk()  { PrintUsageBody(Console.Out); return 0; }
    private static int PrintUsageErr(string msg)
    {
        Console.Error.WriteLine(msg);
        PrintUsageBody(Console.Error);
        return 1;
    }

    private static void PrintUsageBody(TextWriter w)
    {
        w.WriteLine("LicenseKeyGen — Panel Calculator activation tool (PT TTS internal)");
        w.WriteLine();
        w.WriteLine("Commands:");
        w.WriteLine("  generate-keypair [outputDir]");
        w.WriteLine("      One-time setup. Writes license-private.key + prints public key.");
        w.WriteLine();
        w.WriteLine("  issue --fp <hardware-fingerprint> --name <customer name> [--key <path>]");
        w.WriteLine("      Generate a license string for one customer machine.");
        w.WriteLine("      Example:");
        w.WriteLine("        issue --fp \"A3F7-9C2B-1E4D-8F60\" --name \"PT Customer XYZ\"");
        w.WriteLine();
        w.WriteLine("  verify --fp <fp> --license <key> [--key <path>]");
        w.WriteLine("      Sanity-check a generated license before sending to customer.");
        w.WriteLine();
        w.WriteLine("  fingerprint-info --fp <fingerprint>");
        w.WriteLine("      Parse + echo a fingerprint string (debug helper).");
        w.WriteLine();
    }
}
