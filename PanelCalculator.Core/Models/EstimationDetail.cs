using System.Reflection;

namespace PanelCalculator.Core.Models;

[Obfuscation(Exclude = false, ApplyToMembers = true, Feature = "renaming")]
public class EstimationDetail
{
    public int DetailId { get; set; }

    public int EstimationId { get; set; }

    public int ProductId { get; set; }

    public int Quantity { get; set; }

    public decimal UnitPrice { get; set; }

    public decimal LineTotalPrice { get; set; }

    /// <summary>Per-item adjustment: positive = markup %, negative = discount %</summary>
    public decimal AdjPercent { get; set; } = 0m;

    /// <summary>Unit of measurement, e.g. pcs, set, meter, rol</summary>
    public string Satuan { get; set; } = "pcs";

    /// <summary>Material Utama | Material Pendukung | Material Lainnya</summary>
    public string Section { get; set; } = "Material Utama";

    // Navigation properties
    public Estimation Estimation { get; set; } = null!;

    public Product Product { get; set; } = null!;
}
