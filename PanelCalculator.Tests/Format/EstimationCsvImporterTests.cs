using Microsoft.EntityFrameworkCore;
using PanelCalculator.Core.Models;
using PanelCalculator.Data;
using PanelCalculator.Data.DataSeeding;
using Xunit;

namespace PanelCalculator.Tests.Format;

/// <summary>
/// Round-trip tests: produce a CSV with the same writer logic that
/// EstimationHistoryForm uses, then feed it through EstimationCsvImporter
/// and verify the reconstructed Estimation matches the original.
/// </summary>
public class EstimationCsvImporterTests
{
    private static PanelCalculatorContext NewContext()
    {
        var opts = new DbContextOptionsBuilder<PanelCalculatorContext>()
            .UseInMemoryDatabase($"csv-import-{Guid.NewGuid():N}")
            .Options;
        return new PanelCalculatorContext(opts);
    }

    /// <summary>
    /// Produces a CSV in the EXACT format that EstimationHistoryForm.BtnExportCsv
    /// writes — 3 sections (meta / items / summary), English snake_case
    /// headers, plain numbers, ISO dates, UTF-8.
    /// Kept inline here (not shared via internalsVisibleTo) because the
    /// writer lives in WinForms project which is not referenced by tests.
    /// </summary>
    private static string BuildExportCsv(Estimation est, IEnumerable<(EstimationDetail d, Product p)> items)
    {
        string Esc(string? s)
        {
            s ??= "";
            if (s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0)
                return "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("section,key,value");
        sb.AppendLine($"meta,estimation_number,{Esc(est.EstimationNumber)}");
        sb.AppendLine($"meta,nomor_surat,{Esc(est.NomorSurat)}");
        sb.AppendLine($"meta,client_name,{Esc(est.ClientName)}");
        sb.AppendLine($"meta,company,{Esc(est.Company)}");
        sb.AppendLine($"meta,project_name,{Esc(est.ProjectName)}");
        sb.AppendLine($"meta,status,{Esc(est.Status)}");
        sb.AppendLine($"meta,created_date,{est.CreatedDate.ToLocalTime():yyyy-MM-dd}");
        sb.AppendLine($"meta,created_at,{est.CreatedDate.ToLocalTime():yyyy-MM-ddTHH:mm:ss}");
        sb.AppendLine();
        sb.AppendLine("no,section_name,reference_code,product_name,vendor,satuan,quantity,unit_price,line_total");
        int no = 0;
        foreach (var (d, p) in items)
        {
            no++;
            sb.AppendLine(
                $"{no}," +
                $"{Esc(d.Section)}," +
                $"{Esc(p.ReferenceCode)}," +
                $"{Esc(p.ProductName)}," +
                $"{Esc(p.Vendor)}," +
                $"{Esc(d.Satuan)}," +
                $"{d.Quantity}," +
                $"{d.UnitPrice.ToString("0.##", inv)}," +
                $"{d.LineTotalPrice.ToString("0.##", inv)}");
        }
        sb.AppendLine();
        sb.AppendLine("summary,key,amount");
        sb.AppendLine($"summary,subtotal,{est.SubTotal.ToString("0.##", inv)}");
        if (est.Margin != 0)
            sb.AppendLine($"summary,margin,{est.Margin.ToString("0.##", inv)}");
        if (est.ShippingCost > 0)
            sb.AppendLine($"summary,shipping,{est.ShippingCost.ToString("0.##", inv)}");
        if (est.Tax > 0)
            sb.AppendLine($"summary,ppn,{est.Tax.ToString("0.##", inv)}");
        if (est.PPh > 0)
            sb.AppendLine($"summary,pph,{est.PPh.ToString("0.##", inv)}");
        sb.AppendLine($"summary,grand_total,{est.TotalPrice.ToString("0.##", inv)}");
        return sb.ToString();
    }

    [Fact]
    public async Task RoundTrip_ExportThenImport_ProducesIdenticalEstimation()
    {
        using var ctx = NewContext();
        var p1 = new Product { Category = "MCB", ReferenceCode = "C60N", ProductName = "Schneider C60N 6A", Price = 250_000m, Vendor = "Schneider" };
        var p2 = new Product { Category = "MCB", ReferenceCode = "C60N", ProductName = "Himel C60N 6A",     Price =  80_000m, Vendor = "Himel"     };
        ctx.Products.AddRange(p1, p2);
        await ctx.SaveChangesAsync();

        var est = new Estimation
        {
            EstimationNumber = "EST-20260518-001",
            ClientName       = "PT ABC",
            Company          = "PT ABC Indonesia",
            ProjectName      = "Panel MDP 3-Phase",
            Status           = "Draft",
            CreatedDate      = new DateTime(2026, 5, 18, 10, 0, 0, DateTimeKind.Utc),
            SubTotal         = 580_000m,
            Margin           =  20_000m,
            ShippingCost     =  50_000m,
            Tax              =  71_500m,
            PPh              =       0m,
            TotalPrice       = 721_500m,
            Details = new List<EstimationDetail>
            {
                new() { ProductId = p1.ProductId, Quantity = 2, UnitPrice = 250_000m, LineTotalPrice = 500_000m,
                        Section = "Material Utama", Satuan = "pcs" },
                new() { ProductId = p2.ProductId, Quantity = 1, UnitPrice =  80_000m, LineTotalPrice =  80_000m,
                        Section = "Material Pendukung", Satuan = "pcs" },
            }
        };
        // Provide the navigation for the writer:
        var items = new List<(EstimationDetail, Product)>
        {
            (est.Details.First(), p1),
            (est.Details.Last(),  p2),
        };

        var csv = BuildExportCsv(est, items);

        // Now run the import against a fresh DB that already has the products
        using var ctx2 = NewContext();
        var np1 = new Product { Category = "MCB", ReferenceCode = "C60N", ProductName = "Schneider C60N 6A", Price = 250_000m, Vendor = "Schneider" };
        var np2 = new Product { Category = "MCB", ReferenceCode = "C60N", ProductName = "Himel C60N 6A",     Price =  80_000m, Vendor = "Himel"     };
        ctx2.Products.AddRange(np1, np2);
        await ctx2.SaveChangesAsync();

        var importer = new EstimationCsvImporter(ctx2);
        var report = await importer.ImportFromTextAsync(csv);

        Assert.True(report.Imported, "Expected Imported=true. Errors: " + string.Join("|", report.Errors));
        Assert.Equal(0, report.ItemsOrphaned);
        Assert.Equal(2, report.ItemsImported);
        Assert.Equal("EST-20260518-001", report.FinalEstimationNumber);

        var loaded = ctx2.Estimations.Include(e => e.Details).Single();
        Assert.Equal("PT ABC",                loaded.ClientName);
        Assert.Equal("PT ABC Indonesia",      loaded.Company);
        Assert.Equal("Panel MDP 3-Phase",     loaded.ProjectName);
        Assert.Equal("Draft",                 loaded.Status);
        Assert.Equal(580_000m,                loaded.SubTotal);
        Assert.Equal( 20_000m,                loaded.Margin);
        Assert.Equal( 50_000m,                loaded.ShippingCost);
        Assert.Equal( 71_500m,                loaded.Tax);
        Assert.Equal(721_500m,                loaded.TotalPrice);
        Assert.Equal(2,                       loaded.Details.Count);

        var d1 = loaded.Details.First(d => d.ProductId == np1.ProductId);
        Assert.Equal(2,           d1.Quantity);
        Assert.Equal(250_000m,    d1.UnitPrice);
        Assert.Equal(500_000m,    d1.LineTotalPrice);
        Assert.Equal("Material Utama",     d1.Section);
        Assert.Equal("pcs",                d1.Satuan);
    }

    [Fact]
    public async Task Import_OrphanItem_IsReportedAndSkipped()
    {
        using var ctx = NewContext();
        // Only one product seeded; the CSV references TWO refcodes.
        ctx.Products.Add(new Product { Category = "MCB", ReferenceCode = "C60N", ProductName = "Schneider C60N", Price = 250_000m, Vendor = "Schneider" });
        await ctx.SaveChangesAsync();

        var csv = @"section,key,value
meta,estimation_number,EST-X-001
meta,client_name,PT Test

no,section_name,reference_code,product_name,vendor,satuan,quantity,unit_price,line_total
1,Material Utama,C60N,Schneider C60N,Schneider,pcs,1,250000,250000
2,Material Utama,UNKNOWN999,Mystery Item,Acme,pcs,2,100000,200000

summary,key,amount
summary,subtotal,450000
summary,grand_total,450000
";
        var imp = new EstimationCsvImporter(ctx);
        var rep = await imp.ImportFromTextAsync(csv);

        Assert.True(rep.Imported);
        Assert.Equal(2, rep.ItemsParsed);
        Assert.Equal(1, rep.ItemsImported);
        Assert.Equal(1, rep.ItemsOrphaned);
        Assert.Single(rep.OrphanedItems);
        Assert.Contains("UNKNOWN999", rep.OrphanedItems[0]);
    }

    [Fact]
    public async Task Import_ConflictNumber_RespectsResolverChoiceSkip()
    {
        using var ctx = NewContext();
        ctx.Products.Add(new Product { Category = "MCB", ReferenceCode = "C60N", ProductName = "Schneider C60N", Price = 250_000m, Vendor = "Schneider" });
        ctx.Estimations.Add(new Estimation
        {
            EstimationNumber = "EST-DUP-001",
            ClientName       = "Existing",
            Status           = "Draft",
            CreatedDate      = DateTime.UtcNow,
            SubTotal         = 1m,
            TotalPrice       = 1m
        });
        await ctx.SaveChangesAsync();

        var csv = @"section,key,value
meta,estimation_number,EST-DUP-001
meta,client_name,Imported

no,section_name,reference_code,product_name,vendor,satuan,quantity,unit_price,line_total
1,Material Utama,C60N,Schneider C60N,Schneider,pcs,1,250000,250000

summary,key,amount
summary,subtotal,250000
summary,grand_total,250000
";
        var imp = new EstimationCsvImporter(ctx);
        var rep = await imp.ImportFromTextAsync(csv,
            _ => EstimationCsvImporter.ConflictResolution.Skip);

        Assert.False(rep.Imported);
        Assert.Single(rep.Warnings);
        Assert.Contains("Skip", rep.Warnings[0], StringComparison.OrdinalIgnoreCase);

        // Existing estimation unchanged
        var existing = ctx.Estimations.Single();
        Assert.Equal("Existing", existing.ClientName);
    }

    [Fact]
    public async Task Import_ConflictNumber_RenameProducesUniqueName()
    {
        using var ctx = NewContext();
        ctx.Products.Add(new Product { Category = "MCB", ReferenceCode = "C60N", ProductName = "Schneider C60N", Price = 250_000m, Vendor = "Schneider" });
        ctx.Estimations.Add(new Estimation
        {
            EstimationNumber = "EST-DUP-002",
            ClientName       = "Existing",
            Status           = "Draft",
            CreatedDate      = DateTime.UtcNow,
            SubTotal         = 1m,
            TotalPrice       = 1m
        });
        await ctx.SaveChangesAsync();

        var csv = @"section,key,value
meta,estimation_number,EST-DUP-002
meta,client_name,Imported

no,section_name,reference_code,product_name,vendor,satuan,quantity,unit_price,line_total
1,Material Utama,C60N,Schneider C60N,Schneider,pcs,1,250000,250000

summary,key,amount
summary,subtotal,250000
summary,grand_total,250000
";
        var imp = new EstimationCsvImporter(ctx);
        var rep = await imp.ImportFromTextAsync(csv,
            _ => EstimationCsvImporter.ConflictResolution.Rename);

        Assert.True(rep.Imported);
        Assert.NotEqual("EST-DUP-002", rep.FinalEstimationNumber);
        Assert.StartsWith("EST-DUP-002-IMPORT", rep.FinalEstimationNumber);
        Assert.Equal(2, ctx.Estimations.Count());
    }

    [Fact]
    public async Task Import_MissingRequiredField_ReturnsError()
    {
        using var ctx = NewContext();
        var csv = @"section,key,value
meta,client_name,No Number Here

no,section_name,reference_code,product_name,vendor,satuan,quantity,unit_price,line_total
1,Material Utama,C60N,X,Schneider,pcs,1,100,100

summary,key,amount
summary,subtotal,100
summary,grand_total,100
";
        var imp = new EstimationCsvImporter(ctx);
        var rep = await imp.ImportFromTextAsync(csv);

        Assert.False(rep.Imported);
        Assert.Contains(rep.Errors, e => e.Contains("estimation_number", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CsvRowParser_HandlesQuotedFieldsAndEmbeddedComma()
    {
        var row = EstimationCsvImporter.ParseCsvRow("1,\"A, B\",\"with \"\"quote\"\"\",last");
        Assert.Equal(4, row.Count);
        Assert.Equal("1",             row[0]);
        Assert.Equal("A, B",          row[1]);
        Assert.Equal("with \"quote\"",row[2]);
        Assert.Equal("last",          row[3]);
    }
}
