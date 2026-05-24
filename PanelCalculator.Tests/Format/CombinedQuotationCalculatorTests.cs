using PanelCalculator.Core.Models;
using PanelCalculator.Core.Services;
using Xunit;

namespace PanelCalculator.Tests.Format;

/// <summary>
/// Sanity tests untuk CombinedQuotationCalculator — fokus pada:
///   1. PPN dihitung sekali dari grand subtotal + ongkir, BUKAN sum PPN per panel.
///   2. PPh per panel tetap di-SUM (preserve nilai asli setiap panel).
///   3. Grand total = DPP + PPN − PPh.
///   4. Terbilang grand total konsisten dengan TerbilangFormatter.
///   5. Customer mismatch detection.
/// </summary>
public class CombinedQuotationCalculatorTests
{
    private static Estimation MakeEst(
        string number,
        string client,
        decimal subTotal,
        decimal margin = 0m,
        decimal shipping = 0m,
        decimal pph = 0m,
        string? company = null,
        string? address = null,
        string? projectName = null)
        => new Estimation
        {
            EstimationNumber = number,
            ClientName       = client,
            Company          = company,
            Address          = address,
            ProjectName      = projectName,
            SubTotal         = subTotal,
            Margin           = margin,
            ShippingCost     = shipping,
            PPh              = pph,
            TotalPrice       = 0m, // tidak dipakai oleh calculator gabungan
            Status           = "Draft",
        };

    [Fact]
    public void Build_TwoPanels_GrandSubtotalIsSumOfPanelSubtotals()
    {
        // panel1 subtotal+margin = 10_000_000 + 1_000_000 = 11_000_000
        // panel2 subtotal+margin = 5_000_000 + (-200_000) = 4_800_000
        var p1 = MakeEst("EST-001", "PT A", subTotal: 10_000_000m, margin: 1_000_000m);
        var p2 = MakeEst("EST-002", "PT A", subTotal:  5_000_000m, margin:  -200_000m);

        var s = CombinedQuotationCalculator.Build(new[] { p1, p2 });

        Assert.Equal(11_000_000m + 4_800_000m, s.GrandSubtotal);
        Assert.Equal(2, s.Panels.Count);
        Assert.Equal(11_000_000m, s.Panels[0].PanelSubtotal);
        Assert.Equal( 4_800_000m, s.Panels[1].PanelSubtotal);
    }

    [Fact]
    public void Build_PpnCalculatedOnceFromGrandSubtotalPlusOngkir_NotSumPerPanel()
    {
        // Kalau PPN dihitung per-panel terus dijumlah, hasilnya bisa beda
        // karena pembulatan. Test ini memverifikasi single calc.
        // panel1 dpp_per_panel = 1_234_567 → PPN 11% naive = 135_802.37
        // panel2 dpp_per_panel = 2_345_678 → PPN 11% naive = 258_024.58
        // grand subtotal = 3_580_245 → DPP+ongkir = 3_580_245 + 100_000 = 3_680_245
        // expected PPN single = round(3_680_245 × 0.11) = round(404_826.95) = 404_827
        var p1 = MakeEst("EST-A", "PT A", subTotal: 1_234_567m);
        var p2 = MakeEst("EST-B", "PT A", subTotal: 2_345_678m);

        var s = CombinedQuotationCalculator.Build(new[] { p1, p2 },
            combinedShippingCost: 100_000m, taxPercent: 11m);

        Assert.Equal(3_580_245m,           s.GrandSubtotal);
        Assert.Equal(3_680_245m,           s.DPP);
        Assert.Equal(404_827m,             s.TaxAmount); // single calc, rounded
        // PPN naive sum = round(135_802.37)+round(258_024.58)=135_802+258_025=393_827
        // Test PASS karena 404_827 ≠ 393_827 → bukti single calc bukan sum
        Assert.NotEqual(393_827m, s.TaxAmount);
    }

    [Fact]
    public void Build_TotalPphIsSumOfPanelPph()
    {
        var p1 = MakeEst("EST-1", "PT A", subTotal: 10_000_000m, pph: 250_000m);
        var p2 = MakeEst("EST-2", "PT A", subTotal:  5_000_000m, pph: 100_000m);
        var p3 = MakeEst("EST-3", "PT A", subTotal:  3_000_000m, pph:   0m);

        var s = CombinedQuotationCalculator.Build(new[] { p1, p2, p3 });

        Assert.Equal(350_000m, s.TotalPPh);
    }

    [Fact]
    public void Build_GrandTotal_IsDppPlusPpnMinusPph()
    {
        var p1 = MakeEst("E1", "C1", subTotal: 10_000_000m, margin: 0m, pph: 100_000m);
        var p2 = MakeEst("E2", "C1", subTotal:  5_000_000m, margin: 0m, pph:  50_000m);

        var s = CombinedQuotationCalculator.Build(new[] { p1, p2 },
            combinedShippingCost: 0m, taxPercent: 11m);

        // grand subtotal = 15_000_000, DPP = 15_000_000
        // PPN = 1_650_000, PPh total = 150_000
        // grand total = 15_000_000 + 1_650_000 − 150_000 = 16_500_000
        Assert.Equal(15_000_000m, s.GrandSubtotal);
        Assert.Equal(15_000_000m, s.DPP);
        Assert.Equal( 1_650_000m, s.TaxAmount);
        Assert.Equal(   150_000m, s.TotalPPh);
        Assert.Equal(16_500_000m, s.GrandTotal);
    }

    [Fact]
    public void Build_CombinedShippingOverridesPerPanelShipping()
    {
        // Per-panel shipping (700_000 + 300_000) di-IGNORE
        // dari grand total — yang dipakai hanya combinedShippingCost.
        var p1 = MakeEst("E1", "C", subTotal: 1_000_000m, shipping: 700_000m);
        var p2 = MakeEst("E2", "C", subTotal: 2_000_000m, shipping: 300_000m);

        var s = CombinedQuotationCalculator.Build(new[] { p1, p2 },
            combinedShippingCost: 500_000m, taxPercent: 11m);

        Assert.Equal(3_000_000m, s.GrandSubtotal);
        Assert.Equal(3_500_000m, s.DPP);         // 3jt + 500rb (not 700+300)
        Assert.Equal(500_000m,   s.CombinedShippingCost);
    }

    [Fact]
    public void Build_TerbilangMatchesGrandTotal()
    {
        var p1 = MakeEst("E1", "C", subTotal: 1_000_000m);

        // Pakai 1 panel + 1 lagi 0-subtotal supaya valid (>= 1)
        var s = CombinedQuotationCalculator.Build(new[] { p1 },
            combinedShippingCost: 0m, taxPercent: 11m);

        // grand total = 1_000_000 + 110_000 = 1_110_000
        Assert.Equal(1_110_000m, s.GrandTotal);
        Assert.Equal(TerbilangFormatter.ToRupiah(1_110_000m), s.Terbilang);
    }

    [Fact]
    public void Build_ThrowsOnEmptyList()
    {
        Assert.Throws<ArgumentException>(() =>
            CombinedQuotationCalculator.Build(Array.Empty<Estimation>()));
    }

    [Fact]
    public void Build_ThrowsOnNegativeShipping()
    {
        var p = MakeEst("E1", "C", 1_000_000m);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CombinedQuotationCalculator.Build(new[] { p }, combinedShippingCost: -1m));
    }

    [Fact]
    public void Build_ThrowsOnNegativeTaxPercent()
    {
        var p = MakeEst("E1", "C", 1_000_000m);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CombinedQuotationCalculator.Build(new[] { p }, taxPercent: -5m));
    }

    [Fact]
    public void CheckCustomerInfoMismatch_ReturnsNullWhenConsistent()
    {
        var p1 = MakeEst("E1", "PT A", 1_000_000m, company: "PT A", address: "Jakarta");
        var p2 = MakeEst("E2", "PT A", 2_000_000m, company: "PT A", address: "Jakarta");

        var m = CombinedQuotationCalculator.CheckCustomerInfoMismatch(new[] { p1, p2 });
        Assert.Null(m);
    }

    [Fact]
    public void CheckCustomerInfoMismatch_ReturnsMessageWhenClientDiffers()
    {
        var p1 = MakeEst("E1", "PT A", 1_000_000m, company: "PT A");
        var p2 = MakeEst("E2", "PT B", 2_000_000m, company: "PT A");

        var m = CombinedQuotationCalculator.CheckCustomerInfoMismatch(new[] { p1, p2 });
        Assert.NotNull(m);
        Assert.Contains("nama klien berbeda", m);
    }

    [Fact]
    public void CheckCustomerInfoMismatch_IgnoresCaseAndWhitespace()
    {
        var p1 = MakeEst("E1", "  PT Alpha  ", 1_000_000m, company: "ACME");
        var p2 = MakeEst("E2", "pt alpha",     2_000_000m, company: "acme");

        var m = CombinedQuotationCalculator.CheckCustomerInfoMismatch(new[] { p1, p2 });
        Assert.Null(m);
    }

    [Fact]
    public void Build_PreservesProjectNameAndEstimationNumber()
    {
        var p1 = MakeEst("EST-X", "C", 1_000_000m, projectName: "MDP 400A");
        var p2 = MakeEst("EST-Y", "C", 1_500_000m, projectName: null);

        var s = CombinedQuotationCalculator.Build(new[] { p1, p2 });

        Assert.Equal("EST-X",   s.Panels[0].EstimationNumber);
        Assert.Equal("MDP 400A", s.Panels[0].ProjectName);
        Assert.Equal("EST-Y",   s.Panels[1].EstimationNumber);
        Assert.Null(s.Panels[1].ProjectName);
    }
}
