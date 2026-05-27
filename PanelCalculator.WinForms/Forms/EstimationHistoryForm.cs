using PanelCalculator.Core.Models;
using PanelCalculator.Core.Services;
using PanelCalculator.Data.Repositories;
using PanelCalculator.WinForms.Services;
using PanelCalculator.WinForms.Theme;

namespace PanelCalculator.WinForms.Forms;

public class EstimationHistoryForm : Form
{
    private readonly IEstimationRepository _estimationRepo;
    private readonly ICalculationService _calcService;
    private readonly PanelCalculator.Data.PanelCalculatorContext? _context;

    public Estimation? LoadedEstimation { get; private set; }

    private DataGridView dgv = null!;
    private TextBox txtSearch = null!;
    private ComboBox cmbStatus = null!;
    private List<Estimation> _allEstimations = new();
    private Button btnCombine = null!;
    private ToolTip _toolTip = null!;
    private bool _bulkToggling; // re-entrancy guard untuk header-click "Centang Semua"

    public EstimationHistoryForm(
        IEstimationRepository estimationRepo,
        ICalculationService calcService,
        PanelCalculator.Data.PanelCalculatorContext? context = null)
    {
        _estimationRepo = estimationRepo;
        _calcService = calcService;
        _context = context;
        BuildUI();
    }

    private void BuildUI()
    {
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode       = AutoScaleMode.Dpi;
        Text = "Riwayat Estimasi";
        Size = new Size(1080, 620);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = AppTheme.Background;
        Padding = new Padding(16);

        _toolTip = new ToolTip
        {
            AutoPopDelay = 8000,
            InitialDelay = 300,
            ReshowDelay  = 200,
            ShowAlways   = true,
        };

        // Top filter bar
        var pnlFilter = new Panel { Dock = DockStyle.Top, Height = 60, BackColor = AppTheme.SidebarBg, Padding = new Padding(12, 8, 12, 8) };
        pnlFilter.Paint += (s, e) => { using var pen = new Pen(AppTheme.Border); e.Graphics.DrawLine(pen, 0, pnlFilter.Height - 1, pnlFilter.Width, pnlFilter.Height - 1); };

        var lblSearch = AppTheme.MakeLabel("Cari:", AppTheme.FontSmall, AppTheme.TextSecondary);
        lblSearch.Location = new Point(12, 8);

        txtSearch = new TextBox { Location = new Point(12, 28), Width = 300, PlaceholderText = "Nama klien atau nomor estimasi..." };
        AppTheme.StyleTextBox(txtSearch);
        txtSearch.TextChanged += (s, e) => FilterGrid();

        var lblStatus = AppTheme.MakeLabel("Status:", AppTheme.FontSmall, AppTheme.TextSecondary);
        lblStatus.Location = new Point(328, 8);

        cmbStatus = new ComboBox { Location = new Point(328, 28), Width = 140, DropDownStyle = ComboBoxStyle.DropDownList };
        AppTheme.StyleComboBox(cmbStatus);
        cmbStatus.Items.AddRange(new[] { "Semua", "Antri Dihitung", "Draft", "Tunggu Approved", "Approved" });
        cmbStatus.SelectedIndex = 0;
        cmbStatus.SelectedIndexChanged += (s, e) => FilterGrid();

        pnlFilter.Controls.AddRange(new Control[] { lblSearch, txtSearch, lblStatus, cmbStatus });

        // Hint banner (prominen) — kasih tau user cara pakai Penawaran Gabungan.
        // Diletakkan di antara filter dan grid agar selalu kelihatan.
        var pnlHint = new Panel
        {
            Dock      = DockStyle.Top,
            Height    = 44,
            BackColor = AppTheme.Bg2,
            Padding   = new Padding(14, 8, 14, 8),
        };
        pnlHint.Paint += (s, e) =>
        {
            // Bar vertikal aksen di kiri (biar mata user langsung tertarik)
            using var br = new SolidBrush(AppTheme.Brand500);
            e.Graphics.FillRectangle(br, 0, 0, 4, pnlHint.Height);
            using var pen = new Pen(AppTheme.Border);
            e.Graphics.DrawLine(pen, 0, pnlHint.Height - 1, pnlHint.Width, pnlHint.Height - 1);
        };
        var lblHintBanner = new Label
        {
            Text =
                "💡 Penawaran Gabungan: centang ☑ kolom 'Pilih' (kiri) di minimal 2 estimasi, " +
                "lalu klik tombol 'Penawaran Gabungan' di bawah. " +
                "Tips: klik di mana saja pada baris untuk toggle centang; klik header 'Pilih' untuk centang semua.",
            Dock      = DockStyle.Fill,
            ForeColor = AppTheme.Text1,
            BackColor = Color.Transparent,
            Font      = AppTheme.FontSmall,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding   = new Padding(8, 0, 0, 0),
            AutoEllipsis = true,
        };
        pnlHint.Controls.Add(lblHintBanner);

        // Grid
        dgv = new DataGridView { Dock = DockStyle.Fill };
        AppTheme.StyleGrid(dgv);
        // ReadOnly diset per-column supaya kolom "Pilih" (checkbox) tetap editable
        // sementara kolom lain tidak bisa diedit.
        dgv.ReadOnly = false;
        // Checkbox di paling kiri untuk multi-select compose surat penawaran gabungan.
        // HeaderText "Pilih ☐" → akan kita toggle jadi "Pilih ☑" via ColumnHeaderMouseClick.
        dgv.Columns.Add(new DataGridViewCheckBoxColumn { Name = "ColPick", HeaderText = "Pilih ☐", FillWeight = 7, ReadOnly = false, ToolTipText = "Klik header untuk centang/batalkan semua" });
        dgv.Columns.Add(new DataGridViewTextBoxColumn { Name = "ColNo",      HeaderText = "No. Estimasi",  FillWeight = 18, ReadOnly = true });
        dgv.Columns.Add(new DataGridViewTextBoxColumn { Name = "ColClient",  HeaderText = "Klien",         FillWeight = 20, ReadOnly = true });
        dgv.Columns.Add(new DataGridViewTextBoxColumn { Name = "ColCompany", HeaderText = "Perusahaan",    FillWeight = 20, ReadOnly = true });
        dgv.Columns.Add(new DataGridViewTextBoxColumn { Name = "ColDate",    HeaderText = "Tanggal",       FillWeight = 14, ReadOnly = true });
        dgv.Columns.Add(new DataGridViewTextBoxColumn { Name = "ColStatus",  HeaderText = "Status",        FillWeight = 10, ReadOnly = true });
        dgv.Columns.Add(new DataGridViewTextBoxColumn { Name = "ColTotal",   HeaderText = "Total Harga",   FillWeight = 18, ReadOnly = true, DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleRight } });
        dgv.Columns.Add(new DataGridViewTextBoxColumn { Name = "ColId",      Visible = false, ReadOnly = true });
        dgv.CellDoubleClick += Dgv_CellDoubleClick;
        // CellContentClick + CommitEdit -> CellValueChanged firing langsung
        // sehingga tombol "Export PDF Penawaran Gabungan" enabled/disabled
        // segera setelah user centang/uncentang.
        dgv.CellContentClick += (s, e) =>
        {
            if (e.RowIndex >= 0 && dgv.Columns[e.ColumnIndex].Name == "ColPick")
                dgv.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        dgv.CellValueChanged += (s, e) =>
        {
            if (e.RowIndex >= 0) UpdateRowHighlight(dgv.Rows[e.RowIndex]);
            UpdateCombineButtonState();
        };
        // Row-click toggle: klik di mana saja pada baris (selain header) → toggle checkbox.
        // Lebih natural daripada user harus presisi klik kotak checkbox kecil.
        // Skip kolom Pilih sendiri (sudah ditangani CellContentClick) dan double-click
        // (yang artinya buka detail estimasi via Dgv_CellDoubleClick).
        dgv.CellClick += (s, e) =>
        {
            if (e.RowIndex < 0 || _bulkToggling) return;
            // Kalau user klik kolom Pilih, biarkan CellContentClick yang handle
            if (dgv.Columns[e.ColumnIndex].Name == "ColPick") return;
            var row = dgv.Rows[e.RowIndex];
            if (row.IsNewRow) return;
            var cell = row.Cells["ColPick"];
            bool current = cell.Value is bool b && b;
            cell.Value = !current;
            // Tidak perlu CommitEdit di sini — assignment Value langsung mem-fire CellValueChanged.
        };
        // Header-click toggle: klik header "Pilih" → centang/batal-pilih semua baris yang sedang tampil.
        dgv.ColumnHeaderMouseClick += (s, e) =>
        {
            if (e.ColumnIndex < 0 || dgv.Columns[e.ColumnIndex].Name != "ColPick") return;
            ToggleAllVisibleRows();
        };

        // Bottom buttons
        var pnlBottom = new Panel { Dock = DockStyle.Bottom, Height = 76, BackColor = AppTheme.SidebarBg, Padding = new Padding(12) };
        pnlBottom.Paint += (s, e) => { using var pen = new Pen(AppTheme.Border); e.Graphics.DrawLine(pen, 0, 0, pnlBottom.Width, 0); };

        var btnLoad = new Button { Text = "✏ Edit Estimasi", Location = new Point(12, 10), Width = 150, Height = 36 };
        AppTheme.StyleButton(btnLoad, AppTheme.Primary, Color.White);
        btnLoad.Click += BtnLoad_Click;
        _toolTip.SetToolTip(btnLoad, "Buka estimasi yang dipilih (baris dengan kursor) untuk diedit.");

        var btnDelete = new Button { Text = "🗑 Hapus", Location = new Point(174, 10), Width = 100, Height = 36 };
        AppTheme.StyleButton(btnDelete, AppTheme.Danger, Color.White);
        btnDelete.Click += BtnDelete_Click;
        _toolTip.SetToolTip(btnDelete, "Hapus estimasi yang dipilih (baris dengan kursor).");

        var btnChangeStatus = new Button { Text = "✏ Ubah Status", Location = new Point(286, 10), Width = 140, Height = 36 };
        AppTheme.StyleButton(btnChangeStatus, AppTheme.Bg2, AppTheme.Text2);
        btnChangeStatus.Click += BtnChangeStatus_Click;

        // Split-button "Export ▾" — dropdown menu dengan 4 format: PDF Formal/Modern, Word, Excel
        var btnExport = new Button { Text = "📄 Export ▾", Location = new Point(438, 10), Width = 130, Height = 36 };
        AppTheme.StyleButton(btnExport, AppTheme.Brand500, Color.White);
        var exportMenu = new ContextMenuStrip { BackColor = AppTheme.Bg1, ForeColor = AppTheme.Text1 };
        exportMenu.Items.Add(new ToolStripMenuItem("📄 Export PDF (Surat Formal)", null, (s, e) => BtnExport_Click(s, e)) { ForeColor = AppTheme.Text1 });
        exportMenu.Items.Add(new ToolStripMenuItem("📝 Export Word (.docx)", null, BtnExportWord_Click) { ForeColor = AppTheme.Text1 });
        exportMenu.Items.Add(new ToolStripMenuItem("📊 Export Excel (.xlsx)", null, BtnExportExcel_Click) { ForeColor = AppTheme.Text1 });
        exportMenu.Items.Add(new ToolStripSeparator());
        exportMenu.Items.Add(new ToolStripMenuItem("📊 Export CSV (round-trip)", null, BtnExportCsv_Click) { ForeColor = AppTheme.Text1 });
        btnExport.Click += (s, e) => exportMenu.Show(btnExport, new Point(0, btnExport.Height));
        _toolTip.SetToolTip(btnExport, "Export estimasi terpilih ke PDF / Word / Excel / CSV.");

        // Tombol baru: gabungkan beberapa estimasi (yang dicentang) → satu surat
        btnCombine = new Button { Text = "📑 Penawaran Gabungan", Location = new Point(580, 10), Width = 200, Height = 36, Enabled = false };
        AppTheme.StyleButton(btnCombine, AppTheme.Brand500, Color.White);
        btnCombine.Click += BtnCombine_Click;
        _toolTip.SetToolTip(btnCombine, "Centang minimal 2 estimasi dulu di kolom 'Pilih' (kiri tabel).");

        var btnImportCsv = new Button { Text = "📥 Import CSV", Location = new Point(790, 10), Width = 130, Height = 36 };
        AppTheme.StyleButton(btnImportCsv, AppTheme.Bg3, AppTheme.Text1);
        btnImportCsv.Click += BtnImportCsv_Click;
        _toolTip.SetToolTip(btnImportCsv, "Restore estimasi dari file CSV (round-trip dari Export CSV).");

        pnlBottom.Controls.AddRange(new Control[] { btnLoad, btnDelete, btnChangeStatus, btnExport, btnCombine, btnImportCsv });

        // Urutan Add penting karena DockStyle.Top/Fill mengikuti urutan terbalik:
        // dgv (Fill) → pnlHint (Top, di atas dgv) → pnlFilter (Top, paling atas) → pnlBottom (Bottom).
        Controls.Add(dgv);
        Controls.Add(pnlHint);
        Controls.Add(pnlFilter);
        Controls.Add(pnlBottom);

        Load += async (s, e) => await LoadDataAsync();
    }

    /// <summary>Toggle centang semua baris yang sedang tampil di grid (setelah filter).</summary>
    private void ToggleAllVisibleRows()
    {
        _bulkToggling = true;
        try
        {
            // Tentukan target state: kalau ada yang BELUM tercentang → centang semua,
            // kalau semua sudah tercentang → batal pilih semua.
            bool anyUnchecked = false;
            int rowCount = 0;
            foreach (DataGridViewRow row in dgv.Rows)
            {
                if (row.IsNewRow) continue;
                rowCount++;
                var v = row.Cells["ColPick"].Value;
                if (!(v is bool b && b)) { anyUnchecked = true; break; }
            }
            bool targetState = anyUnchecked;  // true=centang semua, false=batal pilih
            foreach (DataGridViewRow row in dgv.Rows)
            {
                if (row.IsNewRow) continue;
                row.Cells["ColPick"].Value = targetState;
                UpdateRowHighlight(row);
            }
            // Update header: ☑ kalau ada yang ke-pilih, ☐ kalau kosong
            dgv.Columns["ColPick"].HeaderText = targetState && rowCount > 0 ? "Pilih ☑" : "Pilih ☐";
        }
        finally
        {
            _bulkToggling = false;
            UpdateCombineButtonState();
        }
    }

    /// <summary>Beri highlight visual pada baris yang ter-centang agar mudah dilihat.</summary>
    private void UpdateRowHighlight(DataGridViewRow row)
    {
        if (row.IsNewRow) return;
        bool picked = row.Cells["ColPick"].Value is bool b && b;
        // Brand500 dengan alpha rendah → jadi accent halus di atas Bg1 (sidebar bg)
        if (picked)
        {
            row.DefaultCellStyle.BackColor = AppTheme.Bg3;          // lebih terang dari Bg2 → kelihatan
            row.DefaultCellStyle.ForeColor = AppTheme.Text1;
            row.DefaultCellStyle.SelectionBackColor = AppTheme.Brand500;
        }
        else
        {
            // Reset ke default — biarkan AlternatingRowsDefaultCellStyle ambil alih
            row.DefaultCellStyle.BackColor = Color.Empty;
            row.DefaultCellStyle.ForeColor = Color.Empty;
            row.DefaultCellStyle.SelectionBackColor = Color.Empty;
        }
    }

    private async Task LoadDataAsync()
    {
        _allEstimations = (await _estimationRepo.GetAllWithDetailsAsync()).ToList();
        FilterGrid();
    }

    private void FilterGrid()
    {
        var searchTerm = txtSearch.Text.Trim().ToLower();
        var status = cmbStatus.SelectedItem?.ToString();

        var filtered = _allEstimations.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(searchTerm))
            filtered = filtered.Where(e =>
                e.EstimationNumber.ToLower().Contains(searchTerm) ||
                e.ClientName.ToLower().Contains(searchTerm));

        if (status != "Semua" && !string.IsNullOrWhiteSpace(status))
            filtered = filtered.Where(e => e.Status == status);

        dgv.Rows.Clear();
        foreach (var est in filtered)
        {
            // Urutan kolom: ColPick, ColNo, ColClient, ColCompany, ColDate, ColStatus, ColTotal, ColId
            var rowIdx = dgv.Rows.Add(
                false, // ColPick (checkbox unchecked default)
                est.EstimationNumber,
                est.ClientName,
                est.Company ?? "",
                est.CreatedDate.ToLocalTime().ToString("dd MMM yyyy"),
                est.Status,
                "Rp " + est.TotalPrice.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("id-ID")),
                est.EstimationId
            );

            // Color-code status (dark palette)
            var (fgColor, _) = AppTheme.GetStatusColor(est.Status);
            dgv.Rows[rowIdx].Cells["ColStatus"].Style.ForeColor = fgColor;
        }
        UpdateCombineButtonState();
    }

    /// <summary>Enable tombol Penawaran Gabungan hanya kalau >= 2 baris ter-centang.</summary>
    private void UpdateCombineButtonState()
    {
        if (btnCombine == null) return;
        int picked = GetCheckedEstimationIds().Count;
        btnCombine.Enabled = picked >= 2;
        btnCombine.Text = picked >= 2
            ? $"📑 Penawaran Gabungan ({picked})"
            : "📑 Penawaran Gabungan";
        // Tooltip yang adaptif: kasih instruksi jelas kalau belum cukup centang
        if (_toolTip != null)
        {
            _toolTip.SetToolTip(btnCombine, picked >= 2
                ? $"Buat 1 surat penawaran gabungan dari {picked} estimasi yang dicentang."
                : (picked == 1
                    ? "Centang minimal 1 estimasi lagi (total ≥ 2) di kolom 'Pilih'."
                    : "Centang minimal 2 estimasi dulu di kolom 'Pilih' (paling kiri tabel)."));
        }
        // Update header text agar reflect state global
        if (dgv?.Columns["ColPick"] != null && dgv.Rows.Count > 0)
        {
            int total = 0;
            foreach (DataGridViewRow row in dgv.Rows) if (!row.IsNewRow) total++;
            dgv.Columns["ColPick"].HeaderText =
                picked == 0 ? "Pilih ☐"
                : picked == total ? "Pilih ☑"
                : $"Pilih ({picked})";
        }
    }

    /// <summary>Ambil ID estimasi yang baris-nya ter-centang oleh user.</summary>
    private List<int> GetCheckedEstimationIds()
    {
        var ids = new List<int>();
        foreach (DataGridViewRow row in dgv.Rows)
        {
            if (row.IsNewRow) continue;
            var pickVal = row.Cells["ColPick"].Value;
            if (pickVal is bool b && b && row.Cells["ColId"].Value is int id)
                ids.Add(id);
        }
        return ids;
    }

    private void Dgv_CellDoubleClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0) return;
        OpenSelectedEstimation();
    }

    private void BtnLoad_Click(object? sender, EventArgs e)
    {
        if (dgv.CurrentRow == null) return;
        OpenSelectedEstimation();
    }

    private void OpenSelectedEstimation()
    {
        if (dgv.CurrentRow == null) return;
        if (dgv.CurrentRow.Cells["ColId"].Value is not int id) return;
        LoadedEstimation = _allEstimations.FirstOrDefault(e => e.EstimationId == id);
        if (LoadedEstimation != null)
        {
            DialogResult = DialogResult.OK;
            Close();
        }
    }

    private async void BtnDelete_Click(object? sender, EventArgs e)
    {
        if (dgv.CurrentRow == null) return;
        if (dgv.CurrentRow.Cells["ColId"].Value is not int delId) return;
        var no = dgv.CurrentRow.Cells["ColNo"].Value?.ToString();
        var confirm = MessageBox.Show($"Hapus estimasi {no}?", "Konfirmasi", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (confirm != DialogResult.Yes) return;

        await _estimationRepo.DeleteAsync(delId);
        await LoadDataAsync();
    }

    private void BtnExport_Click(object? sender, EventArgs e)
    {
        if (dgv.CurrentRow == null) return;
        if (dgv.CurrentRow.Cells["ColId"].Value is not int id) return;
        var est = _allEstimations.FirstOrDefault(x => x.EstimationId == id);
        if (est == null) return;

        // Always export as Surat Resmi (formal letter with company letterhead)
        var tempPath = Path.Combine(Path.GetTempPath(),
            $"Surat_{est.EstimationNumber}_{DateTime.Now:HHmmss}.pdf");
        try
        {
            var settings = _context != null
                ? _context.Settings.ToDictionary(s => s.SettingKey, s => s.SettingValue ?? "")
                : new Dictionary<string, string>();

            var marginPct = est.MarginPercent != 0 ? est.MarginPercent
                : (est.SubTotal > 0 ? Math.Round(est.Margin / est.SubTotal * 100, 1) : 0);
            var taxBase   = est.SubTotal + est.Margin + est.ShippingCost;
            var taxPct    = taxBase > 0 ? Math.Round(est.Tax / taxBase * 100, 1) : 0;
            var pphPct    = est.PPhPercent;

            var letterItems = est.Details.Select(d => new PdfLetterExport.LineItem(
                d.Product?.ReferenceCode ?? "—",
                d.Product?.ProductName ?? "—",
                d.Product?.Vendor ?? "",
                string.IsNullOrWhiteSpace(d.Section) ? "Material Utama" : d.Section,
                d.Quantity,
                string.IsNullOrWhiteSpace(d.Satuan) ? "pcs" : d.Satuan,
                d.UnitPrice,
                d.LineTotalPrice)).ToList();

            PdfLetterExport.Generate(
                outputPath:       tempPath,
                estimationNumber: !string.IsNullOrWhiteSpace(est.NomorSurat) ? est.NomorSurat : est.EstimationNumber,
                clientName:       est.ClientName,
                contactPhone:     est.ContactPhone,
                company:          est.Company,
                address:          est.Address,
                perihal:          est.ProjectName,
                createdDate:      est.CreatedDate,
                notes:            est.Notes ?? "",
                items:            letterItems,
                subtotal:         est.SubTotal,
                margin1Percent:   marginPct,
                margin2Percent:   est.Margin2Percent,
                margin3Percent:   est.Margin3Percent,
                marginAmount:     est.Margin,
                shippingCost:     est.ShippingCost,
                taxPercent:       taxPct,
                taxAmount:        est.Tax,
                pphPercent:       pphPct,
                pphAmount:        est.PPh,
                total:            est.TotalPrice,
                settings:         settings);

            // Open preview in default PDF viewer
            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo(tempPath) { UseShellExecute = true });

            // Offer to save permanently
            var save = MessageBox.Show(
                "Surat Penawaran telah dibuka sebagai preview.\n\nSimpan ke file permanen?",
                "Simpan PDF",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (save == DialogResult.Yes)
            {
                using var sfd = new SaveFileDialog
                {
                    Title      = "Simpan Surat Penawaran PDF",
                    Filter     = "PDF Files (*.pdf)|*.pdf",
                    FileName   = $"SuratPenawaran_{est.EstimationNumber}.pdf",
                    DefaultExt = "pdf"
                };
                if (sfd.ShowDialog() == DialogResult.OK)
                {
                    File.Copy(tempPath, sfd.FileName, overwrite: true);
                    MessageBox.Show($"PDF disimpan:\n{sfd.FileName}", "Tersimpan",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Gagal membuat PDF:\n{ex.Message}", "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Task.Run(async () =>
            {
                await Task.Delay(30_000);
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
            });
        }
    }

    /// <summary>
    /// Helper: ambil estimation yang sedang di-highlight di grid (1 baris saja).
    /// Sama logic dengan BtnExport_Click — return null kalau tidak valid.
    /// </summary>
    private Estimation? GetSelectedEstimation()
    {
        if (dgv.CurrentRow == null) return null;
        if (dgv.CurrentRow.Cells["ColId"].Value is not int id) return null;
        return _allEstimations.FirstOrDefault(x => x.EstimationId == id);
    }

    /// <summary>
    /// Build kumpulan setting (dari DB) yang dipakai writer.
    /// Empty dict kalau context tidak tersedia (mode terbatas).
    /// </summary>
    private Dictionary<string, string> LoadSettingsDict()
        => _context != null
            ? _context.Settings.ToDictionary(s => s.SettingKey, s => s.SettingValue ?? "")
            : new Dictionary<string, string>();

    /// <summary>
    /// Export estimasi terpilih ke Word .docx — file editable, bisa diedit
    /// kolom/susunan/format di Microsoft Word setelah generate.
    /// </summary>
    private void BtnExportWord_Click(object? sender, EventArgs e)
    {
        var est = GetSelectedEstimation();
        if (est == null)
        {
            MessageBox.Show("Pilih estimasi terlebih dahulu (klik baris di tabel).",
                "Perhatian", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        using var sfd = new SaveFileDialog
        {
            Title      = "Simpan Penawaran sebagai Word",
            Filter     = "Word Documents (*.docx)|*.docx",
            FileName   = SanitizeFilename($"Penawaran_{est.EstimationNumber}_{est.ClientName}_{DateTime.Now:yyyyMMdd}.docx"),
            DefaultExt = "docx",
        };
        if (sfd.ShowDialog() != DialogResult.OK) return;

        try
        {
            var marginPct = est.MarginPercent != 0 ? est.MarginPercent
                : (est.SubTotal > 0 ? Math.Round(est.Margin / est.SubTotal * 100, 1) : 0);
            var taxBase   = est.SubTotal + est.Margin + est.ShippingCost;
            var taxPct    = taxBase > 0 ? Math.Round(est.Tax / taxBase * 100, 1) : 0;
            var pphPct    = est.PPhPercent;

            var items = est.Details.Select(d => new WordLetterExport.LineItem(
                d.Product?.ReferenceCode ?? "—",
                d.Product?.ProductName   ?? "—",
                d.Product?.Vendor        ?? "",
                string.IsNullOrWhiteSpace(d.Section) ? "Material Utama" : d.Section,
                d.Quantity,
                string.IsNullOrWhiteSpace(d.Satuan) ? "pcs" : d.Satuan,
                d.UnitPrice,
                d.LineTotalPrice)).ToList();

            WordLetterExport.Generate(
                outputPath:       sfd.FileName,
                estimationNumber: !string.IsNullOrWhiteSpace(est.NomorSurat) ? est.NomorSurat : est.EstimationNumber,
                clientName:       est.ClientName,
                contactPhone:     est.ContactPhone,
                company:          est.Company,
                address:          est.Address,
                perihal:          est.ProjectName,
                createdDate:      est.CreatedDate,
                notes:            est.Notes ?? "",
                items:            items,
                subtotal:         est.SubTotal,
                marginAmount:     est.Margin,
                shippingCost:     est.ShippingCost,
                taxPercent:       taxPct,
                taxAmount:        est.Tax,
                pphPercent:       pphPct,
                pphAmount:        est.PPh,
                total:            est.TotalPrice,
                settings:         LoadSettingsDict());

            var open = MessageBox.Show(
                $"File Word berhasil dibuat:\n{sfd.FileName}\n\nBuka file sekarang?",
                "Export Word Selesai", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
            if (open == DialogResult.Yes)
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(sfd.FileName) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Gagal export Word:\n{ex.Message}", "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>
    /// Export estimasi terpilih ke Excel .xlsx — file editable dengan SUM
    /// formula otomatis. User bisa edit Qty/Harga dan auto-recalc.
    /// </summary>
    private void BtnExportExcel_Click(object? sender, EventArgs e)
    {
        var est = GetSelectedEstimation();
        if (est == null)
        {
            MessageBox.Show("Pilih estimasi terlebih dahulu (klik baris di tabel).",
                "Perhatian", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        using var sfd = new SaveFileDialog
        {
            Title      = "Simpan Penawaran sebagai Excel",
            Filter     = "Excel Workbook (*.xlsx)|*.xlsx",
            FileName   = SanitizeFilename($"Penawaran_{est.EstimationNumber}_{est.ClientName}_{DateTime.Now:yyyyMMdd}.xlsx"),
            DefaultExt = "xlsx",
        };
        if (sfd.ShowDialog() != DialogResult.OK) return;

        try
        {
            var marginPct = est.MarginPercent != 0 ? est.MarginPercent
                : (est.SubTotal > 0 ? Math.Round(est.Margin / est.SubTotal * 100, 1) : 0);
            var taxBase   = est.SubTotal + est.Margin + est.ShippingCost;
            var taxPct    = taxBase > 0 ? Math.Round(est.Tax / taxBase * 100, 1) : 0;
            var pphPct    = est.PPhPercent;

            var items = est.Details.Select(d => new ExcelLetterExport.LineItem(
                d.Product?.ReferenceCode ?? "—",
                d.Product?.ProductName   ?? "—",
                d.Product?.Vendor        ?? "",
                string.IsNullOrWhiteSpace(d.Section) ? "Material Utama" : d.Section,
                d.Quantity,
                string.IsNullOrWhiteSpace(d.Satuan) ? "pcs" : d.Satuan,
                d.UnitPrice,
                d.LineTotalPrice)).ToList();

            ExcelLetterExport.Generate(
                outputPath:       sfd.FileName,
                estimationNumber: !string.IsNullOrWhiteSpace(est.NomorSurat) ? est.NomorSurat : est.EstimationNumber,
                clientName:       est.ClientName,
                contactPhone:     est.ContactPhone,
                company:          est.Company,
                address:          est.Address,
                perihal:          est.ProjectName,
                createdDate:      est.CreatedDate,
                notes:            est.Notes ?? "",
                items:            items,
                subtotal:         est.SubTotal,
                marginAmount:     est.Margin,
                shippingCost:     est.ShippingCost,
                taxPercent:       taxPct,
                taxAmount:        est.Tax,
                pphPercent:       pphPct,
                pphAmount:        est.PPh,
                total:            est.TotalPrice,
                settings:         LoadSettingsDict());

            var open = MessageBox.Show(
                $"File Excel berhasil dibuat:\n{sfd.FileName}\n\nBuka file sekarang?",
                "Export Excel Selesai", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
            if (open == DialogResult.Yes)
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(sfd.FileName) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Gagal export Excel:\n{ex.Message}", "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>
    /// Bangun surat penawaran gabungan dari estimasi-estimasi yang ter-centang.
    /// Order panel mengikuti urutan baris di grid (terlama → terbaru sesuai filter).
    /// </summary>
    private void BtnCombine_Click(object? sender, EventArgs e)
    {
        var ids = GetCheckedEstimationIds();
        if (ids.Count < 2)
        {
            MessageBox.Show("Centang minimal 2 estimasi untuk Penawaran Gabungan.",
                "Perhatian", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        // Ambil estimasi sesuai urutan dicentang (urutan baris di grid)
        var pickedEsts = new List<Estimation>();
        foreach (var id in ids)
        {
            var est = _allEstimations.FirstOrDefault(x => x.EstimationId == id);
            if (est != null) pickedEsts.Add(est);
        }
        if (pickedEsts.Count < 2)
        {
            MessageBox.Show("Estimasi yang dipilih tidak valid lagi. Refresh & coba lagi.",
                "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        // Dialog compose
        using var dlg = new CombineEstimationsDialog(pickedEsts);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        var settings = _context != null
            ? _context.Settings.ToDictionary(s => s.SettingKey, s => s.SettingValue ?? "")
            : new Dictionary<string, string>();

        // Build summary via Core calculator
        var summary = CombinedQuotationCalculator.Build(
            pickedEsts,
            combinedShippingCost: dlg.CombinedShippingCost,
            taxPercent:           CombinedQuotationCalculator.DefaultTaxPercent);

        // Customer info ambil dari panel pertama
        var first = pickedEsts[0];

        // ─────────────────────────────────────────────────────────────────
        // Format Word & Excel: minta save path langsung (bukan preview-then-save
        // seperti PDF), karena file langsung editable di Word/Excel.
        // Format PDF tetap pakai preview-then-save flow.
        // ─────────────────────────────────────────────────────────────────
        var fmt = dlg.SelectedFormat;
        if (fmt == CombineEstimationsDialog.PdfFormat.Word)
        {
            GenerateCombinedWord(pickedEsts, first, summary, dlg.NomorSurat, settings);
            return;
        }
        if (fmt == CombineEstimationsDialog.PdfFormat.Excel)
        {
            GenerateCombinedExcel(pickedEsts, first, summary, dlg.NomorSurat, settings);
            return;
        }

        var tempPath = Path.Combine(Path.GetTempPath(),
            $"SuratGabungan_{DateTime.Now:yyyyMMdd_HHmmss}.pdf");

        try
        {
            if (fmt == CombineEstimationsDialog.PdfFormat.Formal)
            {
                var panels = pickedEsts.Select(est =>
                    new PdfLetterExport.CombinedPanel(
                        est.EstimationNumber,
                        est.ProjectName,
                        est.Details.Select(d => new PdfLetterExport.LineItem(
                            d.Product?.ReferenceCode ?? "—",
                            d.Product?.ProductName ?? "—",
                            d.Product?.Vendor ?? "",
                            string.IsNullOrWhiteSpace(d.Section) ? "Material Utama" : d.Section,
                            d.Quantity,
                            string.IsNullOrWhiteSpace(d.Satuan) ? "pcs" : d.Satuan,
                            d.UnitPrice,
                            d.LineTotalPrice)).ToList()))
                    .ToList();

                PdfLetterExport.GenerateCombined(
                    outputPath:   tempPath,
                    nomorSurat:   dlg.NomorSurat,
                    clientName:   first.ClientName,
                    contactPhone: first.ContactPhone,
                    company:      first.Company,
                    address:      first.Address,
                    perihal:      first.ProjectName,
                    createdDate:  DateTime.UtcNow,
                    notes:        first.Notes ?? "",
                    panels:       panels,
                    summary:      summary,
                    settings:     settings);
            }
            else
            {
                // Modern PDF (default fallback)
                var panels = pickedEsts.Select(est =>
                    new PdfQuotationExport.CombinedPanel(
                        est.EstimationNumber,
                        est.ProjectName,
                        est.Details.Select(d => new PdfQuotationExport.LineItem(
                            d.Product?.ReferenceCode ?? "—",
                            d.Product?.ProductName ?? "—",
                            string.IsNullOrWhiteSpace(d.Section) ? "Material Utama" : d.Section,
                            d.Quantity,
                            string.IsNullOrWhiteSpace(d.Satuan) ? "pcs" : d.Satuan,
                            d.UnitPrice,
                            d.AdjPercent,
                            d.LineTotalPrice)).ToList()))
                    .ToList();

                PdfQuotationExport.GenerateCombined(
                    outputPath:   tempPath,
                    nomorSurat:   dlg.NomorSurat,
                    clientName:   first.ClientName,
                    contactPhone: first.ContactPhone,
                    company:      first.Company,
                    address:      first.Address,
                    createdDate:  DateTime.UtcNow,
                    notes:        first.Notes ?? "",
                    panels:       panels,
                    summary:      summary,
                    settings:     settings);
            }

            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo(tempPath) { UseShellExecute = true });

            var save = MessageBox.Show(
                $"Surat Penawaran Gabungan ({pickedEsts.Count} panel) telah dibuka sebagai preview.\n\nSimpan ke file permanen?",
                "Simpan PDF", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (save == DialogResult.Yes)
            {
                using var sfd = new SaveFileDialog
                {
                    Title      = "Simpan Surat Penawaran Gabungan",
                    Filter     = "PDF Files (*.pdf)|*.pdf",
                    FileName   = $"SuratGabungan_{dlg.NomorSurat}_{DateTime.Now:yyyyMMdd}.pdf",
                    DefaultExt = "pdf",
                };
                if (sfd.ShowDialog() == DialogResult.OK)
                {
                    File.Copy(tempPath, sfd.FileName, overwrite: true);
                    MessageBox.Show($"PDF disimpan:\n{sfd.FileName}", "Tersimpan",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Gagal membuat PDF gabungan:\n{ex.Message}", "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Task.Run(async () =>
            {
                await Task.Delay(30_000);
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
            });
        }
    }

    /// <summary>Subroutine BtnCombine_Click — output Word .docx.</summary>
    private void GenerateCombinedWord(List<Estimation> pickedEsts, Estimation first,
        CombinedQuotationCalculator.CombinedSummary summary, string nomorSurat,
        Dictionary<string, string> settings)
    {
        using var sfd = new SaveFileDialog
        {
            Title      = "Simpan Penawaran Gabungan sebagai Word",
            Filter     = "Word Documents (*.docx)|*.docx",
            FileName   = SanitizeFilename($"SuratGabungan_{nomorSurat}_{DateTime.Now:yyyyMMdd}.docx"),
            DefaultExt = "docx",
        };
        if (sfd.ShowDialog() != DialogResult.OK) return;

        try
        {
            var panels = pickedEsts.Select(est =>
                new WordLetterExport.CombinedPanel(
                    est.EstimationNumber,
                    est.ProjectName,
                    est.Details.Select(d => new WordLetterExport.LineItem(
                        d.Product?.ReferenceCode ?? "—",
                        d.Product?.ProductName   ?? "—",
                        d.Product?.Vendor        ?? "",
                        string.IsNullOrWhiteSpace(d.Section) ? "Material Utama" : d.Section,
                        d.Quantity,
                        string.IsNullOrWhiteSpace(d.Satuan) ? "pcs" : d.Satuan,
                        d.UnitPrice,
                        d.LineTotalPrice)).ToList()))
                .ToList();

            WordLetterExport.GenerateCombined(
                outputPath:   sfd.FileName,
                nomorSurat:   nomorSurat,
                clientName:   first.ClientName,
                contactPhone: first.ContactPhone,
                company:      first.Company,
                address:      first.Address,
                perihal:      first.ProjectName,
                createdDate:  DateTime.UtcNow,
                notes:        first.Notes ?? "",
                panels:       panels,
                summary:      summary,
                settings:     settings);

            var open = MessageBox.Show(
                $"File Word Penawaran Gabungan ({pickedEsts.Count} panel) berhasil dibuat:\n{sfd.FileName}\n\nBuka sekarang?",
                "Export Word Selesai", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
            if (open == DialogResult.Yes)
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(sfd.FileName) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Gagal export Word gabungan:\n{ex.Message}", "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>Subroutine BtnCombine_Click — output Excel .xlsx.</summary>
    private void GenerateCombinedExcel(List<Estimation> pickedEsts, Estimation first,
        CombinedQuotationCalculator.CombinedSummary summary, string nomorSurat,
        Dictionary<string, string> settings)
    {
        using var sfd = new SaveFileDialog
        {
            Title      = "Simpan Penawaran Gabungan sebagai Excel",
            Filter     = "Excel Workbook (*.xlsx)|*.xlsx",
            FileName   = SanitizeFilename($"SuratGabungan_{nomorSurat}_{DateTime.Now:yyyyMMdd}.xlsx"),
            DefaultExt = "xlsx",
        };
        if (sfd.ShowDialog() != DialogResult.OK) return;

        try
        {
            var panels = pickedEsts.Select(est =>
                new ExcelLetterExport.CombinedPanel(
                    est.EstimationNumber,
                    est.ProjectName,
                    est.Details.Select(d => new ExcelLetterExport.LineItem(
                        d.Product?.ReferenceCode ?? "—",
                        d.Product?.ProductName   ?? "—",
                        d.Product?.Vendor        ?? "",
                        string.IsNullOrWhiteSpace(d.Section) ? "Material Utama" : d.Section,
                        d.Quantity,
                        string.IsNullOrWhiteSpace(d.Satuan) ? "pcs" : d.Satuan,
                        d.UnitPrice,
                        d.LineTotalPrice)).ToList()))
                .ToList();

            ExcelLetterExport.GenerateCombined(
                outputPath:   sfd.FileName,
                nomorSurat:   nomorSurat,
                clientName:   first.ClientName,
                contactPhone: first.ContactPhone,
                company:      first.Company,
                address:      first.Address,
                perihal:      first.ProjectName,
                createdDate:  DateTime.UtcNow,
                notes:        first.Notes ?? "",
                panels:       panels,
                summary:      summary,
                settings:     settings);

            var open = MessageBox.Show(
                $"File Excel Penawaran Gabungan ({pickedEsts.Count} panel) berhasil dibuat:\n{sfd.FileName}\n\nBuka sekarang?",
                "Export Excel Selesai", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
            if (open == DialogResult.Yes)
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(sfd.FileName) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Gagal export Excel gabungan:\n{ex.Message}", "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void BtnExportCsv_Click(object? sender, EventArgs e)
    {
        if (dgv.CurrentRow == null) { MessageBox.Show("Pilih estimasi terlebih dahulu.", "Perhatian", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        if (dgv.CurrentRow.Cells["ColId"].Value is not int id) return;
        var est = _allEstimations.FirstOrDefault(x => x.EstimationId == id);
        if (est == null) return;

        using var sfd = new SaveFileDialog
        {
            Title      = "Export Estimasi ke CSV",
            Filter     = "CSV Files (*.csv)|*.csv",
            FileName   = SanitizeFilename($"Estimasi_{est.EstimationNumber}_{est.ClientName}_{DateTime.Now:yyyyMMdd}.csv"),
            DefaultExt = "csv"
        };
        if (sfd.ShowDialog() != DialogResult.OK) return;

        try
        {
            // Round-trip friendly format:
            //   - Headers: English snake_case (machine readable)
            //   - Numbers: plain (no thousand separators) so Excel formulas work
            //   - Dates:   ISO YYYY-MM-DD (unambiguous)
            //   - Encoding: UTF-8 with BOM (Excel ID opens cleanly)
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            var sb = new System.Text.StringBuilder();

            string Esc(string? s)
            {
                s ??= "";
                if (s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0)
                    return "\"" + s.Replace("\"", "\"\"") + "\"";
                return s;
            }

            // ── Section 1 of 3: metadata key/value pairs ──────────────────
            sb.AppendLine("section,key,value");
            sb.AppendLine($"meta,estimation_number,{Esc(est.EstimationNumber)}");
            sb.AppendLine($"meta,nomor_surat,{Esc(est.NomorSurat)}");
            sb.AppendLine($"meta,client_name,{Esc(est.ClientName)}");
            sb.AppendLine($"meta,company,{Esc(est.Company)}");
            sb.AppendLine($"meta,project_name,{Esc(est.ProjectName)}");
            sb.AppendLine($"meta,status,{Esc(est.Status)}");
            sb.AppendLine($"meta,created_date,{est.CreatedDate.ToLocalTime():yyyy-MM-dd}");
            sb.AppendLine($"meta,created_at,{est.CreatedDate.ToLocalTime():yyyy-MM-ddTHH:mm:ss}");
            sb.AppendLine();

            // ── Section 2 of 3: line items ────────────────────────────────
            sb.AppendLine("no,section_name,reference_code,product_name,vendor,satuan,quantity,unit_price,line_total");

            int no = 0;
            foreach (var d in est.Details)
            {
                no++;
                var name   = d.Product?.ProductName ?? "";
                var code   = d.Product?.ReferenceCode ?? "";
                var vendor = d.Product?.Vendor ?? "";
                var sec    = string.IsNullOrWhiteSpace(d.Section) ? "Material Utama" : d.Section;
                sb.AppendLine(
                    $"{no}," +
                    $"{Esc(sec)}," +
                    $"{Esc(code)}," +
                    $"{Esc(name)}," +
                    $"{Esc(vendor)}," +
                    $"{Esc(d.Satuan)}," +
                    $"{d.Quantity}," +
                    $"{d.UnitPrice.ToString("0.##", inv)}," +
                    $"{d.LineTotalPrice.ToString("0.##", inv)}");
            }

            // ── Section 3 of 3: summary key/value pairs ───────────────────
            sb.AppendLine();
            sb.AppendLine("summary,key,amount");
            sb.AppendLine($"summary,subtotal,{est.SubTotal.ToString("0.##", inv)}");
            if (est.Margin != 0)
                sb.AppendLine($"summary,margin,{est.Margin.ToString("0.##", inv)}");
            if (est.ShippingCost > 0)
                sb.AppendLine($"summary,shipping,{est.ShippingCost.ToString("0.##", inv)}");
            if (est.Tax > 0)
                sb.AppendLine($"summary,ppn,{est.Tax.ToString("0.##", inv)}");
            if (est.PPh > 0)
                sb.AppendLine($"summary,pph,{est.PPh.ToString("0.##", inv)}");
            sb.AppendLine($"summary,grand_total,{est.TotalPrice.ToString("0.##", inv)}");

            // UTF-8 with explicit BOM — Excel ID otherwise mis-detects encoding
            // and shows garbled characters for à or special punctuation.
            File.WriteAllText(sfd.FileName, sb.ToString(), new System.Text.UTF8Encoding(true));

            var open = MessageBox.Show(
                $"CSV berhasil dibuat ({no} item).\nBuka folder sekarang?",
                "Export Selesai", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
            if (open == DialogResult.Yes)
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                    "explorer.exe", $"/select,\"{sfd.FileName}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Gagal export CSV:\n{ex.Message}", "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>
    /// Round-trip import: read the 3-section CSV produced by Export CSV and
    /// re-create the estimation in the local DB. Useful for cross-machine
    /// backup &amp; restore. Conflicts on EstimationNumber are resolved by a
    /// prompt: Overwrite / Skip / Rename.
    /// </summary>
    private async void BtnImportCsv_Click(object? sender, EventArgs e)
    {
        if (_context == null)
        {
            MessageBox.Show("Import CSV memerlukan koneksi database. Tidak tersedia di mode terbatas.",
                "Tidak Tersedia", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        using var ofd = new OpenFileDialog
        {
            Title  = "Import Estimasi dari CSV",
            Filter = "CSV Files (*.csv)|*.csv|All Files (*.*)|*.*",
        };
        if (ofd.ShowDialog() != DialogResult.OK) return;

        try
        {
            var importer = new PanelCalculator.Data.DataSeeding.EstimationCsvImporter(_context);

            var report = await importer.ImportFromFileAsync(ofd.FileName, conflictNumber =>
            {
                // Prompt user with 3-button choice
                using var dlg = new ConflictResolutionDialog(conflictNumber);
                dlg.ShowDialog(this);
                return dlg.Result;
            });

            await LoadDataAsync();

            // Write a side-car log for support audit
            try
            {
                var dir = Path.GetDirectoryName(ofd.FileName);
                if (!string.IsNullOrEmpty(dir))
                {
                    var logPath = Path.Combine(dir,
                        $"import-{Path.GetFileNameWithoutExtension(ofd.FileName)}-{DateTime.Now:yyyyMMdd-HHmmss}.log");
                    var lines = new List<string>
                    {
                        $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Estimation CSV import",
                        $"  source       = {ofd.FileName}",
                        $"  imported     = {report.Imported}",
                        $"  est_number   = {report.EstimationNumber}",
                        $"  final_number = {report.FinalEstimationNumber}",
                        $"  parsed       = {report.ItemsParsed}",
                        $"  imported_n   = {report.ItemsImported}",
                        $"  orphaned     = {report.ItemsOrphaned}",
                    };
                    if (report.OrphanedItems.Count > 0)
                        lines.Add("  ORPHANED ITEMS (not found in catalogue):");
                    foreach (var o in report.OrphanedItems) lines.Add("    " + o);
                    if (report.Warnings.Count > 0) lines.Add("  WARNINGS:");
                    foreach (var w in report.Warnings) lines.Add("    " + w);
                    if (report.Errors.Count > 0) lines.Add("  ERRORS:");
                    foreach (var er in report.Errors) lines.Add("    " + er);
                    File.WriteAllLines(logPath, lines);
                }
            }
            catch { /* best-effort log */ }

            // ── Build user-facing message ────────────────────────────────
            var sb = new System.Text.StringBuilder();
            if (report.Imported)
            {
                sb.AppendLine($"Estimasi {report.FinalEstimationNumber} berhasil di-import.");
                sb.AppendLine($"  Item ter-import : {report.ItemsImported} dari {report.ItemsParsed}");
                if (report.ItemsOrphaned > 0)
                {
                    sb.AppendLine($"  Item TANPA produk: {report.ItemsOrphaned} (di-skip)");
                    sb.AppendLine();
                    sb.AppendLine("Daftar item yang ProductId-nya tidak ketemu di katalog:");
                    foreach (var o in report.OrphanedItems.Take(20)) sb.AppendLine("  - " + o);
                    if (report.OrphanedItems.Count > 20)
                        sb.AppendLine($"  ... dan {report.OrphanedItems.Count - 20} lainnya.");
                }
            }
            else
            {
                sb.AppendLine("Import tidak dilakukan.");
                foreach (var w in report.Warnings) sb.AppendLine("  • " + w);
                foreach (var er in report.Errors)  sb.AppendLine("  ⚠ " + er);
            }

            var icon = report.Imported
                ? (report.ItemsOrphaned > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information)
                : MessageBoxIcon.Error;
            MessageBox.Show(sb.ToString(), report.Imported ? "Import Selesai" : "Import Gagal",
                MessageBoxButtons.OK, icon);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Gagal import CSV:\n{ex.Message}", "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>Strip Windows-invalid characters from a filename candidate.</summary>
    private static string SanitizeFilename(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = name.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        var clean = new string(chars).Trim();
        return string.IsNullOrEmpty(clean) ? "Estimasi.csv" : clean;
    }

    private async void BtnChangeStatus_Click(object? sender, EventArgs e)
    {
        if (dgv.CurrentRow == null) return;
        if (dgv.CurrentRow.Cells["ColId"].Value is not int id) return;
        var est = _allEstimations.FirstOrDefault(x => x.EstimationId == id);
        if (est == null) return;

        using var dlg = new StatusChangeDialog(est.Status);
        if (dlg.ShowDialog() != DialogResult.OK) return;

        est.Status = dlg.SelectedStatus;
        await _estimationRepo.UpdateAsync(est);
        await LoadDataAsync();
    }
}

// Simple status change dialog
public class StatusChangeDialog : Form
{
    public string SelectedStatus { get; private set; } = "Draft";
    private ComboBox cmb = null!;

    public StatusChangeDialog(string currentStatus)
    {
        Text = "Ubah Status";
        Size = new Size(300, 200);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        BackColor = AppTheme.Background;

        var lbl = AppTheme.MakeLabel("Pilih status baru:", AppTheme.FontBase, AppTheme.TextPrimary);
        lbl.Location = new Point(20, 20);

        cmb = new ComboBox { Location = new Point(20, 44), Width = 240, DropDownStyle = ComboBoxStyle.DropDownList };
        AppTheme.StyleComboBox(cmb);
        cmb.Items.AddRange(new[] { "Antri Dihitung", "Draft", "Tunggu Approved", "Approved" });
        cmb.SelectedItem = currentStatus;

        var btnOk = new Button { Text = "Simpan", Location = new Point(20, 85), Width = 110, Height = 32 };
        AppTheme.StyleButton(btnOk, AppTheme.Primary, Color.White);
        btnOk.Click += (s, e) =>
        {
            SelectedStatus = cmb.SelectedItem?.ToString() ?? "Draft";
            DialogResult = DialogResult.OK;
            Close();
        };

        var btnCancel = new Button { Text = "Batal", Location = new Point(150, 85), Width = 110, Height = 32 };
        AppTheme.StyleButton(btnCancel, Color.FromArgb(229, 231, 235), AppTheme.TextPrimary);
        btnCancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };

        Controls.AddRange(new Control[] { lbl, cmb, btnOk, btnCancel });
    }
}

/// <summary>
/// 3-button dialog shown when import meets a duplicate estimation number.
/// Choices: Overwrite (replace existing), Skip (cancel import), Rename
/// (append "-IMPORT2" suffix, increment until unique).
/// </summary>
public class ConflictResolutionDialog : Form
{
    public PanelCalculator.Data.DataSeeding.EstimationCsvImporter.ConflictResolution Result
        { get; private set; } =
            PanelCalculator.Data.DataSeeding.EstimationCsvImporter.ConflictResolution.Skip;

    public ConflictResolutionDialog(string existingNumber)
    {
        Text = "Konflik Nomor Estimasi";
        Size = new Size(440, 200);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        BackColor = AppTheme.Background;

        var lbl1 = AppTheme.MakeLabel($"Estimasi {existingNumber} sudah ada di database.", AppTheme.FontBase, AppTheme.TextPrimary);
        lbl1.Location = new Point(20, 18); lbl1.AutoSize = true;

        var lbl2 = AppTheme.MakeLabel("Pilih tindakan:", AppTheme.FontSmall, AppTheme.TextSecondary);
        lbl2.Location = new Point(20, 44); lbl2.AutoSize = true;

        var btnOverwrite = new Button { Text = "Overwrite", Location = new Point(20,  78), Width = 120, Height = 36 };
        AppTheme.StyleButton(btnOverwrite, AppTheme.Danger, Color.White);
        btnOverwrite.Click += (s, e) =>
        {
            Result = PanelCalculator.Data.DataSeeding.EstimationCsvImporter.ConflictResolution.Overwrite;
            DialogResult = DialogResult.OK; Close();
        };

        var btnRename = new Button { Text = "Rename", Location = new Point(150, 78), Width = 120, Height = 36 };
        AppTheme.StyleButton(btnRename, AppTheme.Primary, Color.White);
        btnRename.Click += (s, e) =>
        {
            Result = PanelCalculator.Data.DataSeeding.EstimationCsvImporter.ConflictResolution.Rename;
            DialogResult = DialogResult.OK; Close();
        };

        var btnSkip = new Button { Text = "Skip", Location = new Point(280, 78), Width = 120, Height = 36 };
        AppTheme.StyleButton(btnSkip, AppTheme.Bg2, AppTheme.Text2);
        btnSkip.Click += (s, e) =>
        {
            Result = PanelCalculator.Data.DataSeeding.EstimationCsvImporter.ConflictResolution.Skip;
            DialogResult = DialogResult.Cancel; Close();
        };

        Controls.AddRange(new Control[] { lbl1, lbl2, btnOverwrite, btnRename, btnSkip });
    }
}
