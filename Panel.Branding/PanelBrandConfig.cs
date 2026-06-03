using System.Reflection;
using RabKit.Branding;

namespace PanelBranding;

/// <summary>
/// PT Tritunggal Swarna branding implementation. Every value here is the
/// literal hardcode that previously lived inline in the source tree —
/// see <c>docs/rabkit-01-codebase-audit.md</c> for the original locations.
///
/// <para>
/// Asset bytes are sourced from <see cref="EmbeddedResource"/> entries in
/// this DLL (<c>Panel.Branding.Assets.*</c>), NOT from the WinForms
/// assembly anymore. The legacy paths still resolve in v1.2.9 because the
/// Week 1 refactor leaves the duplicate WinForms-side resources in place;
/// Week 2 removes those duplicates once every consumer is on
/// <see cref="BrandContext"/>.
/// </para>
/// </summary>
public sealed class PanelBrandConfig : IBrandConfig
{
    // ── Company identity ────────────────────────────────────────────────

    public string CompanyName       => "PT. Tritunggal Swarna";
    public string CompanyShortName  => "TTS";
    public string CompanyNpwp       => "01.234.567.8-901.000";
    public string CompanyAddress    => "Jl. Tamblong No. 2 Bandung 40112";
    public string CompanyPhone      => "+62-22 4218800";
    public string CompanyEmail      => "info@tritunggalswarna.co.id";
    public string CompanyWebsite    => "www.tritunggalswarna.co.id";

    /// <summary>
    /// Empty string sampai PT TTS confirm nomor WhatsApp resmi mereka.
    /// Saat kosong, ActivationForm tetap render tombol "Hubungi WA"
    /// dengan placeholder "628XXXXXXXXXX" — non-functional, customer dapat
    /// kontak via email atau telp sementara.
    /// </summary>
    public string SupportWhatsAppNumber => "";

    // ── Default signer ──────────────────────────────────────────────────

    public string DefaultSignerName    => "Kuntjoro Handoko";
    public string DefaultSignerTitle   => "Direktur";
    public string DefaultOfferLocation => "Bandung";

    // ── Branding assets (embedded resources in THIS DLL) ────────────────

    public byte[]? GetLogoBytes()
        => ReadEmbedded("Panel.Branding.Assets.logo.png");

    public byte[]? GetLetterheadBytes()
        => ReadEmbedded("Panel.Branding.Assets.letterhead.jpg");

    public byte[]? GetSignatureBytes()
        => ReadEmbedded("Panel.Branding.Assets.signature.png");

    public byte[]? GetStampBytes()
        => ReadEmbedded("Panel.Branding.Assets.stamp.png");

    // ── Security: machine-key derivation parameters ─────────────────────
    //
    // BOTH values here MUST stay byte-identical with the legacy
    // MachineKeyProvider implementation. Any drift would re-derive a
    // different SQLCipher key and brick every existing customer DB on
    // their next launch.
    //
    // Source-of-truth: MachineKeyProvider.cs line 56 (app tag)
    // and MachineKeyProvider.GetPepper() lines 174-189 (XOR-encoded pepper).

    public string LegacyMachineKeyAppTag => "PanelCalculator.v1";

    /// <summary>
    /// XOR-encoded pepper bytes. Cleartext: "TTS-PanelCalc-pepper-2026-v1"
    /// (28 chars / 28 bytes). XOR mask = 0x5A applied byte-wise; calling
    /// <c>MachineKeyProvider.GetPepper()</c> decodes the same bytes.
    /// The constant lives here instead of in the engine so each brand pack
    /// can pick its own pepper without touching engine code.
    /// </summary>
    public byte[] MachineKeyPepperBytes { get; } = new byte[]
    {
        0x0E, 0x0E, 0x09, 0x77, 0x0A, 0x3B, 0x34, 0x3F,
        0x36, 0x19, 0x3B, 0x36, 0x39, 0x77, 0x2A, 0x3F,
        0x2A, 0x2A, 0x3F, 0x28, 0x77, 0x68, 0x6A, 0x68,
        0x6C, 0x77, 0x2C, 0x6B
    };

    // ── License public key ──────────────────────────────────────────────
    //
    // Source-of-truth: LicenseService.PublicKeyBase64 (Core, line 34).
    // Matching private key lives offline at PT TTS — never commit it.

    public string LicensePublicKeyBase64
        => "D5Bk2OC+FFZdZqqtI86iFCiy1/pFRQLbkMBpVQ+ia6w=";

    // ── Auto-update channel ─────────────────────────────────────────────
    //
    // Source-of-truth: UpdateService.cs lines 29-30, 35.

    public string UpdateGitHubOwner => "FAP-TRY";
    public string UpdateGitHubRepo  => "Panel-Calculator";
    public string UpdateAssetName   => "PanelCalculator.exe";

    // ── App identity ────────────────────────────────────────────────────

    public string AppDisplayName        => "Kalkulator Panel Tritunggal Swarna";
    public string AppDataFolderName     => "PanelCalculator";
    public string AppVersion            => "1.2.9";
    public string EstimationNumberPattern => "EST-{0:yyyyMMdd}-{1:D3}";

    // ── Helpers ─────────────────────────────────────────────────────────

    private static byte[]? ReadEmbedded(string resourceName)
    {
        try
        {
            using var stream = typeof(PanelBrandConfig).Assembly
                .GetManifestResourceStream(resourceName);
            if (stream == null) return null;
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            return ms.ToArray();
        }
        catch
        {
            return null;
        }
    }
}
