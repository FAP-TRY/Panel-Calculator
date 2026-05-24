using PanelCalculator.Core.Models;

namespace PanelCalculator.Core.Services;

/// <summary>
/// Aggregator untuk surat penawaran multi-panel: menggabungkan beberapa
/// Estimation (masing-masing dengan margin & PPh sendiri) menjadi SATU
/// dokumen penawaran. Aturan utama:
///   • Subtotal per panel = SubTotal + Margin + ShippingCost panel itu sendiri
///     (semua nilai yang sudah disimpan per Estimation tetap dipakai apa adanya).
///   • PPN dihitung SATU KALI di akhir dari grand_subtotal × tax_percent
///     (default 11%), BUKAN sum PPN per panel — ini permintaan utama user.
///   • PPh per panel di-SUM (karena dasar tarif PPh bisa berbeda antar panel,
///     mempertahankan nilai PPh asli setiap panel adalah pilihan paling akurat).
///   • Ongkir gabungan: SATU nilai di akhir (input user) — ongkir per panel
///     yang sudah ada DI-IGNORE karena biasanya ongkir surat gabungan
///     dihitung sebagai satu pengiriman.
/// </summary>
public static class CombinedQuotationCalculator
{
    /// <summary>Tarif PPN default Indonesia 2025.</summary>
    public const decimal DefaultTaxPercent = 11m;

    /// <summary>
    /// Satu baris ringkasan per panel — dipakai oleh PDF writer untuk render
    /// blok "Sub-total Panel #N" di ringkasan akhir.
    /// </summary>
    public record PanelLine(
        int      Index,                 // 1-based
        string   EstimationNumber,
        string?  ProjectName,
        decimal  SubTotal,              // SubTotal raw dari Estimation
        decimal  Margin,                // Margin amount dari Estimation
        decimal  ShippingCost,          // Ongkir per panel (ditampilkan, tapi tidak masuk grand total)
        decimal  PPh,                   // PPh per panel
        decimal  PanelSubtotal);        // SubTotal + Margin (TANPA ongkir per-panel & TANPA PPN)

    public record CombinedSummary(
        IReadOnlyList<PanelLine> Panels,
        decimal GrandSubtotal,           // SUM(PanelSubtotal)
        decimal CombinedShippingCost,    // Ongkir gabungan (input user)
        decimal DPP,                     // Grand subtotal + ongkir gabungan (basis PPN)
        decimal TaxPercent,              // mis. 11
        decimal TaxAmount,               // DPP × taxPercent
        decimal TotalPPh,                // SUM PPh per panel
        decimal GrandTotal,              // DPP + PPN − PPh
        string  Terbilang);

    /// <summary>
    /// Bangun ringkasan gabungan. Untuk panel tunggal masih jalan, tapi UI
    /// disarankan disable tombol kalau jumlah panel &lt; 2.
    /// </summary>
    /// <param name="estimations">Panel-panel yang dipilih user.</param>
    /// <param name="combinedShippingCost">Ongkir gabungan (default 0).</param>
    /// <param name="taxPercent">Tarif PPN dalam persen, default 11.</param>
    public static CombinedSummary Build(
        IEnumerable<Estimation> estimations,
        decimal combinedShippingCost = 0m,
        decimal taxPercent = DefaultTaxPercent)
    {
        ArgumentNullException.ThrowIfNull(estimations);
        if (combinedShippingCost < 0m)
            throw new ArgumentOutOfRangeException(nameof(combinedShippingCost),
                "Ongkir gabungan tidak boleh negatif.");
        if (taxPercent < 0m)
            throw new ArgumentOutOfRangeException(nameof(taxPercent),
                "Tarif PPN tidak boleh negatif.");

        var list = estimations.ToList();
        if (list.Count == 0)
            throw new ArgumentException("Minimal satu estimasi diperlukan.", nameof(estimations));

        var panels = new List<PanelLine>(list.Count);
        int i = 0;
        foreach (var e in list)
        {
            i++;
            var panelSubtotal = e.SubTotal + e.Margin; // tanpa ongkir per-panel, tanpa PPN
            panels.Add(new PanelLine(
                Index:            i,
                EstimationNumber: e.EstimationNumber,
                ProjectName:      e.ProjectName,
                SubTotal:         e.SubTotal,
                Margin:           e.Margin,
                ShippingCost:     e.ShippingCost,
                PPh:              e.PPh,
                PanelSubtotal:    panelSubtotal));
        }

        decimal grandSubtotal = panels.Sum(p => p.PanelSubtotal);
        decimal dpp           = grandSubtotal + combinedShippingCost;
        decimal taxAmount     = Math.Round(dpp * (taxPercent / 100m), 0, MidpointRounding.AwayFromZero);
        decimal totalPPh      = panels.Sum(p => p.PPh);
        decimal grandTotal    = dpp + taxAmount - totalPPh;
        string  terbilang     = TerbilangFormatter.ToRupiah(grandTotal);

        return new CombinedSummary(
            Panels:               panels,
            GrandSubtotal:        grandSubtotal,
            CombinedShippingCost: combinedShippingCost,
            DPP:                  dpp,
            TaxPercent:           taxPercent,
            TaxAmount:            taxAmount,
            TotalPPh:             totalPPh,
            GrandTotal:           grandTotal,
            Terbilang:            terbilang);
    }

    /// <summary>
    /// Cek apakah customer info (ClientName + Company + Address) konsisten
    /// di semua estimasi. Dipakai UI untuk menampilkan warning bar.
    /// Returns null kalau konsisten; kalau tidak, kembalikan pesan singkat.
    /// </summary>
    public static string? CheckCustomerInfoMismatch(IEnumerable<Estimation> estimations)
    {
        var list = estimations.ToList();
        if (list.Count <= 1) return null;

        var first = list[0];
        var mismatches = new List<string>();
        foreach (var e in list.Skip(1))
        {
            if (!StringEq(e.ClientName, first.ClientName))
                mismatches.Add($"{e.EstimationNumber}: nama klien berbeda ('{e.ClientName}' vs '{first.ClientName}')");
            if (!StringEq(e.Company, first.Company))
                mismatches.Add($"{e.EstimationNumber}: perusahaan berbeda ('{e.Company}' vs '{first.Company}')");
            if (!StringEq(e.Address, first.Address))
                mismatches.Add($"{e.EstimationNumber}: alamat berbeda");
        }
        return mismatches.Count == 0 ? null : string.Join("\n", mismatches);
    }

    private static bool StringEq(string? a, string? b)
        => string.Equals((a ?? "").Trim(), (b ?? "").Trim(), StringComparison.OrdinalIgnoreCase);
}
