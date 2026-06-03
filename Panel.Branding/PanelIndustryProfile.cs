using RabKit.Branding;

namespace PanelBranding;

/// <summary>
/// Industry profile for the panel-listrik vertical that PT TTS uses today.
/// Mirrors the constants that previously lived inline in MainForm.cs
/// (section list + dark-pro palette), PdfLetterExport/WordLetterExport
/// (section display map), and SettingsForm.FamilyToCategory
/// (MCB/MCCB/ACB/RCCB/Busbar/VSD/ATS/Surge/Kontaktor heuristic).
///
/// <para>
/// Section themes encode the existing dark-pro palette from
/// <c>MainForm.SectionHeaderColor / SectionRowColor / SectionHeaderForeColor</c>.
/// The hex strings here use the <em>header foreground</em> color — that's
/// the value most readily reused by the PDF/Excel writers (where section
/// dividers want a visible accent color, not the near-black background).
/// </para>
/// </summary>
public sealed class PanelIndustryProfile : IIndustryProfile
{
    public string IndustryId          => "panel-electrical";
    public string IndustryDisplayName => "Panel Listrik";

    /// <summary>
    /// Ordered section list — exactly mirrors the legacy
    /// <c>MainForm.Sections</c> array. The "Lainnya" PDF/Word fold target
    /// is intentionally not listed here; that mapping lives in
    /// <see cref="SectionDisplayMap"/> instead so the UI dropdown and the
    /// grouped grid stay clean.
    /// </summary>
    public IReadOnlyList<string> Sections { get; } = new[]
    {
        "Material Utama",
        "Material Pendukung",
        "Material Lainnya",
        "Box",
        "Incoming",
        "Outgoing",
        "Trailer",
        "Karoseri",
        "Jasa",
    };

    /// <summary>
    /// Per-section theme — keys cover every name in <see cref="Sections"/>
    /// plus the <c>"Lainnya"</c> PDF-fold target.
    ///
    /// <para>
    /// Color slot semantics (all values mirror the legacy hardcoded
    /// constants that lived in MainForm.cs lines ~1990-2030 and
    /// PdfQuotationExport.cs lines ~27-46, byte-identical so the W3 port
    /// is a pure no-op visually):
    /// </para>
    /// <list type="bullet">
    ///   <item><c>HexColor</c> = section header foreground accent (sky-blue, amber, …).
    ///     Used by MainForm header row text + PDF Formal divider accent.</item>
    ///   <item><c>TextHexColor</c> = MainForm header row background (dark navy/amber/etc.) —
    ///     near-black contrast for the bright accent above.</item>
    ///   <item><c>UiHeaderBgHex</c> = same as TextHexColor here (kept explicit
    ///     for callers that want the slot semantics directly).</item>
    ///   <item><c>UiRowBgHex</c> = MainForm data row background (darker than header).</item>
    ///   <item><c>PdfModernBgHex</c> = PdfQuotationExport pastel divider bg.</item>
    ///   <item><c>PdfModernFgHex</c> = PdfQuotationExport dark text on pastel divider.</item>
    /// </list>
    /// </summary>
    public IReadOnlyDictionary<string, IndustrySectionTheme> SectionThemes { get; }
        = new Dictionary<string, IndustrySectionTheme>(StringComparer.OrdinalIgnoreCase)
        {
            // Sky-blue
            ["Material Utama"]     = new("#7DD2FF", "#0F1637")
            {
                UiHeaderBgHex  = "#0F1637", UiRowBgHex     = "#0B1020",  // AppTheme.Bg1
                PdfModernBgHex = "#DBEAFE", PdfModernFgHex = "#1E40AF",
            },
            // Amber
            ["Material Pendukung"] = new("#FBBF24", "#201608")
            {
                UiHeaderBgHex  = "#201608", UiRowBgHex     = "#0E0C06",
                PdfModernBgHex = "#FEF9C3", PdfModernFgHex = "#856404",
            },
            // Emerald
            ["Material Lainnya"]   = new("#34D399", "#081C10")
            {
                UiHeaderBgHex  = "#081C10", UiRowBgHex     = "#070D0A",
                PdfModernBgHex = "#DCFCE7", PdfModernFgHex = "#15803D",
            },
            // Orange
            ["Box"]                = new("#FDBA74", "#261406")
            {
                UiHeaderBgHex  = "#261406", UiRowBgHex     = "#100A04",
                PdfModernBgHex = "#EDE9FE", PdfModernFgHex = "#5B21B6",
            },
            // Violet
            ["Incoming"]           = new("#C4B5FD", "#1C1030")
            {
                UiHeaderBgHex  = "#1C1030", UiRowBgHex     = "#0A0614",
                PdfModernBgHex = "#FFEDD5", PdfModernFgHex = "#9A3412",
            },
            // Rose
            ["Outgoing"]           = new("#FDA4AF", "#300A10")
            {
                UiHeaderBgHex  = "#300A10", UiRowBgHex     = "#140508",
                PdfModernBgHex = "#FFEDD5", PdfModernFgHex = "#9A3412",
            },
            // Cyan
            ["Trailer"]            = new("#67E8F9", "#061E26")
            {
                UiHeaderBgHex  = "#061E26", UiRowBgHex     = "#040E12",
                PdfModernBgHex = "#CFFAFE", PdfModernFgHex = "#0E7490",
            },
            // Yellow
            ["Karoseri"]           = new("#FDE047", "#282206")
            {
                UiHeaderBgHex  = "#282206", UiRowBgHex     = "#110E04",
                PdfModernBgHex = "#CFFAFE", PdfModernFgHex = "#0E7490",
            },
            // Fuchsia
            ["Jasa"]               = new("#F0ABFC", "#260A22")
            {
                UiHeaderBgHex  = "#260A22", UiRowBgHex     = "#10050E",
                PdfModernBgHex = "#E2E8F0", PdfModernFgHex = "#334155",
            },
            // PDF "Lainnya" fold target — same accent palette as Material Lainnya
            // so the divider on the "Rincian Material > Lainnya" page reads identical.
            ["Lainnya"]            = new("#34D399", "#081C10")
            {
                UiHeaderBgHex  = "#081C10", UiRowBgHex     = "#070D0A",
                PdfModernBgHex = "#E2E8F0", PdfModernFgHex = "#334155",
            },
        };

    /// <summary>
    /// Maps raw section names to the display label used on the PDF/Word
    /// "Rincian Material" pages and the Page-1 summary table.
    /// Mirrors <c>PdfLetterExport.MapSectionToDisplay</c> (lines 586-606).
    /// </summary>
    public IReadOnlyDictionary<string, string> SectionDisplayMap { get; }
        = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Box"]                = "Box Panel",
            ["Box Panel"]          = "Box Panel",
            ["Material Utama"]     = "Incoming",
            ["Incoming"]           = "Incoming",
            ["Material Pendukung"] = "Outgoing",
            ["Outgoing"]           = "Outgoing",
            ["Material Lainnya"]   = "Lainnya",
            ["Trailer"]            = "Lainnya",
            ["Karoseri"]           = "Lainnya",
            ["Jasa"]               = "Lainnya",
            ["Lainnya"]            = "Lainnya",
        };

    /// <summary>
    /// MCB/MCCB/ACB/RCCB/Busbar/VSD/ATS/Surge family heuristic. Mirrors
    /// <c>SettingsForm.FamilyToCategory</c> lines 925-944.
    /// </summary>
    public string? CategoryFromFamily(string family)
    {
        if (string.IsNullOrWhiteSpace(family)) return null;
        var t = family.ToLowerInvariant();
        if (t.Contains("rccb") || t.Contains("rcbo") || t.Contains("elcb") ||
            t.Contains("residual") || t.Contains("iid")) return "RCCB";
        if (t.Contains("acb")  || t.Contains("air circuit") ||
            t.Contains("masterpact") || t.Contains("nw")) return "ACB";
        if (t.Contains("mccb") || t.Contains("molded") || t.Contains("gopact") ||
            t.Contains("cvs")  || t.Contains("nsx")    || t.Contains("nm1") ||
            t.Contains("nm8")  || t.Contains("nc100")) return "MCCB";
        if (t.Contains("mcb")  || t.Contains("miniature") || t.Contains("easy9") ||
            t.Contains("domae") || t.Contains("nxb")  || t.Contains("nb1") ||
            t.Contains("nb3")  || t.Contains("nb4")) return "MCB";
        if (t.Contains("kontaktor") || t.Contains("contactor") || t.Contains("tesys") ||
            t.Contains("lc1")  || t.Contains("lc3") || t.Contains("nc1") ||
            t.Contains("nc2")) return "Kontaktor";
        if (t.Contains("motor cb") || t.Contains("motor circuit") ||
            t.Contains("gv2") || t.Contains("gv3")) return "Motor CB";
        if (t.Contains("surge") || t.Contains("spd") || t.Contains("lightning") ||
            t.Contains("arrester")) return "Surge Arrester";
        if (t.Contains("vsd")   || t.Contains("inverter") ||
            t.Contains("variable speed") || t.Contains("nv")) return "VSD";
        if (t.Contains("ats")   || t.Contains("transfer switch") || t.Contains("nz7"))
            return "ATS";
        if (t.Contains("busbar") || t.Contains("isobar") || t.Contains("linergy"))
            return "Busbar";
        if (t.Contains("box")    || t.Contains("pragma") || t.Contains("enclosure"))
            return "Box";
        return "Other";
    }

    public string PlaceholderPerihal => "Panel MDP 3-Phase 400A";
}
