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
        Text = "Riwayat Estimasi";
        Size = new Size(900, 580);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = AppTheme.Background;
        Padding = new Padding(16);

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
        cmbStatus.Items.AddRange(new[] {
            "Semua",
            "Antri Hitung", "Selesai Dihitung", "Menunggu Approve", "Sudah Diapprove",
            "Won", "Lost",
            "Draft", "Approved", "Sent"   // kompatibilitas data lama
        });
        cmbStatus.SelectedIndex = 0;
        cmbStatus.SelectedIndexChanged += (s, e) => FilterGrid();

        pnlFilter.Controls.AddRange(new Control[] { lblSearch, txtSearch, lblStatus, cmbStatus });

        // Grid
        dgv = new DataGridView { Dock = DockStyle.Fill };
        AppTheme.StyleGrid(dgv);
        dgv.ReadOnly = true;
        dgv.Columns.Add(new DataGridViewTextBoxColumn { Name = "ColNo",      HeaderText = "No. Estimasi",  FillWeight = 18 });
        dgv.Columns.Add(new DataGridViewTextBoxColumn { Name = "ColClient",  HeaderText = "Klien",         FillWeight = 20 });
        dgv.Columns.Add(new DataGridViewTextBoxColumn { Name = "ColCompany", HeaderText = "Perusahaan",    FillWeight = 20 });
        dgv.Columns.Add(new DataGridViewTextBoxColumn { Name = "ColDate",    HeaderText = "Tanggal",       FillWeight = 14 });
        dgv.Columns.Add(new DataGridViewTextBoxColumn { Name = "ColStatus",  HeaderText = "Status",        FillWeight = 10 });
        dgv.Columns.Add(new DataGridViewTextBoxColumn { Name = "ColTotal",   HeaderText = "Total Harga",   FillWeight = 18, DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleRight } });
        dgv.Columns.Add(new DataGridViewTextBoxColumn { Name = "ColId",      Visible = false });
        dgv.CellDoubleClick += Dgv_CellDoubleClick;

        // Bottom buttons
        var pnlBottom = new Panel { Dock = DockStyle.Bottom, Height = 56, BackColor = AppTheme.SidebarBg, Padding = new Padding(12) };
        pnlBottom.Paint += (s, e) => { using var pen = new Pen(AppTheme.Border); e.Graphics.DrawLine(pen, 0, 0, pnlBottom.Width, 0); };

        var btnLoad = new Button { Text = "📂 Buka Estimasi", Location = new Point(12, 10), Width = 150, Height = 36 };
        AppTheme.StyleButton(btnLoad, AppTheme.Primary, Color.White);
        btnLoad.Click += BtnLoad_Click;

        var btnDelete = new Button { Text = "🗑 Hapus", Location = new Point(174, 10), Width = 100, Height = 36 };
        AppTheme.StyleButton(btnDelete, AppTheme.Danger, Color.White);
        btnDelete.Click += BtnDelete_Click;

        var btnChangeStatus = new Button { Text = "✏ Ubah Status", Location = new Point(286, 10), Width = 140, Height = 36 };
        AppTheme.StyleButton(btnChangeStatus, Color.FromArgb(107, 114, 128), Color.White);
        btnChangeStatus.Click += BtnChangeStatus_Click;

        var btnExport = new Button { Text = "📄 Export PDF", Location = new Point(438, 10), Width = 130, Height = 36 };
        AppTheme.StyleButton(btnExport, Color.FromArgb(37, 99, 235), Color.White);
        btnExport.Click += BtnExport_Click;

        var lblHint = AppTheme.MakeLabel("Klik 2x untuk membuka ke kalkulator.", AppTheme.FontSmall, AppTheme.TextMuted);
        lblHint.Location = new Point(585, 18);
        lblHint.AutoSize = true;

        pnlBottom.Controls.AddRange(new Control[] { btnLoad, btnDelete, btnChangeStatus, btnExport, lblHint });

        Controls.Add(dgv);
        Controls.Add(pnlFilter);
        Controls.Add(pnlBottom);

        Load += async (s, e) => await LoadDataAsync();
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
            var rowIdx = dgv.Rows.Add(
                est.EstimationNumber,
                est.ClientName,
                est.Company ?? "",
                est.CreatedDate.ToLocalTime().ToString("dd MMM yyyy"),
                est.Status,
                "Rp " + est.TotalPrice.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("id-ID")),
                est.EstimationId
            );

            // Color-code status
            dgv.Rows[rowIdx].Cells["ColStatus"].Style.ForeColor = est.Status switch
            {
                "Antri Hitung"     => Color.FromArgb(100, 116, 139),  // slate
                "Selesai Dihitung" => Color.FromArgb(37,  99,  235),  // blue
                "Menunggu Approve" => Color.FromArgb(217, 119,   6),  // amber
                "Sudah Diapprove"  => Color.FromArgb(  5, 150, 105),  // emerald
                "Won"              => AppTheme.Success,
                "Lost"             => AppTheme.Danger,
                "Approved"         => AppTheme.Primary,               // data lama
                "Sent"             => AppTheme.Warning,               // data lama
                _                  => AppTheme.TextSecondary
            };
        }
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

        using var sfd = new SaveFileDialog
        {
            Title = "Simpan Penawaran PDF",
            Filter = "PDF Files (*.pdf)|*.pdf",
            FileName = $"{est.EstimationNumber}.pdf",
            DefaultExt = "pdf"
        };
        if (sfd.ShowDialog() != DialogResult.OK) return;

        try
        {
            var settings = _context != null
                ? _context.Settings.ToDictionary(s => s.SettingKey, s => s.SettingValue ?? "")
                : new Dictionary<string, string>();

            var lineItems = est.Details.Select(d => new PdfQuotationExport.LineItem(
                d.Product?.ReferenceCode ?? "—",
                d.Product?.ProductName ?? "—",
                string.IsNullOrWhiteSpace(d.Section) ? "Material Utama" : d.Section,
                d.Quantity,
                string.IsNullOrWhiteSpace(d.Satuan) ? "pcs" : d.Satuan,
                d.UnitPrice,
                d.AdjPercent,
                d.LineTotalPrice)).ToList();

            // Use stored percent; fall back to deriving from amounts for old records
            var marginPct = est.MarginPercent != 0 ? est.MarginPercent
                : (est.SubTotal > 0 ? Math.Round(est.Margin / est.SubTotal * 100, 1) : 0);
            var taxBase   = est.SubTotal + est.Margin + est.ShippingCost;
            var taxPct    = taxBase > 0 ? Math.Round(est.Tax / taxBase * 100, 1) : 0;
            var pphPct    = est.PPhPercent;

            PdfQuotationExport.Generate(
                outputPath:       sfd.FileName,
                estimationNumber: est.EstimationNumber,
                clientName:       est.ClientName,
                contactPhone:     est.ContactPhone,
                company:          est.Company,
                address:          est.Address,
                createdDate:      est.CreatedDate,
                notes:            est.Notes ?? "",
                items:            lineItems,
                subtotal:         est.SubTotal,
                marginPercent:    marginPct,
                marginAmount:     est.Margin,
                shippingCost:     est.ShippingCost,
                taxPercent:       taxPct,
                taxAmount:        est.Tax,
                pphPercent:       pphPct,
                pphAmount:        est.PPh,
                total:            est.TotalPrice,
                settings:         settings);

            var open = MessageBox.Show("PDF berhasil dibuat. Buka sekarang?", "Export Berhasil",
                MessageBoxButtons.YesNo, MessageBoxIcon.Information);
            if (open == DialogResult.Yes)
                System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo(sfd.FileName) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Gagal membuat PDF:\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
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
        cmb.Items.AddRange(new[] {
            "Antri Hitung", "Selesai Dihitung", "Menunggu Approve", "Sudah Diapprove",
            "Won", "Lost"
        });
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
