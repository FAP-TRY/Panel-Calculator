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
using PanelCalculator.Core.Services;
using System.Globalization;
using System.Reflection;
using ITextRectangle = iText.Kernel.Geom.Rectangle;

namespace PanelCalculator.WinForms.Services;

/// <summary>
/// Generate Surat Penawaran Harga formal PT TTS dengan layout PIXEL-MATCH
/// dengan template DOCX resmi yang sudah dipakai selama ini (mis. 161 PT Gemilang,
/// 191 PT Anugerah Jaya).
/// <para>
/// Background tiap halaman diisi dengan <c>letterhead.jpg</c> embedded resource
/// (logo TTS + sertifikasi + footer alamat). Tanda tangan + stempel di-overlay
/// di akhir surat dari embedded resource <c>signature.png</c> dan <c>stamp.png</c>.
/// </para>
/// <para>
/// Section mapping ringkasan Page-1 dan divider Rincian Material Page-2:
/// "Box"/"Box Panel" → "Box Panel :",
/// "Material Utama"/"Incoming" → "Incoming :",
/// "Material Pendukung"/"Outgoing" → "Outgoing :",
/// "Material Lainnya"/"Trailer"/"Karoseri"/"Jasa" → "Lainnya :"
/// </para>
/// </summary>
public static class PdfLetterExport
{
    // ── Palette ───────────────────────────────────────────────────────────
    private static readonly DeviceRgb ColorDark   = new(25,  25,  25);
    private static readonly DeviceRgb ColorMuted  = new(110, 110, 110);
    private static readonly DeviceRgb ColorBorder = new(160, 170, 185);
    private static readonly DeviceRgb ColorRowAlt = new(248, 250, 252);

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

    public record CombinedPanel(
        string  EstimationNumber,
        string? ProjectName,
        IReadOnlyList<LineItem> Items);

    // ══════════════════════════════════════════════════════════════════════
    //  PUBLIC API — SINGLE PANEL
    // ══════════════════════════════════════════════════════════════════════
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
        ArgumentNullException.ThrowIfNull(outputPath);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(settings);

        using var writer = new PdfWriter(outputPath);
        using var pdf    = new PdfDocument(writer);

        var reg  = PdfFontFactory.CreateFont(StandardFonts.HELVETICA);
        var bold = PdfFontFactory.CreateFont(StandardFonts.HELVETICA_BOLD);

        var signerName    = Get(settings, "SignerName",    "Kuntjoro Handoko");
        var signerTitle   = Get(settings, "SignerTitle",   "Direktur");
        var offerLocation = Get(settings, "OfferLocation", "Bandung");

        // ── Letterhead background ─────────────────────────────────────────
        AttachLetterhead(pdf, settings);

        // Margins match referensi DOCX:
        //   top  = 3.0 cm (85 pt)  — di bawah logo header
        //   bot  = 1.5 cm (43 pt)  — di atas footer alamat
        //   left = 2.5 cm (71 pt)
        //   right= 1.5 cm (43 pt)
        using var doc = new Document(pdf, PageSize.A4);
        doc.SetMargins(85f, 43f, 43f, 71f);

        // Total panel = subtotal + margin (TANPA PPN — itu ditambahkan di bawah).
        // Untuk single panel, baris tabel ringkas = 1 baris dengan harga "satuan panel".
        var panelLabel = !string.IsNullOrWhiteSpace(perihal) ? perihal!.Trim() : "Penawaran Harga";
        decimal panelUnitPrice = subtotal + marginAmount;

        Page1Header(doc, reg, bold, estimationNumber,
            clientName, contactPhone, company, address,
            perihalText: "Informasi Harga",
            lampiranText: "-");

        // Salam + opening sesuai referensi single-item:
        // "Bersama dengan ini kami sampaikan informasi harga material sebagai berikut :"
        doc.Add(P("Dengan hormat,", reg, 10, ColorDark).SetMarginBottom(6));
        doc.Add(P("Bersama dengan ini kami sampaikan informasi harga material sebagai berikut :",
            reg, 10, ColorDark).SetMarginBottom(10));

        // Tabel 3-kolom (single row)
        var rows = new List<(string Label, decimal Price)>
        {
            (panelLabel, panelUnitPrice),
        };
        Add3ColTable(doc, reg, bold, rows);

        // Penutup
        AddKondisiPenawaran(doc, reg, bold, taxPercent, offerLocation, isSingle: true);

        if (!string.IsNullOrWhiteSpace(notes))
        {
            doc.Add(P("", reg, 4, ColorDark).SetMarginBottom(4));
            doc.Add(P($"Catatan: {notes}", reg, 9, ColorMuted).SetMarginBottom(4));
        }

        doc.Add(P("", reg, 4, ColorDark).SetMarginBottom(8));
        doc.Add(P(
            "Demikian informasi harga ini kami sampaikan. Atas perhatian dan kerjasamanya kami ucapkan terima kasih.",
            reg, 10, ColorDark).SetMarginBottom(20));

        AddSignatureBlock(doc, pdf, reg, bold, createdDate, offerLocation, signerName, signerTitle);
    }

    // ══════════════════════════════════════════════════════════════════════
    //  PUBLIC API — COMBINED (MULTI-PANEL)
    // ══════════════════════════════════════════════════════════════════════
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
        ArgumentNullException.ThrowIfNull(summary);
        ArgumentNullException.ThrowIfNull(settings);
        if (panels.Count == 0)
            throw new ArgumentException("Minimal satu panel diperlukan.", nameof(panels));

        using var writer = new PdfWriter(outputPath);
        using var pdf    = new PdfDocument(writer);

        var reg  = PdfFontFactory.CreateFont(StandardFonts.HELVETICA);
        var bold = PdfFontFactory.CreateFont(StandardFonts.HELVETICA_BOLD);

        var signerName    = Get(settings, "SignerName",    "Kuntjoro Handoko");
        var signerTitle   = Get(settings, "SignerTitle",   "Direktur");
        var offerLocation = Get(settings, "OfferLocation", "Bandung");

        AttachLetterhead(pdf, settings);

        using var doc = new Document(pdf, PageSize.A4);
        doc.SetMargins(85f, 43f, 43f, 71f);

        // Lampiran field menyebut "Rincian Material" karena halaman 2+ berisi rincian
        Page1Header(doc, reg, bold, nomorSurat,
            clientName, contactPhone, company, address,
            perihalText: !string.IsNullOrWhiteSpace(perihal) ? perihal! : "Penawaran Harga",
            lampiranText: "Rincian Material");

        doc.Add(P("Dengan hormat,", reg, 10, ColorDark).SetMarginBottom(6));
        doc.Add(P(
            "Bersama dengan ini kami sampaikan surat penawaran harga sebagai berikut :",
            reg, 10, ColorDark).SetMarginBottom(10));

        // Tabel 3-kolom, satu baris per panel (label = ProjectName or estimation number, harga = PanelSubtotal)
        var rows = panels.Select((p, i) =>
        {
            var label = !string.IsNullOrWhiteSpace(p.ProjectName)
                ? p.ProjectName!.Trim()
                : p.EstimationNumber;
            var price = summary.Panels[i].PanelSubtotal;
            return (Label: label, Price: price);
        }).ToList();
        Add3ColTable(doc, reg, bold, rows);

        AddKondisiPenawaran(doc, reg, bold, summary.TaxPercent, offerLocation, isSingle: false);

        if (!string.IsNullOrWhiteSpace(notes))
        {
            doc.Add(P("", reg, 4, ColorDark).SetMarginBottom(4));
            doc.Add(P($"Catatan: {notes}", reg, 9, ColorMuted).SetMarginBottom(4));
        }

        doc.Add(P("", reg, 4, ColorDark).SetMarginBottom(8));
        doc.Add(P(
            "Demikian surat penawaran ini kami sampaikan. Atas perhatian dan kerjasamanya kami ucapkan terima kasih.",
            reg, 10, ColorDark).SetMarginBottom(20));

        AddSignatureBlock(doc, pdf, reg, bold, createdDate, offerLocation, signerName, signerTitle);

        // ── Halaman Rincian Material per panel ───────────────────────────
        for (int pi = 0; pi < panels.Count; pi++)
        {
            doc.Add(new AreaBreak(AreaBreakType.NEXT_PAGE));

            var panel = panels[pi];
            var heading = !string.IsNullOrWhiteSpace(panel.ProjectName)
                ? panel.ProjectName!.Trim()
                : panel.EstimationNumber;

            // "Rincian Material" judul (center, bold, 14pt)
            doc.Add(P("Rincian Material", bold, 14, ColorDark)
                .SetTextAlignment(TextAlignment.CENTER).SetMarginBottom(6));

            // Sub-heading: nama panel
            doc.Add(P(heading, bold, 11, ColorDark).SetMarginBottom(4));

            AddRincianMaterialTable(doc, reg, bold, panel.Items);
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    //  PAGE-1 HEADER  (Nomor/Perihal/Lampiran ↔ Kepada/Address/Up.)
    // ══════════════════════════════════════════════════════════════════════
    private static void Page1Header(
        Document doc, PdfFont reg, PdfFont bold,
        string estNo,
        string clientName, string? contactPhone, string? company, string? address,
        string perihalText, string lampiranText)
    {
        var hdrTbl = new Table(UnitValue.CreatePercentArray(new float[] { 48, 52 }))
            .UseAllAvailableWidth().SetBorder(Border.NO_BORDER).SetMarginBottom(10);

        // ── LEFT: Nomor/Perihal/Lampiran ──
        var leftRefTbl = new Table(UnitValue.CreatePercentArray(new float[] { 28, 4, 68 }))
            .UseAllAvailableWidth().SetBorder(Border.NO_BORDER);
        AddRef(leftRefTbl, "Nomor",    estNo,        reg, bold);
        AddRef(leftRefTbl, "Perihal",  perihalText,  reg, bold);
        AddRef(leftRefTbl, "Lampiran", lampiranText, reg, bold);
        hdrTbl.AddCell(new Cell().SetBorder(Border.NO_BORDER).Add(leftRefTbl));

        // ── RIGHT: Kepada / Address / Up. ──
        bool hasCompany = !string.IsNullOrWhiteSpace(company);
        var rightCell = new Cell().SetBorder(Border.NO_BORDER);
        rightCell.Add(P("Kepada:", reg, 10, ColorDark).SetMarginBottom(2));

        if (hasCompany)
            rightCell.Add(P(company!, bold, 10, ColorDark).SetMarginBottom(0));
        else if (!string.IsNullOrWhiteSpace(clientName))
            rightCell.Add(P(clientName, bold, 10, ColorDark).SetMarginBottom(0));

        if (!string.IsNullOrWhiteSpace(address))
            foreach (var line in address.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
                rightCell.Add(P(line.Trim(), reg, 10, ColorDark).SetMarginBottom(0));

        if (!string.IsNullOrWhiteSpace(contactPhone))
            rightCell.Add(P($"Telp: {contactPhone}", reg, 10, ColorDark).SetMarginBottom(0));

        // "Up. <contact person>" — di bawah alamat, baris kosong di atas.
        // Hanya dimunculkan kalau clientName MEMANG berbeda dari company name
        // (artinya ada contact person yang spesifik). Kalau clientName == company
        // atau clientName kosong, "Up." tidak ditambahkan supaya tidak redundant.
        if (hasCompany && !string.IsNullOrWhiteSpace(clientName) &&
            !string.Equals(clientName.Trim(), company!.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            rightCell.Add(P("", reg, 4, ColorDark).SetMarginTop(4).SetMarginBottom(0));
            rightCell.Add(P($"Up. {clientName.Trim()}", reg, 10, ColorDark).SetMarginBottom(0));
        }

        hdrTbl.AddCell(rightCell);
        doc.Add(hdrTbl);
    }

    // ══════════════════════════════════════════════════════════════════════
    //  3-COL TABLE  (No. / Nama Barang / Harga Satuan)
    // ══════════════════════════════════════════════════════════════════════
    private static void Add3ColTable(
        Document doc, PdfFont reg, PdfFont bold,
        IReadOnlyList<(string Label, decimal Price)> rows)
    {
        float[] cw = { 8, 62, 30 };
        var tbl = new Table(UnitValue.CreatePercentArray(cw))
            .UseAllAvailableWidth().SetMarginBottom(12);

        // Header
        TblHdr(tbl, bold,
            new[] { "No.", "Nama Barang", "Harga Satuan (Rp)" },
            new[] { TextAlignment.CENTER, TextAlignment.LEFT, TextAlignment.RIGHT });

        // Data rows
        for (int i = 0; i < rows.Count; i++)
        {
            var bg = (i % 2 == 0) ? ColorRowAlt : (DeviceRgb?)null;
            tbl.AddCell(DataCell($"{i + 1}.",            reg, 9, bg, TextAlignment.CENTER));
            tbl.AddCell(DataCell(rows[i].Label,          reg, 9, bg, TextAlignment.LEFT));
            tbl.AddCell(DataCell(RpDash(rows[i].Price),  reg, 9, bg, TextAlignment.RIGHT));
        }
        doc.Add(tbl);
    }

    // ══════════════════════════════════════════════════════════════════════
    //  KONDISI PENAWARAN  (bullet list)
    // ══════════════════════════════════════════════════════════════════════
    private static void AddKondisiPenawaran(
        Document doc, PdfFont reg, PdfFont bold,
        decimal taxPercent, string offerLocation, bool isSingle)
    {
        var city = !string.IsNullOrWhiteSpace(offerLocation) ? offerLocation : "Bandung";
        doc.Add(P("Kondisi Penawaran :", bold, 10, ColorDark).SetMarginBottom(4));

        var conds = new List<string>();
        if (taxPercent > 0)
            conds.Add($"Harga belum termasuk PPN {taxPercent:0.##}% (menyesuaikan dengan peraturan pemerintah)");
        else
            conds.Add("Harga belum termasuk PPN (menyesuaikan dengan peraturan pemerintah)");
        conds.Add($"Harga loco {(string.Equals(city, "Bandung", StringComparison.OrdinalIgnoreCase) ? "Pabrik" : city)}");
        if (!isSingle)
            conds.Add("DP 30% saat PO kami terima dan pelunasan 70% sebelum barang dikirim");
        conds.Add("Harga tidak terikat dan dapat berubah sewaktu-waktu");

        foreach (var c in conds)
        {
            var bullet = new Paragraph()
                .SetFont(reg).SetFontSize(10).SetFontColor(ColorDark)
                .SetMarginBottom(2).SetMarginLeft(12)
                .Add(new Text("•  "))
                .Add(new Text(c));
            doc.Add(bullet);
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    //  SIGNATURE BLOCK  (city+date / PT TTS / sig+stamp overlay / nama / jabatan)
    // ══════════════════════════════════════════════════════════════════════
    //
    // Strategy: we capture the layout Y position BEFORE adding the text
    // signature block, then add the text block, then draw the images at
    // the right vertical offset relative to that captured Y (so images
    // overlap the gap between "PT. Tritunggal Swarna" and "Kuntjoro Handoko").
    //
    private static void AddSignatureBlock(
        Document doc, PdfDocument pdf, PdfFont reg, PdfFont bold,
        DateTime createdDate, string offerLocation, string signerName, string signerTitle)
    {
        var city    = !string.IsNullOrWhiteSpace(offerLocation) ? offerLocation : "Bandung";
        var dateStr = createdDate.ToLocalTime().ToString("dd MMMM yyyy", IdCulture);

        // Capture Y BEFORE the block is added. We need to know roughly where
        // the bottom of "PT. Tritunggal Swarna" line will sit so we can draw
        // the signature+stamp in the spacer above the signer name.
        float? yBefore = null;
        int    pageBefore = pdf.GetNumberOfPages();
        try
        {
            var r = doc.GetRenderer();
            // r.GetCurrentArea() returns the current LayoutArea; null until
            // first render. Force a renderer flush so we get a valid Y:
            // simplest is to query CurrentArea AFTER an empty add. The
            // calling code already flushed the closing paragraph, so we
            // can read directly.
            var area = r?.GetCurrentArea();
            if (area != null)
                yBefore = area.GetBBox().GetTop();
        }
        catch { /* renderer state unavailable — fall back below */ }

        // Right-aligned signature block; left half empty
        var sigTbl = new Table(UnitValue.CreatePercentArray(new float[] { 50, 50 }))
            .UseAllAvailableWidth().SetBorder(Border.NO_BORDER)
            .SetMarginTop(0);
        sigTbl.AddCell(new Cell().SetBorder(Border.NO_BORDER));

        var sigCell = new Cell().SetBorder(Border.NO_BORDER);
        sigCell.Add(P($"{city}, {dateStr}", reg, 10, ColorDark).SetMarginBottom(2));
        sigCell.Add(P("PT. Tritunggal Swarna", bold, 10, ColorDark).SetMarginBottom(0));

        // Reserve vertical space for the signature+stamp overlay
        // (must be >= signature image height + small margin = ~100pt to fit cleanly)
        const float SignatureGap = 100f;
        sigCell.Add(P("", reg, 1, ColorDark).SetMarginTop(SignatureGap).SetMarginBottom(0));

        if (!string.IsNullOrWhiteSpace(signerName))
            sigCell.Add(P(signerName, bold, 10, ColorDark).SetMarginBottom(0).SetUnderline());
        if (!string.IsNullOrWhiteSpace(signerTitle))
            sigCell.Add(P(signerTitle, reg, 10, ColorDark));

        sigTbl.AddCell(sigCell);
        doc.Add(sigTbl);

        // ── Draw signature + stamp overlay in the gap we reserved.
        try
        {
            var sigBytes   = TryReadEmbedded("PanelCalculator.WinForms.Assets.Letterhead.signature.png");
            var stampBytes = TryReadEmbedded("PanelCalculator.WinForms.Assets.Letterhead.stamp.png");
            if (sigBytes == null && stampBytes == null) return;

            // The signature should appear on the page that the last paragraph
            // of the sig block landed on. Most often that's the same page as
            // before doc.Add (the closing paragraph is small). If the renderer
            // pushed to a new page (rare; happens when the page is nearly
            // full), the closing block sits at the top of the new page and
            // we should draw the overlay there.
            int pageAfter = pdf.GetNumberOfPages();
            var page      = pdf.GetPage(pageAfter);
            var pageSize  = page.GetPageSize();
            var canvas    = new PdfCanvas(page);
            float pageWidth = pageSize.GetWidth();

            // Position the overlay at the gap reserved above the signer name.
            // The signer name was drawn directly after the SignatureGap spacer,
            // so it sits roughly `bottomMargin + 2 lines` above page bottom.
            //
            //   page bottom margin    ≈ 43 pt
            //   jabatan line height   ≈ 14 pt
            //   signerName underline  ≈ 14 pt
            //   small padding         ≈  4 pt
            // → signer name top edge  ≈ 75 pt from page bottom
            // → overlay should sit ~ a bit above this, fully inside the gap.
            //
            // BUT we need to adjust if the block flowed onto a new page.
            // Heuristic: if yBefore is null OR pageAfter != pageBefore, use
            // the fallback constant. Otherwise compute baseY = yBefore - (height
            // of date+PT TTS lines = 2*14 = 28).
            float signerNameTopY;
            if (yBefore.HasValue && pageAfter == pageBefore)
            {
                // We had a renderer position; estimate where signer name landed.
                //   yBefore  = top of the area where sigTbl starts.
                //   2 text lines used (date + PT TTS) before SignatureGap.
                //   Then SignatureGap, then signer name.
                signerNameTopY = yBefore.Value
                                - 14f * 2f         // 2 lines of header text
                                - SignatureGap;    // reserved gap
            }
            else
            {
                // Fallback: assume signer name is near page bottom.
                signerNameTopY = 43f + 28f + 4f;   // bottom margin + 2 lines + pad
            }

            // Signature: width ~120pt, height proportional (signature is 477x373)
            // → keeps it inside the SignatureGap (~100pt) and avoids spill into
            // the "PT. Tritunggal Swarna" line above.
            const float sigW = 130f;
            const float sigH = sigW * 373f / 477f;   // ≈ 101pt

            // Place signature so its BOTTOM is just above the signer name top.
            float overlaySigY = signerNameTopY + 2f;
            float sigX        = pageWidth * 0.55f;     // right column

            if (sigBytes != null)
            {
                var img = ImageDataFactory.Create(sigBytes);
                canvas.AddImageFittedIntoRectangle(img,
                    new ITextRectangle(sigX, overlaySigY, sigW, sigH), false);
            }

            // Stamp: square, overlapping signature (offset right + slight up)
            const float stampW = 85f;
            const float stampH = 85f;
            float stampX = sigX + sigW * 0.55f;
            float stampY = overlaySigY + 8f;

            if (stampBytes != null)
            {
                var img = ImageDataFactory.Create(stampBytes);
                canvas.AddImageFittedIntoRectangle(img,
                    new ITextRectangle(stampX, stampY, stampW, stampH), false);
            }

            canvas.Release();
        }
        catch
        {
            // Silently skip overlay if assets missing — text signature still renders.
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    //  RINCIAN MATERIAL (Page 2+) — section divider rows
    // ══════════════════════════════════════════════════════════════════════
    private static void AddRincianMaterialTable(
        Document doc, PdfFont reg, PdfFont bold,
        IReadOnlyList<LineItem> items)
    {
        // Kolom: No (5%) / Material (35%) / Merek (16%) / Tipe (22%) / Satuan (9%) / Jumlah (13%)
        float[] cw = { 5, 35, 16, 22, 9, 13 };
        var tbl = new Table(UnitValue.CreatePercentArray(cw))
            .UseAllAvailableWidth().SetMarginBottom(14);
        TblHdr(tbl, bold,
            new[] { "No", "Material", "Merek", "Tipe", "Satuan", "Jumlah" },
            new[] { TextAlignment.CENTER, TextAlignment.LEFT, TextAlignment.LEFT,
                    TextAlignment.LEFT,   TextAlignment.CENTER, TextAlignment.CENTER });

        // Group items by *display section* (after mapping) preserving the
        // canonical order defined below. Items keep their original order
        // inside each group.
        var groups = items
            .Select((it, idx) => (Item: it, Idx: idx, Display: MapSectionToDisplay(it.Section)))
            .GroupBy(x => x.Display)
            .OrderBy(g => DisplaySectionOrder(g.Key))
            .ToList();

        int rowNo = 0;
        foreach (var grp in groups)
        {
            if (grp.Key == null) continue;

            // Section divider row — single cell spanning all 6 cols
            var divider = new Cell(1, 6)
                .SetBorder(new SolidBorder(ColorBorder, 0.4f))
                .SetBackgroundColor(new DeviceRgb(232, 238, 246))
                .SetPaddingTop(4).SetPaddingBottom(4).SetPaddingLeft(6).SetPaddingRight(6)
                .Add(P(grp.Key + " :", bold, 9, ColorDark));
            tbl.AddCell(divider);

            foreach (var x in grp.OrderBy(g => g.Idx))
            {
                rowNo++;
                var bg = (rowNo % 2 == 0) ? ColorRowAlt : (DeviceRgb?)null;
                tbl.AddCell(DataCell(rowNo.ToString(),         reg, 9, bg, TextAlignment.CENTER));
                tbl.AddCell(DataCell(x.Item.ProductName,        reg, 9, bg, TextAlignment.LEFT));
                tbl.AddCell(DataCell(x.Item.Vendor,             reg, 9, bg, TextAlignment.LEFT));
                tbl.AddCell(DataCell(x.Item.ReferenceCode,      reg, 9, bg, TextAlignment.LEFT));
                tbl.AddCell(DataCell(x.Item.Satuan,             reg, 9, bg, TextAlignment.CENTER));
                tbl.AddCell(DataCell(x.Item.Quantity.ToString(),reg, 9, bg, TextAlignment.CENTER));
            }
        }

        doc.Add(tbl);
    }

    /// <summary>Map raw section name to display label per spec.</summary>
    internal static string? MapSectionToDisplay(string section)
    {
        var s = (section ?? "").Trim();
        if (s.Equals("Box", StringComparison.OrdinalIgnoreCase) ||
            s.Equals("Box Panel", StringComparison.OrdinalIgnoreCase))
            return "Box Panel";
        if (s.Equals("Material Utama", StringComparison.OrdinalIgnoreCase) ||
            s.Equals("Incoming", StringComparison.OrdinalIgnoreCase))
            return "Incoming";
        if (s.Equals("Material Pendukung", StringComparison.OrdinalIgnoreCase) ||
            s.Equals("Outgoing", StringComparison.OrdinalIgnoreCase))
            return "Outgoing";
        if (s.Equals("Material Lainnya", StringComparison.OrdinalIgnoreCase) ||
            s.Equals("Trailer", StringComparison.OrdinalIgnoreCase) ||
            s.Equals("Karoseri", StringComparison.OrdinalIgnoreCase) ||
            s.Equals("Jasa", StringComparison.OrdinalIgnoreCase) ||
            s.Equals("Lainnya", StringComparison.OrdinalIgnoreCase))
            return "Lainnya";
        return string.IsNullOrWhiteSpace(s) ? "Incoming" : s;
    }

    private static int DisplaySectionOrder(string? display) => display switch
    {
        "Box Panel" => 0,
        "Incoming"  => 1,
        "Outgoing"  => 2,
        "Lainnya"   => 3,
        _           => 4,
    };

    // ══════════════════════════════════════════════════════════════════════
    //  LETTERHEAD BACKGROUND HANDLER
    // ══════════════════════════════════════════════════════════════════════
    private static void AttachLetterhead(PdfDocument pdf, IDictionary<string, string> settings)
    {
        // 1. Explicit override path (debug/testing)
        if (settings.TryGetValue("LetterheadImagePath", out var sp) &&
            !string.IsNullOrWhiteSpace(sp) && File.Exists(sp))
        {
            pdf.AddEventHandler(PdfDocumentEvent.START_PAGE,
                new BackgroundImageHandler(File.ReadAllBytes(sp)));
            return;
        }

        // 2. Embedded resource
        var bytes = TryReadEmbedded("PanelCalculator.WinForms.Assets.Letterhead.letterhead.jpg");
        if (bytes != null)
            pdf.AddEventHandler(PdfDocumentEvent.START_PAGE,
                new BackgroundImageHandler(bytes));
    }

    private sealed class BackgroundImageHandler : IEventHandler
    {
        private readonly byte[] _imageBytes;
        private ImageData?      _imageData;

        public BackgroundImageHandler(byte[] imageBytes)
        {
            _imageBytes = imageBytes;
        }

        public void HandleEvent(Event evt)
        {
            if (evt is not PdfDocumentEvent docEvt) return;
            var page = docEvt.GetPage();
            var sz   = page.GetPageSize();
            float w  = sz.GetWidth();
            float h  = sz.GetHeight();

            _imageData ??= ImageDataFactory.Create(_imageBytes);

            var cv = new PdfCanvas(page);
            cv.AddImageWithTransformationMatrix(_imageData, w, 0f, 0f, h, 0f, 0f, false);
            cv.Release();
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
            .SetPadding(0).SetPaddingTop(2).SetPaddingBottom(2)
            .Add(P(text, font, size, color));

    private static Cell DataCell(string text, PdfFont font, float size,
        DeviceRgb? bg, TextAlignment align)
    {
        var c = new Cell()
            .SetBorder(new SolidBorder(ColorBorder, 0.4f))
            .SetPaddingTop(5).SetPaddingBottom(5).SetPaddingLeft(6).SetPaddingRight(6)
            .SetTextAlignment(align)
            .Add(P(text, font, size, ColorDark));
        if (bg != null) c.SetBackgroundColor(bg);
        return c;
    }

    private static void TblHdr(Table tbl, PdfFont bold, string[] hdrs, TextAlignment[] aligns)
    {
        for (int i = 0; i < hdrs.Length; i++)
            tbl.AddHeaderCell(new Cell()
                .SetBackgroundColor(new DeviceRgb(232, 238, 246))
                .SetBorder(new SolidBorder(ColorBorder, 0.6f))
                .SetPaddingTop(6).SetPaddingBottom(6).SetPaddingLeft(6).SetPaddingRight(6)
                .SetTextAlignment(aligns[i])
                .Add(P(hdrs[i], bold, 9, ColorDark)));
    }

    private static void AddRef(Table tbl, string label, string value, PdfFont reg, PdfFont bold)
    {
        tbl.AddCell(NB(label, reg, 10, ColorDark));
        tbl.AddCell(NB(":",   reg, 10, ColorDark).SetPaddingRight(4));
        tbl.AddCell(NB(value, reg, 10, ColorDark));
    }

    private static string Get(IDictionary<string, string> s, string key, string fallback)
        => s.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : fallback;

    /// <summary>Format with Indonesian decimal (e.g. 4.320.000,-)</summary>
    private static string RpDash(decimal value)
        => value.ToString("N0", IdCulture) + ",-";

    private static Stream? TryLoadEmbedded(string name)
    {
        var asm = typeof(PdfLetterExport).Assembly;
        return asm.GetManifestResourceStream(name);
    }

    private static byte[]? TryReadEmbedded(string name)
    {
        using var s = TryLoadEmbedded(name);
        if (s == null) return null;
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }

    private static byte[] ReadAll(Stream s)
    {
        if (s is MemoryStream ms2) return ms2.ToArray();
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }
}
