namespace PanelCalculator.Core.Models;

public class EstimationDetail
{
    public int DetailId { get; set; }

    public int EstimationId { get; set; }

    public int ProductId { get; set; }

    public int Quantity { get; set; }

    public decimal UnitPrice { get; set; }

    public decimal LineTotalPrice { get; set; }

    // Navigation properties
    public required Estimation Estimation { get; set; }

    public required Product Product { get; set; }
}
