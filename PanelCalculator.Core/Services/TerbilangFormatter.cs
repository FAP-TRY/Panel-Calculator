namespace PanelCalculator.Core.Services;

/// <summary>
/// Converts a numeric amount into its Indonesian-language spelled-out form
/// ("terbilang"). Designed for currency values up to triliun range.
/// </summary>
public static class TerbilangFormatter
{
    private static readonly string[] Satuan =
    {
        "", "satu", "dua", "tiga", "empat", "lima",
        "enam", "tujuh", "delapan", "sembilan",
        "sepuluh", "sebelas"
    };

    /// <summary>
    /// Returns the rupiah amount spelled out in Indonesian, terminating in
    /// " rupiah". Decimals (sen) are truncated. Negative numbers prefixed
    /// with "minus ".
    /// </summary>
    public static string ToRupiah(decimal amount)
    {
        if (amount == 0) return "Nol rupiah";
        bool negative = amount < 0;
        if (negative) amount = -amount;

        // Truncate to whole rupiah.
        long whole = (long)System.Math.Floor(amount);
        var words = Convert(whole).Trim();

        // Indonesian convention: capitalise first letter, end with " rupiah".
        if (words.Length > 0)
            words = char.ToUpper(words[0]) + words.Substring(1);

        return (negative ? "Minus " : "") + words + " rupiah";
    }

    /// <summary>
    /// Core recursive number-to-words conversion. Returns lowercase string
    /// without trailing space. Supports up to ~999 triliun.
    /// </summary>
    private static string Convert(long n)
    {
        if (n < 0) return "minus " + Convert(-n);
        if (n < 12) return Satuan[n];
        if (n < 20) return Convert(n - 10) + " belas";
        if (n < 100)
        {
            long puluh = n / 10;
            long sisa = n % 10;
            return (Convert(puluh) + " puluh" + (sisa > 0 ? " " + Convert(sisa) : "")).Trim();
        }
        if (n < 200) return ("seratus" + (n - 100 > 0 ? " " + Convert(n - 100) : "")).Trim();
        if (n < 1000)
        {
            long ratus = n / 100;
            long sisa = n % 100;
            return (Convert(ratus) + " ratus" + (sisa > 0 ? " " + Convert(sisa) : "")).Trim();
        }
        if (n < 2000) return ("seribu" + (n - 1000 > 0 ? " " + Convert(n - 1000) : "")).Trim();
        if (n < 1_000_000)
        {
            long ribu = n / 1000;
            long sisa = n % 1000;
            return (Convert(ribu) + " ribu" + (sisa > 0 ? " " + Convert(sisa) : "")).Trim();
        }
        if (n < 1_000_000_000)
        {
            long juta = n / 1_000_000;
            long sisa = n % 1_000_000;
            return (Convert(juta) + " juta" + (sisa > 0 ? " " + Convert(sisa) : "")).Trim();
        }
        if (n < 1_000_000_000_000L)
        {
            long miliar = n / 1_000_000_000L;
            long sisa = n % 1_000_000_000L;
            return (Convert(miliar) + " miliar" + (sisa > 0 ? " " + Convert(sisa) : "")).Trim();
        }
        // Triliun
        long triliun = n / 1_000_000_000_000L;
        long rsisa = n % 1_000_000_000_000L;
        return (Convert(triliun) + " triliun" + (rsisa > 0 ? " " + Convert(rsisa) : "")).Trim();
    }
}
