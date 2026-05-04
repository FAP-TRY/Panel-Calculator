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

        int importedCount = 0;

        using var reader = new StreamReader(csvFilePath, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        using var csv = new CsvReader(reader, config);

        var records = csv.GetRecords<ProductCsvRecord>().ToList();

        foreach (var record in records)
        {
            if (string.IsNullOrWhiteSpace(record.ReferenceCode)) continue;

            var existing = _context.Products
                .FirstOrDefault(p => p.ReferenceCode == record.ReferenceCode);

            if (existing != null)
            {
                existing.Category      = record.Category;
                existing.ProductName   = record.ProductName;
                existing.Specifications = record.Specifications;
                existing.Price         = record.Price;
                existing.StockStatus   = record.StockStatus;
                existing.Vendor        = record.Vendor;
                existing.LastUpdated   = DateTime.UtcNow;
                _context.Products.Update(existing);
            }
            else
            {
                _context.Products.Add(new Product
                {
                    Category      = record.Category,
                    ReferenceCode = record.ReferenceCode,
                    ProductName   = record.ProductName,
                    Specifications = record.Specifications,
                    Price         = record.Price,
                    StockStatus   = record.StockStatus,
                    Vendor        = record.Vendor,
                    LastUpdated   = DateTime.UtcNow
                });
            }

            importedCount++;
        }

        await _context.SaveChangesAsync();
        return importedCount;
    }

    public class ProductCsvRecord
    {
        public string Category { get; set; } = string.Empty;
        public string ReferenceCode { get; set; } = string.Empty;
        public string ProductName { get; set; } = string.Empty;
        public string? Specifications { get; set; }
        public decimal Price { get; set; }
        public int StockStatus { get; set; }
        public string? Vendor { get; set; }
    }
}
