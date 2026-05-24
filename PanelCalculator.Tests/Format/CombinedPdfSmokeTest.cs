using PanelCalculator.Core.Models;
using PanelCalculator.Core.Services;
using Xunit;

namespace PanelCalculator.Tests.Format;

/// <summary>
/// Smoke test: pastikan Build() menghasilkan summary yang konsisten
/// dipanggil oleh kedua PDF exporter (Formal & Modern). Tidak menulis
/// file PDF (project Tests tidak reference WinForms/iText7 untuk
/// menjaga test ringan), tapi memverifikasi struktur data yang dipakai
/// PDF writer.
/// </summary>
public class CombinedPdfSmokeTest
{
    [Fact]
    public void EndToEnd_TwoPanelScenario_ProducesValidSummary()
    {
        // Scenario realistis: 2 panel untuk 1 customer
        var panel1 = new Estimation
        {
            EstimationNumber = "EST-20260520-001",
            ClientName       = "PT Maju Jaya",
            Company          = "PT Maju Jaya Sentosa",
            Address          = "Jl. Sudirman No. 1, Jakarta",
            ProjectName      = "Panel MDP 400A",
            SubTotal         = 25_000_000m,
            Margin           =  2_500_000m,
            ShippingCost     =    500_000m, // diabaikan dalam gabungan
            PPh              =    250_000m,
            TotalPrice       = 30_775_000m,
            Status           = "Approved",
        };
        var panel2 = new Estimation
        {
            EstimationNumber = "EST-20260521-002",
            ClientName       = "PT Maju Jaya",
            Company          = "PT Maju Jaya Sentosa",
            Address          = "Jl. Sudirman No. 1, Jakarta",
            ProjectName      = "Panel SDP Lantai 2",
            SubTotal         = 12_000_000m,
            Margin           =  1_200_000m,
            ShippingCost     =    300_000m, // diabaikan dalam gabungan
            PPh              =    120_000m,
            TotalPrice       = 14_762_000m,
            Status           = "Approved",
        };

        // User input di dialog: ongkir gabungan 750_000
        var summary = CombinedQuotationCalculator.Build(
            new[] { panel1, panel2 },
            combinedShippingCost: 750_000m,
            taxPercent: 11m);

        // Verifikasi end-to-end
        Assert.Equal(2, summary.Panels.Count);

        // Panel 1: 25jt + 2.5jt = 27.5jt
        Assert.Equal(27_500_000m, summary.Panels[0].PanelSubtotal);
        Assert.Equal("Panel MDP 400A", summary.Panels[0].ProjectName);

        // Panel 2: 12jt + 1.2jt = 13.2jt
        Assert.Equal(13_200_000m, summary.Panels[1].PanelSubtotal);

        // Grand subtotal = 40.7jt
        Assert.Equal(40_700_000m, summary.GrandSubtotal);

        // DPP = 40.7jt + 750rb = 41.45jt
        Assert.Equal(41_450_000m, summary.DPP);

        // PPN 11% × 41.45jt = 4_559_500
        Assert.Equal(4_559_500m, summary.TaxAmount);

        // Total PPh = 250rb + 120rb = 370rb
        Assert.Equal(370_000m, summary.TotalPPh);

        // Grand total = 41.45jt + 4.5595jt − 370rb = 45_639_500
        Assert.Equal(45_639_500m, summary.GrandTotal);

        // Customer info konsisten
        Assert.Null(CombinedQuotationCalculator.CheckCustomerInfoMismatch(new[] { panel1, panel2 }));

        // Terbilang non-empty dan mengandung "rupiah"
        Assert.NotEmpty(summary.Terbilang);
        Assert.EndsWith("rupiah", summary.Terbilang);
    }

    [Fact]
    public void CustomerMismatch_Warning_StillProducesValidSummary()
    {
        // Kalau customer info beda, summary tetap di-generate (UI hanya warn user)
        var panel1 = new Estimation
        {
            EstimationNumber = "EST-A", ClientName = "PT Alpha", Status = "Draft",
            SubTotal = 5_000_000m, TotalPrice = 0m,
        };
        var panel2 = new Estimation
        {
            EstimationNumber = "EST-B", ClientName = "PT Beta", Status = "Draft",
            SubTotal = 3_000_000m, TotalPrice = 0m,
        };

        var summary = CombinedQuotationCalculator.Build(new[] { panel1, panel2 });
        Assert.Equal(8_000_000m, summary.GrandSubtotal);

        var mismatch = CombinedQuotationCalculator.CheckCustomerInfoMismatch(new[] { panel1, panel2 });
        Assert.NotNull(mismatch);
    }
}
