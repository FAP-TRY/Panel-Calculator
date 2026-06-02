using PanelCalculator.Core.Models;
using PanelCalculator.Core.Services;
using PanelCalculator.WinForms.Services;

namespace PanelCalculator.Tests.GoldenMaster;

/// <summary>
/// Five deterministic sample estimations used by the golden master suite.
/// Every sample uses fixed values (clientName, items, prices, dates) so the
/// extracted-text fingerprint is stable across runs. The samples cover the
/// PT TTS workflow variations: single panel, multi-panel, complex
/// discounts, large multi-section, and minimal zero-shipping.
///
/// <para>
/// Adding a new sample? Bump the version baseline file under
/// <c>docs/golden-master-hashes-v1.2.9.json</c> and re-run the suite
/// with <c>GOLDEN_MASTER_REGENERATE=1</c> to refresh the expected hashes.
/// </para>
/// </summary>
internal static class GoldenMasterSamples
{
    /// <summary>Stable date used for every sample — avoids "today" drift
    /// in the rendered header/signature city/date line.</summary>
    public static readonly DateTime FixedCreatedDate = new(2026, 5, 28, 0, 0, 0, DateTimeKind.Utc);

    public sealed record SinglePanelSample(
        string Name,
        string EstimationNumber,
        string ClientName,
        string? ContactPhone,
        string? Company,
        string? Address,
        string? Perihal,
        string Notes,
        IReadOnlyList<LineSpec> Items,
        decimal Subtotal,
        decimal Margin1Percent,
        decimal Margin2Percent,
        decimal Margin3Percent,
        decimal MarginAmount,
        decimal ShippingCost,
        decimal TaxPercent,
        decimal TaxAmount,
        decimal PphPercent,
        decimal PphAmount,
        decimal Total);

    public sealed record MultiPanelSample(
        string Name,
        string NomorSurat,
        string ClientName,
        string? ContactPhone,
        string? Company,
        string? Address,
        string? Perihal,
        string Notes,
        IReadOnlyList<PanelSpec> Panels,
        decimal CombinedShippingCost,
        decimal TaxPercent);

    public sealed record PanelSpec(
        string EstimationNumber,
        string ProjectName,
        decimal SubTotal,
        decimal Margin,
        decimal PPh,
        IReadOnlyList<LineSpec> Items);

    public sealed record LineSpec(
        string Reference, string Name, string Vendor, string Section,
        int Qty, string Satuan, decimal UnitPrice, decimal LineTotal);

    // ── Sample 1: Single panel, 1 item, 15% margin, no PPh, no ongkir ───
    public static SinglePanelSample Sample1_SingleSimple() => new(
        Name:             "sample-01-single-simple",
        EstimationNumber: "EST-20260528-001",
        ClientName:       "PT Alpha Engineering",
        ContactPhone:     null,
        Company:          "PT Alpha Engineering",
        Address:          "Jl. Industri Raya No. 12\nBekasi 17131",
        Perihal:          "Box PL CB 600x400x200",
        Notes:            "",
        Items: new List<LineSpec>
        {
            new("BX-PLCB-001", "Box PL CB SPCC t.1.6mm 600Hx400Wx200D Powder Coating",
                "DSP", "Box", 1, "Unit", 4_000_000m, 4_000_000m),
        },
        Subtotal:         4_000_000m,
        Margin1Percent:   15m,
        Margin2Percent:   0m,
        Margin3Percent:   0m,
        MarginAmount:     600_000m,
        ShippingCost:     0m,
        TaxPercent:       11m,
        TaxAmount:        506_000m,   // 11% of 4_600_000
        PphPercent:       0m,
        PphAmount:        0m,
        Total:            5_106_000m);

    // ── Sample 2: Multi-panel — 3 panels, 5-8 items each, 20% margin, ongkir 750k
    public static MultiPanelSample Sample2_MultiPanelMedium() => new(
        Name:        "sample-02-multi-panel-medium",
        NomorSurat:  "180/PR.BDG/V/2026",
        ClientName:  "PT Bersama Sentosa",
        ContactPhone: "081234567890",
        Company:     "PT Bersama Sentosa",
        Address:     "Jl. Cikutra No. 88\nBandung 40124",
        Perihal:     "Penawaran Panel Gedung Office",
        Notes:       "Mohon konfirmasi spesifikasi sebelum produksi.",
        Panels: new List<PanelSpec>
        {
            new(
                EstimationNumber: "EST-20260528-101",
                ProjectName:      "Panel Distribusi Utama (MDP)",
                SubTotal:         30_000_000m,
                Margin:           6_000_000m,    // 20%
                PPh:              0m,
                Items: new List<LineSpec>
                {
                    new("BX-MDP-01", "Box MDP SPCC t.2mm 1800Hx800Wx400D", "DSP", "Box",
                        1, "Unit", 12_000_000m, 12_000_000m),
                    new("MCCB-160", "MCCB 3P 160A 36kA EasyPact", "Schneider", "Incoming",
                        1, "Bh", 6_800_000m, 6_800_000m),
                    new("MCB-32",  "MCB 3P 32A 6kA Acti9", "Schneider", "Outgoing",
                        3, "Bh", 850_000m, 2_550_000m),
                    new("CT-300",  "Current Transformer 300/5A Class 0.5", "HOWIG", "Incoming",
                        3, "Bh", 480_000m, 1_440_000m),
                    new("VM-500",  "Voltmeter Digital 500V 96x96", "HOWIG", "Outgoing",
                        1, "Bh", 1_250_000m, 1_250_000m),
                    new("RAIL-2M", "DIN Rail 35mm x 2m", "Generic", "Material Lainnya",
                        2, "Bh", 75_000m, 150_000m),
                }),
            new(
                EstimationNumber: "EST-20260528-102",
                ProjectName:      "Panel Lighting Lt.1-3",
                SubTotal:         15_000_000m,
                Margin:           3_000_000m,    // 20%
                PPh:              0m,
                Items: new List<LineSpec>
                {
                    new("BX-LP-01", "Box LP SPCC t.1.6mm 1200Hx600Wx250D", "DSP", "Box",
                        1, "Unit", 5_500_000m, 5_500_000m),
                    new("MCCB-100", "MCCB 3P 100A 25kA", "Schneider", "Incoming",
                        1, "Bh", 4_200_000m, 4_200_000m),
                    new("MCB-16",  "MCB 1P 16A 6kA", "Schneider", "Outgoing",
                        12, "Bh", 175_000m, 2_100_000m),
                    new("CONT-25", "Kontaktor 25A 3P 220V coil TeSys", "Schneider", "Outgoing",
                        3, "Bh", 920_000m, 2_760_000m),
                    new("TMR-24H", "Timer 24-hour analog AHC15", "Anly", "Outgoing",
                        2, "Bh", 220_000m, 440_000m),
                }),
            new(
                EstimationNumber: "EST-20260528-103",
                ProjectName:      "Panel AC Outdoor (PAC-1)",
                SubTotal:         10_000_000m,
                Margin:           2_000_000m,    // 20%
                PPh:              0m,
                Items: new List<LineSpec>
                {
                    new("BX-AC-01", "Box AC Outdoor IP54 800Hx600Wx300D", "DSP", "Box",
                        1, "Unit", 4_800_000m, 4_800_000m),
                    new("MCCB-63", "MCCB 3P 63A 18kA", "Schneider", "Incoming",
                        1, "Bh", 2_800_000m, 2_800_000m),
                    new("CONT-40", "Kontaktor 40A 3P 220V coil", "Schneider", "Outgoing",
                        2, "Bh", 1_150_000m, 2_300_000m),
                    new("OVR-10",  "Overload Relay 7-10A LRD", "Schneider", "Outgoing",
                        2, "Bh", 425_000m, 850_000m),
                    new("PB-GRN",  "Push Button Green flush XB7", "Schneider", "Outgoing",
                        4, "Bh", 95_000m, 380_000m),
                }),
        },
        CombinedShippingCost: 750_000m,
        TaxPercent:           11m);

    // ── Sample 3: Single panel — complex 3-tier adjustments, PPh 2%, ongkir 1.2jt ─
    public static SinglePanelSample Sample3_ComplexDiscount() => new(
        Name:             "sample-03-single-complex-discount",
        EstimationNumber: "EST-20260528-201",
        ClientName:       "Bapak Hartono",
        ContactPhone:     "0811223344",
        Company:          "PT Cita Karya Mandiri",
        Address:          "Jl. Asia Afrika No. 19\nBandung",
        Perihal:          "Panel ATS-AMF 200A",
        Notes:            "Termasuk wiring dan test commissioning di pabrik.",
        Items: new List<LineSpec>
        {
            new("BX-ATS-200", "Box ATS-AMF SPCC t.2mm 1600Hx700Wx400D",
                "DSP", "Box", 1, "Unit", 8_500_000m, 8_500_000m),
            new("ATS-200",   "ATS Controller AMF 200A 3P", "Smartgen", "Material Utama",
                1, "Unit", 12_500_000m, 12_500_000m),
            new("MCCB-250-A","MCCB 3P 250A 36kA (PLN)", "Schneider", "Material Utama",
                1, "Bh", 7_800_000m, 7_800_000m),
            new("MCCB-250-B","MCCB 3P 250A 36kA (Genset)", "Schneider", "Material Utama",
                1, "Bh", 7_800_000m, 7_800_000m),
            new("CONT-200",  "Kontaktor 200A 3P 220V coil", "Schneider", "Material Utama",
                2, "Bh", 4_200_000m, 8_400_000m),
            new("WIRING",    "Wiring + lugs + heat-shrink", "Generic", "Material Pendukung",
                1, "Set", 2_500_000m, 2_500_000m),
        },
        // Computed manually: 47_500_000 subtotal,
        // adj1=-5% → -2_375_000 (45_125_000), adj2=-10% → -4_512_500 (40_612_500),
        // adj3=-2% → -812_250 (39_800_250). Margin amount = -7_699_750.
        Subtotal:         47_500_000m,
        Margin1Percent:   -5m,
        Margin2Percent:   -10m,
        Margin3Percent:   -2m,
        MarginAmount:     -7_699_750m,
        ShippingCost:     1_200_000m,
        TaxPercent:       11m,
        // DPP = 39_800_250 + 1_200_000 = 41_000_250
        // PPN = 41_000_250 * 0.11 = 4_510_028 (rounded)
        TaxAmount:        4_510_028m,
        PphPercent:       2m,
        // PPh dasar = 41_000_250 * 2% = 820_005
        PphAmount:        820_005m,
        Total:            44_690_273m); // DPP + PPN - PPh

    // ── Sample 4: Large 5-panel multi-section (Box/Incoming/Outgoing/Trailer/Karoseri)
    public static MultiPanelSample Sample4_LargeMultiSection() => new(
        Name:        "sample-04-multi-panel-large-sections",
        NomorSurat:  "201/PR.BDG/V/2026",
        ClientName:  "Bapak Surya",
        ContactPhone: "0812345600",
        Company:     "PT Damai Sentosa Energi",
        Address:     "Jl. Pasirkaliki No. 100\nBandung 40171",
        Perihal:     "Penawaran Genset Container 500kVA Full",
        Notes:       "Pelunasan via VA BCA. Lead time 6-8 minggu.",
        Panels: new List<PanelSpec>
        {
            new(
                EstimationNumber: "EST-20260528-301",
                ProjectName:      "Panel ATS-AMF Genset",
                SubTotal:         40_000_000m,
                Margin:           6_000_000m,
                PPh:              0m,
                Items: new List<LineSpec>
                {
                    new("BX-301", "Box ATS SPCC t.2mm 1600x800x400", "DSP", "Box",
                        1, "Unit", 9_500_000m, 9_500_000m),
                    new("ATS-300", "ATS Controller 300A", "Smartgen", "Incoming",
                        1, "Unit", 15_000_000m, 15_000_000m),
                    new("MCCB-250", "MCCB 3P 250A 36kA x2", "Schneider", "Incoming",
                        2, "Bh", 7_500_000m, 15_000_000m),
                    new("CONT-150", "Kontaktor 150A 3P", "Schneider", "Outgoing",
                        2, "Bh", 3_200_000m, 6_400_000m),
                }),
            new(
                EstimationNumber: "EST-20260528-302",
                ProjectName:      "Panel Synchronisation 2-Genset",
                SubTotal:         55_000_000m,
                Margin:           8_250_000m,
                PPh:              0m,
                Items: new List<LineSpec>
                {
                    new("BX-302", "Box Synchro 2000Hx1000Wx500D", "DSP", "Box",
                        1, "Unit", 18_000_000m, 18_000_000m),
                    new("SYNC-2", "Synchroscope + sync relay 2-genset", "Deepsea", "Incoming",
                        1, "Set", 22_000_000m, 22_000_000m),
                    new("MCCB-400", "MCCB 3P 400A 36kA x2", "Schneider", "Incoming",
                        2, "Bh", 8_500_000m, 17_000_000m),
                    new("PB-RED", "Push Button Emergency Stop", "Schneider", "Outgoing",
                        2, "Bh", 280_000m, 560_000m),
                }),
            new(
                EstimationNumber: "EST-20260528-303",
                ProjectName:      "Frame Trailer Container 20ft",
                SubTotal:         85_000_000m,
                Margin:           12_750_000m,
                PPh:              0m,
                Items: new List<LineSpec>
                {
                    new("TRL-301", "Chassis Trailer SPCC t.5mm + UNP 200", "Custom", "Trailer",
                        1, "Set", 45_000_000m, 45_000_000m),
                    new("AXLE-2", "Axle Single 8-ton + suspension", "Daido", "Trailer",
                        2, "Set", 12_500_000m, 25_000_000m),
                    new("BAN-385", "Ban Trailer 385/65R22.5 x4", "Bridgestone", "Trailer",
                        4, "Bh", 3_750_000m, 15_000_000m),
                }),
            new(
                EstimationNumber: "EST-20260528-304",
                ProjectName:      "Karoseri Body Container 20ft",
                SubTotal:         65_000_000m,
                Margin:           9_750_000m,
                PPh:              0m,
                Items: new List<LineSpec>
                {
                    new("WALL-301", "Wall Panel Sandwich SPCC t.0.8mm + rockwool 75mm",
                        "Custom", "Karoseri", 1, "Set", 32_000_000m, 32_000_000m),
                    new("DOOR-301", "Pintu Container 2-leaf + lock + seal", "Custom", "Karoseri",
                        1, "Set", 18_000_000m, 18_000_000m),
                    new("ROOF-301", "Atap ACP 4mm fire-retardant", "Seven", "Karoseri",
                        1, "Set", 9_500_000m, 9_500_000m),
                    new("VENT-301", "Louver Ventilasi 800x400 + fan 2x", "Generic", "Karoseri",
                        2, "Bh", 2_750_000m, 5_500_000m),
                }),
            new(
                EstimationNumber: "EST-20260528-305",
                ProjectName:      "Sistem Power Distribution",
                SubTotal:         30_000_000m,
                Margin:           4_500_000m,
                PPh:              0m,
                Items: new List<LineSpec>
                {
                    new("KABEL-301", "Kabel NYY 4x35 mm² @ 50 meter", "Supreme", "Material Utama",
                        50, "m", 320_000m, 16_000_000m),
                    new("KABEL-302", "Kabel NYM 3x2.5 mm² @ 100 meter", "Supreme", "Material Pendukung",
                        100, "m", 28_000m, 2_800_000m),
                    new("LUGS-301", "Lugs + connector + heat-shrink set", "Generic", "Material Pendukung",
                        1, "Set", 3_500_000m, 3_500_000m),
                    new("JASA-301", "Jasa wiring + commissioning di lokasi", "TTS", "Jasa",
                        1, "Set", 7_700_000m, 7_700_000m),
                }),
        },
        CombinedShippingCost: 3_500_000m,
        TaxPercent:           11m);

    // ── Sample 5: Simple single panel, zero shipping (edge case for minimal output)
    public static SinglePanelSample Sample5_SimpleZeroShipping() => new(
        Name:             "sample-05-single-zero-shipping",
        EstimationNumber: "EST-20260528-401",
        ClientName:       "Ibu Linda",
        ContactPhone:     null,
        Company:          null,
        Address:          null,
        Perihal:          "MCB 1P 16A Standard",
        Notes:            "",
        Items: new List<LineSpec>
        {
            new("A9F84116", "MCB 1P 16A 6kA Acti9 iC60N", "Schneider", "Material Utama",
                4, "Bh", 285_000m, 1_140_000m),
            new("A9F84120", "MCB 1P 20A 6kA Acti9 iC60N", "Schneider", "Material Utama",
                2, "Bh", 295_000m, 590_000m),
        },
        Subtotal:         1_730_000m,
        Margin1Percent:   10m,
        Margin2Percent:   0m,
        Margin3Percent:   0m,
        MarginAmount:     173_000m,
        ShippingCost:     0m,
        TaxPercent:       11m,
        TaxAmount:        209_330m,   // 11% of 1_903_000
        PphPercent:       0m,
        PphAmount:        0m,
        Total:            2_112_330m);

    // ── Translation helpers — sample → exporter line-item types ───────────

    public static IReadOnlyList<PdfLetterExport.LineItem> ToPdfItems(IReadOnlyList<LineSpec> items)
        => items.Select(i => new PdfLetterExport.LineItem(
            i.Reference, i.Name, i.Vendor, i.Section,
            i.Qty, i.Satuan, i.UnitPrice, i.LineTotal)).ToList();

    public static IReadOnlyList<WordLetterExport.LineItem> ToWordItems(IReadOnlyList<LineSpec> items)
        => items.Select(i => new WordLetterExport.LineItem(
            i.Reference, i.Name, i.Vendor, i.Section,
            i.Qty, i.Satuan, i.UnitPrice, i.LineTotal)).ToList();

    public static IReadOnlyList<ExcelLetterExport.LineItem> ToExcelItems(IReadOnlyList<LineSpec> items)
        => items.Select(i => new ExcelLetterExport.LineItem(
            i.Reference, i.Name, i.Vendor, i.Section,
            i.Qty, i.Satuan, i.UnitPrice, i.LineTotal)).ToList();

    public static (IReadOnlyList<PdfLetterExport.CombinedPanel> pdf,
                   IReadOnlyList<WordLetterExport.CombinedPanel> word,
                   IReadOnlyList<ExcelLetterExport.CombinedPanel> excel,
                   CombinedQuotationCalculator.CombinedSummary summary)
        ToCombinedExportInputs(MultiPanelSample sample)
    {
        var estimations = sample.Panels.Select(p => new Estimation
        {
            EstimationNumber = p.EstimationNumber,
            ClientName       = sample.ClientName,
            Company          = sample.Company,
            ProjectName      = p.ProjectName,
            Status           = "Draft",
            SubTotal         = p.SubTotal,
            Margin           = p.Margin,
            PPh              = p.PPh,
            TotalPrice       = 0m,
        }).ToList();

        var summary = CombinedQuotationCalculator.Build(
            estimations, sample.CombinedShippingCost, sample.TaxPercent);

        var pdf = sample.Panels.Select(p => new PdfLetterExport.CombinedPanel(
            p.EstimationNumber, p.ProjectName, ToPdfItems(p.Items))).ToList();

        var word = sample.Panels.Select(p => new WordLetterExport.CombinedPanel(
            p.EstimationNumber, p.ProjectName, ToWordItems(p.Items))).ToList();

        var excel = sample.Panels.Select(p => new ExcelLetterExport.CombinedPanel(
            p.EstimationNumber, p.ProjectName, ToExcelItems(p.Items))).ToList();

        return (pdf, word, excel, summary);
    }
}
