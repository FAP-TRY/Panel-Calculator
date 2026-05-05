using Microsoft.EntityFrameworkCore;
using PanelCalculator.Core.Models;
using PanelCalculator.Core.Services;
using PanelCalculator.Data;
using PanelCalculator.Data.Repositories;
using PanelCalculator.WinForms.Services;
using PanelCalculator.WinForms.Theme;

namespace PanelCalculator.WinForms.Forms;

public partial class MainForm : Form
{
    private readonly PanelCalculatorContext _context;
    private readonly IProductRepository _productRepo;
    private readonly IEstimationRepository _estimationRepo;
    private readonly ICalculationService _calcService;

    // ── Current estimation state ─────────────────────────────────────────
    private List<EstimationLineItem> _currentItems = new();
    private decimal _marginPercent = 25m;   // overall: positive = markup, negative = diskon
    private decimal _shippingCost  = 50000m;
    private decimal _taxPercent    = 11m;   // PPN
    private decimal _pphPercent    = 0m;    // PPh
    private bool    _refreshing    = false; // re-entrancy guard

    private static readonly string[] Sections =
        { "Material Utama", "Material Pendukung", "Material Lainnya" };

    // ── UI Controls ──────────────────────────────────────────────────────
    private DataGridView dgvProducts = null!;
    private DataGridView dgvItems    = null!;
    private TextBox   txtSearch    = null!;
    private ComboBox  cmbCategory  = null!;

    private ComboBox cmbTargetSection = null!; // which section new items go to

    private Label lblSubTotal = null!, lblMarginAmt = null!, lblShipping = null!;
    private Label lblTax = null!, lblPPh = null!, lblTotal = null!;
    private Label lblOverallAdjTitle = null!;   // updated dynamically

    private NumericUpDown numMargin   = null!;
    private NumericUpDown numShipping = null!;
    private NumericUpDown numTax      = null!;
    private NumericUpDown numPPh      = null!;

    private Button btnSave = null!, btnNew = null!, btnHistory = null!;
    private Button btnExport = null!, btnReports = null!, btnSettings = null!;
    private Label  lblStatus = null!;

    public MainForm(
        PanelCalculatorContext context,
        IProductRepository productRepo,
        IEstimationRepository estimationRepo,
        ICalculationService calcService)
    {
        _context        = context;
        _productRepo    = productRepo;
        _estimationRepo = estimationRepo;
        _calcService    = calcService;

        InitializeComponent();
        BuildUI();
    }

    private void InitializeComponent()
    {
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode       = AutoScaleMode.Font;
        ClientSize          = new Size(1280, 780);
        MinimumSize         = new Size(1100, 680);
        Name            = "MainForm";
        Text            = "Panel Calculator v1.0";
        StartPosition   = FormStartPosition.CenterScreen;
        BackColor       = AppTheme.Background;
        Load           += MainForm_Load;
    }

    // ════════════════════════════════════════════════════════════════════
    //  BUILD UI
    // ════════════════════════════════════════════════════════════════════
    private void BuildUI()
    {
        // ── TOP TOOLBAR (TableLayoutPanel keeps title + buttons from clipping) ──
        var toolbar = new TableLayoutPanel
        {
            Dock        = DockStyle.Top,
            Height      = 60,
            ColumnCount = 2,
            RowCount    = 1,
            BackColor   = Color.FromArgb(30, 41, 59),
            Padding     = new Padding(0),
            Margin      = new Padding(0)
        };
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));       // title
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f)); // buttons

        var lblAppTitle = new Label
        {
            Text      = "⚡ Panel Calculator",
            Font      = AppTheme.FontTitle,
            ForeColor = Color.White,
            AutoSize  = false,
            Width     = 220,
            Dock      = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding   = new Padding(16, 0, 0, 0)
        };
        toolbar.Controls.Add(lblAppTitle, 0, 0);

        // Buttons right-aligned inside a fill panel
        var btnAreaPanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent };

        var btnFlow = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize      = true,
            WrapContents  = false,
            BackColor     = Color.Transparent,
            Padding       = new Padding(0),
            Margin        = new Padding(0)
        };

        btnNew      = MakeToolbarButton("＋ Estimasi Baru", Color.FromArgb(59, 130, 246), 140);
        btnHistory  = MakeToolbarButton("📋 Riwayat",       Color.FromArgb(55, 65, 81),  110);
        btnExport   = MakeToolbarButton("📄 Export PDF",    Color.FromArgb(55, 65, 81),  120);
        btnReports  = MakeToolbarButton("📊 Laporan",       Color.FromArgb(55, 65, 81),  110);
        btnSettings = MakeToolbarButton("⚙ Settings",       Color.FromArgb(55, 65, 81),  110);

        btnNew.Click      += BtnNew_Click;
        btnHistory.Click  += BtnHistory_Click;
        btnExport.Click   += BtnExport_Click;
        btnReports.Click  += BtnReports_Click;
        btnSettings.Click += BtnSettings_Click;

        btnFlow.Controls.AddRange(new Control[] { btnNew, btnHistory, btnExport, btnReports, btnSettings });
        btnAreaPanel.Controls.Add(btnFlow);

        // Right-align the flow panel whenever the container resizes
        btnAreaPanel.Layout += (s, e) =>
        {
            btnFlow.Top  = (btnAreaPanel.Height - btnFlow.Height) / 2;
            btnFlow.Left = Math.Max(0, btnAreaPanel.Width - btnFlow.Width - 12);
        };

        toolbar.Controls.Add(btnAreaPanel, 1, 0);

        // ── STATUS BAR ───────────────────────────────────────────────────
        var statusBar = new Panel
        {
            Dock      = DockStyle.Bottom,
            Height    = 28,
            BackColor = Color.FromArgb(248, 250, 252),
            Padding   = new Padding(12, 0, 12, 0)
        };
        statusBar.Paint += (s, e) =>
        {
            using var pen = new Pen(AppTheme.Border);
            e.Graphics.DrawLine(pen, 0, 0, statusBar.Width, 0);
        };
        lblStatus = new Label
        {
            Text      = "Siap. Pilih produk dari daftar kiri untuk mulai estimasi.",
            Font      = AppTheme.FontSmall,
            ForeColor = AppTheme.TextSecondary,
            Dock      = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        };
        statusBar.Controls.Add(lblStatus);

        // ── MAIN SPLIT (left=products | right=items+summary) ─────────────
        var mainSplit = new SplitContainer
        {
            Dock          = DockStyle.Fill,
            SplitterWidth = 8,
            BackColor     = AppTheme.Background,
            Orientation   = Orientation.Vertical
        };

        BuildProductPanel(mainSplit.Panel1);

        var rightSplit = new SplitContainer
        {
            Dock          = DockStyle.Fill,
            SplitterWidth = 8,
            BackColor     = AppTheme.Background,
            Orientation   = Orientation.Vertical
        };
        BuildItemsPanel(rightSplit.Panel1);
        BuildSummaryPanel(rightSplit.Panel2);
        mainSplit.Panel2.Controls.Add(rightSplit);

        Controls.Add(mainSplit);
        Controls.Add(toolbar);
        Controls.Add(statusBar);

        Load += (s, e) =>
        {
            mainSplit.SplitterDistance  = (int)(Width * 0.30);
            rightSplit.SplitterDistance = (int)(rightSplit.Width * 0.62);
        };
    }

    private Button MakeToolbarButton(string text, Color bg, int width)
    {
        var btn = new Button { Text = text, Width = width, Height = 36, Margin = new Padding(0, 0, 4, 0) };
        AppTheme.StyleButton(btn, bg, Color.White);
        return btn;
    }

    // ── LEFT PANEL: Product Search & List ────────────────────────────────
    private void BuildProductPanel(Panel parent)
    {
        parent.BackColor = AppTheme.SidebarBg;
        parent.Padding   = new Padding(12);

        var lblPanelTitle = AppTheme.MakeLabel("Daftar Produk", AppTheme.FontBold, AppTheme.TextPrimary);
        lblPanelTitle.Dock   = DockStyle.Top;
        lblPanelTitle.Height = 28;

        var lblCat = AppTheme.MakeLabel("Kategori:", AppTheme.FontSmall, AppTheme.TextSecondary);
        lblCat.Dock   = DockStyle.Top;
        lblCat.Height = 22;

        cmbCategory = new ComboBox { Dock = DockStyle.Top };
        AppTheme.StyleComboBox(cmbCategory);
        cmbCategory.DropDownStyle       = ComboBoxStyle.DropDownList;
        cmbCategory.SelectedIndexChanged += CmbCategory_SelectedIndexChanged;

        var spacer1 = new Panel { Dock = DockStyle.Top, Height = 6 };

        var lblSearch = AppTheme.MakeLabel("Cari produk / kode:", AppTheme.FontSmall, AppTheme.TextSecondary);
        lblSearch.Dock   = DockStyle.Top;
        lblSearch.Height = 22;

        txtSearch = new TextBox { Dock = DockStyle.Top, PlaceholderText = "Ketik nama atau kode referensi..." };
        AppTheme.StyleTextBox(txtSearch);
        txtSearch.TextChanged += TxtSearch_TextChanged;

        var spacer2 = new Panel { Dock = DockStyle.Top, Height = 8 };

        dgvProducts = new DataGridView { Dock = DockStyle.Fill };
        AppTheme.StyleGrid(dgvProducts);
        dgvProducts.ReadOnly = true;
        dgvProducts.Columns.Add(new DataGridViewTextBoxColumn { Name = "ColRef",       HeaderText = "Kode",        Width = 110, FillWeight = 30 });
        dgvProducts.Columns.Add(new DataGridViewTextBoxColumn { Name = "ColName",      HeaderText = "Nama Produk", FillWeight = 50 });
        dgvProducts.Columns.Add(new DataGridViewTextBoxColumn { Name = "ColPrice",     HeaderText = "Harga (Rp)",  FillWeight = 30, DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleRight } });
        dgvProducts.Columns.Add(new DataGridViewTextBoxColumn { Name = "ColProductId", Visible    = false });

        dgvProducts.CellDoubleClick += DgvProducts_CellDoubleClick;
        dgvProducts.CellFormatting  += DgvProducts_CellFormatting;

        // Remove sort arrows from column headers
        foreach (DataGridViewColumn col in dgvProducts.Columns)
            col.SortMode = DataGridViewColumnSortMode.NotSortable;

        var lblHint = AppTheme.MakeLabel("» Klik 2x pada produk untuk tambahkan", AppTheme.FontSmall, AppTheme.TextMuted);
        lblHint.Dock      = DockStyle.Bottom;
        lblHint.Height    = 22;
        lblHint.TextAlign = ContentAlignment.MiddleLeft;

        parent.Controls.Add(dgvProducts);
        parent.Controls.Add(lblHint);
        parent.Controls.Add(spacer2);
        parent.Controls.Add(txtSearch);
        parent.Controls.Add(lblSearch);
        parent.Controls.Add(spacer1);
        parent.Controls.Add(cmbCategory);
        parent.Controls.Add(lblCat);
        parent.Controls.Add(lblPanelTitle);
    }

    // ── CENTER PANEL: Estimation Items Grid ──────────────────────────────
    private void BuildItemsPanel(Panel parent)
    {
        parent.BackColor = AppTheme.Background;
        parent.Padding   = new Padding(8, 12, 8, 8);

        // ── Title row with "Tambah ke:" section selector ──────────────────
        var pnlHeader = new Panel { Dock = DockStyle.Top, Height = 34, BackColor = Color.Transparent };

        var lblTitle = AppTheme.MakeLabel("Item Estimasi", AppTheme.FontBold, AppTheme.TextPrimary);
        lblTitle.Location  = new Point(0, 7);
        lblTitle.AutoSize  = true;

        var lblTambahKe = AppTheme.MakeLabel("Tambah ke:", AppTheme.FontSmall, AppTheme.TextSecondary);
        lblTambahKe.Location = new Point(130, 9);
        lblTambahKe.AutoSize = true;

        cmbTargetSection = new ComboBox
        {
            Location      = new Point(205, 4),
            Width         = 190,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font          = AppTheme.FontBase
        };
        AppTheme.StyleComboBox(cmbTargetSection);
        cmbTargetSection.Items.AddRange(Sections);
        cmbTargetSection.SelectedIndex = 0; // default: Material Utama
        // Update background color to match selected section
        cmbTargetSection.SelectedIndexChanged += (s, e) =>
        {
            cmbTargetSection.BackColor = cmbTargetSection.SelectedIndex switch
            {
                1 => Color.FromArgb(254, 249, 195), // Material Pendukung – kuning
                2 => Color.FromArgb(220, 252, 231), // Material Lainnya   – hijau
                _ => Color.White                    // Material Utama
            };
        };

        pnlHeader.Controls.AddRange(new Control[] { lblTitle, lblTambahKe, cmbTargetSection });

        dgvItems = new DataGridView { Dock = DockStyle.Fill };
        AppTheme.StyleGrid(dgvItems);

        // ── Columns ───────────────────────────────────────────────────────
        dgvItems.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "ColItemRef", HeaderText = "Kode", ReadOnly = true, FillWeight = 12
        });
        dgvItems.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "ColItemName", HeaderText = "Nama Produk", ReadOnly = true, FillWeight = 28
        });

        // Section ComboBox column
        var colSection = new DataGridViewComboBoxColumn
        {
            Name         = "ColItemSection",
            HeaderText   = "Bagian",
            FillWeight   = 14,
            DisplayStyle = DataGridViewComboBoxDisplayStyle.Nothing,
            FlatStyle    = FlatStyle.Flat
        };
        colSection.Items.AddRange("", "Material Utama", "Material Pendukung", "Material Lainnya");
        dgvItems.Columns.Add(colSection);

        dgvItems.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "ColItemQty", HeaderText = "Qty", FillWeight = 7,
            DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleCenter }
        });
        dgvItems.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "ColItemUnitPrice", HeaderText = "Harga Satuan", ReadOnly = true, FillWeight = 13,
            DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleRight }
        });

        // Adj (%) – positive = markup, negative = diskon
        dgvItems.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "ColItemAdj", HeaderText = "Adj (%)", FillWeight = 8,
            DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleRight }
        });

        dgvItems.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "ColItemTotal", HeaderText = "Total", ReadOnly = true, FillWeight = 13,
            DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleRight, Font = AppTheme.FontBold }
        });

        var colDel = new DataGridViewButtonColumn
        {
            Name = "ColDel", HeaderText = "", Text = "✕", UseColumnTextForButtonValue = true, FillWeight = 5,
            DefaultCellStyle = { ForeColor = AppTheme.Danger, Font = AppTheme.FontBold, Alignment = DataGridViewContentAlignment.MiddleCenter }
        };
        dgvItems.Columns.Add(colDel);

        // ── Events ───────────────────────────────────────────────────────
        dgvItems.CellValueChanged   += DgvItems_CellValueChanged;
        dgvItems.CellClick          += DgvItems_CellClick;
        dgvItems.CellFormatting     += DgvItems_CellFormatting;
        dgvItems.DataError          += (s, e) => e.Cancel = true; // suppress ComboBox DataError for header rows

        // Commit ComboBox selection immediately so CellValueChanged fires
        dgvItems.CurrentCellDirtyStateChanged += (s, e) =>
        {
            if (dgvItems.IsCurrentCellDirty && dgvItems.CurrentCell?.OwningColumn is DataGridViewComboBoxColumn)
                dgvItems.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };

        // Remove sort arrows from column headers
        foreach (DataGridViewColumn col in dgvItems.Columns)
            col.SortMode = DataGridViewColumnSortMode.NotSortable;

        parent.Controls.Add(dgvItems);
        parent.Controls.Add(pnlHeader);
    }

    // ── RIGHT PANEL: Cost Summary ─────────────────────────────────────────
    private void BuildSummaryPanel(Panel parent)
    {
        parent.BackColor = AppTheme.SidebarBg;
        parent.Padding   = new Padding(12);

        var lblTitle = AppTheme.MakeLabel("Ringkasan Biaya", AppTheme.FontBold, AppTheme.TextPrimary);
        lblTitle.Dock   = DockStyle.Top;
        lblTitle.Height = 32;

        var pnlSummary = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        int y = 0;

        // SubTotal
        AddSummaryRow(pnlSummary, ref y, "Subtotal:", ref lblSubTotal);
        AddSeparator(pnlSummary, ref y);

        // Overall Margin / Diskon
        lblOverallAdjTitle = AppTheme.MakeLabel("Margin (%):", AppTheme.FontSmall, AppTheme.TextSecondary);
        lblOverallAdjTitle.Location = new Point(0, y);
        lblOverallAdjTitle.AutoSize = true;
        pnlSummary.Controls.Add(lblOverallAdjTitle);
        y += 20;

        numMargin = new NumericUpDown
        {
            Location = new Point(0, y), Width = 200,
            Minimum = -100, Maximum = 500, DecimalPlaces = 1, Value = _marginPercent,
            Font = AppTheme.FontBase, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        numMargin.ValueChanged += NumMargin_ValueChanged;
        pnlSummary.Controls.Add(numMargin);
        y += 30;

        AddSummaryRow(pnlSummary, ref y, "Nilai Margin/Diskon:", ref lblMarginAmt);
        AddSeparator(pnlSummary, ref y);

        // Ongkos Kirim
        var lblShipTitle = AppTheme.MakeLabel("Ongkos Kirim (Rp):", AppTheme.FontSmall, AppTheme.TextSecondary);
        lblShipTitle.Location = new Point(0, y);
        lblShipTitle.AutoSize = true;
        pnlSummary.Controls.Add(lblShipTitle);
        y += 20;

        numShipping = new NumericUpDown
        {
            Location = new Point(0, y), Width = 200,
            Minimum = 0, Maximum = 100_000_000, Increment = 10000, Value = _shippingCost,
            Font = AppTheme.FontBase, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        numShipping.ValueChanged += NumShipping_ValueChanged;
        pnlSummary.Controls.Add(numShipping);
        y += 30;

        AddSummaryRow(pnlSummary, ref y, "Ongkos Kirim:", ref lblShipping);
        AddSeparator(pnlSummary, ref y);

        // PPN
        var lblTaxTitle = AppTheme.MakeLabel("PPN (%):", AppTheme.FontSmall, AppTheme.TextSecondary);
        lblTaxTitle.Location = new Point(0, y);
        lblTaxTitle.AutoSize = true;
        pnlSummary.Controls.Add(lblTaxTitle);
        y += 20;

        numTax = new NumericUpDown
        {
            Location = new Point(0, y), Width = 200,
            Minimum = 0, Maximum = 50, DecimalPlaces = 1, Value = _taxPercent,
            Font = AppTheme.FontBase, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        numTax.ValueChanged += NumTax_ValueChanged;
        pnlSummary.Controls.Add(numTax);
        y += 30;

        AddSummaryRow(pnlSummary, ref y, "PPN:", ref lblTax);
        AddSeparator(pnlSummary, ref y);

        // PPh
        var lblPPhTitle = AppTheme.MakeLabel("PPh (%):", AppTheme.FontSmall, AppTheme.TextSecondary);
        lblPPhTitle.Location = new Point(0, y);
        lblPPhTitle.AutoSize = true;
        pnlSummary.Controls.Add(lblPPhTitle);
        y += 20;

        numPPh = new NumericUpDown
        {
            Location = new Point(0, y), Width = 200,
            Minimum = 0, Maximum = 20, DecimalPlaces = 1, Value = _pphPercent,
            Font = AppTheme.FontBase, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        numPPh.ValueChanged += NumPPh_ValueChanged;
        pnlSummary.Controls.Add(numPPh);
        y += 30;

        AddSummaryRow(pnlSummary, ref y, "PPh (ditahan):", ref lblPPh);
        AddSeparator(pnlSummary, ref y);
        y += 4;

        // Total highlight box
        var pnlTotal = new Panel
        {
            Location  = new Point(0, y),
            Width     = 220,
            Height    = 60,
            BackColor = AppTheme.TotalBg,
            Anchor    = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        var lblTotalLabel = new Label
        {
            Text = "TOTAL HARGA", Font = AppTheme.FontSmall, ForeColor = AppTheme.Primary,
            Location = new Point(10, 8), AutoSize = true
        };
        lblTotal = new Label
        {
            Text = "Rp 0", Font = AppTheme.FontTotal, ForeColor = AppTheme.TotalText,
            Location = new Point(10, 26), AutoSize = true
        };
        pnlTotal.Controls.AddRange(new Control[] { lblTotalLabel, lblTotal });
        pnlSummary.Controls.Add(pnlTotal);
        y += 70;

        // Save button
        y += 10;
        btnSave = new Button
        {
            Text = "💾  Simpan Estimasi", Location = new Point(0, y), Width = 200, Height = 42,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        AppTheme.StyleButton(btnSave, AppTheme.Success, Color.White);
        btnSave.Font   = new Font("Segoe UI", 10f, FontStyle.Bold);
        btnSave.Click += BtnSave_Click;
        pnlSummary.Controls.Add(btnSave);

        parent.Controls.Add(pnlSummary);
        parent.Controls.Add(lblTitle);
    }

    private void AddSummaryRow(Panel parent, ref int y, string label, ref Label valueLabel)
    {
        var lbl = AppTheme.MakeLabel(label, AppTheme.FontSmall, AppTheme.TextSecondary);
        lbl.Location = new Point(0, y);
        parent.Controls.Add(lbl);

        valueLabel = new Label
        {
            Text      = "Rp 0",
            Font      = AppTheme.FontBold,
            ForeColor = AppTheme.TextPrimary,
            AutoSize  = true,
            TextAlign = ContentAlignment.MiddleRight
        };
        valueLabel.Location = new Point(4, y + 16);
        parent.Controls.Add(valueLabel);
        y += 42;
    }

    private void AddSeparator(Panel parent, ref int y)
    {
        var sep = new Panel { Location = new Point(0, y), Width = parent.Width - 4, Height = 1, BackColor = AppTheme.Border };
        parent.Controls.Add(sep);
        y += 10;
    }

    // ════════════════════════════════════════════════════════════════════
    //  EVENT HANDLERS
    // ════════════════════════════════════════════════════════════════════
    private async void MainForm_Load(object? sender, EventArgs e)
    {
        await LoadCategoriesAsync();
        await LoadProductsAsync();
        LoadSettingsFromDb();
        RecalcSummary();
    }

    private async Task LoadCategoriesAsync()
    {
        var categories = (await _productRepo.GetAllCategoriesAsync()).ToList();
        cmbCategory.Items.Clear();
        cmbCategory.Items.Add("— Semua Kategori —");
        foreach (var cat in categories) cmbCategory.Items.Add(cat);
        cmbCategory.SelectedIndex = 0;
    }

    private async Task LoadProductsAsync(string? search = null, string? category = null)
    {
        IEnumerable<Product> products;

        if (!string.IsNullOrWhiteSpace(search))
            products = await _productRepo.SearchAsync(search);
        else if (!string.IsNullOrWhiteSpace(category) && category != "— Semua Kategori —")
            products = await _productRepo.GetByCategoryAsync(category);
        else
            products = await _productRepo.GetAllAsync();

        if (!string.IsNullOrWhiteSpace(search) && !string.IsNullOrWhiteSpace(category) && category != "— Semua Kategori —")
            products = products.Where(p => p.Category == category);

        dgvProducts.Rows.Clear();
        foreach (var p in products)
        {
            // Show product name with "(Indent)" suffix for stock-indent items instead of grey coloring
            var displayName = p.StockStatus == 2 ? $"{p.ProductName}  [Indent]" : p.ProductName;
            dgvProducts.Rows.Add(p.ReferenceCode, displayName, p.Price, p.ProductId);
        }
        SetStatus($"{dgvProducts.Rows.Count} produk ditemukan.");
    }

    private void LoadSettingsFromDb()
    {
        Dictionary<string, string?> settings;
        try { settings = _context.Settings.ToDictionary(s => s.SettingKey, s => s.SettingValue); }
        catch { return; }

        void TryLoad(string key, NumericUpDown num, ref decimal field)
        {
            try
            {
                if (settings.TryGetValue(key, out var raw) && decimal.TryParse(raw, out var v))
                {
                    v = Math.Clamp(v, num.Minimum, num.Maximum);
                    num.Value = v;
                    field = v;
                }
            }
            catch { }
        }

        TryLoad("DefaultMarginPercent", numMargin,   ref _marginPercent);
        TryLoad("DefaultTaxPercent",    numTax,      ref _taxPercent);
        TryLoad("DefaultShippingCost",  numShipping, ref _shippingCost);
    }

    private async void CmbCategory_SelectedIndexChanged(object? sender, EventArgs e) =>
        await LoadProductsAsync(txtSearch.Text, cmbCategory.SelectedItem?.ToString());

    private async void TxtSearch_TextChanged(object? sender, EventArgs e) =>
        await LoadProductsAsync(txtSearch.Text, cmbCategory.SelectedItem?.ToString());

    private void DgvProducts_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex >= 0 && e.ColumnIndex == dgvProducts.Columns["ColPrice"].Index && e.Value is decimal price)
        {
            e.Value = FormatRupiah(price);
            e.FormattingApplied = true;
        }
    }

    private void DgvProducts_CellDoubleClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0) return;
        var row = dgvProducts.Rows[e.RowIndex];

        var refCode = row.Cells["ColRef"].Value?.ToString() ?? "";
        var name    = row.Cells["ColName"].Value?.ToString() ?? "";
        var price   = row.Cells["ColPrice"].Value is decimal p ? p : 0m;
        var prodId  = row.Cells["ColProductId"].Value is int id ? id : 0;

        var targetSection = cmbTargetSection.SelectedItem?.ToString() ?? "Material Utama";

        // If same product already in the SAME section → add qty, else add new row
        var existing = _currentItems.FirstOrDefault(i => i.ProductId == prodId && i.Section == targetSection);
        if (existing != null)
        {
            existing.Quantity++;
        }
        else
        {
            _currentItems.Add(new EstimationLineItem
            {
                ProductId     = prodId,
                ReferenceCode = refCode,
                ProductName   = name,
                UnitPrice     = price,
                Quantity      = 1,
                AdjPercent    = 0m,
                Section       = targetSection
            });
        }

        RefreshItemsGrid();
        RecalcSummary();
        SetStatus($"Ditambahkan: {name}");
    }

    private void DgvItems_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0) return;
        // Format Adj column: display "+10.0%" / "-5.0%" / "—"
        if (e.ColumnIndex == dgvItems.Columns["ColItemAdj"].Index && e.Value is decimal adj)
        {
            e.Value             = adj == 0 ? "—" : (adj > 0 ? $"+{adj:N1}%" : $"{adj:N1}%");
            e.FormattingApplied = true;
        }
    }

    private void DgvItems_CellValueChanged(object? sender, DataGridViewCellEventArgs e)
    {
        if (_refreshing) return;
        if (e.RowIndex < 0) return;

        var row = dgvItems.Rows[e.RowIndex];
        if (row.Tag is not int itemIdx || itemIdx < 0) return;
        if (itemIdx >= _currentItems.Count) return;

        var item = _currentItems[itemIdx];

        if (e.ColumnIndex == dgvItems.Columns["ColItemQty"].Index)
        {
            if (int.TryParse(row.Cells["ColItemQty"].Value?.ToString(), out var qty) && qty > 0)
                item.Quantity = qty;
        }
        else if (e.ColumnIndex == dgvItems.Columns["ColItemAdj"].Index)
        {
            if (decimal.TryParse(row.Cells["ColItemAdj"].Value?.ToString(), out var adj))
                item.AdjPercent = Math.Clamp(adj, -99m, 500m);
        }
        else if (e.ColumnIndex == dgvItems.Columns["ColItemSection"].Index)
        {
            var newSection = row.Cells["ColItemSection"].Value?.ToString();
            if (!string.IsNullOrWhiteSpace(newSection) && Sections.Contains(newSection))
                item.Section = newSection;
        }
        else return; // no recalc needed for other columns

        RefreshItemsGrid();
        RecalcSummary();
    }

    private void DgvItems_CellClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex != dgvItems.Columns["ColDel"].Index) return;
        var row = dgvItems.Rows[e.RowIndex];
        if (row.Tag is not int itemIdx || itemIdx < 0) return;
        _currentItems.RemoveAt(itemIdx);
        RefreshItemsGrid();
        RecalcSummary();
    }

    private void NumMargin_ValueChanged(object? sender, EventArgs e)
    {
        _marginPercent = numMargin.Value;
        lblOverallAdjTitle.Text = _marginPercent >= 0 ? "Margin (%):" : "Diskon (%):";
        RecalcSummary();
    }

    private void NumShipping_ValueChanged(object? sender, EventArgs e)
    {
        _shippingCost = numShipping.Value;
        RecalcSummary();
    }

    private void NumTax_ValueChanged(object? sender, EventArgs e)
    {
        _taxPercent = numTax.Value;
        RecalcSummary();
    }

    private void NumPPh_ValueChanged(object? sender, EventArgs e)
    {
        _pphPercent = numPPh.Value;
        RecalcSummary();
    }

    private void BtnNew_Click(object? sender, EventArgs e)
    {
        if (_currentItems.Count > 0)
        {
            var result = MessageBox.Show("Estimasi saat ini akan dihapus. Lanjutkan?",
                "Estimasi Baru", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (result != DialogResult.Yes) return;
        }
        _currentItems.Clear();
        RefreshItemsGrid();
        RecalcSummary();
        SetStatus("Estimasi baru dimulai.");
    }

    private async void BtnHistory_Click(object? sender, EventArgs e)
    {
        using var form = new EstimationHistoryForm(_estimationRepo, _calcService, _context);
        if (form.ShowDialog() == DialogResult.OK && form.LoadedEstimation != null)
            LoadEstimationIntoCalculator(form.LoadedEstimation);
        await Task.CompletedTask;
    }

    private async void BtnSave_Click(object? sender, EventArgs e)
    {
        if (_currentItems.Count == 0)
        {
            MessageBox.Show("Tambahkan minimal satu produk sebelum menyimpan.", "Perhatian",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        using var dlg = new SaveEstimationDialog();
        if (dlg.ShowDialog() != DialogResult.OK) return;

        var clientName = dlg.ClientName;
        var notes      = dlg.Notes;

        var today    = DateTime.Now;
        var existing = await _estimationRepo.GetByDateRangeAsync(today.Date, today.Date.AddDays(1));
        var seq      = (existing.Count() + 1).ToString("D3");
        var estNumber = $"EST-{today:yyyyMMdd}-{seq}";

        var (subtotal, overallAdjAmt, ppnAmt, pphAmt, total) = CalcAll();

        var estimation = new Estimation
        {
            EstimationNumber = estNumber,
            ClientName       = clientName,
            Notes            = notes,
            SubTotal         = subtotal,
            MarginPercent    = _marginPercent,
            Margin           = overallAdjAmt,
            ShippingCost     = _shippingCost,
            Tax              = ppnAmt,
            PPhPercent       = _pphPercent,
            PPh              = pphAmt,
            TotalPrice       = total,
            Status           = "Draft",
            CreatedDate      = DateTime.UtcNow
        };

        estimation.Details = _currentItems.Select(item => new EstimationDetail
        {
            ProductId      = item.ProductId,
            Quantity       = item.Quantity,
            UnitPrice      = item.UnitPrice,
            AdjPercent     = item.AdjPercent,
            Section        = item.Section,
            LineTotalPrice = item.LineTotal
        }).ToList();

        await _estimationRepo.AddAsync(estimation);
        MessageBox.Show($"Estimasi {estNumber} berhasil disimpan!", "Tersimpan", MessageBoxButtons.OK, MessageBoxIcon.Information);
        SetStatus($"✓ Disimpan: {estNumber} untuk {clientName}");
    }

    private void BtnExport_Click(object? sender, EventArgs e)
    {
        if (_currentItems.Count == 0)
        {
            MessageBox.Show("Tidak ada item untuk di-export.", "Perhatian", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        using var sfd = new SaveFileDialog
        {
            Title      = "Simpan Penawaran PDF",
            Filter     = "PDF Files (*.pdf)|*.pdf",
            FileName   = $"Penawaran_{DateTime.Now:yyyyMMdd_HHmm}.pdf",
            DefaultExt = "pdf"
        };
        if (sfd.ShowDialog() != DialogResult.OK) return;

        try
        {
            var settings = _context.Settings.ToDictionary(s => s.SettingKey, s => s.SettingValue ?? "");
            var (subtotal, overallAdjAmt, ppnAmt, pphAmt, total) = CalcAll();

            var lineItems = _currentItems
                .Select(i => new PdfQuotationExport.LineItem(
                    i.ReferenceCode, i.ProductName, i.Section, i.Quantity,
                    i.UnitPrice, i.AdjPercent, i.LineTotal))
                .ToList();

            PdfQuotationExport.Generate(
                outputPath:       sfd.FileName,
                estimationNumber: $"DRAFT-{DateTime.Now:yyyyMMdd-HHmm}",
                clientName:       "—",
                createdDate:      DateTime.Now,
                notes:            "",
                items:            lineItems,
                subtotal:         subtotal,
                marginPercent:    _marginPercent,
                marginAmount:     overallAdjAmt,
                shippingCost:     _shippingCost,
                taxPercent:       _taxPercent,
                taxAmount:        ppnAmt,
                pphPercent:       _pphPercent,
                pphAmount:        pphAmt,
                total:            total,
                settings:         settings);

            SetStatus($"✓ PDF disimpan: {sfd.FileName}");

            var open = MessageBox.Show("PDF berhasil dibuat. Buka sekarang?", "Export Berhasil",
                MessageBoxButtons.YesNo, MessageBoxIcon.Information);
            if (open == DialogResult.Yes)
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(sfd.FileName) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Gagal membuat PDF:\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void BtnReports_Click(object? sender, EventArgs e)
    {
        using var form = new ReportsForm(_context);
        form.ShowDialog();
    }

    private void BtnSettings_Click(object? sender, EventArgs e)
    {
        using var form = new SettingsForm(_context);
        form.ShowDialog();
        LoadSettingsFromDb();
    }

    // ════════════════════════════════════════════════════════════════════
    //  HELPERS
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Render items grouped by section with colored section header rows.
    /// Each item row stores its _currentItems index in row.Tag so event handlers
    /// can locate the correct item without brittle row-index arithmetic.
    /// </summary>
    private void RefreshItemsGrid()
    {
        _refreshing = true;
        dgvItems.Rows.Clear();

        foreach (var section in Sections)
        {
            // Items belonging to this section (with their original list index)
            var sectionItems = _currentItems
                .Select((item, idx) => (item, idx))
                .Where(x => x.item.Section == section)
                .ToList();

            var hdrBg  = SectionHeaderColor(section);
            var rowBg  = SectionRowColor(section);
            var secTotal = sectionItems.Sum(x => x.item.LineTotal);

            // ── Section header row (ALWAYS shown, even if empty) ──────────
            int hIdx = dgvItems.Rows.Add();
            var hRow = dgvItems.Rows[hIdx];

            hRow.Cells["ColItemRef"].Value  = $"▶  {section.ToUpper()}";
            hRow.Cells["ColItemTotal"].Value = sectionItems.Count > 0 ? FormatRupiah(secTotal) : "";

            hRow.DefaultCellStyle.BackColor = hdrBg;
            hRow.DefaultCellStyle.ForeColor = Color.FromArgb(30, 58, 138);
            hRow.DefaultCellStyle.Font      = AppTheme.FontBold;
            hRow.ReadOnly = true;
            hRow.Tag      = -1; // sentinel: not an item row

            // ── Placeholder row when section is empty ─────────────────────
            if (sectionItems.Count == 0)
            {
                int pIdx = dgvItems.Rows.Add("", "— Belum ada item. Klik 2x produk untuk menambahkan —",
                    "", "", "", "", "");
                var pRow = dgvItems.Rows[pIdx];
                pRow.ReadOnly = true;
                pRow.DefaultCellStyle.BackColor = rowBg;
                pRow.DefaultCellStyle.ForeColor = AppTheme.TextMuted;
                pRow.DefaultCellStyle.Font      = new Font("Segoe UI", 8f, FontStyle.Italic);
                pRow.Tag = -1;
                continue;
            }

            // ── Item rows ─────────────────────────────────────────────────
            foreach (var (item, itemIdx) in sectionItems)
            {
                int rIdx = dgvItems.Rows.Add(
                    item.ReferenceCode,
                    item.ProductName,
                    item.Section,       // ComboBox column value
                    item.Quantity,
                    FormatRupiah(item.UnitPrice),
                    item.AdjPercent,   // raw decimal → formatted via CellFormatting
                    FormatRupiah(item.LineTotal)
                );
                var iRow = dgvItems.Rows[rIdx];
                iRow.Tag = itemIdx; // store _currentItems index for event handlers
                iRow.DefaultCellStyle.BackColor = rowBg;
            }
        }

        _refreshing = false;
    }

    /// <summary>
    /// Core calculation:
    ///   subtotal         = Σ item.LineTotal (per-item adj already included)
    ///   overallAdjAmt    = subtotal × marginPercent / 100  (can be negative = diskon)
    ///   preTax           = subtotal + overallAdjAmt + shipping
    ///   ppnAmt           = preTax × ppnPct / 100
    ///   pphAmt           = preTax × pphPct / 100  (withheld, deducted from total)
    ///   total            = preTax + ppnAmt - pphAmt
    /// </summary>
    private (decimal subtotal, decimal overallAdjAmt, decimal ppnAmt, decimal pphAmt, decimal total) CalcAll()
    {
        var subtotal      = _currentItems.Sum(i => i.LineTotal);
        var overallAdjAmt = subtotal * _marginPercent / 100m;
        var preTax        = subtotal + overallAdjAmt + _shippingCost;
        var ppnAmt        = preTax * _taxPercent / 100m;
        var pphAmt        = preTax * _pphPercent / 100m;
        var total         = preTax + ppnAmt - pphAmt;
        return (subtotal, overallAdjAmt, ppnAmt, pphAmt, total);
    }

    private void RecalcSummary()
    {
        var (subtotal, overallAdjAmt, ppnAmt, pphAmt, total) = CalcAll();

        lblSubTotal.Text  = FormatRupiah(subtotal);
        lblMarginAmt.Text = FormatRupiah(overallAdjAmt);
        lblShipping.Text  = FormatRupiah(_shippingCost);
        lblTax.Text       = FormatRupiah(ppnAmt);
        lblPPh.Text       = FormatRupiah(pphAmt);
        lblTotal.Text     = FormatRupiah(total);

        lblOverallAdjTitle.Text = _marginPercent >= 0 ? "Margin (%):" : "Diskon (%):";
    }

    private void LoadEstimationIntoCalculator(Estimation est)
    {
        _currentItems.Clear();
        foreach (var d in est.Details)
        {
            _currentItems.Add(new EstimationLineItem
            {
                ProductId     = d.ProductId,
                ReferenceCode = d.Product?.ReferenceCode ?? "",
                ProductName   = d.Product?.ProductName ?? "",
                UnitPrice     = d.UnitPrice,
                Quantity      = d.Quantity,
                AdjPercent    = d.AdjPercent,
                Section       = string.IsNullOrWhiteSpace(d.Section) ? "Material Utama" : d.Section
            });
        }

        // Restore overall percentages stored in the estimation
        try
        {
            numMargin.Value   = Math.Clamp(est.MarginPercent, numMargin.Minimum, numMargin.Maximum);
            _marginPercent    = numMargin.Value;
            numTax.Value      = Math.Clamp(est.Tax > 0 && est.SubTotal > 0
                ? Math.Round(est.Tax / (est.SubTotal + est.Margin + est.ShippingCost) * 100, 1)
                : 11m, numTax.Minimum, numTax.Maximum);
            _taxPercent       = numTax.Value;
            numShipping.Value = Math.Clamp(est.ShippingCost, numShipping.Minimum, numShipping.Maximum);
            _shippingCost     = numShipping.Value;
            numPPh.Value      = Math.Clamp(est.PPhPercent, numPPh.Minimum, numPPh.Maximum);
            _pphPercent       = numPPh.Value;
        }
        catch { }

        RefreshItemsGrid();
        RecalcSummary();
        SetStatus($"Loaded: {est.EstimationNumber} — {est.ClientName}");
    }

    // ── Section color helpers ─────────────────────────────────────────────
    private static Color SectionHeaderColor(string section) => section switch
    {
        "Material Utama"      => Color.FromArgb(219, 234, 254), // blue-100
        "Material Pendukung"  => Color.FromArgb(254, 249, 195), // yellow-100
        "Material Lainnya"    => Color.FromArgb(220, 252, 231), // green-100
        _                     => Color.FromArgb(241, 245, 249)
    };

    private static Color SectionRowColor(string section) => section switch
    {
        "Material Utama"      => Color.White,
        "Material Pendukung"  => Color.FromArgb(255, 253, 240), // very light yellow
        "Material Lainnya"    => Color.FromArgb(243, 255, 245), // very light green
        _                     => Color.White
    };

    private void SetStatus(string msg) => lblStatus.Text = msg;

    private static string FormatRupiah(decimal value) =>
        "Rp " + value.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("id-ID"));
}

// ════════════════════════════════════════════════════════════════════════
//  In-memory line item (not persisted directly, only via EstimationDetail)
// ════════════════════════════════════════════════════════════════════════
public class EstimationLineItem
{
    public int     ProductId     { get; set; }
    public string  ReferenceCode { get; set; } = "";
    public string  ProductName   { get; set; } = "";
    public decimal UnitPrice     { get; set; }
    public int     Quantity      { get; set; } = 1;

    /// <summary>Per-item adjustment: positive = markup %, negative = diskon %</summary>
    public decimal AdjPercent { get; set; } = 0m;

    /// <summary>Material Utama | Material Pendukung | Material Lainnya</summary>
    public string Section { get; set; } = "Material Utama";

    public decimal EffectiveUnitPrice => UnitPrice * (1 + AdjPercent / 100m);
    public decimal LineTotal          => EffectiveUnitPrice * Quantity;
}
