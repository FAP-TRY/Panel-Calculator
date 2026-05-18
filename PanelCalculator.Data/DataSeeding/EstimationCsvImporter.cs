using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PanelCalculator.Core.Models;

namespace PanelCalculator.Data.DataSeeding;

/// <summary>
/// Reads back the 3-section CSV format that EstimationHistoryForm /
/// MainForm produce on Export CSV. Used for cross-machine backup &amp;
/// restore (re-install scenario).
///
/// Format reminder (per format-implementation-log.md Item #5):
///   section,key,value
///   meta,estimation_number,EST-20260518-001
///   meta,client_name,"PT ABC"
///   ... (metadata key/value)
///
///   no,section_name,reference_code,product_name,vendor,satuan,quantity,unit_price,line_total
///   1,Material Utama,C60N,Schneider C60N,Schneider,pcs,1,250000,250000
///   ... (line items)
///
///   summary,key,amount
///   summary,subtotal,250000
///   summary,ppn,27500
///   summary,grand_total,277500
///
/// All header names in English snake_case; prices plain numbers; dates ISO.
/// The parser is hand-written (not CsvHelper) because we have multiple
/// concatenated sections with different headers in one file.
/// </summary>
public class EstimationCsvImporter
{
    public enum ConflictResolution { AskUser, Skip, Overwrite, Rename }

    public sealed class ImportReport
    {
        public bool   Imported            { get; set; }
        public string EstimationNumber    { get; set; } = "";
        public int    ItemsParsed         { get; set; }
        public int    ItemsImported       { get; set; }
        public int    ItemsOrphaned       { get; set; }   // product not found in catalogue
        public List<string> Warnings      { get; } = new();
        public List<string> Errors        { get; } = new();
        public List<string> OrphanedItems { get; } = new();   // "RefCode (Vendor): ProductName"
        public string FinalEstimationNumber { get; set; } = "";   // after possible rename
    }

    private readonly PanelCalculatorContext _context;

    public EstimationCsvImporter(PanelCalculatorContext context) => _context = context;

    /// <summary>
    /// Parse + import the CSV at the supplied path. <paramref name="resolveConflict"/>
    /// is invoked when an estimation with the same number already exists in DB —
    /// pass a function that prompts the user; pass a default (e.g. () =&gt; Skip)
    /// for unattended import.
    /// </summary>
    public async Task<ImportReport> ImportFromFileAsync(string csvFilePath,
        Func<string, ConflictResolution>? resolveConflict = null)
    {
        if (!File.Exists(csvFilePath))
            throw new FileNotFoundException($"CSV file not found: {csvFilePath}");

        var text = await File.ReadAllTextAsync(csvFilePath);
        return await ImportFromTextAsync(text, resolveConflict);
    }

    /// <summary>Same as <see cref="ImportFromFileAsync"/> but accepts raw CSV text.</summary>
    public async Task<ImportReport> ImportFromTextAsync(string csvText,
        Func<string, ConflictResolution>? resolveConflict = null)
    {
        var report = new ImportReport();

        var meta  = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var items = new List<ParsedLineItem>();
        var summary = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);

        if (!ParseSections(csvText, meta, items, summary, report))
            return report;   // hard parse failure — report.Errors already populated

        report.ItemsParsed = items.Count;

        // ── Validate required fields ─────────────────────────────────────
        if (!meta.TryGetValue("estimation_number", out var estNumber) || string.IsNullOrWhiteSpace(estNumber))
        {
            report.Errors.Add("Field wajib 'estimation_number' tidak ditemukan di section meta.");
            return report;
        }
        if (!meta.TryGetValue("client_name", out var clientName) || string.IsNullOrWhiteSpace(clientName))
        {
            report.Errors.Add("Field wajib 'client_name' tidak ditemukan di section meta.");
            return report;
        }
        report.EstimationNumber      = estNumber;
        report.FinalEstimationNumber = estNumber;

        // ── Handle estimation-number conflict ────────────────────────────
        var existing = await _context.Estimations
            .Include(e => e.Details)
            .FirstOrDefaultAsync(e => e.EstimationNumber == estNumber);

        if (existing != null)
        {
            var choice = resolveConflict?.Invoke(estNumber) ?? ConflictResolution.Skip;
            switch (choice)
            {
                case ConflictResolution.Skip:
                    report.Warnings.Add($"Estimasi {estNumber} sudah ada di DB → di-skip.");
                    return report;
                case ConflictResolution.Overwrite:
                    _context.EstimationDetails.RemoveRange(existing.Details);
                    _context.Estimations.Remove(existing);
                    await _context.SaveChangesAsync();
                    report.Warnings.Add($"Estimasi {estNumber} sudah ada → overwrite (data lama dihapus).");
                    break;
                case ConflictResolution.Rename:
                    var newNumber = await GenerateUniqueNumberAsync(estNumber);
                    report.Warnings.Add($"Estimasi {estNumber} sudah ada → rename jadi {newNumber}.");
                    estNumber = newNumber;
                    report.FinalEstimationNumber = newNumber;
                    break;
                case ConflictResolution.AskUser:
                default:
                    report.Errors.Add("Konflik nomor estimasi tidak ter-resolve.");
                    return report;
            }
        }

        // ── Resolve product references ───────────────────────────────────
        var details = new List<EstimationDetail>();
        foreach (var li in items)
        {
            var product = await ResolveProductAsync(li.ReferenceCode, li.Vendor);
            if (product == null)
            {
                report.ItemsOrphaned++;
                report.OrphanedItems.Add($"{li.ReferenceCode} ({li.Vendor}): {li.ProductName}");
                continue;
            }
            details.Add(new EstimationDetail
            {
                ProductId      = product.ProductId,
                Quantity       = li.Quantity,
                UnitPrice      = li.UnitPrice,
                LineTotalPrice = li.LineTotal,
                Section        = string.IsNullOrWhiteSpace(li.SectionName) ? "Material Utama" : li.SectionName,
                Satuan         = string.IsNullOrWhiteSpace(li.Satuan) ? "pcs" : li.Satuan,
            });
        }
        report.ItemsImported = details.Count;

        if (details.Count == 0)
        {
            report.Errors.Add(
                "Tidak ada item yang bisa di-import. Pastikan katalog produk sudah terisi " +
                "(item dicocokkan berdasarkan ReferenceCode + Vendor).");
            return report;
        }

        // ── Build estimation header ──────────────────────────────────────
        var est = new Estimation
        {
            EstimationNumber = estNumber,
            ClientName       = clientName,
            NomorSurat       = meta.GetValueOrDefault("nomor_surat"),
            Company          = meta.GetValueOrDefault("company"),
            ProjectName      = meta.GetValueOrDefault("project_name"),
            Status           = string.IsNullOrWhiteSpace(meta.GetValueOrDefault("status"))
                                ? "Draft" : meta["status"],
            CreatedDate      = ParseDateTimeOrNow(meta.GetValueOrDefault("created_at")
                                ?? meta.GetValueOrDefault("created_date")),
            SubTotal         = summary.GetValueOrDefault("subtotal"),
            Margin           = summary.GetValueOrDefault("margin"),
            ShippingCost     = summary.GetValueOrDefault("shipping"),
            Tax              = summary.GetValueOrDefault("ppn"),
            PPh              = summary.GetValueOrDefault("pph"),
            TotalPrice       = summary.GetValueOrDefault("grand_total"),
            Details          = details
        };
        // Recompute SubTotal/TotalPrice if missing from CSV so display in
        // History doesn't show 0.
        if (est.SubTotal == 0) est.SubTotal = details.Sum(d => d.LineTotalPrice);
        if (est.TotalPrice == 0) est.TotalPrice =
            est.SubTotal + est.Margin + est.ShippingCost + est.Tax - est.PPh;

        _context.Estimations.Add(est);
        await _context.SaveChangesAsync();
        report.Imported = true;
        return report;
    }

    // ── Internal: parsing ────────────────────────────────────────────────
    private sealed record ParsedLineItem(
        int     No,
        string  SectionName,
        string  ReferenceCode,
        string  ProductName,
        string  Vendor,
        string  Satuan,
        int     Quantity,
        decimal UnitPrice,
        decimal LineTotal);

    private static bool ParseSections(
        string text,
        Dictionary<string, string> meta,
        List<ParsedLineItem> items,
        Dictionary<string, decimal> summary,
        ImportReport report)
    {
        // Split into logical sections by blank lines. Each section starts
        // with its own header row.
        var rawLines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        if (rawLines.Length == 0)
        {
            report.Errors.Add("File CSV kosong.");
            return false;
        }

        // Strip UTF-8 BOM from first line, if present.
        rawLines[0] = rawLines[0].TrimStart('﻿');

        // Group lines into blocks separated by blank lines.
        var blocks = new List<List<string>>();
        var current = new List<string>();
        foreach (var line in rawLines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                if (current.Count > 0) { blocks.Add(current); current = new List<string>(); }
            }
            else current.Add(line);
        }
        if (current.Count > 0) blocks.Add(current);

        if (blocks.Count == 0)
        {
            report.Errors.Add("CSV tidak punya baris data.");
            return false;
        }

        bool sawMeta = false, sawItems = false;

        foreach (var block in blocks)
        {
            var header = ParseCsvRow(block[0]);
            if (header.Count == 0) continue;

            // Identify section by header signature
            if (header.Count >= 3 && header[0].Equals("section", StringComparison.OrdinalIgnoreCase)
                && header[1].Equals("key", StringComparison.OrdinalIgnoreCase)
                && header[2].Equals("value", StringComparison.OrdinalIgnoreCase))
            {
                // Meta key/value section
                sawMeta = true;
                for (int i = 1; i < block.Count; i++)
                {
                    var cells = ParseCsvRow(block[i]);
                    if (cells.Count < 3) continue;
                    var section = cells[0];
                    if (!section.Equals("meta", StringComparison.OrdinalIgnoreCase)) continue;
                    var k = cells[1];
                    var v = cells[2];
                    if (!string.IsNullOrWhiteSpace(k))
                        meta[k] = v;
                }
            }
            else if (header.Count >= 3 && header[0].Equals("summary", StringComparison.OrdinalIgnoreCase))
            {
                // Summary section uses "summary,key,amount" header
                for (int i = 1; i < block.Count; i++)
                {
                    var cells = ParseCsvRow(block[i]);
                    if (cells.Count < 3) continue;
                    var k = cells[1];
                    if (decimal.TryParse(cells[2], NumberStyles.Any, CultureInfo.InvariantCulture, out var amt))
                        summary[k] = amt;
                }
            }
            else if (header.Count >= 9 && header[0].Equals("no", StringComparison.OrdinalIgnoreCase))
            {
                // Items section
                sawItems = true;
                // Map column index by header name so column reordering is OK
                var idx = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < header.Count; i++) idx[header[i]] = i;

                int Get(IList<string> row, string name) =>
                    idx.TryGetValue(name, out var p) && p < row.Count ? p : -1;

                for (int i = 1; i < block.Count; i++)
                {
                    var cells = ParseCsvRow(block[i]);
                    if (cells.Count == 0) continue;

                    string Cell(string name)
                    {
                        var p = Get(cells, name);
                        return p < 0 ? "" : cells[p];
                    }

                    if (!int.TryParse(Cell("no"), out var no)) continue;
                    var code   = Cell("reference_code");
                    var name   = Cell("product_name");
                    var vendor = Cell("vendor");
                    var sec    = Cell("section_name");
                    var sat    = Cell("satuan");
                    if (!int.TryParse(Cell("quantity"), out var qty)) qty = 0;
                    decimal.TryParse(Cell("unit_price"), NumberStyles.Any, CultureInfo.InvariantCulture, out var up);
                    decimal.TryParse(Cell("line_total"), NumberStyles.Any, CultureInfo.InvariantCulture, out var lt);

                    if (string.IsNullOrWhiteSpace(code))
                    {
                        report.Warnings.Add($"Baris item ke-{no}: reference_code kosong → di-skip.");
                        continue;
                    }
                    items.Add(new ParsedLineItem(no, sec, code, name, vendor, sat, qty, up, lt));
                }
            }
            // else: unknown block — ignore silently
        }

        if (!sawMeta)
        {
            report.Errors.Add("Section meta (header 'section,key,value') tidak ditemukan.");
            return false;
        }
        if (!sawItems)
        {
            report.Errors.Add("Section items (header 'no,section_name,reference_code,...') tidak ditemukan.");
            return false;
        }
        return true;
    }

    /// <summary>
    /// Minimal CSV row parser that understands double-quoted fields and
    /// `""` escape. Matches the writer in EstimationHistoryForm.cs.
    /// </summary>
    internal static List<string> ParseCsvRow(string line)
    {
        var list = new List<string>();
        if (line == null) return list;
        var sb = new System.Text.StringBuilder();
        bool inQuotes = false;
        for (int i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                    else inQuotes = false;
                }
                else sb.Append(c);
            }
            else
            {
                if (c == ',') { list.Add(sb.ToString()); sb.Clear(); }
                else if (c == '"') { inQuotes = true; }
                else sb.Append(c);
            }
        }
        list.Add(sb.ToString());
        return list;
    }

    private static DateTime ParseDateTimeOrNow(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return DateTime.UtcNow;
        if (DateTime.TryParse(raw, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeLocal | DateTimeStyles.AdjustToUniversal, out var dt))
            return dt;
        if (DateTime.TryParseExact(raw, "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeLocal | DateTimeStyles.AdjustToUniversal, out dt))
            return dt;
        return DateTime.UtcNow;
    }

    private async Task<Product?> ResolveProductAsync(string referenceCode, string vendor)
    {
        if (string.IsNullOrWhiteSpace(referenceCode)) return null;
        var v = string.IsNullOrWhiteSpace(vendor) ? null : vendor;
        // Prefer composite match
        if (v != null)
        {
            var p = await _context.Products
                .FirstOrDefaultAsync(x => x.ReferenceCode == referenceCode && x.Vendor == v);
            if (p != null) return p;
        }
        // Fall back to any row with the same ReferenceCode (ignore vendor) —
        // helpful when the source DB tagged Vendor and the target DB didn't.
        return await _context.Products
            .FirstOrDefaultAsync(x => x.ReferenceCode == referenceCode);
    }

    private async Task<string> GenerateUniqueNumberAsync(string original)
    {
        for (int suffix = 2; suffix < 1000; suffix++)
        {
            var candidate = $"{original}-IMPORT{suffix}";
            var exists = await _context.Estimations.AnyAsync(e => e.EstimationNumber == candidate);
            if (!exists) return candidate;
        }
        // Fallback: timestamp
        return $"{original}-IMPORT-{DateTime.UtcNow:HHmmss}";
    }
}
