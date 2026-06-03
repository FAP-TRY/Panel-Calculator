using ClosedXML.Excel;
using PanelCalculator.Core.Services;
using RabKit.Branding;
using System.Globalization;

namespace PanelCalculator.WinForms.Services;

/// <summary>
/// Generate Surat Penawaran sebagai workbook Excel (.xlsx) <strong>editable</strong>.
/// Berbeda dengan PDF/Word yang sifatnya statis untuk dikirim ke customer,
/// versi Excel ini ditujukan untuk internal editing (kalau user mau ganti
/// susunan, tambah kolom, atau pakai SUM formula sendiri).
/// <para>
/// Layout: 2 sheet — <c>Penawaran</c> (header info + tabel item dengan SUM
/// formula otomatis) dan <c>Ringkasan</c> (DPP/PPN/Grand Total dengan formula
/// agar tetap re-calculate kalau user edit angka).
/// </para>
/// <para>
/// Library: ClosedXML 0.105.0 (sudah ada di project).
/// </para>
/// </summary>
public static class ExcelLetterExport
{
    private static readonly CultureInfo IdCulture = CultureInfo.GetCultureInfo("id-ID");

    private static readonly string[] AllSections =
    {
        "Material Utama", "Material Pendukung", "Material Lainnya",
        "Box", "Incoming", "Outgoing", "Trailer", "Karoseri", "Jasa"
    };

    public record LineItem(
        string  ReferenceCode,
        string  ProductName,
        string  Vendor,
        string  Section,
        int     Quantity,
        string  Satuan,
        decimal UnitPrice,
        decimal LineTotal);

    public record CombinedPanel(
        string  EstimationNumber,
        string? ProjectName,
        IReadOnlyList<LineItem> Items);

    // ════════════════════════════════════════════════════════════════════════
    //  SINGLE PANEL
    // ════════════════════════════════════════════════════════════════════════
    public static void Generate(
        string outputPath,
        string estimationNumber,
        string clientName,
        string? contactPhone,
        string? company,
        string? address,
        string? perihal,
        DateTime createdDate,
        string notes,
        IReadOnlyList<LineItem> items,
        decimal subtotal,
        decimal marginAmount,
        decimal shippingCost,
        decimal taxPercent,
        decimal taxAmount,
        decimal pphPercent,
        decimal pphAmount,
        decimal total,
        IDictionary<string, string> settings)
    {
        ArgumentNullException.ThrowIfNull(outputPath);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(settings);

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Penawaran");

        var companyName  = Get(settings, "CompanyName",    BrandContext.Current.CompanyName.ToUpperInvariant());
        var companyAddr  = Get(settings, "CompanyAddress", "");
        var companyPhone = Get(settings, "CompanyPhone",   "");
        var signerName   = Get(settings, "SignerName",     "");
        var signerTitle  = Get(settings, "SignerTitle",    "Marketing");
        var offerCity    = Get(settings, "OfferLocation",  BrandContext.Current.DefaultOfferLocation);

        int row = 1;

        // ── Header perusahaan ─────────────────────────────────────────────
        ws.Cell(row, 1).Value = companyName;
        ws.Range(row, 1, row, 8).Merge().Style
            .Font.SetBold().Font.SetFontSize(14)
            .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
        row++;
        if (!string.IsNullOrWhiteSpace(companyAddr))
        {
            ws.Cell(row, 1).Value = companyAddr;
            ws.Range(row, 1, row, 8).Merge().Style
                .Font.SetFontSize(10)
                .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
            row++;
        }
        if (!string.IsNullOrWhiteSpace(companyPhone))
        {
            ws.Cell(row, 1).Value = companyPhone;
            ws.Range(row, 1, row, 8).Merge().Style
                .Font.SetFontSize(10)
                .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
            row++;
        }
        row++;

        // ── Nomor / Perihal / Lampiran (kiri) + Kepada (kanan) ────────────
        int hdrStartRow = row;
        ws.Cell(row, 1).Value = "Nomor";   ws.Cell(row, 1).Style.Font.SetBold();
        ws.Cell(row, 2).Value = ":";
        ws.Cell(row, 3).Value = estimationNumber;
        ws.Cell(row, 5).Value = "Kepada:"; ws.Cell(row, 5).Style.Font.SetBold();
        row++;
        ws.Cell(row, 1).Value = "Perihal"; ws.Cell(row, 1).Style.Font.SetBold();
        ws.Cell(row, 2).Value = ":";
        ws.Cell(row, 3).Value = !string.IsNullOrWhiteSpace(perihal) ? perihal : "Informasi Harga";
        bool hasCompany = !string.IsNullOrWhiteSpace(company);
        ws.Cell(row, 5).Value = hasCompany ? company! : clientName;
        ws.Cell(row, 5).Style.Font.SetBold();
        row++;
        ws.Cell(row, 1).Value = "Lampiran"; ws.Cell(row, 1).Style.Font.SetBold();
        ws.Cell(row, 2).Value = ":";
        ws.Cell(row, 3).Value = "Rincian Material";
        if (!string.IsNullOrWhiteSpace(address))
        {
            ws.Cell(row, 5).Value = address.Replace('\r', ' ').Replace('\n', ' ');
        }
        row++;
        if (!string.IsNullOrWhiteSpace(contactPhone))
        {
            ws.Cell(row, 5).Value = $"Telp: {contactPhone}";
            row++;
        }
        if (hasCompany && !string.IsNullOrWhiteSpace(clientName))
        {
            ws.Cell(row, 5).Value = $"Up. {clientName}";
            ws.Cell(row, 5).Style.Font.SetBold();
            row++;
        }
        row += 2;

        // ── Pembuka ───────────────────────────────────────────────────────
        ws.Cell(row, 1).Value = "Dengan hormat,"; row++;
        ws.Cell(row, 1).Value = "Berikut ini kami sampaikan informasi harga Panel sebagai berikut:";
        ws.Range(row, 1, row, 8).Merge();
        row += 2;

        // ── Tabel item per section ────────────────────────────────────────
        int tableStartRow = row;
        var headers = new[] { "No", "Section", "Kode Referensi", "Nama Barang", "Merek", "Satuan", "Qty", "Harga Satuan", "Total" };
        for (int c = 0; c < headers.Length; c++)
        {
            var cell = ws.Cell(row, c + 1);
            cell.Value = headers[c];
            cell.Style
                .Font.SetBold()
                .Fill.SetBackgroundColor(XLColor.FromArgb(210, 225, 245))
                .Border.SetOutsideBorder(XLBorderStyleValues.Thin)
                .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
        }
        row++;

        int itemStartRow = row;
        int no = 0;
        foreach (var sec in AllSections)
        {
            var secItems = items.Where(i => i.Section == sec).ToList();
            foreach (var it in secItems)
            {
                no++;
                ws.Cell(row, 1).Value = no;
                ws.Cell(row, 2).Value = sec;
                ws.Cell(row, 3).Value = it.ReferenceCode;
                ws.Cell(row, 4).Value = it.ProductName;
                ws.Cell(row, 5).Value = it.Vendor;
                ws.Cell(row, 6).Value = it.Satuan;
                ws.Cell(row, 7).Value = it.Quantity;
                ws.Cell(row, 8).Value = it.UnitPrice;
                // Total: pakai FORMULA biar user bisa edit Qty/Harga dan auto-recalc
                ws.Cell(row, 9).FormulaA1 = $"=G{row}*H{row}";
                ws.Cell(row, 8).Style.NumberFormat.Format = "#,##0";
                ws.Cell(row, 9).Style.NumberFormat.Format = "#,##0";
                ws.Range(row, 1, row, 9).Style.Border.SetOutsideBorder(XLBorderStyleValues.Hair);
                row++;
            }
        }
        int itemEndRow = row - 1;
        if (itemEndRow < itemStartRow) itemEndRow = itemStartRow; // empty edge case

        // Subtotal row dengan SUM formula
        ws.Cell(row, 8).Value = "Subtotal";
        ws.Cell(row, 8).Style.Font.SetBold().Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);
        ws.Cell(row, 9).FormulaA1 = $"=SUM(I{itemStartRow}:I{itemEndRow})";
        ws.Cell(row, 9).Style.Font.SetBold().NumberFormat.Format = "#,##0";
        int subtotalCellRow = row;
        row++;

        // Margin
        int marginRow = -1;
        if (marginAmount != 0)
        {
            ws.Cell(row, 8).Value = marginAmount >= 0 ? "Margin" : "Diskon";
            ws.Cell(row, 8).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);
            ws.Cell(row, 9).Value = marginAmount;
            ws.Cell(row, 9).Style.NumberFormat.Format = "#,##0";
            marginRow = row;
            row++;
        }

        int shippingRow = -1;
        if (shippingCost > 0)
        {
            ws.Cell(row, 8).Value = "Ongkos Kirim";
            ws.Cell(row, 8).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);
            ws.Cell(row, 9).Value = shippingCost;
            ws.Cell(row, 9).Style.NumberFormat.Format = "#,##0";
            shippingRow = row;
            row++;
        }

        // DPP = Subtotal + Margin + Shipping
        ws.Cell(row, 8).Value = "DPP (Dasar Pengenaan Pajak)";
        ws.Cell(row, 8).Style.Font.SetBold().Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);
        string dppFormula = $"=I{subtotalCellRow}";
        if (marginRow > 0) dppFormula += $"+I{marginRow}";
        if (shippingRow > 0) dppFormula += $"+I{shippingRow}";
        ws.Cell(row, 9).FormulaA1 = dppFormula;
        ws.Cell(row, 9).Style.Font.SetBold().NumberFormat.Format = "#,##0";
        int dppRow = row;
        row++;

        // PPN
        int taxRow = -1;
        if (taxAmount > 0)
        {
            ws.Cell(row, 8).Value = $"PPN {taxPercent:F0}%";
            ws.Cell(row, 8).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);
            // Pakai formula biar tetap akurat kalau user ganti DPP
            ws.Cell(row, 9).FormulaA1 = $"=ROUND(I{dppRow}*{(taxPercent / 100m).ToString(CultureInfo.InvariantCulture)}, 0)";
            ws.Cell(row, 9).Style.NumberFormat.Format = "#,##0";
            taxRow = row;
            row++;
        }

        // PPh
        int pphRow = -1;
        if (pphAmount > 0)
        {
            ws.Cell(row, 8).Value = "PPh (ditahan)";
            ws.Cell(row, 8).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);
            ws.Cell(row, 9).Value = -pphAmount; // negatif karena ditahan
            ws.Cell(row, 9).Style.NumberFormat.Format = "#,##0";
            pphRow = row;
            row++;
        }

        // GRAND TOTAL
        ws.Cell(row, 8).Value = "GRAND TOTAL";
        ws.Cell(row, 8).Style.Font.SetBold().Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);
        string totalFormula = $"=I{dppRow}";
        if (taxRow > 0) totalFormula += $"+I{taxRow}";
        if (pphRow > 0) totalFormula += $"+I{pphRow}";
        ws.Cell(row, 9).FormulaA1 = totalFormula;
        ws.Cell(row, 9).Style
            .Font.SetBold().Font.SetFontSize(12)
            .NumberFormat.Format = "#,##0"
            ;
        ws.Cell(row, 9).Style.Fill.SetBackgroundColor(XLColor.FromArgb(235, 242, 255));
        ws.Cell(row, 8).Style.Fill.SetBackgroundColor(XLColor.FromArgb(235, 242, 255));
        int totalRow = row;
        row += 2;

        // Terbilang
        ws.Cell(row, 1).Value = "Terbilang: " + TerbilangFormatter.ToRupiah(total);
        ws.Range(row, 1, row, 9).Merge().Style.Font.SetItalic().Font.SetBold();
        row += 2;

        // Kondisi penawaran
        WriteConditions(ws, ref row, offerCity, taxPercent);

        if (!string.IsNullOrWhiteSpace(notes))
        {
            ws.Cell(row, 1).Value = $"Catatan: {notes}";
            ws.Cell(row, 1).Style.Font.SetFontSize(9).Font.SetItalic();
            row++;
        }
        row += 2;

        // Signature
        var dateStr = createdDate.ToLocalTime().ToString("dd MMMM yyyy", IdCulture);
        ws.Cell(row, 1).Value = $"{offerCity}, {dateStr}"; row++;
        ws.Cell(row, 1).Value = BrandContext.Current.CompanyName; row += 4;
        if (!string.IsNullOrWhiteSpace(signerName))
        {
            ws.Cell(row, 1).Value = signerName;
            ws.Cell(row, 1).Style.Font.SetBold();
            row++;
        }
        if (!string.IsNullOrWhiteSpace(signerTitle))
        {
            ws.Cell(row, 1).Value = signerTitle;
            ws.Cell(row, 1).Style.Font.SetFontSize(9).Font.FontColor = XLColor.Gray;
            row++;
        }

        // Auto-fit + freeze header
        ws.Columns().AdjustToContents();
        ws.SheetView.FreezeRows(tableStartRow);

        // ── Sheet "Ringkasan" untuk re-cap angka ──────────────────────────
        var ws2 = wb.Worksheets.Add("Ringkasan");
        BuildSummarySheet(ws2, subtotal, marginAmount, shippingCost,
            taxPercent, taxAmount, pphAmount, total);

        wb.SaveAs(outputPath);
    }

    // ════════════════════════════════════════════════════════════════════════
    //  COMBINED MULTI-PANEL
    // ════════════════════════════════════════════════════════════════════════
    public static void GenerateCombined(
        string outputPath,
        string nomorSurat,
        string clientName,
        string? contactPhone,
        string? company,
        string? address,
        string? perihal,
        DateTime createdDate,
        string notes,
        IReadOnlyList<CombinedPanel> panels,
        CombinedQuotationCalculator.CombinedSummary summary,
        IDictionary<string, string> settings)
    {
        ArgumentNullException.ThrowIfNull(outputPath);
        ArgumentNullException.ThrowIfNull(panels);
        if (panels.Count == 0)
            throw new ArgumentException("Minimal satu panel diperlukan.", nameof(panels));

        using var wb = new XLWorkbook();

        var companyName  = Get(settings, "CompanyName",    BrandContext.Current.CompanyName.ToUpperInvariant());
        var companyAddr  = Get(settings, "CompanyAddress", "");
        var signerName   = Get(settings, "SignerName",     "");
        var signerTitle  = Get(settings, "SignerTitle",    "Marketing");
        var offerCity    = Get(settings, "OfferLocation",  BrandContext.Current.DefaultOfferLocation);

        // ── Sheet 1: Penawaran Gabungan (overview) ────────────────────────
        var ws = wb.Worksheets.Add("Penawaran");
        int row = 1;

        ws.Cell(row, 1).Value = companyName;
        ws.Range(row, 1, row, 9).Merge().Style.Font.SetBold().Font.SetFontSize(14)
            .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
        row++;
        if (!string.IsNullOrWhiteSpace(companyAddr))
        {
            ws.Cell(row, 1).Value = companyAddr;
            ws.Range(row, 1, row, 9).Merge().Style.Font.SetFontSize(10)
                .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
            row++;
        }
        row += 2;

        ws.Cell(row, 1).Value = "Nomor Surat";  ws.Cell(row, 1).Style.Font.SetBold();
        ws.Cell(row, 2).Value = ":";  ws.Cell(row, 3).Value = nomorSurat; row++;
        ws.Cell(row, 1).Value = "Perihal";  ws.Cell(row, 1).Style.Font.SetBold();
        ws.Cell(row, 2).Value = ":";
        ws.Cell(row, 3).Value = !string.IsNullOrWhiteSpace(perihal) ? perihal : "Penawaran Harga Multi-Panel"; row++;
        ws.Cell(row, 1).Value = "Lampiran";  ws.Cell(row, 1).Style.Font.SetBold();
        ws.Cell(row, 2).Value = ":";  ws.Cell(row, 3).Value = $"{panels.Count} Rincian Panel"; row++;
        row++;

        ws.Cell(row, 1).Value = "Kepada:"; ws.Cell(row, 1).Style.Font.SetBold(); row++;
        bool hasCompany = !string.IsNullOrWhiteSpace(company);
        ws.Cell(row, 1).Value = hasCompany ? company! : clientName;
        ws.Cell(row, 1).Style.Font.SetBold();
        row++;
        if (!string.IsNullOrWhiteSpace(address))
        {
            ws.Cell(row, 1).Value = address.Replace('\r', ' ').Replace('\n', ' '); row++;
        }
        if (!string.IsNullOrWhiteSpace(contactPhone))
        {
            ws.Cell(row, 1).Value = $"Telp: {contactPhone}"; row++;
        }
        if (hasCompany && !string.IsNullOrWhiteSpace(clientName))
        {
            ws.Cell(row, 1).Value = $"Up. {clientName}";
            ws.Cell(row, 1).Style.Font.SetBold(); row++;
        }
        row += 2;

        ws.Cell(row, 1).Value = "Dengan hormat,"; row++;
        ws.Cell(row, 1).Value = $"Bersama ini kami sampaikan penawaran harga untuk {panels.Count} (panel) sebagai berikut:";
        ws.Range(row, 1, row, 9).Merge();
        row += 2;

        // ── Tabel ringkasan per panel ─────────────────────────────────────
        ws.Cell(row, 1).Value = "RINGKASAN PER PANEL";
        ws.Range(row, 1, row, 9).Merge().Style.Font.SetBold().Font.SetFontSize(11)
            .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
        row += 2;

        var ringHdr = new[] { "No", "No. Estimasi", "Project / Panel", "Sub-total + Margin", "Ongkir per Panel", "PPh per Panel" };
        for (int c = 0; c < ringHdr.Length; c++)
        {
            var cell = ws.Cell(row, c + 1);
            cell.Value = ringHdr[c];
            cell.Style.Font.SetBold()
                .Fill.SetBackgroundColor(XLColor.FromArgb(210, 225, 245))
                .Border.SetOutsideBorder(XLBorderStyleValues.Thin)
                .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
        }
        row++;
        int panelStartRow = row;
        foreach (var pl in summary.Panels)
        {
            ws.Cell(row, 1).Value = pl.Index;
            ws.Cell(row, 2).Value = pl.EstimationNumber;
            ws.Cell(row, 3).Value = !string.IsNullOrWhiteSpace(pl.ProjectName) ? pl.ProjectName : "—";
            ws.Cell(row, 4).Value = pl.PanelSubtotal;
            ws.Cell(row, 5).Value = pl.ShippingCost;
            ws.Cell(row, 6).Value = pl.PPh;
            ws.Range(row, 4, row, 6).Style.NumberFormat.Format = "#,##0";
            ws.Range(row, 1, row, 6).Style.Border.SetOutsideBorder(XLBorderStyleValues.Hair);
            row++;
        }
        int panelEndRow = row - 1;

        // Grand subtotal pakai SUM formula
        ws.Cell(row, 3).Value = "Total Sub-total Semua Panel";
        ws.Cell(row, 3).Style.Font.SetBold().Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);
        ws.Cell(row, 4).FormulaA1 = $"=SUM(D{panelStartRow}:D{panelEndRow})";
        ws.Cell(row, 4).Style.Font.SetBold().NumberFormat.Format = "#,##0";
        int grandSubRow = row; row++;

        // Ongkir gabungan (input akhir, dari summary)
        int combOngkirRow = -1;
        if (summary.CombinedShippingCost > 0)
        {
            ws.Cell(row, 3).Value = "Ongkos Kirim Gabungan";
            ws.Cell(row, 3).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);
            ws.Cell(row, 4).Value = summary.CombinedShippingCost;
            ws.Cell(row, 4).Style.NumberFormat.Format = "#,##0";
            combOngkirRow = row; row++;
        }

        // DPP
        ws.Cell(row, 3).Value = "DPP (Dasar Pengenaan Pajak)";
        ws.Cell(row, 3).Style.Font.SetBold().Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);
        ws.Cell(row, 4).FormulaA1 = combOngkirRow > 0
            ? $"=D{grandSubRow}+D{combOngkirRow}"
            : $"=D{grandSubRow}";
        ws.Cell(row, 4).Style.Font.SetBold().NumberFormat.Format = "#,##0";
        int dppRow = row; row++;

        // PPN
        int taxRow = -1;
        if (summary.TaxAmount > 0)
        {
            ws.Cell(row, 3).Value = $"PPN {summary.TaxPercent:F0}% (dihitung sekali)";
            ws.Cell(row, 3).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);
            ws.Cell(row, 4).FormulaA1 = $"=ROUND(D{dppRow}*{(summary.TaxPercent / 100m).ToString(CultureInfo.InvariantCulture)}, 0)";
            ws.Cell(row, 4).Style.NumberFormat.Format = "#,##0";
            taxRow = row; row++;
        }

        // Total PPh
        int totPphRow = -1;
        if (summary.TotalPPh > 0)
        {
            ws.Cell(row, 3).Value = "PPh (ditahan, total semua panel)";
            ws.Cell(row, 3).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);
            ws.Cell(row, 4).FormulaA1 = $"=-SUM(F{panelStartRow}:F{panelEndRow})";
            ws.Cell(row, 4).Style.NumberFormat.Format = "#,##0";
            totPphRow = row; row++;
        }

        // GRAND TOTAL
        ws.Cell(row, 3).Value = "GRAND TOTAL";
        ws.Cell(row, 3).Style.Font.SetBold().Font.SetFontSize(12)
            .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);
        string gtFormula = $"=D{dppRow}";
        if (taxRow > 0)    gtFormula += $"+D{taxRow}";
        if (totPphRow > 0) gtFormula += $"+D{totPphRow}";
        ws.Cell(row, 4).FormulaA1 = gtFormula;
        ws.Cell(row, 4).Style.Font.SetBold().Font.SetFontSize(12).NumberFormat.Format = "#,##0";
        ws.Range(row, 3, row, 4).Style.Fill.SetBackgroundColor(XLColor.FromArgb(235, 242, 255));
        row += 2;

        ws.Cell(row, 1).Value = "Terbilang: " + summary.Terbilang;
        ws.Range(row, 1, row, 9).Merge().Style.Font.SetItalic().Font.SetBold();
        row += 2;

        WriteConditions(ws, ref row, offerCity, summary.TaxPercent, isCombined: true);

        if (!string.IsNullOrWhiteSpace(notes))
        {
            ws.Cell(row, 1).Value = $"Catatan: {notes}";
            ws.Cell(row, 1).Style.Font.SetFontSize(9).Font.SetItalic();
            row++;
        }
        row += 2;

        var dateStr = createdDate.ToLocalTime().ToString("dd MMMM yyyy", IdCulture);
        ws.Cell(row, 1).Value = $"{offerCity}, {dateStr}"; row++;
        ws.Cell(row, 1).Value = BrandContext.Current.CompanyName; row += 4;
        if (!string.IsNullOrWhiteSpace(signerName))
        {
            ws.Cell(row, 1).Value = signerName;
            ws.Cell(row, 1).Style.Font.SetBold(); row++;
        }
        if (!string.IsNullOrWhiteSpace(signerTitle))
        {
            ws.Cell(row, 1).Value = signerTitle;
            ws.Cell(row, 1).Style.Font.SetFontSize(9).Font.FontColor = XLColor.Gray;
            row++;
        }

        ws.Columns().AdjustToContents();

        // ── Sheet per-panel detail items ──────────────────────────────────
        for (int pi = 0; pi < panels.Count; pi++)
        {
            var panel = panels[pi];
            var sheetName = SanitizeSheetName($"Panel {pi + 1}");
            var wsP = wb.Worksheets.Add(sheetName);
            int r = 1;
            wsP.Cell(r, 1).Value = !string.IsNullOrWhiteSpace(panel.ProjectName)
                ? $"PANEL #{pi + 1} — {panel.ProjectName}"
                : $"PANEL #{pi + 1} — {panel.EstimationNumber}";
            wsP.Range(r, 1, r, 9).Merge().Style.Font.SetBold().Font.SetFontSize(12)
                .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
            r++;
            wsP.Cell(r, 1).Value = $"Ref: {panel.EstimationNumber}";
            wsP.Range(r, 1, r, 9).Merge().Style.Font.SetFontSize(9).Font.FontColor = XLColor.Gray;
            r += 2;

            var hdrs = new[] { "No", "Section", "Kode Referensi", "Nama Barang", "Merek", "Satuan", "Qty", "Harga Satuan", "Total" };
            for (int c = 0; c < hdrs.Length; c++)
            {
                var cell = wsP.Cell(r, c + 1);
                cell.Value = hdrs[c];
                cell.Style.Font.SetBold()
                    .Fill.SetBackgroundColor(XLColor.FromArgb(210, 225, 245))
                    .Border.SetOutsideBorder(XLBorderStyleValues.Thin)
                    .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
            }
            r++;
            int itemStart = r;
            int no_ = 0;
            foreach (var sec in AllSections)
            {
                var secItems = panel.Items.Where(i => i.Section == sec).ToList();
                foreach (var it in secItems)
                {
                    no_++;
                    wsP.Cell(r, 1).Value = no_;
                    wsP.Cell(r, 2).Value = sec;
                    wsP.Cell(r, 3).Value = it.ReferenceCode;
                    wsP.Cell(r, 4).Value = it.ProductName;
                    wsP.Cell(r, 5).Value = it.Vendor;
                    wsP.Cell(r, 6).Value = it.Satuan;
                    wsP.Cell(r, 7).Value = it.Quantity;
                    wsP.Cell(r, 8).Value = it.UnitPrice;
                    wsP.Cell(r, 9).FormulaA1 = $"=G{r}*H{r}";
                    wsP.Cell(r, 8).Style.NumberFormat.Format = "#,##0";
                    wsP.Cell(r, 9).Style.NumberFormat.Format = "#,##0";
                    wsP.Range(r, 1, r, 9).Style.Border.SetOutsideBorder(XLBorderStyleValues.Hair);
                    r++;
                }
            }
            int itemEnd = r - 1;
            if (itemEnd < itemStart) itemEnd = itemStart;
            wsP.Cell(r, 8).Value = "Subtotal";
            wsP.Cell(r, 8).Style.Font.SetBold().Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);
            wsP.Cell(r, 9).FormulaA1 = $"=SUM(I{itemStart}:I{itemEnd})";
            wsP.Cell(r, 9).Style.Font.SetBold().NumberFormat.Format = "#,##0";
            wsP.Columns().AdjustToContents();
            wsP.SheetView.FreezeRows(itemStart - 1);
        }

        // ── Sheet Ringkasan (kompak, untuk pivot-style review) ────────────
        var wsSummary = wb.Worksheets.Add("Ringkasan");
        BuildCombinedSummarySheet(wsSummary, summary);

        wb.SaveAs(outputPath);
    }

    // ════════════════════════════════════════════════════════════════════════
    //  HELPERS
    // ════════════════════════════════════════════════════════════════════════
    private static void BuildSummarySheet(IXLWorksheet ws, decimal subtotal,
        decimal marginAmount, decimal shippingCost, decimal taxPercent, decimal taxAmount,
        decimal pphAmount, decimal total)
    {
        ws.Cell(1, 1).Value = "RINGKASAN";
        ws.Range(1, 1, 1, 2).Merge().Style.Font.SetBold().Font.SetFontSize(12)
            .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
        int r = 3;
        void Put(string label, decimal value, bool bold)
        {
            ws.Cell(r, 1).Value = label;
            ws.Cell(r, 2).Value = value;
            ws.Cell(r, 2).Style.NumberFormat.Format = "#,##0";
            if (bold) { ws.Cell(r, 1).Style.Font.SetBold(); ws.Cell(r, 2).Style.Font.SetBold(); }
            r++;
        }
        Put("Subtotal", subtotal, false);
        if (marginAmount != 0) Put(marginAmount >= 0 ? "Margin" : "Diskon", marginAmount, false);
        if (shippingCost > 0)  Put("Ongkos Kirim", shippingCost, false);
        Put("DPP",             subtotal + marginAmount + shippingCost, true);
        if (taxAmount > 0)     Put($"PPN {taxPercent:F0}%", taxAmount, false);
        if (pphAmount > 0)     Put("PPh (ditahan)", -pphAmount, false);
        Put("GRAND TOTAL",     total, true);
        ws.Columns().AdjustToContents();
    }

    private static void BuildCombinedSummarySheet(IXLWorksheet ws,
        CombinedQuotationCalculator.CombinedSummary summary)
    {
        ws.Cell(1, 1).Value = "RINGKASAN PENAWARAN GABUNGAN";
        ws.Range(1, 1, 1, 2).Merge().Style.Font.SetBold().Font.SetFontSize(12)
            .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
        int r = 3;
        foreach (var pl in summary.Panels)
        {
            var label = !string.IsNullOrWhiteSpace(pl.ProjectName)
                ? $"Sub-total Panel #{pl.Index} — {pl.ProjectName}"
                : $"Sub-total Panel #{pl.Index} ({pl.EstimationNumber})";
            ws.Cell(r, 1).Value = label;
            ws.Cell(r, 2).Value = pl.PanelSubtotal;
            ws.Cell(r, 2).Style.NumberFormat.Format = "#,##0";
            r++;
        }
        void PutLine(string label, decimal value, bool bold)
        {
            ws.Cell(r, 1).Value = label;
            ws.Cell(r, 2).Value = value;
            ws.Cell(r, 2).Style.NumberFormat.Format = "#,##0";
            if (bold) { ws.Cell(r, 1).Style.Font.SetBold(); ws.Cell(r, 2).Style.Font.SetBold(); }
            r++;
        }
        PutLine("Total Sub-total Semua Panel", summary.GrandSubtotal, true);
        if (summary.CombinedShippingCost > 0) PutLine("Ongkos Kirim Gabungan", summary.CombinedShippingCost, false);
        PutLine("DPP", summary.DPP, true);
        if (summary.TaxAmount > 0)   PutLine($"PPN {summary.TaxPercent:F0}%", summary.TaxAmount, false);
        if (summary.TotalPPh > 0)    PutLine("PPh (ditahan)", -summary.TotalPPh, false);
        PutLine("GRAND TOTAL", summary.GrandTotal, true);
        r++;
        ws.Cell(r, 1).Value = "Terbilang: " + summary.Terbilang;
        ws.Range(r, 1, r, 2).Merge().Style.Font.SetItalic();
        ws.Columns().AdjustToContents();
    }

    private static void WriteConditions(IXLWorksheet ws, ref int row, string city,
        decimal taxPercent, bool isCombined = false)
    {
        ws.Cell(row, 1).Value = "Kondisi Penawaran :";
        ws.Cell(row, 1).Style.Font.SetBold();
        row++;
        var conds = new List<string>();
        if (isCombined)
            conds.Add(taxPercent > 0
                ? "Harga belum termasuk PPN (menyesuaikan peraturan pemerintah), dihitung sekali atas total seluruh panel"
                : "Harga sudah termasuk PPN");
        else
            conds.Add(taxPercent > 0
                ? "Harga belum termasuk PPN (menyesuaikan peraturan pemerintah)"
                : "Harga sudah termasuk PPN");
        conds.Add($"Harga loco {city}");
        conds.Add("DP 30% saat PO kami terima dan pelunasan 70% pada saat barang akan dikirimkan");
        conds.Add("Harga tidak terikat dan dapat berubah sewaktu-waktu");
        for (int i = 0; i < conds.Count; i++)
        {
            ws.Cell(row, 1).Value = $"{i + 1}. {conds[i]}";
            row++;
        }
    }

    private static string SanitizeSheetName(string name)
    {
        var invalid = new[] { '\\', '/', '?', '*', '[', ']', ':' };
        var arr = name.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        var s = new string(arr).Trim();
        if (string.IsNullOrEmpty(s)) s = "Sheet";
        return s.Length > 31 ? s.Substring(0, 31) : s;
    }

    private static string Get(IDictionary<string, string> s, string key, string fallback)
        => s.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : fallback;
}
