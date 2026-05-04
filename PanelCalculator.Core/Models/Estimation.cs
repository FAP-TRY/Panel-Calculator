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

    public decimal MarginPercent { get; set; } = 0m; // overall margin/discount percent (negative = diskon)

    public decimal Margin { get; set; } // nilai margin/diskon

    public decimal Tax { get; set; } // PPN

    public decimal PPhPercent { get; set; } = 0m;

    public decimal PPh { get; set; } = 0m; // nilai PPh ditahan

    public decimal TotalPrice { get; set; }

    public required string Status { get; set; } // Draft, Approved, Sent, Won, Lost

    // Navigation property
    public ICollection<EstimationDetail> Details { get; set; } = new List<EstimationDetail>();
}
