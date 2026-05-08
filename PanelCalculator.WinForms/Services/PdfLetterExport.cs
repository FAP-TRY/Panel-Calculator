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
            estimationNumber, clientName, contactPhone, company, address,
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
        string clientName, string? contactPhone, string? company, string? address,
        DateTime date, string notes,
        IReadOnlyList<LineItem> items,
        decimal subtotal, decimal marginAmount, decimal shippingCost,
        decimal taxPct, decimal taxAmt, decimal pphAmt, decimal total,
        string signerName, string signerTitle, string offerLocation)
    {
        // ── Date (right-aligned) ──────────────────────────────────────────
        var city    = !string.IsNullOrWhiteSpace(offerLocation) ? offerLocation : "Bandung";
        var dateStr = date.ToLocalTime().ToString("dd MMMM yyyy", IdCulture);
        doc.Add(P($"{city}, {dateStr}", reg, 10, ColorDark)
            .SetTextAlignment(TextAlignment.RIGHT).SetMarginBottom(14));

        // ── Nomor / Perihal / Lampiran ────────────────────────────────────
        var refTbl = new Table(UnitValue.CreatePercentArray(new float[] { 20, 2, 78 }))
            .UseAllAvailableWidth().SetBorder(Border.NO_BORDER).SetMarginBottom(14);
        AddRef(refTbl, "Nomor",    estNo,              reg, bold);
        AddRef(refTbl, "Perihal",  "Informasi Harga",  reg, bold);
        AddRef(refTbl, "Lampiran", "Rincian Material", reg, bold);
        doc.Add(refTbl);

        // ── Kepada Yth. ───────────────────────────────────────────────────
        doc.Add(P("Kepada Yth.", bold, 10, ColorDark).SetMarginBottom(1));
        var toLines = new List<string> { clientName };
        if (!string.IsNullOrWhiteSpace(company))      toLines.Add(company);
        if (!string.IsNullOrWhiteSpace(address))      toLines.Add(address);
        if (!string.IsNullOrWhiteSpace(contactPhone)) toLines.Add($"Telp: {contactPhone}");
        foreach (var line in toLines)
            doc.Add(P(line, reg, 10, ColorDark).SetMarginBottom(0));
        doc.Add(P("", reg, 4, ColorDark).SetMarginBottom(12));

        // ── Salutation ────────────────────────────────────────────────────
        doc.Add(P("Dengan hormat,", reg, 10, ColorDark).SetMarginBottom(8));
        doc.Add(P(
            "Bersama surat ini kami sampaikan informasi harga untuk pengadaan " +
            "material panel listrik kepada Bapak/Ibu. " +
            "Adapun rincian harga adalah sebagai berikut:",
            reg, 10, ColorDark).SetMarginBottom(12));

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
            new[] { "No.", "Nama Barang", "Harga" },
            new[] { TextAlignment.CENTER, TextAlignment.LEFT, TextAlignment.RIGHT });

        int no = 0;
        foreach (var (name, st) in sectionTotals)
        {
            no++;
            var bg = no % 2 == 0 ? ColorTableAlt : ColorWhite;
            ptbl.AddCell(DC(no.ToString(),  reg, 9, bg, TextAlignment.CENTER));
            ptbl.AddCell(DC(name,           reg, 9, bg, TextAlignment.LEFT));
            ptbl.AddCell(DC(Rp(st) + ",-", reg, 9, bg, TextAlignment.RIGHT));
        }

        // Total row
        ptbl.AddCell(new Cell(1, 2)
            .SetBackgroundColor(ColorTotal).SetBorder(new SolidBorder(ColorBorder, 0.5f))
            .SetPaddingTop(6).SetPaddingBottom(6).SetPaddingLeft(6)
            .Add(P("Total", bold, 9, ColorDark)));
        ptbl.AddCell(new Cell()
            .SetBackgroundColor(ColorTotal).SetBorder(new SolidBorder(ColorBorder, 0.5f))
            .SetTextAlignment(TextAlignment.RIGHT)
            .SetPaddingTop(6).SetPaddingBottom(6).SetPaddingRight(6)
            .Add(P(Rp(subtotal) + ",-", bold, 9, ColorDark)));
        doc.Add(ptbl);

        if (taxPct > 0)
            doc.Add(P($"*) Harga belum termasuk PPN {taxPct:F0}%",
                reg, 8, ColorMuted).SetMarginBottom(10).SetMarginLeft(2));
        else
            doc.Add(P("", reg, 4, ColorDark).SetMarginBottom(8));

        // ── Kondisi Penawaran ─────────────────────────────────────────────
        doc.Add(P("Kondisi Penawaran:", bold, 10, ColorDark).SetMarginBottom(4));
        var conds = new[]
        {
            taxPct > 0 ? $"Harga belum termasuk PPN {taxPct:F0}%" : "Harga sudah termasuk PPN",
            $"Loco {city}",
            "Uang muka 30% dari total harga",
            "Harga dapat berubah sewaktu-waktu tanpa pemberitahuan terlebih dahulu"
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
            "Demikian informasi harga yang dapat kami sampaikan. " +
            "Atas perhatian dan kerja sama Bapak/Ibu, kami ucapkan terima kasih.",
            reg, 10, ColorDark).SetMarginBottom(18));

        // ── Signature ─────────────────────────────────────────────────────
        var sigTbl = new Table(UnitValue.CreatePercentArray(new float[] { 55, 45 }))
            .UseAllAvailableWidth().SetBorder(Border.NO_BORDER);
        var sigCell = new Cell().SetBorder(Border.NO_BORDER)
            .Add(P("Hormat kami,",          reg,  10, ColorDark).SetMarginBottom(2))
            .Add(P("PT. Tritunggal Swarna", bold, 10, ColorDark).SetMarginBottom(50));
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
            .SetPaddingTop(1).SetPaddingBottom(1)
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
        tbl.AddCell(NB(label, bold, 10, ColorDark));
        tbl.AddCell(NB(":",   reg,  10, ColorDark));
        tbl.AddCell(NB(value, reg,  10, ColorDark));
    }

    private static string Get(IDictionary<string, string> s, string key, string fallback)
        => s.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : fallback;

    private static string Rp(decimal value)
        => "Rp " + value.ToString("N0", IdCulture);
}
