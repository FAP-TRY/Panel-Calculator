namespace RabKit.Branding;

/// <summary>
/// Per-industry profile — every property here is <em>domain-specific</em>
/// (panel listrik vs konstruksi sipil vs MEP vs carrosserie, etc.).
/// Per-company branding (signer, company name, license keys) lives in
/// <see cref="IBrandConfig"/> instead.
///
/// <para>
/// Implementations are expected to be immutable, stateless and safe
/// to share across threads — they are resolved once at startup via
/// <see cref="BrandContext"/>.
/// </para>
/// </summary>
public interface IIndustryProfile
{
    /// <summary>
    /// Machine identifier for the industry vertical
    /// (e.g. <c>"panel-electrical"</c>, <c>"carrosserie"</c>).
    /// Used in telemetry, license claims and pack metadata.
    /// </summary>
    string IndustryId { get; }

    /// <summary>
    /// Human-readable industry label shown in the UI
    /// (e.g. <c>"Panel Listrik"</c>).
    /// </summary>
    string IndustryDisplayName { get; }

    /// <summary>
    /// Ordered list of section names used for grouping estimation items
    /// in MainForm and the Rincian Material pages of penawaran.
    /// E.g. for panel: <c>["Box","Incoming","Outgoing","Trailer","Karoseri","Jasa","Lainnya"]</c>.
    /// </summary>
    IReadOnlyList<string> Sections { get; }

    /// <summary>
    /// Per-section UI/PDF theme (background + text color) for grid rows
    /// and Rincian Material dividers. Keys MUST match values from
    /// <see cref="Sections"/>. Case-insensitive lookups are the caller's
    /// responsibility — provide both raw forms if needed.
    /// </summary>
    IReadOnlyDictionary<string, IndustrySectionTheme> SectionThemes { get; }

    /// <summary>
    /// Map from raw section key (as stored in DB / used in code) to the
    /// display label shown on PDF/Word "Rincian Material" pages.
    /// E.g. <c>"Box" → "Box Panel"</c>, <c>"Incoming" → "Incoming"</c>,
    /// <c>"Trailer" → "Lainnya"</c>.
    /// </summary>
    IReadOnlyDictionary<string, string> SectionDisplayMap { get; }

    /// <summary>
    /// Heuristic mapping from a vendor "family" string (parsed out of CSV
    /// imports) to an in-app category, e.g. <c>"MCB 1P" → "MCB"</c>.
    /// Returns <c>null</c> when no rule matches — the caller falls back
    /// to the raw family string.
    /// </summary>
    string? CategoryFromFamily(string family);

    /// <summary>
    /// Placeholder text for the "Perihal / Project Name" field, used as
    /// the watermark example for new estimations
    /// (e.g. <c>"Panel MDP 3-Phase 400A"</c>).
    /// </summary>
    string PlaceholderPerihal { get; }
}

/// <summary>
/// UI/PDF theme for a single industry section. Colors are expressed as
/// CSS-style hex strings so the same definition is reusable across
/// WinForms (System.Drawing.Color) and iText (DeviceRgb) without bringing
/// a Windows-only dependency into this contract assembly.
/// </summary>
public sealed record IndustrySectionTheme(string HexColor, string TextHexColor);
