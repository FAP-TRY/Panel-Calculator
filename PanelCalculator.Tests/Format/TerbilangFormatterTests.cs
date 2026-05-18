using PanelCalculator.Core.Services;
using Xunit;

namespace PanelCalculator.Tests.Format;

public class TerbilangFormatterTests
{
    [Theory]
    [InlineData(0,            "Nol rupiah")]
    [InlineData(1,            "Satu rupiah")]
    [InlineData(11,           "Sebelas rupiah")]
    [InlineData(15,           "Lima belas rupiah")]
    [InlineData(20,           "Dua puluh rupiah")]
    [InlineData(99,           "Sembilan puluh sembilan rupiah")]
    [InlineData(100,          "Seratus rupiah")]
    [InlineData(101,          "Seratus satu rupiah")]
    [InlineData(1000,         "Seribu rupiah")]
    [InlineData(1500,         "Seribu lima ratus rupiah")]
    [InlineData(2000,         "Dua ribu rupiah")]
    [InlineData(1_000_000,    "Satu juta rupiah")]
    [InlineData(15_234_567,   "Lima belas juta dua ratus tiga puluh empat ribu lima ratus enam puluh tujuh rupiah")]
    [InlineData(1_000_000_000L,         "Satu miliar rupiah")]
    public void ToRupiah_ProducesExpectedString(long amount, string expected)
    {
        Assert.Equal(expected, TerbilangFormatter.ToRupiah(amount));
    }

    [Fact]
    public void ToRupiah_TruncatesDecimals()
    {
        // 1234.99 should be "Seribu dua ratus tiga puluh empat rupiah" (no sen)
        var result = TerbilangFormatter.ToRupiah(1234.99m);
        Assert.Equal("Seribu dua ratus tiga puluh empat rupiah", result);
    }

    [Fact]
    public void ToRupiah_NegativeAmount_HasMinusPrefix()
    {
        var result = TerbilangFormatter.ToRupiah(-500m);
        Assert.StartsWith("Minus", result);
        Assert.EndsWith("rupiah", result);
    }
}
