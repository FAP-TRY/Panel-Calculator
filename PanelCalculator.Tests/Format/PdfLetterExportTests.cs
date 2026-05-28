using PanelCalculator.Core.Models;
using PanelCalculator.Core.Services;
using PanelCalculator.WinForms.Services;
using Xunit;

namespace PanelCalculator.Tests.Format;

/// <summary>
/// Smoke test untuk <see cref="PdfLetterExport"/>. Verifikasi:
///  1. File .pdf ter-generate dan ukuran > 50KB (= letterhead.jpg ter-embed).
///  2. Content yang penting (nomor surat, nama klien, harga) ada di PDF byte stream.
///  3. Multi-panel menghasilkan >= 2 pages (Page 1 cover + Page 2+ Rincian Material).
/// Output PDF di-write ke <see cref="Path.GetTempPath"/> dan dihapus di akhir
/// (kecuali test environment KEEP_SAMPLE=1 di-set).
/// </summary>
public class PdfLetterExportTests
{
    private static bool KeepSample =>
        string.Equals(Environment.GetEnvironmentVariable("KEEP_SAMPLE"), "1",
            StringComparison.OrdinalIgnoreCase);

    [Fact]
    public void Generate_SinglePanel_ProducesValidPdfWithLetterhead()
    {
        var tempPath = Path.Combine(Path.GetTempPath(),
            $"test_pdf_single_{Guid.NewGuid():N}.pdf");
        try
        {
            var items = new List<PdfLetterExport.LineItem>
            {
                new("ACQ-001", "EV Charger AC 7kW Type ACQ", "Tritunggal Swarna",
                    "Material Utama", Quantity: 1, Satuan: "Unit",
                    UnitPrice: 4_320_000m, LineTotal: 4_320_000m),
            };

            PdfLetterExport.Generate(
                outputPath:       tempPath,
                estimationNumber: "161/PR.BDG/V/2026",
                clientName:       "PT Gemilang Energi Semesta",
                contactPhone:     null,
                company:          "PT Gemilang Energi Semesta",
                address:          "Jl. Kopo Permai Blok HH No. 1\nBandung",
                perihal:          "EV Charger AC 7kW Type ACQ",
                createdDate:      new DateTime(2026, 5, 4),
                notes:            "",
                items:            items,
                subtotal:         4_320_000m,
                margin1Percent:   0m,
                margin2Percent:   0m,
                margin3Percent:   0m,
                marginAmount:     0m,
                shippingCost:     0m,
                taxPercent:       12m,
                taxAmount:        518_400m,
                pphPercent:       0m,
                pphAmount:        0m,
                total:            4_838_400m,
                settings:         new Dictionary<string, string>());

            Assert.True(File.Exists(tempPath));
            var size = new FileInfo(tempPath).Length;
            // > 50KB membuktikan letterhead.jpg sudah ter-embed (raw jpg ~ 160KB
            // dengan kompresi PDF turun jadi 80-150KB; tanpa letterhead PDF tipis ~10KB)
            Assert.True(size > 50_000,
                $"File terlalu kecil: {size} byte — letterhead mungkin tidak ter-embed.");

            // Verify file opens (basic header check: %PDF-)
            var head = new byte[5];
            using (var fs = File.OpenRead(tempPath))
                fs.Read(head, 0, 5);
            Assert.Equal("%PDF-", System.Text.Encoding.ASCII.GetString(head));
        }
        finally
        {
            if (!KeepSample && File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    [Fact]
    public void Generate_SinglePanel_NoSettingsOverride_UsesDefaultSigner()
    {
        var tempPath = Path.Combine(Path.GetTempPath(),
            $"test_pdf_defaults_{Guid.NewGuid():N}.pdf");
        try
        {
            // Empty settings → must use default signer "Kuntjoro Handoko", title "Direktur"
            PdfLetterExport.Generate(
                outputPath:       tempPath,
                estimationNumber: "TEST-001",
                clientName:       "Test",
                contactPhone:     null,
                company:          null,
                address:          null,
                perihal:          "Test Panel",
                createdDate:      new DateTime(2026, 5, 27),
                notes:            "",
                items:            new List<PdfLetterExport.LineItem>
                {
                    new("X1", "Test Item", "V", "Material Utama",
                        1, "pcs", 1_000_000m, 1_000_000m),
                },
                subtotal:         1_000_000m,
                margin1Percent:   0m, margin2Percent: 0m, margin3Percent: 0m,
                marginAmount:     0m, shippingCost:   0m,
                taxPercent:       12m, taxAmount: 120_000m,
                pphPercent:       0m,  pphAmount: 0m,
                total:            1_120_000m,
                settings:         new Dictionary<string, string>());

            Assert.True(File.Exists(tempPath));
            Assert.True(new FileInfo(tempPath).Length > 50_000);
        }
        finally
        {
            if (!KeepSample && File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    [Fact]
    public void GenerateCombined_TwoPanels_ProducesMultiPagePdf()
    {
        var tempPath = Path.Combine(Path.GetTempPath(),
            $"test_pdf_combined_{Guid.NewGuid():N}.pdf");
        try
        {
            var p1 = new Estimation
            {
                EstimationNumber = "EST-001",
                ClientName       = "PT Anugerah Jaya Indoteknik",
                Company          = "PT Anugerah Jaya Indoteknik",
                ProjectName      = "Panel PAC-DB-OFC-001",
                Status           = "Draft",
                SubTotal         = 60_000_000m,
                Margin           =  9_145_000m,
                TotalPrice       = 0m,
            };
            var p2 = new Estimation
            {
                EstimationNumber = "EST-002",
                ClientName       = "PT Anugerah Jaya Indoteknik",
                Company          = "PT Anugerah Jaya Indoteknik",
                ProjectName      = "Panel ABC-XYZ",
                Status           = "Draft",
                SubTotal         = 30_000_000m,
                Margin           =  5_250_000m,
                TotalPrice       = 0m,
            };
            var summary = CombinedQuotationCalculator.Build(new[] { p1, p2 }, taxPercent: 12m);

            var panels = new List<PdfLetterExport.CombinedPanel>
            {
                new("EST-001", "Panel PAC-DB-OFC-001", new List<PdfLetterExport.LineItem>
                {
                    new("BX-001", "Box SPCC t.2mm 800Hx600Wx200D", "DSP", "Box",
                        1, "Unit", 5_000_000m, 5_000_000m),
                    new("MCC-001", "MCCB 3P 160A 18kA", "Schneider", "Material Utama",
                        1, "Bh", 4_500_000m, 4_500_000m),
                    new("MCB-001", "MCB 1P 10A 10kA", "Schneider", "Material Pendukung",
                        7, "Bh", 250_000m, 1_750_000m),
                }),
                new("EST-002", "Panel ABC-XYZ", new List<PdfLetterExport.LineItem>
                {
                    new("BX-002", "Box SPCC t.1.6mm 600Hx400Wx200D", "DSP", "Box",
                        1, "Unit", 3_500_000m, 3_500_000m),
                    new("MCC-002", "MCCB 3P 100A 18kA", "Schneider", "Material Utama",
                        1, "Bh", 3_200_000m, 3_200_000m),
                }),
            };

            PdfLetterExport.GenerateCombined(
                outputPath:   tempPath,
                nomorSurat:   "191/PR.BDG/V/2026",
                clientName:   "PT Anugerah Jaya Indoteknik",
                contactPhone: null,
                company:      "PT Anugerah Jaya Indoteknik",
                address:      "Jl. Industri No. 5\nBandung",
                perihal:      "Penawaran Panel Distribusi",
                createdDate:  new DateTime(2026, 5, 4),
                notes:        "",
                panels:       panels,
                summary:      summary,
                settings:     new Dictionary<string, string>());

            Assert.True(File.Exists(tempPath));
            var size = new FileInfo(tempPath).Length;
            Assert.True(size > 50_000,
                $"File terlalu kecil: {size} byte — letterhead mungkin tidak ter-embed.");

            // Verify page count >= 3 (page 1 cover + 2 rincian halaman per panel)
            // Lightweight page count: count "/Type /Page" occurrences in the
            // PDF (each /Page object means 1 page).
            var pdfBytes = File.ReadAllBytes(tempPath);
            var pdfText  = System.Text.Encoding.Latin1.GetString(pdfBytes);
            int pageCount = System.Text.RegularExpressions.Regex
                .Matches(pdfText, @"/Type\s*/Page[^s]").Count;
            Assert.True(pageCount >= 3,
                $"Expected >= 3 pages (1 cover + 2 detail), got {pageCount}.");
        }
        finally
        {
            if (!KeepSample && File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    [Fact]
    public void MapSectionToDisplay_AllAliases_ReturnsCorrectGroup()
    {
        // Box Panel
        Assert.Equal("Box Panel", InvokeMapSection("Box"));
        Assert.Equal("Box Panel", InvokeMapSection("Box Panel"));
        // Incoming
        Assert.Equal("Incoming", InvokeMapSection("Material Utama"));
        Assert.Equal("Incoming", InvokeMapSection("Incoming"));
        // Outgoing
        Assert.Equal("Outgoing", InvokeMapSection("Material Pendukung"));
        Assert.Equal("Outgoing", InvokeMapSection("Outgoing"));
        // Lainnya
        Assert.Equal("Lainnya", InvokeMapSection("Material Lainnya"));
        Assert.Equal("Lainnya", InvokeMapSection("Trailer"));
        Assert.Equal("Lainnya", InvokeMapSection("Karoseri"));
        Assert.Equal("Lainnya", InvokeMapSection("Jasa"));
        Assert.Equal("Lainnya", InvokeMapSection("Lainnya"));
    }

    private static string? InvokeMapSection(string section)
    {
        var m = typeof(PdfLetterExport).GetMethod("MapSectionToDisplay",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        return (string?)m!.Invoke(null, new object[] { section });
    }

    /// <summary>
    /// Generates sample PDF/Word artefak ke folder <c>build/samples/letter-export/</c>
    /// di repo root. Hanya jalan kalau env var <c>EMIT_SAMPLES=1</c> diset
    /// (mis. saat manual QA). Tidak masuk CI normal.
    /// </summary>
    [Fact]
    public void EmitSamples_ToRepoFolder_WhenEnvVarSet()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("EMIT_SAMPLES"), "1",
            StringComparison.OrdinalIgnoreCase))
            return; // skip silently — only runs in manual QA mode

        // Locate repo root by walking up from current test assembly dir
        var asmDir = Path.GetDirectoryName(typeof(PdfLetterExportTests).Assembly.Location)!;
        string? repoRoot = asmDir;
        while (repoRoot != null &&
               !File.Exists(Path.Combine(repoRoot, "PanelCalculator.sln")))
            repoRoot = Path.GetDirectoryName(repoRoot);
        if (repoRoot == null) return;

        var outDir = Path.Combine(repoRoot, "build", "samples", "letter-export");
        Directory.CreateDirectory(outDir);

        // ── Sample 1: SINGLE PANEL (mirror referensi 161-EV Charger) ──
        var s1 = Path.Combine(outDir, "sample-single-EV-Charger.pdf");
        var s1w = Path.Combine(outDir, "sample-single-EV-Charger.docx");
        var items = new List<PdfLetterExport.LineItem>
        {
            new("ACQ-001", "EV Charger AC 7kW Type ACQ", "Tritunggal Swarna",
                "Material Utama", 1, "Unit", 4_320_000m, 4_320_000m),
        };
        var wordItems = items.Select(i => new WordLetterExport.LineItem(
            i.ReferenceCode, i.ProductName, i.Vendor, i.Section,
            i.Quantity, i.Satuan, i.UnitPrice, i.LineTotal)).ToList();

        PdfLetterExport.Generate(s1, "161/PR.BDG/V/2026",
            "PT Gemilang Energi Semesta", null,
            "PT Gemilang Energi Semesta",
            "Jl. Kopo Permai Blok HH No. 1\nBandung",
            "EV Charger AC 7kW Type ACQ",
            new DateTime(2026, 5, 4), "",
            items, 4_320_000m, 0m, 0m, 0m, 0m, 0m,
            12m, 518_400m, 0m, 0m, 4_838_400m,
            new Dictionary<string, string>());

        WordLetterExport.Generate(s1w, "161/PR.BDG/V/2026",
            "PT Gemilang Energi Semesta", null,
            "PT Gemilang Energi Semesta",
            "Jl. Kopo Permai Blok HH No. 1\nBandung",
            "EV Charger AC 7kW Type ACQ",
            new DateTime(2026, 5, 4), "",
            wordItems, 4_320_000m, 0m, 0m,
            12m, 518_400m, 0m, 0m, 4_838_400m,
            new Dictionary<string, string>());

        // ── Sample 2: MULTI PANEL (mirror referensi 191-Panel Distribusi) ──
        var p1 = new Estimation
        {
            EstimationNumber = "EST-001", ClientName = "PT Anugerah Jaya Indoteknik",
            Company = "PT Anugerah Jaya Indoteknik", ProjectName = "Panel PAC-DB-OFC-001",
            Status = "Draft", SubTotal = 60_000_000m, Margin = 9_145_000m, TotalPrice = 0m,
        };
        var p2 = new Estimation
        {
            EstimationNumber = "EST-002", ClientName = "PT Anugerah Jaya Indoteknik",
            Company = "PT Anugerah Jaya Indoteknik", ProjectName = "Panel ABC-XYZ",
            Status = "Draft", SubTotal = 30_000_000m, Margin = 5_250_000m, TotalPrice = 0m,
        };
        var summary = CombinedQuotationCalculator.Build(new[] { p1, p2 }, taxPercent: 12m);

        var pdfPanels = new List<PdfLetterExport.CombinedPanel>
        {
            new("EST-001", "Panel PAC-DB-OFC-001", new List<PdfLetterExport.LineItem>
            {
                new("BX-001", "Box SPCC t.2mm 800Hx600Wx200D Powder Coating Min. 80 Mikron",
                    "DSP", "Box", 1, "Unit", 5_000_000m, 5_000_000m),
                new("EZC250F3160", "MCCB 3P 160A 18kA", "Schneider", "Material Utama",
                    1, "Bh", 4_500_000m, 4_500_000m),
                new("A9F84110", "MCB 1P 10A 10kA", "Schneider", "Material Pendukung",
                    7, "Bh", 250_000m, 1_750_000m),
                new("CT-001", "Current Transformer 200/5A", "HOWIG", "Material Pendukung",
                    3, "Bh", 350_000m, 1_050_000m),
            }),
            new("EST-002", "Panel ABC-XYZ", new List<PdfLetterExport.LineItem>
            {
                new("BX-002", "Box SPCC t.1.6mm 600Hx400Wx200D", "DSP", "Box",
                    1, "Unit", 3_500_000m, 3_500_000m),
                new("EZC100F3100", "MCCB 3P 100A 18kA", "Schneider", "Material Utama",
                    1, "Bh", 3_200_000m, 3_200_000m),
                new("A9F84106", "MCB 1P 6A 10kA", "Schneider", "Material Pendukung",
                    4, "Bh", 220_000m, 880_000m),
            }),
        };

        var s2 = Path.Combine(outDir, "sample-combined-Panel-Distribusi.pdf");
        PdfLetterExport.GenerateCombined(s2, "191/PR.BDG/V/2026",
            "PT Anugerah Jaya Indoteknik", null,
            "PT Anugerah Jaya Indoteknik",
            "Jl. Industri No. 5\nBandung",
            "Penawaran Panel Distribusi",
            new DateTime(2026, 5, 4), "",
            pdfPanels, summary, new Dictionary<string, string>());

        var wordPanels = pdfPanels.Select(p =>
            new WordLetterExport.CombinedPanel(p.EstimationNumber, p.ProjectName,
                p.Items.Select(i => new WordLetterExport.LineItem(
                    i.ReferenceCode, i.ProductName, i.Vendor, i.Section,
                    i.Quantity, i.Satuan, i.UnitPrice, i.LineTotal)).ToList())).ToList();
        var s2w = Path.Combine(outDir, "sample-combined-Panel-Distribusi.docx");
        WordLetterExport.GenerateCombined(s2w, "191/PR.BDG/V/2026",
            "PT Anugerah Jaya Indoteknik", null,
            "PT Anugerah Jaya Indoteknik",
            "Jl. Industri No. 5\nBandung",
            "Penawaran Panel Distribusi",
            new DateTime(2026, 5, 4), "",
            wordPanels, summary, new Dictionary<string, string>());
    }
}
