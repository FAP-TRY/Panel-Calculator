using iText.IO.Font.Constants;
using iText.Kernel.Colors;
using iText.Kernel.Font;
using iText.Kernel.Pdf;
using iText.Layout;
using iText.Layout.Borders;
using iText.Layout.Element;
using iText.Layout.Properties;

namespace PanelCalculator.WinForms.Services;

public static class PdfQuotationExport
{
    private static readonly DeviceRgb ColorPrimary    = new(37,  99,  235);
    private static readonly DeviceRgb ColorDark       = new(17,  24,  39);
    private static readonly DeviceRgb ColorMuted      = new(107, 114, 128);
    private static readonly DeviceRgb ColorBorderRow  = new(229, 231, 235);
    private static readonly DeviceRgb ColorWhite      = new(255, 255, 255);
    private static readonly DeviceRgb ColorLightGray  = new(249, 250, 251);
    private static readonly DeviceRgb ColorSecHdrBlue = new(219, 234, 254);
    private static readonly DeviceRgb ColorSecHdrYellow = new(254, 249, 195);
    private static readonly DeviceRgb ColorSecHdrGreen  = new(220, 252, 231);
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

        var sectionGroups = new[] { "Material Utama", "Material Pendukung", "Material Lainnya" };
        int globalNo = 0;

        foreach (var section in sectionGroups)
        {
            var sectionItems = items.Where(i => i.Section == section).ToList();
            if (sectionItems.Count == 0) continue;

            // Section header row (spans all columns)
            var secBg = SectionHeaderColor(section);
            var secLabel = new Paragraph($"▶  {section.ToUpper()}")
                .SetFont(fontBold).SetFontSize(9).SetFontColor(ColorAccentBlue);
            var secTotal = sectionItems.Sum(i => i.LineTotal);

            // Use first 7 cols merged for label, last col for subtotal
            for (int c = 0; c < 7; c++)
            {
                var cell = new Cell().SetBackgroundColor(secBg).SetBorder(Border.NO_BORDER)
                    .SetPaddingTop(6).SetPaddingBottom(6).SetPaddingLeft(6);
                if (c == 0) cell.Add(secLabel);
                else cell.Add(new Paragraph("").SetFont(fontRegular).SetFontSize(9));
                itemsTable.AddCell(cell);
            }
            itemsTable.AddCell(new Cell().SetBackgroundColor(secBg).SetBorder(Border.NO_BORDER)
                .SetTextAlignment(TextAlignment.RIGHT).SetPaddingTop(6).SetPaddingBottom(6).SetPaddingRight(6)
                .Add(new Paragraph(FormatRupiah(secTotal)).SetFont(fontBold).SetFontSize(9).SetFontColor(ColorAccentBlue)));

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
        AddSummaryRow("Ongkos Kirim", FormatRupiah(shippingCost));
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

        // ── FOOTER ───────────────────────────────────────────────────────
        document.Add(new LineSeparator(new iText.Kernel.Pdf.Canvas.Draw.SolidLine(0.5f))
            .SetStrokeColor(ColorBorderRow).SetMarginBottom(10));
        document.Add(new Paragraph(
            "Harga berlaku selama 14 hari sejak tanggal penawaran. " +
            "Harga belum termasuk biaya instalasi kecuali disebutkan secara khusus.")
            .SetFont(fontRegular).SetFontSize(8).SetFontColor(ColorMuted).SetTextAlignment(TextAlignment.CENTER));
    }

    // ── Helpers ──────────────────────────────────────────────────────────
    private static DeviceRgb SectionHeaderColor(string section) => section switch
    {
        "Material Utama"     => ColorSecHdrBlue,
        "Material Pendukung" => ColorSecHdrYellow,
        "Material Lainnya"   => ColorSecHdrGreen,
        _                    => ColorLightGray
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
