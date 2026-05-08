using System.Reflection;

namespace PanelCalculator.Core.Models;

[Obfuscation(Exclude = false, ApplyToMembers = true, Feature = "renaming")]
public class Estimation
{
    public int EstimationId { get; set; }

    public required string EstimationNumber { get; set; } // EST-20260504-001

    public required string ClientName { get; set; }

    public string? ContactPhone { get; set; }

    public string? Company { get; set; }

    public string? Address { get; set; }

    /// <summary>Nomor surat resmi yang diketik manual, e.g. "136.Rev1/PR.BDG/IV/2026"</summary>
    public string? NomorSurat { get; set; }

    /// <summary>Nama panel/produk yang dikerjakan, e.g. "Panel MDP 3-Phase 400A"</summary>
    public string? ProjectName { get; set; }

    /// <summary>Perkiraan tanggal pemesanan oleh klien</summary>
    public DateTime? EstimatedOrderDate { get; set; }

    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;

    public string? Notes { get; set; }

    public decimal SubTotal { get; set; }

    public decimal ShippingCost { get; set; }

    public decimal MarginPercent { get; set; } = 0m; // overall margin/discount percent (negative = diskon)

    public decimal Margin2Percent { get; set; } = 0m;
    public decimal Margin3Percent { get; set; } = 0m;

    public decimal Margin { get; set; } // nilai margin/diskon

    public decimal Tax { get; set; } // PPN

    public decimal PPhPercent { get; set; } = 0m;

    public decimal PPh { get; set; } = 0m; // nilai PPh ditahan

    public decimal TotalPrice { get; set; }

    public required string Status { get; set; } // Draft, Approved, Sent, Won, Lost

    // Navigation property
    public ICollection<EstimationDetail> Details { get; set; } = new List<EstimationDetail>();
}
