using PanelCalculator.Core.Services;
using RabKit.Branding;
using System.Globalization;
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

        var signerName    = Get(settings, "SignerName",    BrandContext.Current.DefaultSignerName);
        var signerTitle   = Get(settings, "SignerTitle",   BrandContext.Current.DefaultSignerTitle);
        var offerLocation = Get(settings, "OfferLocation", BrandContext.Current.DefaultOfferLocation);

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

        var signerName    = Get(settings, "SignerName",    BrandContext.Current.DefaultSignerName);
        var signerTitle   = Get(settings, "SignerTitle",   BrandContext.Current.DefaultSignerTitle);
        var offerLocation = Get(settings, "OfferLocation", BrandContext.Current.DefaultOfferLocation);

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
        // Revisi 2026-05-28 (fix #4): consistent heading layout.
        for (int pi = 0; pi < panels.Count; pi++)
        {
            doc.InsertParagraph().InsertPageBreakAfterSelf();

            var panel = panels[pi];
            var heading = !string.IsNullOrWhiteSpace(panel.ProjectName)
                ? panel.ProjectName!.Trim()
                : panel.EstimationNumber;

            // Heading 1: "Rincian Material" — center, bold, 14pt
            var titlePara = doc.InsertParagraph("Rincian Material").Bold().FontSize(14);
            titlePara.Alignment = Alignment.center;
            titlePara.SpacingAfter(12);

            // Heading 2: panel name — left-aligned, bold, 11pt
            var subPara = doc.InsertParagraph(heading).Bold().FontSize(11);
            subPara.Alignment = Alignment.left;
            subPara.SpacingBefore(0).SpacingAfter(6);

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
        // Margins (revisi 2026-05-28 — fix #1): tambah 5mm breathing room
        // antara konten body vs banner letterhead atas/bawah.
        //   top = 3.5 cm (99 pt)   ← sebelumnya 3.0 cm (85 pt)
        //   bot = 2.0 cm (57 pt)   ← sebelumnya 1.5 cm (43 pt)
        doc.MarginTop    = 99f;
        doc.MarginBottom = 57f;
        doc.MarginLeft   = 71f;   // 2.5cm (unchanged)
        doc.MarginRight  = 43f;   // 1.5cm (unchanged)
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
            // 2. Brand-pack asset (sumber: Panel.Branding.dll embedded resource)
            imgBytes ??= BrandContext.Current.GetLetterheadBytes();
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

        // ── Right: Kepada / address / Up. ─── RIGHT-ALIGNED (fix #2)
        var rightCell = t.Rows[0].Cells[1];
        var firstPara = rightCell.Paragraphs.FirstOrDefault() ?? rightCell.InsertParagraph();
        firstPara.RemoveText(0, firstPara.Text.Length);
        firstPara.Alignment = Alignment.right;
        firstPara.Append("Kepada:").FontSize(10);
        firstPara.SpacingAfter(2);

        bool hasCompany = !string.IsNullOrWhiteSpace(company);
        if (hasCompany)
        {
            var p = rightCell.InsertParagraph(company!).Bold().FontSize(10);
            p.Alignment = Alignment.right;
        }
        else if (!string.IsNullOrWhiteSpace(clientName))
        {
            var p = rightCell.InsertParagraph(clientName).Bold().FontSize(10);
            p.Alignment = Alignment.right;
        }

        if (!string.IsNullOrWhiteSpace(address))
            foreach (var line in address.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var p = rightCell.InsertParagraph(line.Trim()).FontSize(10);
                p.Alignment = Alignment.right;
            }

        if (!string.IsNullOrWhiteSpace(contactPhone))
        {
            var p = rightCell.InsertParagraph($"Telp: {contactPhone}").FontSize(10);
            p.Alignment = Alignment.right;
        }

        // "Up. <contact person>" hanya kalau clientName beda dari company
        if (hasCompany && !string.IsNullOrWhiteSpace(clientName) &&
            !string.Equals(clientName.Trim(), company!.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            rightCell.InsertParagraph("").FontSize(4);
            var p = rightCell.InsertParagraph($"Up. {clientName.Trim()}").FontSize(10);
            p.Alignment = Alignment.right;
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
        var defaultCity = BrandContext.Current.DefaultOfferLocation;
        var city = !string.IsNullOrWhiteSpace(offerLocation) ? offerLocation : defaultCity;
        doc.InsertParagraph("Kondisi Penawaran :").Bold().FontSize(10).SpacingBefore(4).SpacingAfter(4);

        var conds = new List<string>();
        if (taxPercent > 0)
            conds.Add($"Harga belum termasuk PPN {taxPercent:0.##}% (menyesuaikan dengan peraturan pemerintah)");
        else
            conds.Add("Harga belum termasuk PPN (menyesuaikan dengan peraturan pemerintah)");
        // "Harga loco Pabrik" trigger ketika city == default brand city.
        conds.Add($"Harga loco {(string.Equals(city, defaultCity, StringComparison.OrdinalIgnoreCase) ? "Pabrik" : city)}");
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
    //
    // Revisi 2026-05-28 (fix #3): semua paragraph signature block sekarang
    // RIGHT-ALIGNED (sejajar dengan edge kanan tabel item & blok Kepada).
    // Sebelumnya pakai 2-col table tapi text di dalam kolom kanan left-align
    // → visual jadi terkesan "tengah halaman" karena kolom kanan ~50% width.
    //
    private static void WriteSignatureBlock(DocX doc, string offerLocation,
        DateTime createdDate, string signerName, string signerTitle)
    {
        var city    = !string.IsNullOrWhiteSpace(offerLocation) ? offerLocation : BrandContext.Current.DefaultOfferLocation;
        var dateStr = createdDate.ToLocalTime().ToString("dd MMMM yyyy", IdCulture);

        // 2-col table; kolom kanan lebih sempit + all content right-aligned.
        var t = doc.AddTable(1, 2);
        RemoveBorders(t);
        t.SetColumnWidth(0, 5500);
        t.SetColumnWidth(1, 4000);

        var rightCell = t.Rows[0].Cells[1];
        var firstPara = rightCell.Paragraphs.FirstOrDefault() ?? rightCell.InsertParagraph();
        firstPara.RemoveText(0, firstPara.Text.Length);
        firstPara.Alignment = Alignment.right;
        firstPara.Append($"{city}, {dateStr}").FontSize(10);

        var ptPara = rightCell.InsertParagraph(BrandContext.Current.CompanyName).Bold().FontSize(10);
        ptPara.Alignment = Alignment.right;

        // ── Signature + stamp image overlay ──
        try
        {
            var sigBytes   = BrandContext.Current.GetSignatureBytes();
            var stampBytes = BrandContext.Current.GetStampBytes();

            var imgPara = rightCell.InsertParagraph();
            imgPara.SpacingBefore(4);
            imgPara.Alignment = Alignment.right;

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
            var blank = rightCell.InsertParagraph("").FontSize(38);
            blank.Alignment = Alignment.right;
        }

        if (!string.IsNullOrWhiteSpace(signerName))
        {
            var p = rightCell.InsertParagraph(signerName).Bold().FontSize(10)
                .UnderlineStyle(UnderlineStyle.singleLine);
            p.Alignment = Alignment.right;
        }
        if (!string.IsNullOrWhiteSpace(signerTitle))
        {
            var p = rightCell.InsertParagraph(signerTitle).FontSize(10);
            p.Alignment = Alignment.right;
        }

        doc.InsertTable(t);
    }

    // ══════════════════════════════════════════════════════════════════════
    //  RINCIAN MATERIAL — section divider rows
    // ══════════════════════════════════════════════════════════════════════
    //
    // Revisi 2026-05-28 (fix #4):
    //   - Divider row sekarang MERGE 6 cols (pakai Row.MergeCells) supaya
    //     teks "Incoming :" / "Outgoing :" / "Lainnya :" beneran span
    //     seluruh table width seperti di screenshot user.
    //   - Italic + bold pada divider text, bg lebih terang dari header.
    //   - Alternating row color tetap (light gray vs white) untuk readability.
    //
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
        var dividerBg = XColor.Parse(238, 243, 248);  // slightly lighter than header
        foreach (var grp in groups)
        {
            // Divider row: put text in cell[0], then merge cell[0..5] horizontally
            var dRow = t.Rows[rIdx++];

            // Fill all 6 cells with divider bg color BEFORE merging
            // (merge logic of DocX preserves first cell formatting).
            foreach (var c in dRow.Cells)
                c.FillColor = dividerBg;

            // Set the divider text (cell[0]) — bold + italic
            var dPara = dRow.Cells[0].Paragraphs.FirstOrDefault()
                        ?? dRow.Cells[0].InsertParagraph();
            dPara.RemoveText(0, dPara.Text.Length);
            dPara.Alignment = Alignment.left;
            dPara.Append(grp.Key + " :").Bold().Italic().FontSize(9);

            // Clear text in cells 1..5 (they'll be merged away but be safe)
            for (int ci = 1; ci < 6; ci++)
                SetCell(dRow.Cells[ci], "", Alignment.left);

            // Merge cell 0 across all 6 cols (start=0, end=5)
            try { dRow.MergeCells(0, 5); } catch { /* DocX may throw if already merged; safe to ignore */ }

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

                // Alternating row color: white vs light gray (#F8FAFC)
                if (itemNo % 2 == 0)
                {
                    var altBg = XColor.Parse(248, 250, 252);
                    foreach (var c in r.Cells)
                        c.FillColor = altBg;
                }
            }
        }

        doc.InsertTable(t);
    }

    /// <summary>
    /// Map raw section name to display label. Delegates to
    /// <see cref="IIndustryProfile.SectionDisplayMap"/> so each industry
    /// pack owns its own mapping. Falls back to <c>"Incoming"</c> for empty
    /// input (legacy behavior) or the trimmed raw value if unmapped.
    /// </summary>
    internal static string? MapSectionToDisplay(string section)
    {
        var s = (section ?? "").Trim();
        if (string.IsNullOrWhiteSpace(s)) return "Incoming";
        var map = BrandContext.CurrentIndustry.SectionDisplayMap;
        return map.TryGetValue(s, out var display) ? display : s;
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

    private static string Get(IDictionary<string, string> s, string key, string fallback)
        => s.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : fallback;

    /// <summary>Format Indonesian decimal with trailing comma-dash (e.g. 4.320.000,-)</summary>
    private static string RpDash(decimal value)
        => value.ToString("N0", IdCulture) + ",-";
}
