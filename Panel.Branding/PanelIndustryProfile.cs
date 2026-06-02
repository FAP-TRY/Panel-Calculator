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
    /// Ordered section list. Mirrors <c>MainForm.Sections</c> + the
    /// "Lainnya" alias that the PDF/Word writers fold into.
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
        "Lainnya",
    };

    /// <summary>
    /// Per-section theme — keys match <see cref="Sections"/>.
    /// Hex color = section header foreground accent (sky-blue, amber, etc.),
    /// text hex = near-black so the accent reads well as a chip background.
    /// </summary>
    public IReadOnlyDictionary<string, IndustrySectionTheme> SectionThemes { get; }
        = new Dictionary<string, IndustrySectionTheme>(StringComparer.OrdinalIgnoreCase)
        {
            // Sky-blue
            ["Material Utama"]     = new("#7DD2FF", "#0F1637"),
            // Amber
            ["Material Pendukung"] = new("#FBBF24", "#201608"),
            // Emerald
            ["Material Lainnya"]   = new("#34D399", "#081C10"),
            // Orange
            ["Box"]                = new("#FDBA74", "#261406"),
            // Violet
            ["Incoming"]           = new("#C4B5FD", "#1C1030"),
            // Rose
            ["Outgoing"]           = new("#FDA4AF", "#300A10"),
            // Cyan
            ["Trailer"]            = new("#67E8F9", "#061E26"),
            // Yellow
            ["Karoseri"]           = new("#FDE047", "#282206"),
            // Fuchsia
            ["Jasa"]               = new("#F0ABFC", "#260A22"),
            // Same as Material Lainnya — used by the PDF map
            ["Lainnya"]            = new("#34D399", "#081C10"),
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
