using iText.IO.Font.Constants;
using iText.IO.Image;
using iText.Kernel.Colors;
using iText.Kernel.Events;
using iText.Kernel.Font;
using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas;
using iText.Layout;
using iText.Layout.Borders;
using iText.Layout.Element;
using iText.Layout.Properties;
using System.Globalization;

namespace PanelCalculator.WinForms.Services;

/// <summary>
/// Generates a formal Surat Penawaran Harga.
/// If "letterhead.png" (or .jpg) exists next to the EXE it is drawn as a full-page
/// background on every page (START_PAGE event), so the PDF looks like it was printed on
/// the company's official letterhead paper.
/// </summary>
public static class PdfLetterExport
{
    // ── Palette (content only – light / professional) ─────────────────────
    private static readonly DeviceRgb ColorDark     = new(25,  25,  25);
    private static readonly DeviceRgb ColorMuted    = new(100, 100, 100);
    private static readonly DeviceRgb ColorTableHdr = new(210, 225, 245);
    private static readonly DeviceRgb ColorTableAlt = new(245, 249, 255);
    private static readonly DeviceRgb ColorWhite    = new(255, 255, 255);
    private static readonly DeviceRgb ColorBorder   = new(170, 185, 210);
    private static readonly DeviceRgb ColorTotal    = new(235, 242, 255);

    private static readonly CultureInfo IdCulture = CultureInfo.GetCultureInfo("id-ID");

    // ── Public line-item record ───────────────────────────────────────────
    public record LineItem(
        string  ReferenceCode,
        string  ProductName,
        string  Vendor,
        string  Section,
        int     Quantity,
        string  Satuan,
        decimal UnitPrice,
        decimal LineTotal);

    // ── Public API ────────────────────────────────────────────────────────
    public static void Generate(
        string  outputPath,
        string  estimationNumber,
        string  clientName,
        string? contactPhone,
        string? company,
        string? address,
        string? perihal,
        DateTime createdDate,
        string  notes,
        IReadOnlyList<LineItem> items,
        decimal subtotal,
        decimal margin1Percent,
        decimal margin2Percent,
        decimal margin3Percent,
        decimal marginAmount,
        decimal shippingCost,
        decimal taxPercent,
        decimal taxAmount,
        decimal pphPercent,
        decimal pphAmount,
        decimal total,
        IDictionary<string, string> settings)
    {
        using var writer = new PdfWriter(outputPath);
        using var pdf    = new PdfDocument(writer);

        var reg  = PdfFontFactory.CreateFont(StandardFonts.HELVETICA);
        var bold = PdfFontFactory.CreateFont(StandardFonts.HELVETICA_BOLD);

        // ── Settings ─────────────────────────────────────────────────────
        var signerName    = Get(settings, "SignerName",    "");
        var signerTitle   = Get(settings, "SignerTitle",   "Marketing");
        var offerLocation = Get(settings, "OfferLocation", "Bandung");

        // ── Letterhead background image ───────────────────────────────────
        // Look for letterhead.png / letterhead.jpg next to the EXE (or path in settings)
        var bgPath = FindLetterheadImage(settings);
        if (bgPath != null)
            pdf.AddEventHandler(PdfDocumentEvent.START_PAGE,
                new BackgroundImageHandler(bgPath));

        // Margins sized to match the letterhead body area:
        //   top  ~90 pt  = below the company logo + separator line
        //   bot  ~72 pt  = above address lines + footer bar
        using var doc = new Document(pdf, PageSize.A4);
        doc.SetMargins(90f, 60f, 72f, 60f);

        // ── Page 1 ────────────────────────────────────────────────────────
        Page1(doc, reg, bold,
            estimationNumber, clientName, contactPhone, company, address, perihal,
            createdDate, notes, items,
            subtotal, marginAmount, shippingCost, taxPercent, taxAmount, pphAmount, total,
            signerName, signerTitle, offerLocation);

        // ── Page 2 ────────────────────────────────────────────────────────
        doc.Add(new AreaBreak(AreaBreakType.NEXT_PAGE));
        Page2(doc, reg, bold, items, estimationNumber);
    }

    // ── Find the letterhead image file ────────────────────────────────────
    private static string? FindLetterheadImage(IDictionary<string, string> settings)
    {
        // 1. Explicit path in Settings
        if (settings.TryGetValue("LetterheadImagePath", out var sp) &&
            !string.IsNullOrWhiteSpace(sp) && File.Exists(sp))
            return sp;

        // 2. Next to the EXE: letterhead.png / letterhead.jpg
        var dir = AppDomain.CurrentDomain.BaseDirectory;
        foreach (var name in new[] { "letterhead.png", "letterhead.jpg", "kopsurat.png", "kopsurat.jpg" })
        {
            var path = System.IO.Path.Combine(dir, name);
            if (File.Exists(path)) return path;
        }
        return null;   // no letterhead image found — no background drawn
    }

    // ══════════════════════════════════════════════════════════════════════
    //  BACKGROUND IMAGE HANDLER  —  draws letterhead on every page
    // ══════════════════════════════════════════════════════════════════════
    private sealed class BackgroundImageHandler : IEventHandler
    {
        private readonly string   _imagePath;
        private ImageData?        _imageData;

        public BackgroundImageHandler(string imagePath) { _imagePath = imagePath; }

        public void HandleEvent(Event evt)
        {
            if (evt is not PdfDocumentEvent docEvt) return;
            var page = docEvt.GetPage();
            var sz   = page.GetPageSize();
            float w  = sz.GetWidth();
            float h  = sz.GetHeight();

            // Lazy-load once, reuse for all pages
            _imageData ??= ImageDataFactory.Create(_imagePath);

            // Draw image stretched to fill the entire page (background layer)
            var cv = new PdfCanvas(page);
            // Transformation matrix: [width, 0, 0, height, x0, y0]
            cv.AddImageWithTransformationMatrix(_imageData, w, 0f, 0f, h, 0f, 0f, false);
            cv.Release();
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    //  PAGE 1  –  Surat Penawaran Harga
    // ══════════════════════════════════════════════════════════════════════
    private static void Page1(
        Document doc, PdfFont reg, PdfFont bold,
        string estNo,
        string clientName, string? contactPhone, string? company, string? address, string? perihal,
        DateTime date, string notes,
        IReadOnlyList<LineItem> items,
        decimal subtotal, decimal marginAmount, decimal shippingCost,
        decimal taxPct, decimal taxAmt, decimal pphAmt, decimal total,
        string signerName, string signerTitle, string offerLocation)
    {
        var city    = !string.IsNullOrWhiteSpace(offerLocation) ? offerLocation : "Bandung";
        var dateStr = date.ToLocalTime().ToString("dd MMMM yyyy", IdCulture);

        // ── Two-column header: [Nomor/Perihal/Lampiran] | [Kepada + address] ──
        var hdrTbl = new Table(UnitValue.CreatePercentArray(new float[] { 48, 52 }))
            .UseAllAvailableWidth().SetBorder(Border.NO_BORDER).SetMarginBottom(10);

        // Left: ref block
        var leftRefTbl = new Table(UnitValue.CreatePercentArray(new float[] { 28, 4, 68 }))
            .UseAllAvailableWidth().SetBorder(Border.NO_BORDER);
        AddRef(leftRefTbl, "Nomor",    estNo,              reg, bold);
        AddRef(leftRefTbl, "Perihal",  !string.IsNullOrWhiteSpace(perihal) ? perihal : "Informasi Harga", reg, bold);
        AddRef(leftRefTbl, "Lampiran", "Rincian Material", reg, bold);
        var leftCell = new Cell().SetBorder(Border.NO_BORDER).Add(leftRefTbl);
        hdrTbl.AddCell(leftCell);

        // Right: Kepada block
        // If company provided: "Kepada: [company]" + address, then "Up. [client]" centered
        // If no company:       "Kepada Yth. [client]" + address (no Up. line)
        bool hasCompany = !string.IsNullOrWhiteSpace(company);
        var rightCell = new Cell().SetBorder(Border.NO_BORDER);
        rightCell.Add(P("Kepada:", bold, 10, ColorDark).SetMarginBottom(1));
        if (hasCompany)
            rightCell.Add(P(company!, bold, 10, ColorDark).SetMarginBottom(0));
        else if (!string.IsNullOrWhiteSpace(clientName))
            rightCell.Add(P(clientName, bold, 10, ColorDark).SetMarginBottom(0));
        if (!string.IsNullOrWhiteSpace(address))
        {
            foreach (var line in address.Split(new[]{'\n','\r'}, StringSplitOptions.RemoveEmptyEntries))
                rightCell.Add(P(line.Trim(), reg, 10, ColorDark).SetMarginBottom(0));
        }
        if (!string.IsNullOrWhiteSpace(contactPhone))
            rightCell.Add(P($"Telp: {contactPhone}", reg, 10, ColorDark).SetMarginBottom(0));
        // "Up." stays inside the right cell — aligned with Kepada block
        if (hasCompany && !string.IsNullOrWhiteSpace(clientName))
            rightCell.Add(P($"\nUp. {clientName}", bold, 10, ColorDark).SetMarginBottom(0));
        hdrTbl.AddCell(rightCell);
        doc.Add(hdrTbl);
        doc.Add(P("", reg, 4, ColorDark).SetMarginBottom(10));

        // ── Salutation ────────────────────────────────────────────────────
        doc.Add(P("Dengan hormat,", reg, 10, ColorDark).SetMarginBottom(4));
        doc.Add(P(
            "Berikut ini kami sampaikan informasi harga Panel sebagai berikut:",
            reg, 10, ColorDark).SetMarginBottom(10));

        // ── Price summary table ───────────────────────────────────────────
        var allSections = new[] { "Material Utama", "Material Pendukung", "Material Lainnya",
            "Box", "Incoming", "Outgoing", "Trailer", "Karoseri", "Jasa" };
        var sectionTotals = allSections
            .Select(s => (Name: s, Total: items.Where(i => i.Section == s).Sum(i => i.LineTotal)))
            .Where(x => x.Total > 0)
            .ToList();

        float[] pw = { 8, 62, 30 };
        var ptbl = new Table(UnitValue.CreatePercentArray(pw)).UseAllAvailableWidth().SetMarginBottom(4);
        TblHdr(ptbl, bold,
            new[] { "No.", "Nama Barang", "Harga Satuan (Rp)" },
            new[] { TextAlignment.CENTER, TextAlignment.LEFT, TextAlignment.RIGHT });

        int no = 0;
        foreach (var (name, st) in sectionTotals)
        {
            no++;
            var bg = no % 2 == 0 ? ColorTableAlt : ColorWhite;
            ptbl.AddCell(DC($"{no}.",      reg, 9, bg, TextAlignment.CENTER));
            ptbl.AddCell(DC(name,          reg, 9, bg, TextAlignment.LEFT));
            ptbl.AddCell(DC(FmtNum(st) + ",-", reg, 9, bg, TextAlignment.RIGHT));
        }
        doc.Add(ptbl);

        if (taxPct > 0)
            doc.Add(P($"*) Harga belum termasuk PPN {taxPct:F0}%",
                reg, 8, ColorMuted).SetMarginBottom(10).SetMarginLeft(2));
        else
            doc.Add(P("", reg, 4, ColorDark).SetMarginBottom(8));

        // ── Kondisi Penawaran ─────────────────────────────────────────────
        doc.Add(P("Kondisi Penawaran :", bold, 10, ColorDark).SetMarginBottom(4));
        var conds = new[]
        {
            taxPct > 0
                ? $"Harga belum termasuk PPN (menyesuaikan peraturan pemerintah)"
                : "Harga sudah termasuk PPN",
            $"Harga loco {city}",
            "DP 30% saat PO kami terima dan pelunasan 70% pada saat barang akan dikirimkan",
            "Harga tidak terikat dan dapat berubah sewaktu-waktu"
        };
        for (int ci = 0; ci < conds.Length; ci++)
            doc.Add(P($"{ci + 1}. {conds[ci]}", reg, 10, ColorDark).SetMarginBottom(2));

        if (!string.IsNullOrWhiteSpace(notes))
        {
            doc.Add(P("", reg, 4, ColorDark).SetMarginBottom(4));
            doc.Add(P($"Catatan: {notes}", reg, 9, ColorMuted).SetMarginBottom(2));
        }
        doc.Add(P("", reg, 4, ColorDark).SetMarginBottom(14));

        // ── Closing ───────────────────────────────────────────────────────
        doc.Add(P(
            "Demikian surat penawaran ini kami sampaikan. " +
            "Atas perhatian dan kerja samanya, kami ucapkan terima kasih.",
            reg, 10, ColorDark).SetMarginBottom(20));

        // ── Signature block ───────────────────────────────────────────────
        var sigTbl = new Table(UnitValue.CreatePercentArray(new float[] { 45, 55 }))
            .UseAllAvailableWidth().SetBorder(Border.NO_BORDER);
        var sigCell = new Cell().SetBorder(Border.NO_BORDER)
            .Add(P($"{city}, {dateStr}", reg, 10, ColorDark).SetMarginBottom(1))
            .Add(P("PT. Tritunggal Swarna", reg, 10, ColorDark).SetMarginBottom(46));
        if (!string.IsNullOrWhiteSpace(signerName))
            sigCell.Add(P(signerName,  bold, 10, ColorDark).SetMarginBottom(0));
        if (!string.IsNullOrWhiteSpace(signerTitle))
            sigCell.Add(P(signerTitle, reg,   9, ColorMuted));
        sigTbl.AddCell(sigCell);
        sigTbl.AddCell(new Cell().SetBorder(Border.NO_BORDER));
        doc.Add(sigTbl);
    }

    // ══════════════════════════════════════════════════════════════════════
    //  PAGE 2  –  Rincian Material
    // ══════════════════════════════════════════════════════════════════════
    private static void Page2(
        Document doc, PdfFont reg, PdfFont bold,
        IReadOnlyList<LineItem> items, string estNo)
    {
        doc.Add(P("RINCIAN MATERIAL", bold, 14, ColorDark)
            .SetTextAlignment(TextAlignment.CENTER).SetMarginBottom(2));
        doc.Add(P($"Ref: {estNo}", reg, 9, ColorMuted)
            .SetTextAlignment(TextAlignment.CENTER).SetMarginBottom(10));
        doc.Add(new LineSeparator(new iText.Kernel.Pdf.Canvas.Draw.SolidLine(1f))
            .SetStrokeColor(ColorDark).SetMarginBottom(14));

        var sections = new[] { "Material Utama", "Material Pendukung", "Material Lainnya",
            "Box", "Incoming", "Outgoing", "Trailer", "Karoseri", "Jasa" };
        int panelNo  = 0;

        foreach (var sec in sections)
        {
            var secItems = items.Where(i => i.Section == sec).ToList();
            if (secItems.Count == 0) continue;
            panelNo++;

            doc.Add(P($"{panelNo}. {sec}", bold, 11, ColorDark).SetMarginBottom(6));

            float[] cw = { 6, 37, 16, 23, 9, 9 };
            var tbl = new Table(UnitValue.CreatePercentArray(cw))
                .UseAllAvailableWidth().SetMarginBottom(18);
            TblHdr(tbl, bold,
                new[] { "No", "Material", "Merek", "Tipe", "Satuan", "Jumlah" },
                new[] { TextAlignment.CENTER, TextAlignment.LEFT, TextAlignment.LEFT,
                        TextAlignment.LEFT,   TextAlignment.CENTER, TextAlignment.CENTER });

            int itemNo = 0;
            foreach (var item in secItems)
            {
                itemNo++;
                var bg = itemNo % 2 == 0 ? ColorTableAlt : ColorWhite;
                tbl.AddCell(DC(itemNo.ToString(),        reg, 9, bg, TextAlignment.CENTER));
                tbl.AddCell(DC(item.ProductName,         reg, 9, bg, TextAlignment.LEFT));
                tbl.AddCell(DC(item.Vendor,              reg, 9, bg, TextAlignment.LEFT));
                tbl.AddCell(DC(item.ReferenceCode,       reg, 9, bg, TextAlignment.LEFT));
                tbl.AddCell(DC(item.Satuan,              reg, 9, bg, TextAlignment.CENTER));
                tbl.AddCell(DC(item.Quantity.ToString(), reg, 9, bg, TextAlignment.CENTER));
            }
            doc.Add(tbl);
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    //  HELPERS
    // ══════════════════════════════════════════════════════════════════════
    private static Paragraph P(string text, PdfFont font, float size, DeviceRgb color)
        => new Paragraph(text).SetFont(font).SetFontSize(size).SetFontColor(color)
            .SetMultipliedLeading(1.2f);

    private static Cell NB(string text, PdfFont font, float size, DeviceRgb color)
        => new Cell().SetBorder(Border.NO_BORDER)
            .SetPadding(0).SetPaddingTop(2).SetPaddingBottom(2)   // zero horizontal — avoids double-gap around ':'
            .Add(P(text, font, size, color));

    private static Cell DC(string text, PdfFont font, float size, DeviceRgb bg,
        TextAlignment align = TextAlignment.LEFT)
        => new Cell()
            .SetBackgroundColor(bg)
            .SetBorder(new SolidBorder(ColorBorder, 0.3f))
            .SetPaddingTop(5).SetPaddingBottom(5).SetPaddingLeft(6).SetPaddingRight(6)
            .SetTextAlignment(align)
            .Add(P(text, font, size, ColorDark));

    private static void TblHdr(Table tbl, PdfFont bold, string[] hdrs, TextAlignment[] aligns)
    {
        for (int i = 0; i < hdrs.Length; i++)
            tbl.AddHeaderCell(new Cell()
                .SetBackgroundColor(ColorTableHdr)
                .SetBorder(new SolidBorder(ColorBorder, 0.5f))
                .SetPaddingTop(6).SetPaddingBottom(6).SetPaddingLeft(6).SetPaddingRight(6)
                .SetTextAlignment(aligns[i])
                .Add(P(hdrs[i], bold, 9, ColorDark)));
    }

    private static void AddRef(Table tbl, string label, string value, PdfFont reg, PdfFont bold)
    {
        // Label left-aligned → unused column width = natural gap before ':'
        tbl.AddCell(NB(label, bold, 10, ColorDark));
        // ':' flush to label, 4 pt right padding = one space after ':'
        tbl.AddCell(NB(":",   reg,  10, ColorDark).SetPaddingRight(4));
        // Value starts right after the one-space gap
        tbl.AddCell(NB(value, reg,  10, ColorDark));
    }

    private static string Get(IDictionary<string, string> s, string key, string fallback)
        => s.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : fallback;

    private static string Rp(decimal value)
        => "Rp " + value.ToString("N0", IdCulture);

    /// <summary>Format number Indonesian style without "Rp" prefix (e.g. 31.284.000)</summary>
    private static string FmtNum(decimal value)
        => value.ToString("N0", IdCulture);
}
