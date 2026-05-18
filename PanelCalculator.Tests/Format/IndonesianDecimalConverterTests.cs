using PanelCalculator.Data.DataSeeding;
using Xunit;

namespace PanelCalculator.Tests.Format;

/// <summary>
/// Tolerance tests for the Indonesian-style price parser used by the CSV
/// importer. The parser must handle plain integers, Indonesian thousand
/// separators, Rupiah prefixes, and decimal fractions in either ID or
/// invariant convention.
/// </summary>
public class IndonesianDecimalConverterTests
{
    [Theory]
    [InlineData("1234567",        1234567)]
    [InlineData("1.234.567",      1234567)]
    [InlineData("Rp 1.234.567",   1234567)]
    [InlineData("Rp1234567",      1234567)]
    [InlineData("1,234,567.00",   1234567)]
    [InlineData("1,234,567",      1234567)]
    [InlineData("100",            100)]
    [InlineData("0",              0)]
    [InlineData("",               0)]
    [InlineData("   ",            0)]
    public void ParsePrice_HandlesCommonFormats(string input, decimal expected)
    {
        Assert.Equal(expected, ProductSeeder.IndonesianDecimalConverter.ParsePrice(input));
    }

    [Theory]
    [InlineData("1.234.567,50",   1234567.50)]
    [InlineData("1234567,50",     1234567.50)]
    [InlineData("1234567.50",     1234567.50)]
    [InlineData("Rp 1.234,5",     1234.5)]
    public void ParsePrice_HandlesDecimalFractions(string input, double expectedDouble)
    {
        var expected = (decimal)expectedDouble;
        Assert.Equal(expected, ProductSeeder.IndonesianDecimalConverter.ParsePrice(input));
    }
}
