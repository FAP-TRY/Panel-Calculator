using System;
using System.IO;
using System.Text.Json;
using RabKit.Branding;

namespace PanelCalculator.Tools.LicenseKeyGen;

/// <summary>
/// PT TTS internal CLI — DO NOT distribute to customers.
///
/// Thin wrapper over <see cref="LicenseIssuer"/>. For a friendlier GUI
/// version, run the LicenseKeyGenGui project instead.
///
/// Usage:
///   dotnet run --project Tools/LicenseKeyGen -- generate-keypair [outputDir]
///   dotnet run --project Tools/LicenseKeyGen -- issue --fp "A3F7-9C2B-1E4D-8F60" --name "PT Customer XYZ"
///   dotnet run --project Tools/LicenseKeyGen -- issue --fp "..." --name "..." --key "C:\path\to\license-private.key"
///   dotnet run --project Tools/LicenseKeyGen -- issue --fp "..." --name "..." \
///       --edition "custom-tts-panel-v1" --tier "custom" --industry "panel-electrical" \
///       [--expires "2027-06-01"] [--features "watermark,branding,marketplace,online"]
///   dotnet run --project Tools/LicenseKeyGen -- verify --fp "..." --license "..." [--key "..."]
///   dotnet run --project Tools/LicenseKeyGen -- fingerprint-info --fp "..."
///   dotnet run --project Tools/LicenseKeyGen -- sign-manifest --input X.json --output Y.bundle --key path\to\license-private.key
/// </summary>
internal static class Program
{
    static int Main(string[] args)
    {
        try
        {
            if (args.Length == 0) { PrintUsage(); return 1; }

            return args[0].ToLowerInvariant() switch
            {
                "generate-keypair" => CmdGenerateKeyPair(args),
                "issue"            => CmdIssueLicense(args),
                "verify"           => CmdVerifyLicense(args),
                "fingerprint-info" => CmdFingerprintInfo(args),
                "sign-manifest"    => CmdSignManifest(args),
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

    private static int CmdGenerateKeyPair(string[] args)
    {
        var outDir = args.Length >= 2 ? args[1] : Environment.CurrentDirectory;
        var result = LicenseIssuer.GenerateKeyPair(outDir);

        Console.WriteLine("========================================================");
        Console.WriteLine("  Ed25519 keypair generated successfully");
        Console.WriteLine("========================================================");
        Console.WriteLine();
        Console.WriteLine($"  Private key file : {result.PrivateKeyPath}");
        Console.WriteLine($"  Public key (b64) : {result.PublicKeyBase64}");
        Console.WriteLine();
        Console.WriteLine("NEXT STEPS:");
        Console.WriteLine("  1. BACKUP the private key file to a safe offline location.");
        Console.WriteLine("  2. NEVER commit the private key to git.");
        Console.WriteLine("  3. Paste the public key above into");
        Console.WriteLine("     Panel.Branding/PanelBrandConfig.cs (LicensePublicKeyBase64)");
        Console.WriteLine("     and rebuild the app.");
        Console.WriteLine();
        return 0;
    }

    private static int CmdIssueLicense(string[] args)
    {
        string fp     = GetArg(args, "--fp")   ?? throw new ArgumentException("--fp <fingerprint> required");
        string name   = GetArg(args, "--name") ?? throw new ArgumentException("--name <customer name> required");
        string keyPath = GetArg(args, "--key") ?? Path.Combine(Environment.CurrentDirectory, LicenseIssuer.DefaultKeyFileName);

        string? edition  = GetArg(args, "--edition");
        string? tier     = GetArg(args, "--tier");
        string? industry = GetArg(args, "--industry");
        string? expires  = GetArg(args, "--expires");
        string? features = GetArg(args, "--features");

        // Decide V1 vs V2 by whether ANY edition claim was provided.
        bool wantV2 = !string.IsNullOrEmpty(edition) || !string.IsNullOrEmpty(tier)
                   || !string.IsNullOrEmpty(industry) || !string.IsNullOrEmpty(expires)
                   || !string.IsNullOrEmpty(features);

        LicenseIssuer.IssueResult result;
        if (!wantV2)
        {
            result = LicenseIssuer.Issue(fp, name, keyPath);
        }
        else
        {
            if (string.IsNullOrWhiteSpace(edition))
                throw new ArgumentException("V2 issuance requires --edition <editionId>.");
            if (string.IsNullOrWhiteSpace(tier))
                throw new ArgumentException("V2 issuance requires --tier <custom|generic-basic|generic-pro|...>.");
            if (string.IsNullOrWhiteSpace(industry))
                throw new ArgumentException("V2 issuance requires --industry <panel-electrical|carrosserie|...>.");

            DateTime? expiresAt = null;
            if (!string.IsNullOrWhiteSpace(expires))
            {
                if (!DateTime.TryParse(expires, System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
                        out var parsed))
                    throw new ArgumentException($"--expires '{expires}' not a valid ISO date.");
                expiresAt = parsed;
            }

            var fx = ParseFeatures(features);
            var spec = new LicenseIssuer.EditionTierSpec(
                EditionId: edition, Tier: tier, Industry: industry,
                ExpiresAtUtc: expiresAt, Features: fx);

            result = LicenseIssuer.Issue(fp, name, spec, keyPath);
        }

        Console.WriteLine("========================================================");
        Console.WriteLine("  License issued successfully");
        Console.WriteLine("========================================================");
        Console.WriteLine();
        Console.WriteLine($"  Format version       : V{result.FormatVersion}");
        Console.WriteLine($"  Customer name        : {result.CustomerName}");
        Console.WriteLine($"  Hardware fingerprint : {result.HardwareFingerprintDisplay}");
        Console.WriteLine($"  Issue date (UTC)     : {result.IssueDateUtc:yyyy-MM-dd HH:mm:ss}");
        if (wantV2)
        {
            Console.WriteLine($"  Edition              : {edition}");
            Console.WriteLine($"  Tier                 : {tier}");
            Console.WriteLine($"  Industry             : {industry}");
            Console.WriteLine($"  Expires              : {expires ?? "never"}");
            Console.WriteLine($"  Features             : {features ?? "(none)"}");
        }
        Console.WriteLine();
        Console.WriteLine("LICENSE KEY (send via WhatsApp):");
        Console.WriteLine();
        Console.WriteLine(result.LicenseKey);
        Console.WriteLine();
        Console.WriteLine($"  Total length: {result.LicenseLength} chars");
        Console.WriteLine();
        return 0;
    }

    private static int CmdVerifyLicense(string[] args)
    {
        string fp      = GetArg(args, "--fp")      ?? throw new ArgumentException("--fp <fingerprint> required");
        string license = GetArg(args, "--license") ?? throw new ArgumentException("--license <key> required");
        string keyPath = GetArg(args, "--key") ?? Path.Combine(Environment.CurrentDirectory, LicenseIssuer.DefaultKeyFileName);

        var r = LicenseIssuer.Verify(fp, license, keyPath);
        Console.WriteLine($"  Format version       : V{r.FormatVersion}");
        Console.WriteLine($"  Signature OK         : {r.SignatureOk}");
        Console.WriteLine($"  Customer name        : {r.CustomerName}");
        Console.WriteLine($"  Hardware fingerprint : {r.HardwareFingerprintDisplay}");
        Console.WriteLine($"  Issue date (UTC)     : {r.IssueDateUtc:yyyy-MM-dd HH:mm:ss}");
        Console.WriteLine($"  Fingerprint matches  : {r.FingerprintMatches}");
        if (r.FormatVersion >= 2)
        {
            Console.WriteLine($"  Edition              : {r.EditionId}");
            Console.WriteLine($"  Tier                 : {r.Tier}");
            Console.WriteLine($"  Industry             : {r.Industry}");
            Console.WriteLine($"  Expires (UTC)        : {(r.ExpiresAtUtc?.ToString("yyyy-MM-dd HH:mm:ss") ?? "never")}");
        }
        return (r.SignatureOk && r.FingerprintMatches) ? 0 : 4;
    }

    private static int CmdFingerprintInfo(string[] args)
    {
        string fp = GetArg(args, "--fp") ?? throw new ArgumentException("--fp <fingerprint> required");
        var bytes = LicenseIssuer.ParseFingerprintLoose(fp);
        Console.WriteLine($"Parsed bytes (hex)   : {Convert.ToHexString(bytes)}");
        Console.WriteLine($"Display form         : {LicenseIssuer.BytesToFingerprintDisplay(bytes)}");
        Console.WriteLine($"Length               : {bytes.Length} bytes");
        return 0;
    }

    private static int CmdSignManifest(string[] args)
    {
        string input  = GetArg(args, "--input")  ?? throw new ArgumentException("--input <manifest.json> required");
        string output = GetArg(args, "--output") ?? throw new ArgumentException("--output <manifest.bundle> required");
        string keyPath = GetArg(args, "--key")   ?? LicenseIssuer.DefaultKeyPath;

        if (!File.Exists(input))
            throw new FileNotFoundException($"Manifest JSON not found: {input}", input);
        if (!File.Exists(keyPath))
            throw new FileNotFoundException($"Private key not found: {keyPath}", keyPath);

        var jsonText = File.ReadAllText(input);
        var manifest = JsonSerializer.Deserialize<EditionManifest>(jsonText, SignedManifestLoader.JsonOptions)
            ?? throw new InvalidOperationException("Manifest JSON deserialised to null.");

        // Validate before signing — fail loud at signing time, not at customer runtime.
        if (string.IsNullOrWhiteSpace(manifest.EditionId))
            throw new InvalidOperationException("Manifest editionId is empty.");
        if (string.IsNullOrWhiteSpace(manifest.EditionTier))
            throw new InvalidOperationException("Manifest editionTier is empty.");
        if (string.IsNullOrWhiteSpace(manifest.Industry))
            throw new InvalidOperationException("Manifest industry is empty.");
        if (manifest.Features == null)
            throw new InvalidOperationException("Manifest features section is missing.");

        var bundle = LicenseIssuer.SignManifestBundle(manifest, keyPath);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllBytes(output, bundle);

        Console.WriteLine("========================================================");
        Console.WriteLine("  Manifest signed successfully");
        Console.WriteLine("========================================================");
        Console.WriteLine();
        Console.WriteLine($"  Input JSON     : {input}");
        Console.WriteLine($"  Output bundle  : {output}");
        Console.WriteLine($"  Bundle size    : {bundle.Length} bytes");
        Console.WriteLine($"  Edition        : {manifest.EditionId}");
        Console.WriteLine($"  Tier           : {manifest.EditionTier}");
        Console.WriteLine($"  Industry       : {manifest.Industry}");
        Console.WriteLine();
        Console.WriteLine("Embed the bundle as <EmbeddedResource> in your brand pack csproj.");
        Console.WriteLine();
        return 0;
    }

    private static EditionFeatures ParseFeatures(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv)) return new EditionFeatures();
        var tokens = csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        bool watermark   = false;
        bool branding    = false;
        bool marketplace = false;
        bool online      = false;

        foreach (var t in tokens)
        {
            switch (t.ToLowerInvariant())
            {
                case "watermark":            watermark   = true; break;
                case "branding":             branding    = true; break;
                case "marketplace":          marketplace = true; break;
                case "online":               online      = true; break;
                default:
                    throw new ArgumentException(
                        $"Unknown feature token '{t}'. " +
                        "Valid: watermark, branding, marketplace, online.");
            }
        }

        return new EditionFeatures(
            WatermarkOutput: watermark,
            AllowBrandingOverride: branding,
            AllowMarketplacePacks: marketplace,
            MaxEstimationsPerMonth: 0,
            MaxConcurrentSeats: 1,
            RequiresOnlineActivation: online);
    }

    private static string? GetArg(string[] args, string flag)
    {
        for (int i = 0; i < args.Length - 1; i++)
            if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        return null;
    }

    private static int PrintUsage()   { PrintUsageBody(Console.Out); return 1; }
    private static int PrintUsageOk() { PrintUsageBody(Console.Out); return 0; }
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
        w.WriteLine("TIP: untuk UI grafis, jalankan project LicenseKeyGenGui.");
        w.WriteLine();
        w.WriteLine("Commands:");
        w.WriteLine("  generate-keypair [outputDir]");
        w.WriteLine();
        w.WriteLine("  issue --fp <hardware-fingerprint> --name <customer name> [--key <path>]");
        w.WriteLine("      Emits a V1 license (legacy — for Custom edition with bundled manifest)");
        w.WriteLine();
        w.WriteLine("  issue --fp <fp> --name <name> --edition <id> --tier <t> --industry <slug>");
        w.WriteLine("        [--expires <ISO date>] [--features watermark,branding,marketplace,online]");
        w.WriteLine("      Emits a V2 license with edition claims baked into the payload");
        w.WriteLine();
        w.WriteLine("  verify --fp <fp> --license <key> [--key <path>]");
        w.WriteLine();
        w.WriteLine("  fingerprint-info --fp <fingerprint>");
        w.WriteLine();
        w.WriteLine("  sign-manifest --input <manifest.json> --output <manifest.bundle> [--key <path>]");
        w.WriteLine("      Produces a signed bundle suitable for embedding in a brand pack DLL");
        w.WriteLine();
    }
}
