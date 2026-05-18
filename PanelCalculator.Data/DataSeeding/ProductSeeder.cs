using CsvHelper;
using CsvHelper.Configuration;
using CsvHelper.TypeConversion;
using PanelCalculator.Core.Models;
using System.Globalization;

namespace PanelCalculator.Data.DataSeeding;

public class ProductSeeder
{
    private readonly PanelCalculatorContext _context;

    public ProductSeeder(PanelCalculatorContext context)
    {
        _context = context;
    }

    public async Task<int> SeedFromCsvAsync(string csvFilePath)
    {
        if (!File.Exists(csvFilePath))
            throw new FileNotFoundException($"CSV file not found: {csvFilePath}");

        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HeaderValidated   = null,   // Abaikan validasi header
            MissingFieldFound = null,   // Abaikan kolom yang tidak ada
            // Normalisasi header: hapus underscore, lowercase → cocok PascalCase property
            PrepareHeaderForMatch = args => args.Header.Replace("_", "").ToLower(),
        };

        using var reader = new StreamReader(csvFilePath, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        using var csv    = new CsvReader(reader, config);

        // Register a flexible ClassMap that accepts BOTH English and
        // Indonesian header aliases, plus a custom decimal converter that
        // tolerates "Rp ", thousand dots, and trailing decimals.
        csv.Context.RegisterClassMap<ProductCsvRecordMap>();

        var records = csv.GetRecords<ProductCsvRecord>().ToList();
        return await UpsertRecordsAsync(records);
    }

    /// <summary>
    /// Upsert a collection of pre-built records.
    /// Used by Excel import (ClosedXML) so DB logic stays in one place.
    /// </summary>
    public Task<int> SeedFromRecordsAsync(IEnumerable<ProductCsvRecord> records)
        => UpsertRecordsAsync(records);

    // ── Core upsert engine ────────────────────────────────────────────────
    // Saves in batches of 100. Skips individual records that violate
    // constraints and collects their errors, then throws a summary at the end
    // so the caller can decide how to surface partial failures.
    private async Task<int> UpsertRecordsAsync(IEnumerable<ProductCsvRecord> input)
    {
        const int BatchSize = 100;
        int importedCount   = 0;
        var errors          = new List<string>();

        var list = input.Where(r => !string.IsNullOrWhiteSpace(r.ReferenceCode)).ToList();

        for (int batchStart = 0; batchStart < list.Count; batchStart += BatchSize)
        {
            var batch = list.Skip(batchStart).Take(BatchSize).ToList();

            // ── Truncate fields to DB column limits ───────────────────────
            foreach (var record in batch)
            {
                if (record.ReferenceCode.Length > 50)
                    record.ReferenceCode = record.ReferenceCode[..50];
                if (record.Category.Length > 50)
                    record.Category = record.Category[..50];
                if (record.ProductName.Length > 500)
                    record.ProductName = record.ProductName[..500];
            }

            // ── Queue upserts ─────────────────────────────────────────────
            // Match by (ReferenceCode, Vendor) so the same code from two
            // different vendors does NOT overwrite each other. Vendor null
            // is treated as a distinct slot from any non-null vendor.
            foreach (var record in batch)
            {
                var existing = FindExistingProduct(record);

                if (existing != null)
                {
                    existing.Category       = record.Category;
                    existing.ProductName    = record.ProductName;
                    existing.Specifications = record.Specifications;
                    existing.Price          = record.Price;
                    existing.PriceYear      = record.PriceYear;
                    existing.StockStatus    = record.StockStatus;
                    existing.Vendor         = record.Vendor;
                    existing.LastUpdated    = DateTime.UtcNow;
                    _context.Products.Update(existing);
                }
                else
                {
                    _context.Products.Add(new Product
                    {
                        Category       = record.Category,
                        ReferenceCode  = record.ReferenceCode,
                        ProductName    = record.ProductName,
                        Specifications = record.Specifications,
                        Price          = record.Price,
                        PriceYear      = record.PriceYear,
                        StockStatus    = record.StockStatus,
                        Vendor         = record.Vendor,
                        LastUpdated    = DateTime.UtcNow
                    });
                }
            }

            // ── Save batch — on failure retry one-by-one ──────────────────
            try
            {
                await _context.SaveChangesAsync();
                importedCount += batch.Count;
            }
            catch
            {
                // Batch failed — detach everything and retry one by one
                _context.ChangeTracker.Clear();

                foreach (var record in batch)
                {
                    try
                    {
                        var existing2 = FindExistingProduct(record);

                        if (existing2 != null)
                        {
                            existing2.Category       = record.Category;
                            existing2.ProductName    = record.ProductName;
                            existing2.Specifications = record.Specifications;
                            existing2.Price          = record.Price;
                            existing2.PriceYear      = record.PriceYear;
                            existing2.StockStatus    = record.StockStatus;
                            existing2.Vendor         = record.Vendor;
                            existing2.LastUpdated    = DateTime.UtcNow;
                            _context.Products.Update(existing2);
                        }
                        else
                        {
                            _context.Products.Add(new Product
                            {
                                Category       = record.Category,
                                ReferenceCode  = record.ReferenceCode,
                                ProductName    = record.ProductName,
                                Specifications = record.Specifications,
                                Price          = record.Price,
                                PriceYear      = record.PriceYear,
                                StockStatus    = record.StockStatus,
                                Vendor         = record.Vendor,
                                LastUpdated    = DateTime.UtcNow
                            });
                        }

                        await _context.SaveChangesAsync();
                        importedCount++;
                    }
                    catch (Exception exRow)
                    {
                        _context.ChangeTracker.Clear();
                        // Collect inner-most exception message for reporting
                        var inner = exRow;
                        while (inner.InnerException != null) inner = inner.InnerException;
                        errors.Add($"[{record.ReferenceCode}] {inner.Message}");
                    }
                }
            }
        }

        // If any rows failed, throw a summary so the UI can display details
        if (errors.Count > 0)
            throw new InvalidOperationException(
                $"{importedCount} baris berhasil, {errors.Count} baris gagal:\n" +
                string.Join("\n", errors.Take(10)) +
                (errors.Count > 10 ? $"\n... dan {errors.Count - 10} lainnya." : ""));

        return importedCount;
    }

    /// <summary>
    /// Lookup existing product using composite identity (ReferenceCode + Vendor).
    /// This prevents cross-vendor overwrites when two vendors share the same
    /// reference code (e.g. Schneider "C60N" vs Himel "C60N").
    /// Note: the legacy unique-index on ReferenceCode alone may still block
    /// inserts when a code already exists under a different vendor — those
    /// rows will surface as per-row errors during the retry phase.
    /// </summary>
    private Product? FindExistingProduct(ProductCsvRecord record)
    {
        var refCode = record.ReferenceCode;
        var vendor  = string.IsNullOrWhiteSpace(record.Vendor) ? null : record.Vendor;

        if (vendor == null)
        {
            // Records without a vendor match only against rows that also have
            // a null/empty vendor — never overwrite a vendor-tagged product.
            return _context.Products.FirstOrDefault(p =>
                p.ReferenceCode == refCode &&
                (p.Vendor == null || p.Vendor == ""));
        }
        return _context.Products.FirstOrDefault(p =>
            p.ReferenceCode == refCode && p.Vendor == vendor);
    }

    public class ProductCsvRecord
    {
        public string Category { get; set; } = string.Empty;
        public string ReferenceCode { get; set; } = string.Empty;
        public string ProductName { get; set; } = string.Empty;
        public string? Specifications { get; set; }
        public decimal Price { get; set; }
        public int? PriceYear { get; set; }
        // Default 1 = "Stock" (valid). Old default of 0 wrote an invalid value
        // when the CSV had no stock_status column.
        public int StockStatus { get; set; } = 1;
        public string? Vendor { get; set; }
    }

    /// <summary>
    /// CsvHelper ClassMap that accepts BOTH English (snake_case / PascalCase)
    /// and Indonesian column headers. Combined with the
    /// PrepareHeaderForMatch normalisation (lowercase, drop underscores)
    /// applied at the CsvConfiguration level, this means headers like
    /// "Kode", "Reference Code", "ReferenceCode", "reference_code", and "REF"
    /// all map to ReferenceCode.
    /// </summary>
    public sealed class ProductCsvRecordMap : ClassMap<ProductCsvRecord>
    {
        public ProductCsvRecordMap()
        {
            Map(m => m.Category).Name(
                "category", "kategori", "cat").Optional();
            Map(m => m.ReferenceCode).Name(
                "referencecode", "kode", "koderef", "koderfrnsi", "koderefrnsi", "referensi",
                "ref", "code", "itemcode");
            Map(m => m.ProductName).Name(
                "productname", "namaproduk", "nama", "deskripsi", "description", "product");
            Map(m => m.Specifications).Name(
                "specifications", "spesifikasi", "spec", "specs", "keterangan").Optional();
            Map(m => m.Price).Name(
                "price", "harga", "hargajual", "hargasatuan", "unitprice", "hargarp")
                .TypeConverter<IndonesianDecimalConverter>();
            Map(m => m.PriceYear).Name(
                "priceyear", "tahunharga", "tahun", "year", "thn").Optional();
            Map(m => m.StockStatus).Name(
                "stockstatus", "stok", "stock", "statusstok", "ss")
                .TypeConverter<StockStatusConverter>().Optional();
            Map(m => m.Vendor).Name(
                "vendor", "merk", "merek", "brand", "supplier").Optional();
        }
    }

    /// <summary>
    /// Decimal converter tolerant of Indonesian price formats:
    ///   "1234567", "1.234.567", "Rp 1.234.567", "1,234,567.00", "Rp1234567,50"
    /// Strategy: strip currency tokens and whitespace, identify the rightmost
    /// "." or "," as the decimal point only when followed by exactly 1-2 digits,
    /// otherwise treat dots/commas as thousand separators.
    /// </summary>
    public sealed class IndonesianDecimalConverter : DefaultTypeConverter
    {
        public override object? ConvertFromString(string? text, IReaderRow row, MemberMapData memberMapData)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0m;
            return ParsePrice(text);
        }

        public static decimal ParsePrice(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return 0m;

            // Strip currency tokens, NBSP, regular spaces.
            var s = raw.Trim()
                       .Replace("Rp.", "", StringComparison.OrdinalIgnoreCase)
                       .Replace("Rp",  "", StringComparison.OrdinalIgnoreCase)
                       .Replace("IDR", "", StringComparison.OrdinalIgnoreCase)
                       .Replace(" ", "")  // NBSP
                       .Replace(" ",  "")
                       .Trim();

            if (s.Length == 0) return 0m;

            // Detect decimal separator: rightmost '.' or ',' followed by 1-2 digits
            // and no further separator afterward.
            int lastDot   = s.LastIndexOf('.');
            int lastComma = s.LastIndexOf(',');
            int decIdx    = -1;

            if (lastDot > lastComma && IsDecimalCandidate(s, lastDot))
                decIdx = lastDot;
            else if (lastComma > lastDot && IsDecimalCandidate(s, lastComma))
                decIdx = lastComma;

            string intPart, fracPart;
            if (decIdx >= 0)
            {
                intPart  = s.Substring(0, decIdx);
                fracPart = s.Substring(decIdx + 1);
            }
            else
            {
                intPart  = s;
                fracPart = "";
            }

            // Strip all remaining dots/commas (thousand separators) from int part.
            intPart = new string(intPart.Where(c => c == '-' || char.IsDigit(c)).ToArray());
            fracPart = new string(fracPart.Where(char.IsDigit).ToArray());

            if (intPart.Length == 0 || intPart == "-") return 0m;

            string normalised = fracPart.Length > 0 ? $"{intPart}.{fracPart}" : intPart;
            if (decimal.TryParse(normalised, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
                                 CultureInfo.InvariantCulture, out var d))
                return d;
            return 0m;
        }

        private static bool IsDecimalCandidate(string s, int idx)
        {
            int tail = s.Length - idx - 1;
            if (tail < 1 || tail > 2) return false;
            for (int i = idx + 1; i < s.Length; i++)
                if (!char.IsDigit(s[i])) return false;
            return true;
        }
    }

    /// <summary>
    /// Converts stock status strings into the internal int code
    /// (1 = Stock, 2 = Indent). Accepts "1"/"2" raw values and common
    /// label aliases ("Stock", "Stok", "Ready", "Indent", "Inden", "Order").
    /// </summary>
    public sealed class StockStatusConverter : DefaultTypeConverter
    {
        public override object? ConvertFromString(string? text, IReaderRow row, MemberMapData memberMapData)
        {
            if (string.IsNullOrWhiteSpace(text)) return 1;
            var t = text.Trim();
            if (int.TryParse(t, out var n) && (n == 1 || n == 2)) return n;
            var lower = t.ToLowerInvariant();
            if (lower.Contains("indent") || lower.Contains("inden") || lower.Contains("order"))
                return 2;
            // Default Stock for anything else (Ready/Stok/Stock).
            return 1;
        }
    }
}
