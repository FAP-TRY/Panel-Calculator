namespace PanelCalculator.Core.Models;

public class Estimation
{
    public int EstimationId { get; set; }

    public required string EstimationNumber { get; set; } // EST-20260504-001

    public required string ClientName { get; set; }

    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;

    public string? Notes { get; set; }

    public decimal SubTotal { get; set; }

    public decimal ShippingCost { get; set; }

    public decimal Margin { get; set; } // % atau Rp

    public decimal Tax { get; set; } // PPN 11%

    public decimal TotalPrice { get; set; }

    public required string Status { get; set; } // Draft, Approved, Sent, Won, Lost

    // Navigation property
    public ICollection<EstimationDetail> Details { get; set; } = new List<EstimationDetail>();
}
