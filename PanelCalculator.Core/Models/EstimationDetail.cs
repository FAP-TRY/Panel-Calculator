namespace PanelCalculator.Core.Models;

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

    /// <summary>Material Utama | Material Pendukung | Material Lainnya</summary>
    public string Section { get; set; } = "Material Utama";

    // Navigation properties
    public Estimation Estimation { get; set; } = null!;

    public Product Product { get; set; } = null!;
}
