using Microsoft.EntityFrameworkCore;
using PanelCalculator.Core.Models;
using PanelCalculator.Data;
using PanelCalculator.Data.DataSeeding;
using Xunit;

namespace PanelCalculator.Tests.Format;

/// <summary>
/// Tests for the composite (ReferenceCode, Vendor) upsert behaviour added in
/// the format-bug-fix pass. We rely on the EF Core InMemory provider here
/// because the production DB has a legacy UNIQUE index on ReferenceCode
/// alone — these tests validate the *lookup* logic, while the limitation
/// note in docs/format-implementation-log.md covers what happens against a
/// real SQLite DB with that legacy index in place.
/// </summary>
public class ProductSeederUpsertTests
{
    private static PanelCalculatorContext NewContext()
    {
        // Unique DB name per test so suites stay isolated.
        var opts = new DbContextOptionsBuilder<PanelCalculatorContext>()
            .UseInMemoryDatabase($"upsert-test-{Guid.NewGuid():N}")
            .Options;
        return new PanelCalculatorContext(opts);
    }

    [Fact]
    public async Task SameRefCodeDifferentVendors_BothCoexist()
    {
        using var ctx = NewContext();
        var seeder   = new ProductSeeder(ctx);

        var records = new List<ProductSeeder.ProductCsvRecord>
        {
            new() { Category = "MCB", ReferenceCode = "C60N", ProductName = "Schneider C60N 6A", Price = 100_000m, Vendor = "Schneider" },
            new() { Category = "MCB", ReferenceCode = "C60N", ProductName = "Himel C60N 6A",     Price =  80_000m, Vendor = "Himel"     },
        };
        var imported = await seeder.SeedFromRecordsAsync(records);

        Assert.Equal(2, imported);
        var stored = ctx.Products.OrderBy(p => p.Vendor).ToList();
        Assert.Equal(2, stored.Count);
        Assert.Equal("Himel",     stored[0].Vendor);
        Assert.Equal("Schneider", stored[1].Vendor);
        Assert.Equal(80_000m,     stored[0].Price);
        Assert.Equal(100_000m,    stored[1].Price);
    }

    [Fact]
    public async Task SameRefCodeAndVendor_UpdatesInPlace()
    {
        using var ctx = NewContext();
        var seeder   = new ProductSeeder(ctx);

        // Initial insert
        await seeder.SeedFromRecordsAsync(new List<ProductSeeder.ProductCsvRecord>
        {
            new() { Category = "MCB", ReferenceCode = "C60N", ProductName = "Schneider C60N 6A old", Price = 100_000m, Vendor = "Schneider" },
        });

        // Same key (C60N + Schneider) — should UPDATE not duplicate.
        await seeder.SeedFromRecordsAsync(new List<ProductSeeder.ProductCsvRecord>
        {
            new() { Category = "MCB", ReferenceCode = "C60N", ProductName = "Schneider C60N 6A new", Price = 125_000m, Vendor = "Schneider" },
        });

        var stored = ctx.Products.ToList();
        Assert.Single(stored);
        Assert.Equal("Schneider C60N 6A new", stored[0].ProductName);
        Assert.Equal(125_000m,                stored[0].Price);
    }

    [Fact]
    public async Task NullVendor_DoesNotOverwriteVendorTaggedProduct()
    {
        using var ctx = NewContext();
        var seeder   = new ProductSeeder(ctx);

        await seeder.SeedFromRecordsAsync(new List<ProductSeeder.ProductCsvRecord>
        {
            new() { Category = "MCB", ReferenceCode = "C60N", ProductName = "Schneider C60N", Price = 100_000m, Vendor = "Schneider" },
        });
        await seeder.SeedFromRecordsAsync(new List<ProductSeeder.ProductCsvRecord>
        {
            // No vendor — must be treated as separate product, not as Schneider update.
            new() { Category = "MCB", ReferenceCode = "C60N", ProductName = "Generic C60N", Price = 50_000m, Vendor = null },
        });

        var stored = ctx.Products.OrderBy(p => p.Vendor).ToList();
        Assert.Equal(2, stored.Count);
        // Schneider product preserved unchanged
        Assert.Contains(stored, p => p.Vendor == "Schneider" && p.Price == 100_000m);
        // Vendor-less generic added
        Assert.Contains(stored, p => string.IsNullOrEmpty(p.Vendor) && p.Price == 50_000m);
    }

    [Fact]
    public async Task EmptyReferenceCode_IsSkipped()
    {
        using var ctx = NewContext();
        var seeder   = new ProductSeeder(ctx);

        var imported = await seeder.SeedFromRecordsAsync(new List<ProductSeeder.ProductCsvRecord>
        {
            new() { Category = "MCB", ReferenceCode = "",  ProductName = "Skipped", Price = 1m, Vendor = "X" },
            new() { Category = "MCB", ReferenceCode = "A", ProductName = "OK",      Price = 1m, Vendor = "X" },
        });

        Assert.Equal(1, imported);
        Assert.Single(ctx.Products);
        Assert.Equal("A", ctx.Products.First().ReferenceCode);
    }
}
