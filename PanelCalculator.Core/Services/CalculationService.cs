namespace PanelCalculator.Core.Services;

public interface ICalculationService
{
    decimal CalculateLineTotal(int quantity, decimal unitPrice);
    decimal CalculateSubTotal(IEnumerable<(int Quantity, decimal UnitPrice)> items);
    decimal ApplyMargin(decimal subtotal, decimal marginPercent);
    decimal CalculateTax(decimal subtotal, decimal taxPercent);
    decimal CalculateFinalPrice(decimal subtotal, decimal margin, decimal tax, decimal shippingCost);
    (decimal MarginAmount, decimal TotalBeforeTax) CalculateWithMargin(decimal subtotal, decimal marginPercent, decimal shippingCost);
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
    {
        return items.Sum(item => CalculateLineTotal(item.Quantity, item.UnitPrice));
    }

    public decimal ApplyMargin(decimal subtotal, decimal marginPercent)
    {
        // Allow negative marginPercent for discount scenarios
        return subtotal * (marginPercent / 100m);
    }

    public decimal CalculateTax(decimal subtotal, decimal taxPercent)
    {
        return subtotal * (taxPercent / 100m);
    }

    public decimal CalculateFinalPrice(decimal subtotal, decimal margin, decimal tax, decimal shippingCost)
    {
        // margin can be negative (discount), tax can include PPh deduction
        return subtotal + margin + tax + shippingCost;
    }

    public (decimal MarginAmount, decimal TotalBeforeTax) CalculateWithMargin(
        decimal subtotal,
        decimal marginPercent,
        decimal shippingCost)
    {
        var marginAmount = ApplyMargin(subtotal, marginPercent);
        var totalBeforeTax = subtotal + marginAmount + shippingCost;
        return (marginAmount, totalBeforeTax);
    }
}
