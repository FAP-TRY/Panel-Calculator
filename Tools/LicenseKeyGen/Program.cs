using System;
using System.IO;

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
///   dotnet run --project Tools/LicenseKeyGen -- verify --fp "..." --license "..." [--key "..."]
///   dotnet run --project Tools/LicenseKeyGen -- fingerprint-info --fp "..."
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
        Console.WriteLine("     PanelCalculator.Core/Security/LicenseService.cs");
        Console.WriteLine("     (constant PublicKeyBase64) and rebuild the app.");
        Console.WriteLine();
        return 0;
    }

    private static int CmdIssueLicense(string[] args)
    {
        string fp     = GetArg(args, "--fp")   ?? throw new ArgumentException("--fp <fingerprint> required");
        string name   = GetArg(args, "--name") ?? throw new ArgumentException("--name <customer name> required");
        string keyPath = GetArg(args, "--key") ?? Path.Combine(Environment.CurrentDirectory, LicenseIssuer.DefaultKeyFileName);

        var result = LicenseIssuer.Issue(fp, name, keyPath);

        Console.WriteLine("========================================================");
        Console.WriteLine("  License issued successfully");
        Console.WriteLine("========================================================");
        Console.WriteLine();
        Console.WriteLine($"  Customer name        : {result.CustomerName}");
        Console.WriteLine($"  Hardware fingerprint : {result.HardwareFingerprintDisplay}");
        Console.WriteLine($"  Issue date (UTC)     : {result.IssueDateUtc:yyyy-MM-dd HH:mm:ss}");
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
        Console.WriteLine($"  Signature OK         : {r.SignatureOk}");
        Console.WriteLine($"  Customer name        : {r.CustomerName}");
        Console.WriteLine($"  Hardware fingerprint : {r.HardwareFingerprintDisplay}");
        Console.WriteLine($"  Issue date (UTC)     : {r.IssueDateUtc:yyyy-MM-dd HH:mm:ss}");
        Console.WriteLine($"  Fingerprint matches  : {r.FingerprintMatches}");
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
        w.WriteLine("  issue --fp <hardware-fingerprint> --name <customer name> [--key <path>]");
        w.WriteLine("  verify --fp <fp> --license <key> [--key <path>]");
        w.WriteLine("  fingerprint-info --fp <fingerprint>");
        w.WriteLine();
    }
}
