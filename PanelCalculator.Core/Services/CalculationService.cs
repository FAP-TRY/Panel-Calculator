namespace PanelCalculator.Core.Services;

public interface ICalculationService
{
    decimal CalculateLineTotal(int quantity, decimal unitPrice);
    decimal CalculateSubTotal(IEnumerable<(int Quantity, decimal UnitPrice)> items);
    decimal ApplyMargin(decimal subtotal, decimal marginPercent);
    decimal CalculateTax(decimal subtotal, decimal taxPercent);
    decimal CalculateFinalPrice(decimal subtotal, decimal margin, decimal tax, decimal shippingCost);
    (decimal MarginAmount, decimal TotalBeforeTax) CalculateWithMargin(decimal subtotal, decimal marginPercent, decimal shippingCost);

    /// <summary>
    /// 3-tier sequential margin. Each tier compounds on the previous result.
    /// e.g. +20% then -10% = ×1.2×0.9 = ×1.08, NOT ×1.10
    /// </summary>
    (decimal Tier1Amt, decimal Tier2Amt, decimal Tier3Amt, decimal TotalMarginAmt, decimal AfterMargin)
        ApplyMargin3Tier(decimal subtotal, decimal m1Pct, decimal m2Pct, decimal m3Pct);
}

public class CalculationService : ICalculationService
{
    public decimal CalculateLineTotal(int quantity, decimal unitPrice)
    {
        if (quantity < 0 || unitPrice < 0)
            throw new ArgumentException("Quantity and UnitPrice must be non-negative");
        return quantity * unitPrice;
    }

    public decimal CalculateSubTotal(IEnumerable<(int Quantity, decimal UnitPrice)> items)
        => items.Sum(item => CalculateLineTotal(item.Quantity, item.UnitPrice));

    public decimal ApplyMargin(decimal subtotal, decimal marginPercent)
        => subtotal * (marginPercent / 100m);

    public decimal CalculateTax(decimal subtotal, decimal taxPercent)
        => subtotal * (taxPercent / 100m);

    public decimal CalculateFinalPrice(decimal subtotal, decimal margin, decimal tax, decimal shippingCost)
        => subtotal + margin + tax + shippingCost;

    public (decimal MarginAmount, decimal TotalBeforeTax) CalculateWithMargin(
        decimal subtotal, decimal marginPercent, decimal shippingCost)
    {
        var marginAmount = ApplyMargin(subtotal, marginPercent);
        var totalBeforeTax = subtotal + marginAmount + shippingCost;
        return (marginAmount, totalBeforeTax);
    }

    public (decimal Tier1Amt, decimal Tier2Amt, decimal Tier3Amt, decimal TotalMarginAmt, decimal AfterMargin)
        ApplyMargin3Tier(decimal subtotal, decimal m1Pct, decimal m2Pct, decimal m3Pct)
    {
        var after1     = subtotal * (1m + m1Pct / 100m);
        var after2     = after1   * (1m + m2Pct / 100m);
        var after3     = after2   * (1m + m3Pct / 100m);
        var tier1Amt   = after1 - subtotal;
        var tier2Amt   = after2 - after1;
        var tier3Amt   = after3 - after2;
        var totalAmt   = after3 - subtotal;
        return (tier1Amt, tier2Amt, tier3Amt, totalAmt, after3);
    }
}
