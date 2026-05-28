using PanelCalculator.Core.Services;
using System.Globalization;
using System.Reflection;
using Xceed.Document.NET;
using Xceed.Words.NET;
// Alias untuk resolve namespace clash:
//   Xceed.Document.NET.Orientation     vs System.Windows.Forms.Orientation
//   Xceed.Document.NET.BorderStyle     vs System.Windows.Forms.BorderStyle
//   Xceed.Drawing.Color                vs System.Drawing.Color  (DocX 4.x pakai Xceed.Drawing)
using XOrientation = Xceed.Document.NET.Orientation;
using XBorderStyle = Xceed.Document.NET.BorderStyle;
using XColor       = Xceed.Drawing.Color;

namespace PanelCalculator.WinForms.Services;

/// <summary>
/// Generate Surat Penawaran Harga formal PT TTS sebagai dokumen Word (.docx)
/// dengan layout PIXEL-MATCH dengan template DOCX resmi.
/// <para>
/// Image letterhead, signature, dan stamp dimuat dari embedded resource
/// (<c>Assets/Letterhead/*.png|.jpg</c>). Letterhead disisipkan sebagai
/// gambar inline di awal dokumen (DocX 4.x tidak support layered background
/// gambar di section properties, jadi pakai inline image lalu konten letak
/// di bawahnya — atau via header bila tersedia).
/// </para>
/// <para>
/// File .docx <strong>editable</strong> — customer bisa fine-tune di Word
/// sebelum kirim.
/// </para>
/// </summary>
public static class WordLetterExport
{
    private static readonly CultureInfo IdCulture = CultureInfo.GetCultureInfo("id-ID");

    /// <summary>Section yang dipakai untuk grouping rincian material (display label).</summary>
    private static readonly string[] DisplaySectionsOrder =
        { "Box Panel", "Incoming", "Outgoing", "Lainnya" };

    // ── Public line-item record (mirror PdfLetterExport.LineItem) ────────────
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

        using var doc = DocX.Create(outputPath);
        SetupPage(doc);

        var signerName    = Get(settings, "SignerName",    "Kuntjoro Handoko");
        var signerTitle   = Get(settings, "SignerTitle",   "Direktur");
        var offerLocation = Get(settings, "OfferLocation", "Bandung");

        AddLetterheadHeader(doc, settings);

        var panelLabel = !string.IsNullOrWhiteSpace(perihal) ? perihal!.Trim() : "Penawaran Harga";
        decimal panelUnitPrice = subtotal + marginAmount;

        WriteHeaderBlock(doc, estimationNumber,
            perihalText: "Informasi Harga",
            lampiranText: "-",
            clientName, contactPhone, company, address);

        WriteSalutation(doc,
            "Bersama dengan ini kami sampaikan informasi harga material sebagai berikut :");

        Write3ColTable(doc, new List<(string Label, decimal Price)>
        {
            (panelLabel, panelUnitPrice),
        });

        WriteKondisiPenawaran(doc, taxPercent, offerLocation, isSingle: true);

        if (!string.IsNullOrWhiteSpace(notes))
            doc.InsertParagraph($"Catatan: {notes}").FontSize(9).Color(XColor.Parse(120, 120, 120))
                .SpacingBefore(4);

        doc.InsertParagraph(
            "Demikian informasi harga ini kami sampaikan. Atas perhatian dan kerjasamanya kami ucapkan terima kasih.")
            .FontSize(10).SpacingBefore(10).SpacingAfter(20);

        WriteSignatureBlock(doc, offerLocation, createdDate, signerName, signerTitle);

        doc.Save();
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

        using var doc = DocX.Create(outputPath);
        SetupPage(doc);

        var signerName    = Get(settings, "SignerName",    "Kuntjoro Handoko");
        var signerTitle   = Get(settings, "SignerTitle",   "Direktur");
        var offerLocation = Get(settings, "OfferLocation", "Bandung");

        AddLetterheadHeader(doc, settings);

        WriteHeaderBlock(doc, nomorSurat,
            perihalText: !string.IsNullOrWhiteSpace(perihal) ? perihal! : "Penawaran Harga",
            lampiranText: "Rincian Material",
            clientName, contactPhone, company, address);

        WriteSalutation(doc,
            "Bersama dengan ini kami sampaikan surat penawaran harga sebagai berikut :");

        var rows = panels.Select((p, i) =>
        {
            var label = !string.IsNullOrWhiteSpace(p.ProjectName)
                ? p.ProjectName!.Trim()
                : p.EstimationNumber;
            var price = summary.Panels[i].PanelSubtotal;
            return (Label: label, Price: price);
        }).ToList();
        Write3ColTable(doc, rows);

        WriteKondisiPenawaran(doc, summary.TaxPercent, offerLocation, isSingle: false);

        if (!string.IsNullOrWhiteSpace(notes))
            doc.InsertParagraph($"Catatan: {notes}").FontSize(9).Color(XColor.Parse(120, 120, 120))
                .SpacingBefore(4);

        doc.InsertParagraph(
            "Demikian surat penawaran ini kami sampaikan. Atas perhatian dan kerjasamanya kami ucapkan terima kasih.")
            .FontSize(10).SpacingBefore(10).SpacingAfter(20);

        WriteSignatureBlock(doc, offerLocation, createdDate, signerName, signerTitle);

        // ── Halaman Rincian Material per panel ────────────────────────────
        for (int pi = 0; pi < panels.Count; pi++)
        {
            doc.InsertParagraph().InsertPageBreakAfterSelf();

            var panel = panels[pi];
            var heading = !string.IsNullOrWhiteSpace(panel.ProjectName)
                ? panel.ProjectName!.Trim()
                : panel.EstimationNumber;

            doc.InsertParagraph("Rincian Material").Bold().FontSize(14).Alignment = Alignment.center;
            doc.InsertParagraph(heading).Bold().FontSize(11).SpacingBefore(8).SpacingAfter(4);

            WriteRincianMaterialTable(doc, panel.Items);
        }

        doc.Save();
    }

    // ══════════════════════════════════════════════════════════════════════
    //  SETUP & LETTERHEAD
    // ══════════════════════════════════════════════════════════════════════
    private static void SetupPage(DocX doc)
    {
        doc.PageLayout.Orientation = XOrientation.Portrait;
        // Margins (1pt = 1/72 inch; 1cm = 28.35pt)
        doc.MarginTop    = 85f;   // 3.0cm
        doc.MarginBottom = 43f;   // 1.5cm
        doc.MarginLeft   = 71f;   // 2.5cm
        doc.MarginRight  = 43f;   // 1.5cm
    }

    /// <summary>
    /// Inject letterhead.jpg sebagai gambar di header dokumen agar muncul
    /// di SETIAP halaman. DocX 4.x mendukung first-page-header + default-header,
    /// kita pakai default-header sehingga letterhead konsisten di semua halaman.
    /// </summary>
    private static void AddLetterheadHeader(DocX doc, IDictionary<string, string> settings)
    {
        try
        {
            // 1. Explicit override path
            byte[]? imgBytes = null;
            if (settings.TryGetValue("LetterheadImagePath", out var sp) &&
                !string.IsNullOrWhiteSpace(sp) && File.Exists(sp))
            {
                imgBytes = File.ReadAllBytes(sp);
            }
            // 2. Embedded resource
            imgBytes ??= TryReadEmbedded("PanelCalculator.WinForms.Assets.Letterhead.letterhead.jpg");
            if (imgBytes == null) return;

            doc.AddHeaders();
            // DocX 4.x: Headers struct has Odd/Even/First. "Odd" is the default
            // header that appears on every page when DifferentFirstPage/Even are off.
            var header = doc.Headers.Odd;
            if (header == null) return;

            // Insert image as inline picture in header. Width = page width minus
            // small slack. DocX 4.x: Image.CreatePicture(width, height) in points.
            using var ms = new MemoryStream(imgBytes);
            var image = doc.AddImage(ms, "image/jpeg");

            // Page width = A4 portrait = 595.27pt. We want full width.
            // Image native: 1819 x 2458 px → ratio 1:1.351
            float widthPt  = 595f;
            float heightPt = widthPt * 2458f / 1819f;   // ≈ 804pt
            var pic = image.CreatePicture(heightPt, widthPt);

            // Add image to header, then push it behind text using positioning
            // (DocX doesn't expose z-order directly; instead we use a paragraph
            // in the header that contains the full-page picture).
            var headerPara = header.InsertParagraph();
            headerPara.AppendPicture(pic);
            headerPara.Alignment = Alignment.center;

            // Negative spacing so picture starts at page top regardless of
            // header default offset.
            headerPara.LineSpacing = 0;
            headerPara.IndentationFirstLine = 0;
        }
        catch
        {
            // Letterhead opsional — silent fallback
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    //  HEADER BLOCK  (Nomor/Perihal/Lampiran ↔ Kepada/Address/Up.)
    // ══════════════════════════════════════════════════════════════════════
    private static void WriteHeaderBlock(DocX doc,
        string nomorSurat, string perihalText, string lampiranText,
        string clientName, string? contactPhone, string? company, string? address)
    {
        // 2-column borderless table; left = ref, right = recipient
        var t = doc.AddTable(1, 2);
        RemoveBorders(t);
        // Equal split
        t.SetColumnWidth(0, 4000);
        t.SetColumnWidth(1, 5500);

        // ── Left: ref block ────────────────────────────────────────────
        var refTbl = t.Rows[0].Cells[0].InsertTable(3, 3);
        RemoveBorders(refTbl);
        refTbl.SetColumnWidth(0, 1200);
        refTbl.SetColumnWidth(1, 200);
        refTbl.SetColumnWidth(2, 2600);

        AddRefRow(refTbl.Rows[0], "Nomor",    nomorSurat);
        AddRefRow(refTbl.Rows[1], "Perihal",  perihalText);
        AddRefRow(refTbl.Rows[2], "Lampiran", lampiranText);

        // Remove default empty paragraph in cell that auto-DocX adds before our table
        var leftCellParas = t.Rows[0].Cells[0].Paragraphs.ToList();
        if (leftCellParas.Count > 1)
        {
            // The InsertTable adds at-the-end; first para is the empty stub
            leftCellParas[0].RemoveText(0, leftCellParas[0].Text.Length);
        }

        // ── Right: Kepada / address / Up. ───────────────────────────────
        var rightCell = t.Rows[0].Cells[1];
        var firstPara = rightCell.Paragraphs.FirstOrDefault() ?? rightCell.InsertParagraph();
        firstPara.RemoveText(0, firstPara.Text.Length);
        firstPara.Append("Kepada:").FontSize(10);
        firstPara.SpacingAfter(2);

        bool hasCompany = !string.IsNullOrWhiteSpace(company);
        if (hasCompany)
            rightCell.InsertParagraph(company!).Bold().FontSize(10);
        else if (!string.IsNullOrWhiteSpace(clientName))
            rightCell.InsertParagraph(clientName).Bold().FontSize(10);

        if (!string.IsNullOrWhiteSpace(address))
            foreach (var line in address.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
                rightCell.InsertParagraph(line.Trim()).FontSize(10);

        if (!string.IsNullOrWhiteSpace(contactPhone))
            rightCell.InsertParagraph($"Telp: {contactPhone}").FontSize(10);

        // "Up. <contact person>" hanya kalau clientName beda dari company
        if (hasCompany && !string.IsNullOrWhiteSpace(clientName) &&
            !string.Equals(clientName.Trim(), company!.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            rightCell.InsertParagraph("").FontSize(4);
            rightCell.InsertParagraph($"Up. {clientName.Trim()}").FontSize(10);
        }

        doc.InsertTable(t);
        doc.InsertParagraph("").FontSize(4);
    }

    private static void AddRefRow(Row r, string label, string value)
    {
        SetCell(r.Cells[0], label, Alignment.left);
        SetCell(r.Cells[1], ":",   Alignment.left);
        SetCell(r.Cells[2], value, Alignment.left);
    }

    private static void WriteSalutation(DocX doc, string opening)
    {
        doc.InsertParagraph("Dengan hormat,").FontSize(10).SpacingBefore(8).SpacingAfter(4);
        doc.InsertParagraph(opening).FontSize(10).SpacingAfter(8);
    }

    // ══════════════════════════════════════════════════════════════════════
    //  3-COL TABLE
    // ══════════════════════════════════════════════════════════════════════
    private static void Write3ColTable(DocX doc, IReadOnlyList<(string Label, decimal Price)> rows)
    {
        var t = doc.AddTable(rows.Count + 1, 3);
        ApplyTableStyle(t);

        SetHeaderRow(t.Rows[0],
            new[] { "No.", "Nama Barang", "Harga Satuan (Rp)" },
            new[] { Alignment.center, Alignment.left, Alignment.right });

        for (int i = 0; i < rows.Count; i++)
        {
            var r = t.Rows[i + 1];
            SetCell(r.Cells[0], $"{i + 1}.",        Alignment.center);
            SetCell(r.Cells[1], rows[i].Label,      Alignment.left);
            SetCell(r.Cells[2], RpDash(rows[i].Price), Alignment.right);
        }

        doc.InsertTable(t);
        doc.InsertParagraph("").FontSize(6);
    }

    // ══════════════════════════════════════════════════════════════════════
    //  KONDISI PENAWARAN  (bullet list)
    // ══════════════════════════════════════════════════════════════════════
    private static void WriteKondisiPenawaran(DocX doc, decimal taxPercent,
        string offerLocation, bool isSingle)
    {
        var city = !string.IsNullOrWhiteSpace(offerLocation) ? offerLocation : "Bandung";
        doc.InsertParagraph("Kondisi Penawaran :").Bold().FontSize(10).SpacingBefore(4).SpacingAfter(4);

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
            var p = doc.InsertParagraph("•  " + c).FontSize(10);
            p.IndentationBefore = 0.3f; // ~7.5mm
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    //  SIGNATURE BLOCK
    // ══════════════════════════════════════════════════════════════════════
    private static void WriteSignatureBlock(DocX doc, string offerLocation,
        DateTime createdDate, string signerName, string signerTitle)
    {
        var city    = !string.IsNullOrWhiteSpace(offerLocation) ? offerLocation : "Bandung";
        var dateStr = createdDate.ToLocalTime().ToString("dd MMMM yyyy", IdCulture);

        // Right-aligned signature block — use a 2-col table for clean align
        var t = doc.AddTable(1, 2);
        RemoveBorders(t);
        t.SetColumnWidth(0, 4500);
        t.SetColumnWidth(1, 5000);

        var rightCell = t.Rows[0].Cells[1];
        var firstPara = rightCell.Paragraphs.FirstOrDefault() ?? rightCell.InsertParagraph();
        firstPara.RemoveText(0, firstPara.Text.Length);
        firstPara.Append($"{city}, {dateStr}").FontSize(10);

        rightCell.InsertParagraph("PT. Tritunggal Swarna").Bold().FontSize(10);

        // ── Signature + stamp image overlay ──
        try
        {
            var sigBytes   = TryReadEmbedded("PanelCalculator.WinForms.Assets.Letterhead.signature.png");
            var stampBytes = TryReadEmbedded("PanelCalculator.WinForms.Assets.Letterhead.stamp.png");

            // Render signature, then immediately overlap with stamp on the
            // same paragraph. DocX inline images go side-by-side; we use
            // a single paragraph and rely on Word allowing images to overlap
            // if their offsets are set. Simpler: place signature, then stamp
            // alongside, accepting that the visual is "next to" rather than
            // strictly overlapping. The handwritten signature already looks
            // like a signature, and the stamp visually complements it.
            var imgPara = rightCell.InsertParagraph();
            imgPara.SpacingBefore(4);

            if (sigBytes != null)
            {
                using var ms1 = new MemoryStream(sigBytes);
                var sigImg = doc.AddImage(ms1, "image/png");
                // signature 477x373 px → height/width ratio 0.781
                float w1 = 150f;
                float h1 = w1 * 373f / 477f;
                var pic1 = sigImg.CreatePicture(h1, w1);
                imgPara.AppendPicture(pic1);
            }
            if (stampBytes != null)
            {
                using var ms2 = new MemoryStream(stampBytes);
                var stampImg = doc.AddImage(ms2, "image/png");
                // stamp 309x309 (square)
                float w2 = 95f;
                float h2 = 95f;
                var pic2 = stampImg.CreatePicture(h2, w2);
                imgPara.AppendPicture(pic2);
            }
        }
        catch
        {
            // Reserve fixed space if assets missing
            rightCell.InsertParagraph("").FontSize(38);
        }

        if (!string.IsNullOrWhiteSpace(signerName))
            rightCell.InsertParagraph(signerName).Bold().FontSize(10).UnderlineStyle(UnderlineStyle.singleLine);
        if (!string.IsNullOrWhiteSpace(signerTitle))
            rightCell.InsertParagraph(signerTitle).FontSize(10);

        doc.InsertTable(t);
    }

    // ══════════════════════════════════════════════════════════════════════
    //  RINCIAN MATERIAL — section divider rows
    // ══════════════════════════════════════════════════════════════════════
    private static void WriteRincianMaterialTable(DocX doc, IReadOnlyList<LineItem> items)
    {
        // Group by display section, preserving insertion order within each group
        var groups = items
            .Select((it, idx) => (Item: it, Idx: idx, Display: MapSectionToDisplay(it.Section)))
            .Where(x => x.Display != null)
            .GroupBy(x => x.Display!)
            .OrderBy(g => DisplaySectionOrder(g.Key))
            .ToList();

        // Calculate total rows: header + (1 divider + N items) per group
        int totalRows = 1 + groups.Sum(g => 1 + g.Count());
        if (totalRows <= 1) return;

        var t = doc.AddTable(totalRows, 6);
        ApplyTableStyle(t);

        SetHeaderRow(t.Rows[0],
            new[] { "No", "Material", "Merek", "Tipe", "Satuan", "Jumlah" },
            new[] { Alignment.center, Alignment.left, Alignment.left,
                    Alignment.left,   Alignment.center, Alignment.center });

        int rIdx = 1;
        int itemNo = 0;
        foreach (var grp in groups)
        {
            // Divider row: bold section name in first cell, others blank
            var dRow = t.Rows[rIdx++];
            SetCell(dRow.Cells[0], "",                Alignment.left);
            SetCell(dRow.Cells[1], grp.Key + " :",    Alignment.left, bold: true);
            SetCell(dRow.Cells[2], "",                Alignment.left);
            SetCell(dRow.Cells[3], "",                Alignment.left);
            SetCell(dRow.Cells[4], "",                Alignment.center);
            SetCell(dRow.Cells[5], "",                Alignment.center);
            foreach (var c in dRow.Cells)
                c.FillColor = XColor.Parse(232, 238, 246);

            foreach (var x in grp.OrderBy(g => g.Idx))
            {
                itemNo++;
                var r = t.Rows[rIdx++];
                SetCell(r.Cells[0], itemNo.ToString(),         Alignment.center);
                SetCell(r.Cells[1], x.Item.ProductName,         Alignment.left);
                SetCell(r.Cells[2], x.Item.Vendor,              Alignment.left);
                SetCell(r.Cells[3], x.Item.ReferenceCode,       Alignment.left);
                SetCell(r.Cells[4], x.Item.Satuan,              Alignment.center);
                SetCell(r.Cells[5], x.Item.Quantity.ToString(), Alignment.center);
            }
        }

        doc.InsertTable(t);
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

    private static int DisplaySectionOrder(string display) => display switch
    {
        "Box Panel" => 0,
        "Incoming"  => 1,
        "Outgoing"  => 2,
        "Lainnya"   => 3,
        _           => 4,
    };

    // ══════════════════════════════════════════════════════════════════════
    //  TABLE HELPERS
    // ══════════════════════════════════════════════════════════════════════
    private static void ApplyTableStyle(Table t)
    {
        t.Design  = TableDesign.TableGrid;
        t.AutoFit = AutoFit.Window;
    }

    private static void RemoveBorders(Table t)
    {
        var noBorder = new Border(XBorderStyle.Tcbs_none, BorderSize.one, 0, XColor.Parse(255, 255, 255));
        t.SetBorder(TableBorderType.Top,     noBorder);
        t.SetBorder(TableBorderType.Bottom,  noBorder);
        t.SetBorder(TableBorderType.Left,    noBorder);
        t.SetBorder(TableBorderType.Right,   noBorder);
        t.SetBorder(TableBorderType.InsideH, noBorder);
        t.SetBorder(TableBorderType.InsideV, noBorder);
    }

    private static void SetHeaderRow(Row r, string[] headers, Alignment[] aligns)
    {
        for (int i = 0; i < headers.Length; i++)
        {
            SetCell(r.Cells[i], headers[i], aligns[i], bold: true);
            r.Cells[i].FillColor = XColor.Parse(232, 238, 246);
        }
    }

    private static void SetCell(Cell c, string text, Alignment align, bool bold = false)
    {
        var p = c.Paragraphs.FirstOrDefault() ?? c.InsertParagraph();
        p.RemoveText(0, p.Text.Length);
        p.Alignment = align;
        var run = p.Append(text ?? "").FontSize(9);
        if (bold) run.Bold();
    }

    // ══════════════════════════════════════════════════════════════════════
    //  EMBEDDED RESOURCE HELPERS
    // ══════════════════════════════════════════════════════════════════════
    private static byte[]? TryReadEmbedded(string name)
    {
        var asm = typeof(WordLetterExport).Assembly;
        using var s = asm.GetManifestResourceStream(name);
        if (s == null) return null;
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }

    private static string Get(IDictionary<string, string> s, string key, string fallback)
        => s.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : fallback;

    /// <summary>Format Indonesian decimal with trailing comma-dash (e.g. 4.320.000,-)</summary>
    private static string RpDash(decimal value)
        => value.ToString("N0", IdCulture) + ",-";
}
