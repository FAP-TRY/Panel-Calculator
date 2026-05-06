using System.Reflection;

namespace PanelCalculator.Core.Models;

// EF Core maps columns by property name via reflection.
// [Obfuscation] tells Obfuscar to leave property names intact.
[Obfuscation(Exclude = false, ApplyToMembers = true, Feature = "renaming")]
public class Product
{
    public int ProductId { get; set; }

    public required string Category { get; set; } // 'MCB', 'Box', 'RCCB', 'Busbar', etc

    public required string ReferenceCode { get; set; } // DOMF01102, A9K14106, etc

    public required string ProductName { get; set; }

    public string? Specifications { get; set; } // 25A, 1Kutub, 3Kutub, etc

    public decimal Price { get; set; } // Harga dari vendor (Rp)

    public int? PriceYear { get; set; } // Tahun harga daftar (e.g. 2024)

    public int StockStatus { get; set; } // 1=Stock, 2=Indent

    public string? Vendor { get; set; } // Optional: Schneider, etc

    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;

    // Navigation property
    public ICollection<EstimationDetail> EstimationDetails { get; set; } = new List<EstimationDetail>();
}
