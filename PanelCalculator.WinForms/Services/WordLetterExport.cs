using PanelCalculator.Core.Services;
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
/// Generate Surat Penawaran Harga formal sebagai dokumen Word (.docx).
/// Tujuannya: file <strong>editable</strong> — user bisa buka di MS Word
/// untuk ganti susunan, kolom, format teks, dll. sesuai kebutuhan.
/// Layout content paralel dengan <see cref="PdfLetterExport"/> sehingga
/// hasil Word dan PDF konsisten.
/// <para>
/// Library: <c>Xceed.Words.NET</c> (DocX 4.x, Xceed Community License — gratis untuk
/// commercial use). Format file: OpenXML (.docx) standar Microsoft Word 2007+.
/// </para>
/// </summary>
public static class WordLetterExport
{
    private static readonly CultureInfo IdCulture = CultureInfo.GetCultureInfo("id-ID");

    /// <summary>Section yang dipakai untuk grouping line items (sama dengan PDF).</summary>
    private static readonly string[] AllSections =
    {
        "Material Utama", "Material Pendukung", "Material Lainnya",
        "Box", "Incoming", "Outgoing", "Trailer", "Karoseri", "Jasa"
    };

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

    // ── Single panel ─────────────────────────────────────────────────────────
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

        var signerName    = Get(settings, "SignerName",    "");
        var signerTitle   = Get(settings, "SignerTitle",   "Marketing");
        var offerLocation = Get(settings, "OfferLocation", "Bandung");

        WriteCompanyHeader(doc, settings);
        WriteRefBlock(doc, estimationNumber, perihal, "Rincian Material");
        WriteRecipient(doc, clientName, contactPhone, company, address);
        WriteSalutation(doc, "Berikut ini kami sampaikan informasi harga Panel sebagai berikut:");

        // ── Tabel ringkasan per section (sama format dengan PDF formal) ───
        var sectionTotals = AllSections
            .Select(s => (Name: s, Total: items.Where(i => i.Section == s).Sum(i => i.LineTotal)))
            .Where(x => x.Total > 0)
            .ToList();

        var sumTbl = doc.AddTable(sectionTotals.Count + 1, 3);
        ApplyTableStyle(sumTbl);
        SetHeaderRow(sumTbl.Rows[0], new[] { "No.", "Nama Barang", "Harga Satuan (Rp)" });
        for (int i = 0; i < sectionTotals.Count; i++)
        {
            var (name, total_) = sectionTotals[i];
            var r = sumTbl.Rows[i + 1];
            SetCell(r.Cells[0], $"{i + 1}.",    Alignment.center);
            SetCell(r.Cells[1], name,           Alignment.left);
            SetCell(r.Cells[2], Rp(total_),     Alignment.right);
        }
        doc.InsertTable(sumTbl);
        doc.InsertParagraph("").FontSize(6);

        // ── Cost summary (Subtotal → Margin → DPP → PPN → PPh → GRAND TOTAL) ──
        decimal dpp = subtotal + marginAmount + shippingCost;
        var rows = new List<(string Label, decimal Value, bool Bold, bool Negative)>
        {
            ("Subtotal", subtotal, false, false),
        };
        if (marginAmount != 0)
            rows.Add((marginAmount >= 0 ? "Margin" : "Diskon", marginAmount, false, marginAmount < 0));
        if (shippingCost > 0)
            rows.Add(("Ongkos Kirim", shippingCost, false, false));
        rows.Add(("DPP (Dasar Pengenaan Pajak)", dpp, true, false));
        if (taxAmount > 0)
            rows.Add(($"PPN {taxPercent:F0}%", taxAmount, false, false));
        if (pphAmount > 0)
            rows.Add(("PPh (ditahan)", pphAmount, false, true));
        rows.Add(("GRAND TOTAL", total, true, false));

        var costTbl = doc.AddTable(rows.Count, 2);
        ApplyTableStyle(costTbl);
        for (int i = 0; i < rows.Count; i++)
        {
            var (lbl, val, bold, neg) = rows[i];
            string disp = neg ? "- " + Rp(Math.Abs(val)) : Rp(val);
            var r = costTbl.Rows[i];
            SetCell(r.Cells[0], lbl,  Alignment.left,  bold);
            SetCell(r.Cells[1], disp, Alignment.right, bold);
            if (bold)
            {
                r.Cells[0].FillColor = XColor.Parse(235, 242, 255);
                r.Cells[1].FillColor = XColor.Parse(235, 242, 255);
            }
        }
        doc.InsertTable(costTbl);

        // ── Terbilang ─────────────────────────────────────────────────────
        var terb = doc.InsertParagraph()
            .Append("Terbilang: ").Bold()
            .Append(TerbilangFormatter.ToRupiah(total)).Italic();
        terb.SpacingBefore(6).SpacingAfter(8);

        // ── Kondisi penawaran ─────────────────────────────────────────────
        WriteConditions(doc, offerLocation, taxPercent);

        if (!string.IsNullOrWhiteSpace(notes))
            doc.InsertParagraph($"Catatan: {notes}").FontSize(9).Color(XColor.Parse(128, 128, 128));

        doc.InsertParagraph(
            "Demikian surat penawaran ini kami sampaikan. " +
            "Atas perhatian dan kerja samanya, kami ucapkan terima kasih.")
            .SpacingBefore(8).SpacingAfter(20);

        WriteSignature(doc, offerLocation, createdDate, signerName, signerTitle);

        // ── Halaman Rincian Material ───────────────────────────────────────
        doc.InsertParagraph().InsertPageBreakAfterSelf();
        WriteDetailPage(doc, items, estimationNumber);

        doc.Save();
    }

    // ── Combined multi-panel ─────────────────────────────────────────────────
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

        using var doc = DocX.Create(outputPath);
        SetupPage(doc);

        var signerName    = Get(settings, "SignerName",    "");
        var signerTitle   = Get(settings, "SignerTitle",   "Marketing");
        var offerLocation = Get(settings, "OfferLocation", "Bandung");

        WriteCompanyHeader(doc, settings);
        WriteRefBlock(doc, nomorSurat,
            !string.IsNullOrWhiteSpace(perihal) ? perihal : "Penawaran Harga Multi-Panel",
            $"{panels.Count} Rincian Panel");
        WriteRecipient(doc, clientName, contactPhone, company, address);
        WriteSalutation(doc,
            $"Bersama ini kami sampaikan penawaran harga untuk {panels.Count} (panel) sebagai berikut:");

        // ── Per panel section ─────────────────────────────────────────────
        for (int pi = 0; pi < panels.Count; pi++)
        {
            var panel = panels[pi];
            var title = !string.IsNullOrWhiteSpace(panel.ProjectName)
                ? $"Panel #{pi + 1} — {panel.ProjectName}"
                : $"Panel #{pi + 1} — {panel.EstimationNumber}";
            doc.InsertParagraph(title).Bold().FontSize(11).SpacingBefore(6).SpacingAfter(4);

            var sectionTotals = AllSections
                .Select(s => (Name: s, Total: panel.Items.Where(i => i.Section == s).Sum(i => i.LineTotal)))
                .Where(x => x.Total > 0)
                .ToList();

            if (sectionTotals.Count == 0) continue;

            var t = doc.AddTable(sectionTotals.Count + 1, 3);
            ApplyTableStyle(t);
            SetHeaderRow(t.Rows[0], new[] { "No.", "Nama Barang", "Harga Satuan (Rp)" });
            for (int i = 0; i < sectionTotals.Count; i++)
            {
                var (name, st) = sectionTotals[i];
                var r = t.Rows[i + 1];
                SetCell(r.Cells[0], $"{i + 1}.", Alignment.center);
                SetCell(r.Cells[1], name,        Alignment.left);
                SetCell(r.Cells[2], Rp(st),      Alignment.right);
            }
            doc.InsertTable(t);

            // Subtotal per panel
            var pst = summary.Panels[pi].PanelSubtotal;
            var subRow = doc.AddTable(1, 2);
            ApplyTableStyle(subRow);
            SetCell(subRow.Rows[0].Cells[0], $"Sub-total Panel #{pi + 1}", Alignment.right, bold: true);
            SetCell(subRow.Rows[0].Cells[1], Rp(pst), Alignment.right, bold: true);
            subRow.Rows[0].Cells[1].FillColor = XColor.Parse(235, 242, 255);
            doc.InsertTable(subRow);
            doc.InsertParagraph("").FontSize(4);
        }

        // ── Ringkasan akhir ───────────────────────────────────────────────
        doc.InsertParagraph().AppendLine();
        doc.InsertParagraph("RINGKASAN PENAWARAN").Bold().FontSize(12).Alignment = Alignment.center;

        var summaryRows = new List<(string Label, decimal Value, bool Bold, bool Negative, bool IsTotal)>();
        foreach (var pl in summary.Panels)
        {
            var label = !string.IsNullOrWhiteSpace(pl.ProjectName)
                ? $"Sub-total Panel #{pl.Index} — {pl.ProjectName}"
                : $"Sub-total Panel #{pl.Index} ({pl.EstimationNumber})";
            summaryRows.Add((label, pl.PanelSubtotal, false, false, false));
        }
        summaryRows.Add(("Total Sub-total Semua Panel", summary.GrandSubtotal, true, false, false));
        if (summary.CombinedShippingCost > 0)
            summaryRows.Add(("Ongkos Kirim Gabungan", summary.CombinedShippingCost, false, false, false));
        summaryRows.Add(("DPP (Dasar Pengenaan Pajak)", summary.DPP, true, false, false));
        if (summary.TaxAmount > 0)
            summaryRows.Add(($"PPN {summary.TaxPercent:F0}% (dihitung sekali)", summary.TaxAmount, false, false, false));
        if (summary.TotalPPh > 0)
            summaryRows.Add(("PPh (ditahan, total dari semua panel)", summary.TotalPPh, false, true, false));
        summaryRows.Add(("GRAND TOTAL", summary.GrandTotal, true, false, true));

        var sumT = doc.AddTable(summaryRows.Count, 2);
        ApplyTableStyle(sumT);
        for (int i = 0; i < summaryRows.Count; i++)
        {
            var (lbl, val, bold, neg, isTotal) = summaryRows[i];
            string disp = neg ? "- " + Rp(Math.Abs(val)) : Rp(val);
            var r = sumT.Rows[i];
            SetCell(r.Cells[0], lbl, Alignment.left, bold);
            SetCell(r.Cells[1], disp, Alignment.right, bold);
            if (isTotal || bold)
            {
                r.Cells[0].FillColor = XColor.Parse(235, 242, 255);
                r.Cells[1].FillColor = XColor.Parse(235, 242, 255);
            }
        }
        doc.InsertTable(sumT);

        doc.InsertParagraph()
            .Append("Terbilang: ").Bold()
            .Append(summary.Terbilang).Italic();

        WriteConditions(doc, offerLocation, summary.TaxPercent, isCombined: true);

        if (!string.IsNullOrWhiteSpace(notes))
            doc.InsertParagraph($"Catatan: {notes}").FontSize(9).Color(XColor.Parse(128, 128, 128));

        doc.InsertParagraph(
            "Demikian surat penawaran ini kami sampaikan. " +
            "Atas perhatian dan kerja samanya, kami ucapkan terima kasih.")
            .SpacingBefore(8).SpacingAfter(20);

        WriteSignature(doc, offerLocation, createdDate, signerName, signerTitle);

        // ── Halaman Rincian per panel ─────────────────────────────────────
        for (int pi = 0; pi < panels.Count; pi++)
        {
            doc.InsertParagraph().InsertPageBreakAfterSelf();
            var panel = panels[pi];
            var heading = !string.IsNullOrWhiteSpace(panel.ProjectName)
                ? $"RINCIAN PANEL #{pi + 1} — {panel.ProjectName!.ToUpper()}"
                : $"RINCIAN PANEL #{pi + 1}";
            doc.InsertParagraph(heading).Bold().FontSize(14).Alignment = Alignment.center;
            doc.InsertParagraph($"Ref: {panel.EstimationNumber}").FontSize(9)
                .Color(XColor.Parse(128, 128, 128)).Alignment = Alignment.center;
            WriteDetailSections(doc, panel.Items);
        }

        doc.Save();
    }

    // ════════════════════════════════════════════════════════════════════════
    //  HELPERS
    // ════════════════════════════════════════════════════════════════════════
    private static void SetupPage(DocX doc)
    {
        doc.PageLayout.Orientation = XOrientation.Portrait;
        doc.MarginTop    = 60f;
        doc.MarginBottom = 60f;
        doc.MarginLeft   = 60f;
        doc.MarginRight  = 60f;
    }

    private static void WriteCompanyHeader(DocX doc, IDictionary<string, string> settings)
    {
        var companyName  = Get(settings, "CompanyName",    "PT. TRITUNGGAL SWARNA");
        var companyAddr  = Get(settings, "CompanyAddress", "");
        var companyPhone = Get(settings, "CompanyPhone",   "");

        var p = doc.InsertParagraph(companyName).Bold().FontSize(14);
        p.Alignment = Alignment.center;
        if (!string.IsNullOrWhiteSpace(companyAddr))
            doc.InsertParagraph(companyAddr).FontSize(9).Alignment = Alignment.center;
        if (!string.IsNullOrWhiteSpace(companyPhone))
            doc.InsertParagraph(companyPhone).FontSize(9).Alignment = Alignment.center;

        // Garis pemisah simpel — pakai paragraph kosong + border bawah
        // (DocX 4.x menghapus overload InsertHorizontalLine yang menerima warna)
        doc.InsertParagraph("").FontSize(2);
        doc.InsertParagraph("─────────────────────────────────────────────────────────────────────────")
            .FontSize(6).Alignment = Alignment.center;
        doc.InsertParagraph("").FontSize(2);
    }

    private static void WriteRefBlock(DocX doc, string nomorSurat, string? perihal, string lampiran)
    {
        var t = doc.AddTable(3, 3);
        t.Design = TableDesign.None;
        // Kolom: label (~28%), ":" (~4%), value (~68%)
        SetCell(t.Rows[0].Cells[0], "Nomor",    Alignment.left, bold: true);
        SetCell(t.Rows[0].Cells[1], ":",        Alignment.left);
        SetCell(t.Rows[0].Cells[2], nomorSurat, Alignment.left);
        SetCell(t.Rows[1].Cells[0], "Perihal",  Alignment.left, bold: true);
        SetCell(t.Rows[1].Cells[1], ":",        Alignment.left);
        SetCell(t.Rows[1].Cells[2],
            !string.IsNullOrWhiteSpace(perihal) ? perihal! : "Informasi Harga", Alignment.left);
        SetCell(t.Rows[2].Cells[0], "Lampiran", Alignment.left, bold: true);
        SetCell(t.Rows[2].Cells[1], ":",        Alignment.left);
        SetCell(t.Rows[2].Cells[2], lampiran,   Alignment.left);
        RemoveBorders(t);
        doc.InsertTable(t);
    }

    private static void WriteRecipient(DocX doc, string clientName,
        string? contactPhone, string? company, string? address)
    {
        doc.InsertParagraph("Kepada:").Bold().FontSize(10).SpacingBefore(8);
        bool hasCompany = !string.IsNullOrWhiteSpace(company);
        if (hasCompany)
            doc.InsertParagraph(company!).Bold().FontSize(10);
        else if (!string.IsNullOrWhiteSpace(clientName))
            doc.InsertParagraph(clientName).Bold().FontSize(10);
        if (!string.IsNullOrWhiteSpace(address))
        {
            foreach (var line in address.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
                doc.InsertParagraph(line.Trim()).FontSize(10);
        }
        if (!string.IsNullOrWhiteSpace(contactPhone))
            doc.InsertParagraph($"Telp: {contactPhone}").FontSize(10);
        if (hasCompany && !string.IsNullOrWhiteSpace(clientName))
            doc.InsertParagraph($"Up. {clientName}").Bold().FontSize(10);
    }

    private static void WriteSalutation(DocX doc, string opening)
    {
        doc.InsertParagraph("Dengan hormat,").FontSize(10).SpacingBefore(12);
        doc.InsertParagraph(opening).FontSize(10).SpacingAfter(8);
    }

    private static void WriteConditions(DocX doc, string city, decimal taxPercent, bool isCombined = false)
    {
        doc.InsertParagraph("Kondisi Penawaran :").Bold().FontSize(10).SpacingBefore(6);
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
            doc.InsertParagraph($"{i + 1}. {conds[i]}").FontSize(10);
    }

    private static void WriteSignature(DocX doc, string city, DateTime date,
        string signerName, string signerTitle)
    {
        var dateStr = date.ToLocalTime().ToString("dd MMMM yyyy", IdCulture);
        doc.InsertParagraph($"{city}, {dateStr}").FontSize(10).SpacingBefore(20);
        doc.InsertParagraph("PT. Tritunggal Swarna").FontSize(10).SpacingAfter(46);
        if (!string.IsNullOrWhiteSpace(signerName))
            doc.InsertParagraph(signerName).Bold().FontSize(10);
        if (!string.IsNullOrWhiteSpace(signerTitle))
            doc.InsertParagraph(signerTitle).FontSize(9).Color(XColor.Parse(128, 128, 128));
    }

    private static void WriteDetailPage(DocX doc, IReadOnlyList<LineItem> items, string estNo)
    {
        doc.InsertParagraph("RINCIAN MATERIAL").Bold().FontSize(14).Alignment = Alignment.center;
        doc.InsertParagraph($"Ref: {estNo}").FontSize(9)
            .Color(XColor.Parse(128, 128, 128)).Alignment = Alignment.center;
        WriteDetailSections(doc, items);
    }

    private static void WriteDetailSections(DocX doc, IReadOnlyList<LineItem> items)
    {
        int sectionNo = 0;
        foreach (var sec in AllSections)
        {
            var secItems = items.Where(i => i.Section == sec).ToList();
            if (secItems.Count == 0) continue;
            sectionNo++;
            doc.InsertParagraph($"{sectionNo}. {sec}").Bold().FontSize(11).SpacingBefore(8).SpacingAfter(4);

            var t = doc.AddTable(secItems.Count + 1, 6);
            ApplyTableStyle(t);
            SetHeaderRow(t.Rows[0], new[] { "No", "Material", "Merek", "Tipe", "Satuan", "Jumlah" });
            for (int i = 0; i < secItems.Count; i++)
            {
                var it = secItems[i];
                var r = t.Rows[i + 1];
                SetCell(r.Cells[0], (i + 1).ToString(),    Alignment.center);
                SetCell(r.Cells[1], it.ProductName,         Alignment.left);
                SetCell(r.Cells[2], it.Vendor,              Alignment.left);
                SetCell(r.Cells[3], it.ReferenceCode,       Alignment.left);
                SetCell(r.Cells[4], it.Satuan,              Alignment.center);
                SetCell(r.Cells[5], it.Quantity.ToString(), Alignment.center);
            }
            doc.InsertTable(t);
        }
    }

    // ── Table helpers ────────────────────────────────────────────────────────
    private static void ApplyTableStyle(Table t)
    {
        t.Design = TableDesign.TableGrid;
        t.AutoFit = AutoFit.Window;
    }

    private static void RemoveBorders(Table t)
    {
        var noBorder = new Border(XBorderStyle.Tcbs_none, BorderSize.one, 0, XColor.Parse(255, 255, 255));
        t.SetBorder(TableBorderType.Top,            noBorder);
        t.SetBorder(TableBorderType.Bottom,         noBorder);
        t.SetBorder(TableBorderType.Left,           noBorder);
        t.SetBorder(TableBorderType.Right,          noBorder);
        t.SetBorder(TableBorderType.InsideH,        noBorder);
        t.SetBorder(TableBorderType.InsideV,        noBorder);
    }

    private static void SetHeaderRow(Row r, string[] headers)
    {
        for (int i = 0; i < headers.Length; i++)
        {
            SetCell(r.Cells[i], headers[i], Alignment.center, bold: true);
            r.Cells[i].FillColor = XColor.Parse(210, 225, 245);
        }
    }

    private static void SetCell(Cell c, string text, Alignment align, bool bold = false)
    {
        // Pastikan cell punya minimal 1 paragraph
        var p = c.Paragraphs.FirstOrDefault() ?? c.InsertParagraph();
        p.RemoveText(0, p.Text.Length);
        p.Alignment = align;
        var run = p.Append(text ?? "").FontSize(9);
        if (bold) run.Bold();
    }

    private static string Get(IDictionary<string, string> s, string key, string fallback)
        => s.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : fallback;

    private static string Rp(decimal value)
        => "Rp " + value.ToString("N0", IdCulture);
}
