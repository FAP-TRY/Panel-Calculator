using iText.IO.Font.Constants;
using iText.Kernel.Colors;
using iText.Kernel.Font;
using iText.Kernel.Pdf;
using iText.Layout;
using iText.Layout.Borders;
using iText.Layout.Element;
using iText.Layout.Properties;
using PanelCalculator.Core.Services;

namespace PanelCalculator.WinForms.Services;

public static class PdfQuotationExport
{
    private static readonly DeviceRgb ColorPrimary    = new(37,  99,  235);
    private static readonly DeviceRgb ColorDark       = new(17,  24,  39);
    private static readonly DeviceRgb ColorMuted      = new(107, 114, 128);
    private static readonly DeviceRgb ColorBorderRow  = new(229, 231, 235);
    private static readonly DeviceRgb ColorWhite      = new(255, 255, 255);
    private static readonly DeviceRgb ColorLightGray  = new(249, 250, 251);

    // ── Section header palette (one unique color per section) ────────────
    // Each tone is a *light* tint used as the row background; a matching
    // *dark* tone is used for the label text so contrast stays >= 4.5:1
    // (engineer-friendly, not girly: every accent is desaturated and dark
    // enough to read clearly even on cheap office printers).
    private static readonly DeviceRgb ColorSecBgBlue    = new(219, 234, 254);  // Material Utama
    private static readonly DeviceRgb ColorSecFgBlue    = new( 30,  64, 175);

    private static readonly DeviceRgb ColorSecBgYellow  = new(254, 249, 195);  // Material Pendukung
    private static readonly DeviceRgb ColorSecFgYellow  = new(133, 100,   4);

    private static readonly DeviceRgb ColorSecBgGreen   = new(220, 252, 231);  // Material Lainnya
    private static readonly DeviceRgb ColorSecFgGreen   = new( 21, 128,  61);

    private static readonly DeviceRgb ColorSecBgPurple  = new(237, 233, 254);  // Box
    private static readonly DeviceRgb ColorSecFgPurple  = new( 91,  33, 182);

    private static readonly DeviceRgb ColorSecBgOrange  = new(255, 237, 213);  // Incoming / Outgoing
    private static readonly DeviceRgb ColorSecFgOrange  = new(154,  52,  18);

    private static readonly DeviceRgb ColorSecBgTeal    = new(207, 250, 254);  // Trailer / Karoseri
    private static readonly DeviceRgb ColorSecFgTeal    = new( 14, 116, 144);

    private static readonly DeviceRgb ColorSecBgSlate   = new(226, 232, 240);  // Jasa
    private static readonly DeviceRgb ColorSecFgSlate   = new( 51,  65,  85);

    // Reused by other helpers
    private static readonly DeviceRgb ColorAccentBlue   = new(30,  58,  138);

    // ── Public API ───────────────────────────────────────────────────────
    public static void Generate(
        string outputPath,
        string estimationNumber,
        string clientName,
        string? contactPhone,
        string? company,
        string? address,
        DateTime createdDate,
        string notes,
        IReadOnlyList<LineItem> items,
        decimal subtotal,
        decimal marginPercent,
        decimal marginAmount,
        decimal shippingCost,
        decimal taxPercent,
        decimal taxAmount,
        decimal pphPercent,
        decimal pphAmount,
        decimal total,
        IDictionary<string, string> settings)
    {
        using var writer   = new PdfWriter(outputPath);
        using var pdfDoc   = new PdfDocument(writer);
        using var document = new Document(pdfDoc);
        document.SetMargins(40, 50, 40, 50);

        var fontRegular = PdfFontFactory.CreateFont(StandardFonts.HELVETICA);
        var fontBold    = PdfFontFactory.CreateFont(StandardFonts.HELVETICA_BOLD);

        var companyName    = GetSetting(settings, "CompanyName",    "PT Electrical Supplies");
        var companyAddress = GetSetting(settings, "CompanyAddress", "Jakarta, Indonesia");
        var companyPhone   = GetSetting(settings, "CompanyPhone",   "");

        // ── HEADER ───────────────────────────────────────────────────────
        var headerTable = new Table(UnitValue.CreatePercentArray(new float[] { 60, 40 }))
            .UseAllAvailableWidth().SetBorder(Border.NO_BORDER).SetMarginBottom(20);

        var companyCell = new Cell().SetBorder(Border.NO_BORDER)
            .Add(new Paragraph(companyName).SetFont(fontBold).SetFontSize(16).SetFontColor(ColorDark).SetMarginBottom(2))
            .Add(new Paragraph(companyAddress).SetFont(fontRegular).SetFontSize(9).SetFontColor(ColorMuted).SetMarginBottom(1));
        if (!string.IsNullOrWhiteSpace(companyPhone))
            companyCell.Add(new Paragraph(companyPhone).SetFont(fontRegular).SetFontSize(9).SetFontColor(ColorMuted));
        headerTable.AddCell(companyCell);

        headerTable.AddCell(new Cell().SetBorder(Border.NO_BORDER).SetTextAlignment(TextAlignment.RIGHT)
            .Add(new Paragraph("PENAWARAN HARGA").SetFont(fontBold).SetFontSize(18).SetFontColor(ColorPrimary).SetMarginBottom(4))
            .Add(new Paragraph(estimationNumber).SetFont(fontBold).SetFontSize(11).SetFontColor(ColorDark)));
        document.Add(headerTable);

        document.Add(new LineSeparator(new iText.Kernel.Pdf.Canvas.Draw.SolidLine(2f))
            .SetStrokeColor(ColorPrimary).SetMarginBottom(16));

        // ── CLIENT / DATE ─────────────────────────────────────────────────
        var infoTable = new Table(UnitValue.CreatePercentArray(new float[] { 50, 50 }))
            .UseAllAvailableWidth().SetBorder(Border.NO_BORDER).SetMarginBottom(20);

        var clientCell = new Cell().SetBorder(Border.NO_BORDER)
            .Add(new Paragraph("KEPADA YTH.").SetFont(fontBold).SetFontSize(8).SetFontColor(ColorMuted).SetMarginBottom(2))
            .Add(new Paragraph(clientName).SetFont(fontBold).SetFontSize(12).SetFontColor(ColorDark).SetMarginBottom(1));
        if (!string.IsNullOrWhiteSpace(company))
            clientCell.Add(new Paragraph(company).SetFont(fontRegular).SetFontSize(9).SetFontColor(ColorDark).SetMarginBottom(1));
        if (!string.IsNullOrWhiteSpace(address))
            clientCell.Add(new Paragraph(address).SetFont(fontRegular).SetFontSize(9).SetFontColor(ColorMuted).SetMarginBottom(1));
        if (!string.IsNullOrWhiteSpace(contactPhone))
            clientCell.Add(new Paragraph(contactPhone).SetFont(fontRegular).SetFontSize(9).SetFontColor(ColorMuted).SetMarginBottom(1));
        if (!string.IsNullOrWhiteSpace(notes))
            clientCell.Add(new Paragraph(notes).SetFont(fontRegular).SetFontSize(9).SetFontColor(ColorMuted));
        infoTable.AddCell(clientCell);

        infoTable.AddCell(new Cell().SetBorder(Border.NO_BORDER).SetTextAlignment(TextAlignment.RIGHT)
            .Add(new Paragraph("Tanggal").SetFont(fontRegular).SetFontSize(8).SetFontColor(ColorMuted).SetMarginBottom(2))
            .Add(new Paragraph(createdDate.ToLocalTime().ToString("dd MMMM yyyy",
                    System.Globalization.CultureInfo.GetCultureInfo("id-ID")))
                .SetFont(fontBold).SetFontSize(10).SetFontColor(ColorDark)));
        document.Add(infoTable);

        // ── ITEMS TABLE (grouped by section) ─────────────────────────────
        float[] colWidths = { 5, 11, 32, 7, 8, 12, 7, 18 };
        var itemsTable = new Table(UnitValue.CreatePercentArray(colWidths))
            .UseAllAvailableWidth().SetMarginBottom(20);

        string[] headers = { "No", "Kode", "Nama Produk", "Qty", "Satuan", "Harga Satuan", "Adj", "Total" };
        foreach (var h in headers)
        {
            itemsTable.AddHeaderCell(
                new Cell().SetBackgroundColor(ColorPrimary).SetBorder(Border.NO_BORDER)
                    .SetPaddingTop(8).SetPaddingBottom(8).SetPaddingLeft(6).SetPaddingRight(6)
                    .Add(new Paragraph(h).SetFont(fontBold).SetFontSize(9).SetFontColor(ColorWhite)));
        }

        // Render ALL sections present in the estimate, not just 3.
        // This matches PdfLetterExport.cs section list so Box/Incoming/Outgoing/
        // Trailer/Karoseri/Jasa show up in the Modern format too.
        var sectionGroups = new[] {
            "Material Utama", "Material Pendukung", "Material Lainnya",
            "Box", "Incoming", "Outgoing", "Trailer", "Karoseri", "Jasa"
        };
        int globalNo = 0;

        foreach (var section in sectionGroups)
        {
            var sectionItems = items.Where(i => i.Section == section).ToList();
            if (sectionItems.Count == 0) continue;

            // Section header row spans the first 7 columns; last column shows subtotal.
            // Each section gets a UNIQUE bg + matching dark fg for contrast.
            var (secBg, secFg) = SectionHeaderColors(section);
            var secLabel = new Paragraph($"▶  {section.ToUpper()}")
                .SetFont(fontBold).SetFontSize(9).SetFontColor(secFg);
            var secTotal = sectionItems.Sum(i => i.LineTotal);

            // SetColspan(7) replaces 7 manual empty cells — robust to column-count changes.
            var labelCell = new Cell(1, 7).SetBackgroundColor(secBg).SetBorder(Border.NO_BORDER)
                .SetPaddingTop(6).SetPaddingBottom(6).SetPaddingLeft(6)
                .Add(secLabel);
            itemsTable.AddCell(labelCell);
            itemsTable.AddCell(new Cell().SetBackgroundColor(secBg).SetBorder(Border.NO_BORDER)
                .SetTextAlignment(TextAlignment.RIGHT).SetPaddingTop(6).SetPaddingBottom(6).SetPaddingRight(6)
                .Add(new Paragraph(FormatRupiah(secTotal)).SetFont(fontBold).SetFontSize(9).SetFontColor(secFg)));

            // Item rows
            foreach (var item in sectionItems)
            {
                globalNo++;
                var rowBg = globalNo % 2 == 0 ? ColorLightGray : ColorWhite;
                var adjStr = item.AdjPercent == 0 ? "—"
                    : (item.AdjPercent > 0 ? $"+{item.AdjPercent:N1}%" : $"{item.AdjPercent:N1}%");

                AddItemCell(itemsTable, globalNo.ToString(),          fontRegular, rowBg, TextAlignment.CENTER);
                AddItemCell(itemsTable, item.ReferenceCode,           fontRegular, rowBg);
                AddItemCell(itemsTable, item.ProductName,             fontRegular, rowBg);
                AddItemCell(itemsTable, item.Quantity.ToString(),     fontRegular, rowBg, TextAlignment.CENTER);
                AddItemCell(itemsTable, item.Satuan,                  fontRegular, rowBg, TextAlignment.CENTER);
                AddItemCell(itemsTable, FormatRupiah(item.UnitPrice), fontRegular, rowBg, TextAlignment.RIGHT);
                AddItemCell(itemsTable, adjStr,                       fontRegular, rowBg, TextAlignment.CENTER);
                AddItemCell(itemsTable, FormatRupiah(item.LineTotal), fontBold,    rowBg, TextAlignment.RIGHT);
            }
        }

        document.Add(itemsTable);

        // ── COST SUMMARY ─────────────────────────────────────────────────
        var summaryTable = new Table(UnitValue.CreatePercentArray(new float[] { 55, 45 }))
            .UseAllAvailableWidth().SetMarginBottom(10);

        void AddSummaryRow(string label, string value, bool bold = false, bool isDiscount = false)
        {
            summaryTable.AddCell(new Cell().SetBorder(Border.NO_BORDER));
            var inner = new Table(UnitValue.CreatePercentArray(new float[] { 55, 45 })).UseAllAvailableWidth();
            var labelColor = isDiscount ? new DeviceRgb(220, 38, 38) : (bold ? ColorDark : ColorMuted);
            var valueColor = isDiscount ? new DeviceRgb(220, 38, 38) : (bold ? ColorDark : ColorMuted);

            inner.AddCell(new Cell().SetBorder(Border.NO_BORDER)
                .SetBorderBottom(new SolidBorder(ColorBorderRow, 0.5f))
                .SetPaddingTop(5).SetPaddingBottom(5)
                .Add(new Paragraph(label).SetFont(bold ? fontBold : fontRegular)
                    .SetFontSize(9).SetFontColor(labelColor)));
            inner.AddCell(new Cell().SetBorder(Border.NO_BORDER)
                .SetBorderBottom(new SolidBorder(ColorBorderRow, 0.5f))
                .SetTextAlignment(TextAlignment.RIGHT).SetPaddingTop(5).SetPaddingBottom(5)
                .Add(new Paragraph(value).SetFont(bold ? fontBold : fontRegular)
                    .SetFontSize(9).SetFontColor(valueColor)));
            summaryTable.AddCell(new Cell().SetBorder(Border.NO_BORDER).Add(inner));
        }

        bool isDiskon = marginPercent < 0;
        AddSummaryRow("Subtotal", FormatRupiah(subtotal));
        if (isDiskon)
            AddSummaryRow($"Diskon ({Math.Abs(marginPercent):F1}%)", $"- {FormatRupiah(Math.Abs(marginAmount))}", isDiscount: true);
        else
            AddSummaryRow($"Margin ({marginPercent:F1}%)", FormatRupiah(marginAmount));
        if (shippingCost > 0)
            AddSummaryRow("Ongkos Kirim", FormatRupiah(shippingCost));
        // DPP = subtotal + margin + ongkir (basis perhitungan PPN)
        decimal dpp = subtotal + marginAmount + shippingCost;
        AddSummaryRow("DPP (Dasar Pengenaan Pajak)", FormatRupiah(dpp), bold: true);
        if (taxAmount > 0)
            AddSummaryRow($"PPN ({taxPercent:F1}%)", FormatRupiah(taxAmount));
        if (pphAmount > 0)
            AddSummaryRow($"PPh ({pphPercent:F1}%) ditahan", $"- {FormatRupiah(pphAmount)}", isDiscount: true);
        document.Add(summaryTable);

        // Total row
        var totalTable = new Table(UnitValue.CreatePercentArray(new float[] { 55, 45 }))
            .UseAllAvailableWidth().SetMarginBottom(30);
        totalTable.AddCell(new Cell().SetBorder(Border.NO_BORDER));
        var totalInner = new Table(UnitValue.CreatePercentArray(new float[] { 50, 50 })).UseAllAvailableWidth();
        totalInner.AddCell(new Cell().SetBackgroundColor(ColorPrimary).SetBorder(Border.NO_BORDER)
            .SetPaddingTop(10).SetPaddingBottom(10).SetPaddingLeft(10)
            .Add(new Paragraph("TOTAL HARGA").SetFont(fontBold).SetFontSize(10).SetFontColor(ColorWhite)));
        totalInner.AddCell(new Cell().SetBackgroundColor(ColorPrimary).SetBorder(Border.NO_BORDER)
            .SetTextAlignment(TextAlignment.RIGHT).SetPaddingTop(10).SetPaddingBottom(10).SetPaddingRight(10)
            .Add(new Paragraph(FormatRupiah(total)).SetFont(fontBold).SetFontSize(12).SetFontColor(ColorWhite)));
        totalTable.AddCell(new Cell().SetBorder(Border.NO_BORDER).Add(totalInner));
        document.Add(totalTable);

        // ── TERBILANG ────────────────────────────────────────────────────
        document.Add(new Paragraph("Terbilang: " + TerbilangFormatter.ToRupiah(total))
            .SetFont(fontBold).SetFontSize(10).SetFontColor(ColorDark)
            .SetItalic().SetMarginBottom(14));

        // ── SYARAT & KETENTUAN ───────────────────────────────────────────
        document.Add(new Paragraph("Syarat & Ketentuan")
            .SetFont(fontBold).SetFontSize(10).SetFontColor(ColorDark).SetMarginBottom(4));
        var skItems = new[]
        {
            "Penawaran berlaku selama 14 (empat belas) hari sejak tanggal penawaran.",
            taxPercent > 0 ? $"Harga belum termasuk PPN {taxPercent:F0}% (menyesuaikan peraturan pemerintah)."
                           : "Harga sudah termasuk PPN.",
            "Pembayaran: DP 30% saat PO diterima, pelunasan 70% sebelum pengiriman.",
            "Lead time pengiriman menyesuaikan ketersediaan stok / indent vendor.",
            "Garansi produk mengikuti garansi pabrikan masing-masing brand.",
            "Harga tidak terikat dan dapat berubah sewaktu-waktu bila ada perubahan harga vendor.",
        };
        int sk = 0;
        foreach (var line in skItems)
        {
            sk++;
            document.Add(new Paragraph($"{sk}. {line}")
                .SetFont(fontRegular).SetFontSize(9).SetFontColor(ColorDark).SetMarginBottom(1));
        }

        // ── SIGNATURE BLOCK ──────────────────────────────────────────────
        var signerName  = GetSetting(settings, "SignerName",    "");
        var signerTitle = GetSetting(settings, "SignerTitle",   "Marketing");
        var offerCity   = GetSetting(settings, "OfferLocation", "Bandung");
        var dateStr     = createdDate.ToLocalTime().ToString("dd MMMM yyyy",
                            System.Globalization.CultureInfo.GetCultureInfo("id-ID"));

        var sigTable = new Table(UnitValue.CreatePercentArray(new float[] { 55, 45 }))
            .UseAllAvailableWidth().SetMarginTop(18).SetBorder(Border.NO_BORDER);
        sigTable.AddCell(new Cell().SetBorder(Border.NO_BORDER));
        var sigCell = new Cell().SetBorder(Border.NO_BORDER)
            .Add(new Paragraph($"{offerCity}, {dateStr}").SetFont(fontRegular).SetFontSize(9).SetFontColor(ColorDark).SetMarginBottom(1))
            .Add(new Paragraph(companyName).SetFont(fontRegular).SetFontSize(9).SetFontColor(ColorDark).SetMarginBottom(46));
        if (!string.IsNullOrWhiteSpace(signerName))
            sigCell.Add(new Paragraph(signerName).SetFont(fontBold).SetFontSize(10).SetFontColor(ColorDark));
        else
            sigCell.Add(new Paragraph("(______________________)").SetFont(fontRegular).SetFontSize(9).SetFontColor(ColorMuted));
        if (!string.IsNullOrWhiteSpace(signerTitle))
            sigCell.Add(new Paragraph(signerTitle).SetFont(fontRegular).SetFontSize(9).SetFontColor(ColorMuted));
        sigTable.AddCell(sigCell);
        document.Add(sigTable);

        // ── FOOTER ───────────────────────────────────────────────────────
        document.Add(new LineSeparator(new iText.Kernel.Pdf.Canvas.Draw.SolidLine(0.5f))
            .SetStrokeColor(ColorBorderRow).SetMarginTop(20).SetMarginBottom(6));
        document.Add(new Paragraph(
            "Dokumen ini diterbitkan secara elektronik dan sah tanpa tanda tangan basah.")
            .SetFont(fontRegular).SetFontSize(8).SetFontColor(ColorMuted).SetTextAlignment(TextAlignment.CENTER));
    }

    // ── Helpers ──────────────────────────────────────────────────────────
    /// <summary>
    /// Returns the (background, foreground) tuple for a section header. Each
    /// canonical section gets its own colour pair — no rotation — so a PDF
    /// containing all sections reads as 7 distinct bands. Unknown sections
    /// fall back to slate (engineer-neutral grey-blue).
    /// </summary>
    private static (DeviceRgb bg, DeviceRgb fg) SectionHeaderColors(string section) => section switch
    {
        "Material Utama"     => (ColorSecBgBlue,   ColorSecFgBlue),
        "Material Pendukung" => (ColorSecBgYellow, ColorSecFgYellow),
        "Material Lainnya"   => (ColorSecBgGreen,  ColorSecFgGreen),
        "Box"                => (ColorSecBgPurple, ColorSecFgPurple),
        "Incoming"           => (ColorSecBgOrange, ColorSecFgOrange),
        "Outgoing"           => (ColorSecBgOrange, ColorSecFgOrange),
        "Trailer"            => (ColorSecBgTeal,   ColorSecFgTeal),
        "Karoseri"           => (ColorSecBgTeal,   ColorSecFgTeal),
        "Jasa"               => (ColorSecBgSlate,  ColorSecFgSlate),
        _                    => (ColorSecBgSlate,  ColorSecFgSlate)
    };

    private static void AddItemCell(
        Table table, string text, PdfFont font,
        DeviceRgb bgColor, TextAlignment align = TextAlignment.LEFT)
    {
        table.AddCell(new Cell()
            .SetBackgroundColor(bgColor).SetBorder(Border.NO_BORDER)
            .SetBorderBottom(new SolidBorder(ColorBorderRow, 0.5f))
            .SetPaddingTop(6).SetPaddingBottom(6).SetPaddingLeft(6).SetPaddingRight(6)
            .SetTextAlignment(align)
            .Add(new Paragraph(text).SetFont(font).SetFontSize(9).SetFontColor(ColorDark)));
    }

    private static string GetSetting(IDictionary<string, string> settings, string key, string fallback)
        => settings.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : fallback;

    private static string FormatRupiah(decimal value)
        => "Rp " + value.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("id-ID"));

    // ── Public record ─────────────────────────────────────────────────────
    public record LineItem(
        string  ReferenceCode,
        string  ProductName,
        string  Section,
        int     Quantity,
        string  Satuan,
        decimal UnitPrice,
        decimal AdjPercent,
        decimal LineTotal);
}
