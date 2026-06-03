namespace RabKit.Branding;

/// <summary>
/// Per-tenant branding configuration — every property here is
/// <em>company-specific</em> (PT TTS, RAB Cepat, future Carrosserie, etc.).
/// Industry-specific concerns (section vocab, MCB heuristic, etc.) live
/// in <see cref="IIndustryProfile"/> instead.
///
/// <para>
/// IMPLEMENTORS BEWARE — the security-sensitive properties
/// (<see cref="LegacyMachineKeyAppTag"/>, <see cref="MachineKeyPepperBytes"/>,
/// <see cref="LicensePublicKeyBase64"/>) must return values byte-identical
/// with the previous hardcoded constants for any existing customer install,
/// otherwise their SQLCipher DB becomes unreadable and their license stops
/// validating. See <c>docs/rabkit-04-execution-plan.md</c> Week 2 / Risk #1.
/// </para>
/// </summary>
public interface IBrandConfig
{
    // ── Company identity ────────────────────────────────────────────────

    /// <summary>Full legal name (e.g. "PT. Tritunggal Swarna").</summary>
    string CompanyName { get; }

    /// <summary>Short form for buttons/labels (e.g. "TTS").</summary>
    string CompanyShortName { get; }

    /// <summary>NPWP — optional, empty string when unknown.</summary>
    string CompanyNpwp { get; }

    /// <summary>Multi-line company address (uses \n between lines).</summary>
    string CompanyAddress { get; }

    /// <summary>Primary contact phone (e.g. "+62-22 4218800").</summary>
    string CompanyPhone { get; }

    /// <summary>Customer-facing email.</summary>
    string CompanyEmail { get; }

    /// <summary>Marketing website without scheme (e.g. "www.tritunggalswarna.co.id").</summary>
    string CompanyWebsite { get; }

    /// <summary>
    /// Support WhatsApp number used as the wa.me deep-link target in the
    /// activation form ("Hubungi support via WhatsApp"). Format: international
    /// digits without "+" or spaces (e.g. <c>"628123456789"</c>). Return
    /// empty string when the brand has no public WhatsApp channel — the
    /// activation form falls back to a non-functional placeholder.
    /// </summary>
    string SupportWhatsAppNumber { get; }

    // ── Default signer (override-able per-user via AppSettings) ──────────

    /// <summary>Default signer name shown on PDF/Word penawaran.</summary>
    string DefaultSignerName { get; }

    /// <summary>Default signer title (e.g. "Direktur").</summary>
    string DefaultSignerTitle { get; }

    /// <summary>Default city shown above signer (e.g. "Bandung").</summary>
    string DefaultOfferLocation { get; }

    // ── Branding assets ─────────────────────────────────────────────────
    // Returns bytes so the implementation is free to source them from an
    // embedded resource, file on disk, or remote download. Each accessor
    // returns null when the asset is not present (caller may fall back to
    // a generic default or skip the visual).

    /// <summary>Logo PNG bytes — small image used in top bar and login screen.</summary>
    byte[]? GetLogoBytes();

    /// <summary>Letterhead JPG bytes — drawn as full-page background in
    /// PDF/Word penawaran. Aspect ratio must match the page template.</summary>
    byte[]? GetLetterheadBytes();

    /// <summary>Handwriting signature PNG bytes (transparent background)
    /// overlaid above the signer name in penawaran.</summary>
    byte[]? GetSignatureBytes();

    /// <summary>Round stamp PNG bytes (transparent background) overlaid
    /// next to the signature in penawaran.</summary>
    byte[]? GetStampBytes();

    // ── Security: machine key derivation parameters ─────────────────────

    /// <summary>
    /// Legacy app tag string mixed into the SQLCipher key derivation
    /// (SHA-256 over board serial | CPU id | MachineGuid | this tag |
    /// pepper).
    ///
    /// <para>
    /// <strong>KRITIS</strong> — for PT TTS installs this MUST return the
    /// exact string <c>"PanelCalculator.v1"</c>. Changing the value would
    /// re-derive a different SQLCipher key, leaving every existing
    /// customer database unreadable on the next launch.
    /// </para>
    /// </summary>
    string LegacyMachineKeyAppTag { get; }

    /// <summary>
    /// Pepper bytes mixed into the SQLCipher key derivation.
    ///
    /// <para>
    /// <strong>KRITIS</strong> — same constraint as <see cref="LegacyMachineKeyAppTag"/>.
    /// For PT TTS this must decode to the cleartext
    /// <c>"TTS-PanelCalc-pepper-2026-v1"</c> (28 characters / 28 bytes),
    /// byte-identical with the legacy <c>MachineKeyProvider.GetPepper()</c>
    /// implementation. Existing installs depend on it.
    /// </para>
    /// </summary>
    byte[] MachineKeyPepperBytes { get; }

    // ── License public key ──────────────────────────────────────────────

    /// <summary>
    /// Ed25519 license-signing public key (base64-encoded 32 raw bytes →
    /// 44 chars). The matching private key lives offline at the brand
    /// owner (issuer) and is never committed.
    /// </summary>
    string LicensePublicKeyBase64 { get; }

    // ── Auto-update channel ─────────────────────────────────────────────

    /// <summary>GitHub org/owner for release downloads (e.g. "FAP-TRY").</summary>
    string UpdateGitHubOwner { get; }

    /// <summary>GitHub repo slug (e.g. "Panel-Calculator").</summary>
    string UpdateGitHubRepo { get; }

    /// <summary>
    /// EXE asset filename uploaded with each release
    /// (e.g. "PanelCalculator.exe"). Matched literally against the
    /// release asset list.
    /// </summary>
    string UpdateAssetName { get; }

    // ── App identity ────────────────────────────────────────────────────

    /// <summary>
    /// Title shown in window title bar and login screen
    /// (e.g. "Kalkulator Panel Tritunggal Swarna").
    /// </summary>
    string AppDisplayName { get; }

    /// <summary>
    /// Folder name under <c>%AppData%</c> for the local DB + logs
    /// (e.g. "PanelCalculator"). Two brands can coexist on one machine
    /// by using different folder names.
    /// </summary>
    string AppDataFolderName { get; }

    /// <summary>App semantic version (e.g. "1.2.9"). Bumped on every
    /// distributable release.</summary>
    string AppVersion { get; }

    /// <summary>
    /// <c>string.Format</c> template for the per-day estimation number,
    /// e.g. <c>"EST-{0:yyyyMMdd}-{1:D3}"</c>. Slot 0 = creation date,
    /// slot 1 = same-day sequence number.
    /// </summary>
    string EstimationNumberPattern { get; }

    // ── Edition manifest bundle (W4 / v1.3.0+) ──────────────────────────

    /// <summary>
    /// Returns the signed <c>edition.manifest.bundle</c> bytes embedded in
    /// this brand pack, or <c>null</c> when no manifest is bundled (dev
    /// builds / unit tests).
    ///
    /// <para>
    /// Bundle layout: 4-byte BE length + UTF-8 JSON manifest + 64-byte
    /// Ed25519 signature. Decoded via
    /// <see cref="SignedManifestLoader.LoadAndVerifyFromBundle"/> in
    /// Program.Main; result is bound into <see cref="EditionContext"/>.
    /// </para>
    /// </summary>
    byte[]? GetEditionManifestBundle();
}
