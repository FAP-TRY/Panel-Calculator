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

    public async Task SeedFromCsvAsync(string csvFilePath)
    {
        if (!File.Exists(csvFilePath))
        {
            throw new FileNotFoundException($"CSV file not found: {csvFilePath}");
        }

        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HeaderValidationMode = HeaderValidationMode.IgnoreCase
        };

        try
        {
            using (var reader = new StreamReader(csvFilePath))
            using (var csv = new CsvReader(reader, config))
            {
                var records = csv.GetRecords<ProductCsvRecord>();

                foreach (var record in records)
                {
                    // Check if product already exists
                    var existing = _context.Products
                        .FirstOrDefault(p => p.ReferenceCode == record.ReferenceCode);

                    if (existing != null)
                    {
                        // Update existing
                        existing.Category = record.Category;
                        existing.ProductName = record.ProductName;
                        existing.Specifications = record.Specifications;
                        existing.Price = record.Price;
                        existing.StockStatus = record.StockStatus;
                        existing.Vendor = record.Vendor;
                        existing.LastUpdated = DateTime.UtcNow;

                        _context.Products.Update(existing);
                    }
                    else
                    {
                        // Add new
                        var product = new Product
                        {
                            Category = record.Category,
                            ReferenceCode = record.ReferenceCode,
                            ProductName = record.ProductName,
                            Specifications = record.Specifications,
                            Price = record.Price,
                            StockStatus = record.StockStatus,
                            Vendor = record.Vendor,
                            LastUpdated = DateTime.UtcNow
                        };

                        _context.Products.Add(product);
                    }
                }

                await _context.SaveChangesAsync();
                Console.WriteLine($"✓ Successfully seeded {records.Count()} products");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ Error seeding products: {ex.Message}");
            throw;
        }
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
