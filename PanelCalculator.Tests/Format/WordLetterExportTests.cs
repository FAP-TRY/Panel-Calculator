using ClosedXML.Excel;
using PanelCalculator.Core.Models;
using PanelCalculator.Core.Services;
using PanelCalculator.WinForms.Services;
using System.IO.Compression;
using Xceed.Words.NET;
using Xunit;

namespace PanelCalculator.Tests.Format;

/// <summary>
/// Smoke test untuk WordLetterExport. Verifikasi:
///  1. File .docx ter-generate dan ukuran > 1KB.
///  2. File bisa di-load kembali via Xceed.Words.NET.DocX.Load() tanpa throw.
///  3. Content yang penting (kode estimasi, nama klien, Rp angka) ada di paragraph.
/// Test ini menulis ke <see cref="Path.GetTempPath"/> dan hapus di akhir.
/// </summary>
public class WordLetterExportTests
{
    [Fact]
    public void Generate_SinglePanel_ProducesValidDocxFile()
    {
        var tempPath = Path.Combine(Path.GetTempPath(),
            $"test_word_single_{Guid.NewGuid():N}.docx");
        try
        {
            var items = new List<WordLetterExport.LineItem>
            {
                new("REF-001", "Circuit Breaker 3P 100A", "Schneider", "Material Utama",
                    Quantity: 2, Satuan: "pcs", UnitPrice: 1_500_000m, LineTotal: 3_000_000m),
                new("REF-002", "Kabel NYY 4x10mm", "Supreme", "Material Pendukung",
                    Quantity: 50, Satuan: "m", UnitPrice: 25_000m, LineTotal: 1_250_000m),
            };

            WordLetterExport.Generate(
                outputPath:       tempPath,
                estimationNumber: "EST-20260527-001",
                clientName:       "Bpk. Budi",
                contactPhone:     "0812-3456-7890",
                company:          "PT Test Sentosa",
                address:          "Jl. Test No. 1, Bandung",
                perihal:          "Panel MDP Test",
                createdDate:      new DateTime(2026, 5, 27),
                notes:            "Test catatan",
                items:            items,
                subtotal:         4_250_000m,
                marginAmount:     425_000m,
                shippingCost:     0m,
                taxPercent:       11m,
                taxAmount:        514_250m,
                pphPercent:       0m,
                pphAmount:        0m,
                total:            5_189_250m,
                settings:         new Dictionary<string, string>());

            // 1. File ada & > 1KB
            Assert.True(File.Exists(tempPath));
            var size = new FileInfo(tempPath).Length;
            Assert.True(size > 1024, $"File terlalu kecil: {size} byte (harus > 1KB).");

            // 2. .docx bisa di-load kembali (artinya valid OpenXML structure)
            using (var doc = DocX.Load(tempPath))
            {
                Assert.NotNull(doc);
                // 3. Cek content: kumpulkan semua text di document
                var allText = string.Join("\n", doc.Paragraphs.Select(p => p.Text));
                Assert.Contains("EST-20260527-001", allText);
                Assert.Contains("Bpk. Budi", allText);
                Assert.Contains("PT Test Sentosa", allText);
                Assert.Contains("Bandung", allText);
                // Cek summary contains "GRAND TOTAL"
                var allTablesText = doc.Tables.SelectMany(t => t.Rows)
                    .SelectMany(r => r.Cells)
                    .SelectMany(c => c.Paragraphs)
                    .Select(p => p.Text);
                var combinedTableText = string.Join(" | ", allTablesText);
                Assert.Contains("GRAND TOTAL", combinedTableText);
            }
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    [Fact]
    public void GenerateCombined_TwoPanels_ProducesValidDocxFile()
    {
        var tempPath = Path.Combine(Path.GetTempPath(),
            $"test_word_combined_{Guid.NewGuid():N}.docx");
        try
        {
            // Buat 2 panel sederhana — content tidak terlalu penting, fokus ke validitas file.
            var p1 = new Estimation
            {
                EstimationNumber = "EST-001", ClientName = "PT Alpha", Status = "Draft",
                SubTotal = 5_000_000m, Margin = 500_000m, TotalPrice = 0m,
            };
            var p2 = new Estimation
            {
                EstimationNumber = "EST-002", ClientName = "PT Alpha", Status = "Draft",
                SubTotal = 3_000_000m, Margin = 300_000m, TotalPrice = 0m,
            };
            var summary = CombinedQuotationCalculator.Build(new[] { p1, p2 }, taxPercent: 11m);

            var panels = new List<WordLetterExport.CombinedPanel>
            {
                new("EST-001", "Panel A",
                    new List<WordLetterExport.LineItem>
                    {
                        new("R1", "Item Panel A", "Vendor", "Material Utama",
                            1, "pcs", 5_000_000m, 5_000_000m),
                    }),
                new("EST-002", "Panel B",
                    new List<WordLetterExport.LineItem>
                    {
                        new("R2", "Item Panel B", "Vendor", "Material Utama",
                            1, "pcs", 3_000_000m, 3_000_000m),
                    }),
            };

            WordLetterExport.GenerateCombined(
                outputPath:   tempPath,
                nomorSurat:   "GAB-2026-001",
                clientName:   "PT Alpha",
                contactPhone: null,
                company:      null,
                address:      null,
                perihal:      "Penawaran Gabungan 2 Panel",
                createdDate:  DateTime.UtcNow,
                notes:        "",
                panels:       panels,
                summary:      summary,
                settings:     new Dictionary<string, string>());

            Assert.True(File.Exists(tempPath));
            Assert.True(new FileInfo(tempPath).Length > 1024);

            using var doc = DocX.Load(tempPath);
            Assert.NotNull(doc);
            var allText = string.Join("\n", doc.Paragraphs.Select(p => p.Text));
            Assert.Contains("GAB-2026-001", allText);
            Assert.Contains("PT Alpha", allText);
            // Panel info
            Assert.Contains("Panel A", allText);
            Assert.Contains("Panel B", allText);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }
}

/// <summary>
/// Smoke test untuk ExcelLetterExport. Verifikasi file .xlsx valid via ClosedXML.
/// </summary>
public class ExcelLetterExportTests
{
    [Fact]
    public void Generate_SinglePanel_ProducesValidXlsxFile()
    {
        var tempPath = Path.Combine(Path.GetTempPath(),
            $"test_excel_single_{Guid.NewGuid():N}.xlsx");
        try
        {
            var items = new List<ExcelLetterExport.LineItem>
            {
                new("REF-001", "MCB 3P 16A", "Schneider", "Material Utama",
                    Quantity: 5, Satuan: "pcs", UnitPrice: 250_000m, LineTotal: 1_250_000m),
            };

            ExcelLetterExport.Generate(
                outputPath:       tempPath,
                estimationNumber: "EST-XLSX-001",
                clientName:       "PT Excel Test",
                contactPhone:     null,
                company:          "PT Excel Test Tbk",
                address:          "Jl. Excel No. 1",
                perihal:          "Test Excel",
                createdDate:      new DateTime(2026, 5, 27),
                notes:            "",
                items:            items,
                subtotal:         1_250_000m,
                marginAmount:     0m,
                shippingCost:     0m,
                taxPercent:       11m,
                taxAmount:        137_500m,
                pphPercent:       0m,
                pphAmount:        0m,
                total:            1_387_500m,
                settings:         new Dictionary<string, string>());

            Assert.True(File.Exists(tempPath));
            Assert.True(new FileInfo(tempPath).Length > 1024);

            // Re-open dengan ClosedXML
            using var wb = new XLWorkbook(tempPath);
            Assert.True(wb.Worksheets.Count >= 1);
            // Sheet utama bernama "Penawaran" + sheet "Ringkasan"
            var sheetNames = wb.Worksheets.Select(w => w.Name).ToList();
            Assert.Contains("Penawaran", sheetNames);
            Assert.Contains("Ringkasan", sheetNames);

            // Cari salah satu cell yang harus berisi nomor estimasi
            var penawaran = wb.Worksheet("Penawaran");
            var usedRange = penawaran.RangeUsed();
            Assert.NotNull(usedRange);
            bool foundEstNumber = false;
            foreach (var cell in usedRange!.CellsUsed())
            {
                if (cell.GetString().Contains("EST-XLSX-001"))
                {
                    foundEstNumber = true;
                    break;
                }
            }
            Assert.True(foundEstNumber, "Nomor estimasi tidak ditemukan di sheet Penawaran.");
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    [Fact]
    public void GenerateCombined_TwoPanels_ProducesValidXlsxFile()
    {
        var tempPath = Path.Combine(Path.GetTempPath(),
            $"test_excel_combined_{Guid.NewGuid():N}.xlsx");
        try
        {
            var p1 = new Estimation { EstimationNumber = "EXC-1", ClientName = "C", Status = "Draft",
                SubTotal = 10_000_000m, Margin = 1_000_000m, TotalPrice = 0m };
            var p2 = new Estimation { EstimationNumber = "EXC-2", ClientName = "C", Status = "Draft",
                SubTotal = 6_000_000m, Margin = 600_000m, TotalPrice = 0m };
            var summary = CombinedQuotationCalculator.Build(new[] { p1, p2 }, taxPercent: 11m);

            var panels = new List<ExcelLetterExport.CombinedPanel>
            {
                new("EXC-1", "Panel X",
                    new List<ExcelLetterExport.LineItem>
                    {
                        new("R1", "Item X", "V", "Material Utama", 1, "pcs", 10_000_000m, 10_000_000m),
                    }),
                new("EXC-2", "Panel Y",
                    new List<ExcelLetterExport.LineItem>
                    {
                        new("R2", "Item Y", "V", "Material Utama", 1, "pcs", 6_000_000m, 6_000_000m),
                    }),
            };

            ExcelLetterExport.GenerateCombined(
                outputPath:   tempPath,
                nomorSurat:   "EXCEL-GAB-001",
                clientName:   "Customer X",
                contactPhone: null, company: null, address: null,
                perihal:      "Test Combined Excel",
                createdDate:  DateTime.UtcNow,
                notes:        "",
                panels:       panels,
                summary:      summary,
                settings:     new Dictionary<string, string>());

            Assert.True(File.Exists(tempPath));
            Assert.True(new FileInfo(tempPath).Length > 1024);

            using var wb = new XLWorkbook(tempPath);
            // Harus ada: Penawaran, Panel 1, Panel 2, Ringkasan
            var sheetNames = wb.Worksheets.Select(w => w.Name).ToList();
            Assert.Contains("Penawaran", sheetNames);
            Assert.Contains("Ringkasan", sheetNames);
            // Sheet per panel (total ≥ 4 = Penawaran + 2 panel + Ringkasan)
            Assert.True(wb.Worksheets.Count >= 4,
                $"Expected ≥ 4 sheets, got {wb.Worksheets.Count}");

            // Cek nomor surat ada di sheet Penawaran
            var penawaran = wb.Worksheet("Penawaran");
            bool found = false;
            foreach (var cell in penawaran.RangeUsed()!.CellsUsed())
            {
                if (cell.GetString().Contains("EXCEL-GAB-001")) { found = true; break; }
            }
            Assert.True(found, "Nomor surat gabungan tidak ditemukan.");
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }
}
