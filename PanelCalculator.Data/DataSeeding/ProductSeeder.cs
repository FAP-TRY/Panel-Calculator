using CsvHelper;
using CsvHelper.Configuration;
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
            foreach (var record in batch)
            {
                var existing = _context.Products
                    .FirstOrDefault(p => p.ReferenceCode == record.ReferenceCode);

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
                        var existing2 = _context.Products
                            .FirstOrDefault(p => p.ReferenceCode == record.ReferenceCode);

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

    public class ProductCsvRecord
    {
        public string Category { get; set; } = string.Empty;
        public string ReferenceCode { get; set; } = string.Empty;
        public string ProductName { get; set; } = string.Empty;
        public string? Specifications { get; set; }
        public decimal Price { get; set; }
        public int? PriceYear { get; set; }
        public int StockStatus { get; set; }
        public string? Vendor { get; set; }
    }
}
