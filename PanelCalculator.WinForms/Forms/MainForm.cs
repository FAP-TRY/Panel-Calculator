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
    private decimal _margin1Percent = 25m;  // overall tier 1: kenaikan harga / markup
    private decimal _margin2Percent = 0m;   // overall tier 2: diskon atau adjustment
    private decimal _margin3Percent = 0m;   // overall tier 3: margin final
    private decimal _shippingCost  = 0m;
    private string  _shippingNote  = "";
    private decimal _taxPercent    = 11m;   // PPN
    private decimal _pphPercent    = 0m;    // PPh
    private bool    _refreshing    = false; // re-entrancy guard

    /// <summary>Sections explicitly added by the user via "Tambah Grup".</summary>
    private readonly List<string> _activeSections = new();

    // ── Drag-from-product-list state ──────────────────────────────────────
    private Point _dragStartPoint;
    private bool  _isDraggingProduct;

    private static readonly string[] Sections =
    {
        "Material Utama", "Material Pendukung", "Material Lainnya",
        "Box", "Incoming", "Outgoing", "Trailer", "Karoseri", "Jasa"
    };

    // ── UI Controls ──────────────────────────────────────────────────────
    private DataGridView dgvProducts = null!;
    private DataGridView dgvItems    = null!;
    private TextBox   txtSearch    = null!;
    private ComboBox  cmbCategory  = null!;
    private ComboBox  cmbVendor    = null!;

    private ComboBox cmbTargetSection = null!; // which section new items go to

    private Label lblSubTotal = null!, lblShipping = null!;
    private Label lblTax = null!, lblPPh = null!, lblTotal = null!;
    private Label lblMarginAmt1 = null!, lblMarginAmt2 = null!, lblMarginAmt3 = null!;
    private Label lblTotalMarginAmt = null!;
    private Label lblMargin1Title = null!, lblMargin2Title = null!, lblMargin3Title = null!;
    private Label lblShippingNote    = null!;   // shows method/ekspedisi

    private NumericUpDown numMargin1 = null!, numMargin2 = null!, numMargin3 = null!;
    private NumericUpDown numTax    = null!;
    private NumericUpDown numPPh    = null!;

    // ── Client info fields (live in summary panel, no dialog) ────────────
    private TextBox        txtNomorSurat    = null!;
    private TextBox        txtClientName    = null!;
    private TextBox        txtContactPhone  = null!;
    private TextBox        txtCompany       = null!;
    private TextBox        txtAddress       = null!;
    private TextBox        txtProjectName   = null!;
    private TextBox        txtNotes         = null!;
    private DateTimePicker dtpCreatedDate   = null!;
    private DateTimePicker dtpEstOrderDate  = null!;

    private Button btnSave = null!, btnNew = null!, btnHistory = null!;
    private Button btnSettings = null!;
    private Button btnDashboard = null!;
    private Label  lblStatus = null!;

    /// <summary>Set by ShellForm after login so Dashboard can show user greeting.</summary>
    public User? CurrentUser { get; set; }

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
        Text            = "Kalkulator Panel Tritunggal Swarna";
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
            BackColor   = AppTheme.BgHeader,
            Padding     = new Padding(0),
            Margin      = new Padding(0)
        };
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));       // title
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f)); // buttons

        var lblAppTitle = new Label
        {
            Text      = "⚡ Kalkulator Panel",
            Font      = AppTheme.FontTitle,
            ForeColor = Color.White,
            AutoSize  = false,
            Width     = 220,
            Dock      = DockStyle.Fill,
            TextAlign = ContentAlignment.TopLeft,
            Padding   = new Padding(16, 14, 0, 0)
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

        btnDashboard = MakeToolbarButton("🏠 Dashboard",     AppTheme.Bg2,      120);
        btnNew      = MakeToolbarButton("＋ Estimasi Baru", AppTheme.Brand500, 140);
        btnHistory  = MakeToolbarButton("📋 Riwayat",       AppTheme.Bg2,      110);
        btnSettings = MakeToolbarButton("⚙ Settings",       AppTheme.Bg2,      110);

        btnDashboard.Click += BtnDashboard_Click;
        btnNew.Click      += BtnNew_Click;
        btnHistory.Click  += BtnHistory_Click;
        btnSettings.Click += BtnSettings_Click;

        btnFlow.Controls.AddRange(new Control[] { btnDashboard, btnNew, btnHistory, btnSettings });
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
            BackColor = AppTheme.Bg1,
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

        // Panel title row: "Daftar Produk"  [+ Tambah]
        var pnlTitleRow = new Panel { Dock = DockStyle.Top, Height = 30 };

        var lblPanelTitle = AppTheme.MakeLabel("Daftar Produk", AppTheme.FontBold, AppTheme.TextPrimary);
        lblPanelTitle.Dock   = DockStyle.Left;
        lblPanelTitle.Width  = 160;
        lblPanelTitle.Height = 30;
        lblPanelTitle.TextAlign = ContentAlignment.MiddleLeft;

        var btnAddProduct = new Button
        {
            Text      = "➕ Tambah",
            Dock      = DockStyle.Right,
            Width     = 90,
            Height    = 26,
            FlatStyle = FlatStyle.Flat,
            BackColor = AppTheme.Success500,
            ForeColor = Color.White,
            Font      = AppTheme.FontSmall,
            Cursor    = Cursors.Hand
        };
        btnAddProduct.FlatAppearance.BorderSize = 0;
        btnAddProduct.FlatAppearance.MouseOverBackColor = AppTheme.Success400;
        btnAddProduct.Click += BtnAddProduct_Click;

        var btnExportProd = new Button
        {
            Text      = "📤 Export",
            Dock      = DockStyle.Right,
            Width     = 82,
            Height    = 26,
            FlatStyle = FlatStyle.Flat,
            BackColor = AppTheme.Bg2,
            ForeColor = AppTheme.Text2,
            Font      = AppTheme.FontSmall,
            Cursor    = Cursors.Hand
        };
        btnExportProd.FlatAppearance.BorderSize = 0;
        btnExportProd.FlatAppearance.MouseOverBackColor = AppTheme.Bg3;
        btnExportProd.Click += BtnExportProducts_Click;

        // DockStyle.Right: last-added appears leftmost → Export left of Tambah
        pnlTitleRow.Controls.Add(btnAddProduct);
        pnlTitleRow.Controls.Add(btnExportProd);
        pnlTitleRow.Controls.Add(lblPanelTitle);

        var lblCat = AppTheme.MakeLabel("Kategori:", AppTheme.FontSmall, AppTheme.TextSecondary);
        lblCat.Dock   = DockStyle.Top;
        lblCat.Height = 22;

        cmbCategory = new ComboBox { Dock = DockStyle.Top };
        AppTheme.StyleComboBox(cmbCategory);
        cmbCategory.DropDownStyle        = ComboBoxStyle.DropDownList;
        cmbCategory.SelectedIndexChanged += CmbFilter_Changed;

        var spacer1 = new Panel { Dock = DockStyle.Top, Height = 4 };

        var lblVendor = AppTheme.MakeLabel("Merk:", AppTheme.FontSmall, AppTheme.TextSecondary);
        lblVendor.Dock   = DockStyle.Top;
        lblVendor.Height = 20;

        cmbVendor = new ComboBox { Dock = DockStyle.Top };
        AppTheme.StyleComboBox(cmbVendor);
        cmbVendor.DropDownStyle        = ComboBoxStyle.DropDownList;
        cmbVendor.SelectedIndexChanged += CmbFilter_Changed;

        var spacer1b = new Panel { Dock = DockStyle.Top, Height = 6 };

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
        dgvProducts.Columns.Add(new DataGridViewTextBoxColumn { Name = "ColRef",       HeaderText = "Kode",        Width = 100, FillWeight = 25 });
        dgvProducts.Columns.Add(new DataGridViewTextBoxColumn { Name = "ColName",      HeaderText = "Nama Produk", FillWeight = 45 });
        dgvProducts.Columns.Add(new DataGridViewTextBoxColumn { Name = "ColVendor",    HeaderText = "Merk",        FillWeight = 18 });
        dgvProducts.Columns.Add(new DataGridViewTextBoxColumn { Name = "ColPrice",     HeaderText = "Harga (Rp)",  FillWeight = 28, DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleRight } });
        dgvProducts.Columns.Add(new DataGridViewTextBoxColumn { Name = "ColYear",      HeaderText = "Thn",         Width = 46,      DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleCenter, ForeColor = Color.FromArgb(100, 116, 139) } });
        dgvProducts.Columns.Add(new DataGridViewTextBoxColumn { Name = "ColProductId", Visible    = false });

        dgvProducts.CellDoubleClick += DgvProducts_CellDoubleClick;
        dgvProducts.CellFormatting  += DgvProducts_CellFormatting;
        dgvProducts.MouseDown       += DgvProducts_MouseDown;
        dgvProducts.MouseMove       += DgvProducts_MouseMove;

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
        parent.Controls.Add(spacer1b);
        parent.Controls.Add(cmbVendor);
        parent.Controls.Add(lblVendor);
        parent.Controls.Add(spacer1);
        parent.Controls.Add(cmbCategory);
        parent.Controls.Add(lblCat);
        parent.Controls.Add(pnlTitleRow);
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
        // Update background color to match selected section (dark tinted variants)
        cmbTargetSection.SelectedIndexChanged += (s, e) =>
        {
            cmbTargetSection.BackColor = cmbTargetSection.SelectedItem?.ToString() switch
            {
                "Material Pendukung" => Color.FromArgb(38,  30,  10),  // amber
                "Material Lainnya"   => Color.FromArgb(10,  32,  22),  // green
                "Box"                => Color.FromArgb(38,  20,   6),  // orange
                "Incoming"           => Color.FromArgb(28,  16,  48),  // purple
                "Outgoing"           => Color.FromArgb(48,  10,  16),  // rose
                "Trailer"            => Color.FromArgb( 6,  30,  38),  // cyan
                "Karoseri"           => Color.FromArgb(40,  34,   6),  // yellow
                "Jasa"               => Color.FromArgb(38,  10,  34),  // fuchsia
                _                    => AppTheme.Bg2
            };
        };

        // ── "Tambah Grup" button ──────────────────────────────────────────
        var btnTambahGrup = new Button
        {
            Text      = "＋ Tambah Grup",
            Location  = new Point(399, 4),
            Width     = 115,
            Height    = 26,
            FlatStyle = FlatStyle.Flat,
            BackColor = AppTheme.Success500,
            ForeColor = Color.White,
            Font      = AppTheme.FontSmall,
            Cursor    = Cursors.Hand,
        };
        btnTambahGrup.FlatAppearance.BorderSize = 0;
        btnTambahGrup.FlatAppearance.MouseOverBackColor = AppTheme.Success400;
        btnTambahGrup.Click += BtnTambahGrup_Click;

        pnlHeader.Controls.AddRange(new Control[] { lblTitle, lblTambahKe, cmbTargetSection, btnTambahGrup });

        dgvItems = new DataGridView { Dock = DockStyle.Fill, AllowDrop = true };
        AppTheme.StyleGrid(dgvItems);
        dgvItems.DragEnter += DgvItems_DragEnter;
        dgvItems.DragDrop  += DgvItems_DragDrop;

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
        colSection.Items.AddRange("", "Material Utama", "Material Pendukung", "Material Lainnya",
            "Box", "Incoming", "Outgoing", "Trailer", "Karoseri", "Jasa");
        dgvItems.Columns.Add(colSection);

        dgvItems.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "ColItemQty", HeaderText = "Qty", FillWeight = 7,
            DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleCenter }
        });
        dgvItems.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "ColItemSatuan", HeaderText = "Satuan", FillWeight = 8,
            DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleCenter }
        });
        dgvItems.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "ColItemUnitPrice", HeaderText = "Harga Satuan", ReadOnly = true, FillWeight = 13,
            DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleRight }
        });

        // 3-tier per-item adjustments (cascading: Adj1 → Adj2 → Adj3)
        dgvItems.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "ColItemAdj1", HeaderText = "Adj1 (%)", FillWeight = 7,
            DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleRight }
        });
        dgvItems.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "ColItemAdj2", HeaderText = "Adj2 (%)", FillWeight = 7,
            DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleRight }
        });
        dgvItems.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "ColItemAdj3", HeaderText = "Adj3 (%)", FillWeight = 7,
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

    // ── RIGHT PANEL: Info Estimasi + Cost Summary ─────────────────────────
    private void BuildSummaryPanel(Panel parent)
    {
        parent.BackColor = AppTheme.SidebarBg;
        parent.Padding   = new Padding(12);

        var pnlSummary = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        int y = 0;

        // ════ INFO ESTIMASI ══════════════════════════════════════════════
        var lblInfoHead = AppTheme.MakeLabel("INFO ESTIMASI", AppTheme.FontBold, AppTheme.Primary);
        lblInfoHead.Location = new Point(0, y); y += 24;
        pnlSummary.Controls.Add(lblInfoHead);

        // ── No. Surat (manual) + lanjutkan button ────────────────────────
        var lblNomorSurat = AppTheme.MakeLabel("No. Surat", AppTheme.FontSmall, AppTheme.TextSecondary);
        lblNomorSurat.Location = new Point(0, y);
        pnlSummary.Controls.Add(lblNomorSurat); y += 17;

        txtNomorSurat = new TextBox
        {
            Location         = new Point(0, y),
            Width            = 160,
            PlaceholderText  = "Contoh: 136/PR.BDG/IV/2026",
            Font             = AppTheme.FontBase,
            Anchor           = AnchorStyles.Top | AnchorStyles.Left
        };
        AppTheme.StyleTextBox(txtNomorSurat);

        var btnLanjutkan = new Button
        {
            Text      = "↩",
            Location  = new Point(165, y),
            Width     = 34,
            Height    = 26,
            FlatStyle = FlatStyle.Flat,
            BackColor = AppTheme.Bg2,
            ForeColor = AppTheme.Text2,
            Font      = new Font("Segoe UI", 10f),
            Cursor    = Cursors.Hand
        };
        btnLanjutkan.FlatAppearance.BorderSize = 0;
        btnLanjutkan.FlatAppearance.MouseOverBackColor = AppTheme.Bg3;
        btnLanjutkan.Click += async (s, e) => await LanjutkanNomorSuratAsync();
        pnlSummary.Controls.Add(txtNomorSurat);
        pnlSummary.Controls.Add(btnLanjutkan);
        y += 30;

        txtClientName   = AddInfoField(pnlSummary, ref y, "Nama Klien *",     "Contoh: Budi Santoso");
        txtCompany      = AddInfoField(pnlSummary, ref y, "Perusahaan",        "Nama perusahaan / instansi");
        txtContactPhone = AddInfoField(pnlSummary, ref y, "No Kontak",         "08xx-xxxx-xxxx");
        txtProjectName  = AddInfoField(pnlSummary, ref y, "Perihal",            "Panel MDP 3-Phase 400A");
        txtAddress      = AddInfoField(pnlSummary, ref y, "Alamat",            "Alamat pengiriman");

        // Tanggal Pembuatan
        var lblCreated = AppTheme.MakeLabel("Tgl. Pembuatan", AppTheme.FontSmall, AppTheme.TextSecondary);
        lblCreated.Location = new Point(0, y);
        pnlSummary.Controls.Add(lblCreated); y += 17;
        dtpCreatedDate = new DateTimePicker { Location = new Point(0, y), Width = 200, Format = DateTimePickerFormat.Short, Value = DateTime.Today, Font = AppTheme.FontBase, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
        pnlSummary.Controls.Add(dtpCreatedDate); y += 30;

        // Estimasi Pemesanan
        var lblEstOrder = AppTheme.MakeLabel("Est. Pemesanan", AppTheme.FontSmall, AppTheme.TextSecondary);
        lblEstOrder.Location = new Point(0, y);
        pnlSummary.Controls.Add(lblEstOrder); y += 17;
        dtpEstOrderDate = new DateTimePicker { Location = new Point(0, y), Width = 200, Format = DateTimePickerFormat.Short, Value = DateTime.Today.AddDays(14), Font = AppTheme.FontBase, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
        pnlSummary.Controls.Add(dtpEstOrderDate); y += 30;

        txtNotes = AddInfoField(pnlSummary, ref y, "Catatan",  "Catatan tambahan...");

        AddSeparator(pnlSummary, ref y);
        y += 4;

        // ════ RINGKASAN BIAYA ════════════════════════════════════════════
        var lblTitle = AppTheme.MakeLabel("RINGKASAN BIAYA", AppTheme.FontBold, AppTheme.TextSecondary);
        lblTitle.Location = new Point(0, y); y += 24;
        pnlSummary.Controls.Add(lblTitle);

        // SubTotal
        AddSummaryRow(pnlSummary, ref y, "Subtotal:", ref lblSubTotal);
        AddSeparator(pnlSummary, ref y);

        // ── 3-tier sequential margin ─────────────────────────────────────
        var lblMarginHeader = AppTheme.MakeLabel("MARGIN / ADJUSTMENT", AppTheme.FontBold, AppTheme.TextSecondary);
        lblMarginHeader.Location = new Point(0, y); y += 22;
        pnlSummary.Controls.Add(lblMarginHeader);

        BuildMarginTierRow(pnlSummary, ref y, 1, _margin1Percent,
            ref lblMargin1Title, ref numMargin1, ref lblMarginAmt1,
            (s, e) => { _margin1Percent = numMargin1.Value; UpdateMarginTitles(); RecalcSummary(); });

        BuildMarginTierRow(pnlSummary, ref y, 2, _margin2Percent,
            ref lblMargin2Title, ref numMargin2, ref lblMarginAmt2,
            (s, e) => { _margin2Percent = numMargin2.Value; UpdateMarginTitles(); RecalcSummary(); });

        BuildMarginTierRow(pnlSummary, ref y, 3, _margin3Percent,
            ref lblMargin3Title, ref numMargin3, ref lblMarginAmt3,
            (s, e) => { _margin3Percent = numMargin3.Value; UpdateMarginTitles(); RecalcSummary(); });

        AddSummaryRow(pnlSummary, ref y, "Total Margin:", ref lblTotalMarginAmt);
        AddSeparator(pnlSummary, ref y);

        // Ongkos Kirim
        var lblShipTitle = AppTheme.MakeLabel("Ongkos Kirim:", AppTheme.FontSmall, AppTheme.TextSecondary);
        lblShipTitle.Location = new Point(0, y);
        lblShipTitle.AutoSize = true;
        pnlSummary.Controls.Add(lblShipTitle);

        var btnHitungOngkir = new Button
        {
            Text     = "🚚 Hitung Ongkir",
            Location = new Point(0, y + 18),
            Width    = 160,
            Height   = 30
        };
        AppTheme.StyleButton(btnHitungOngkir, AppTheme.Bg2, AppTheme.Text2);
        btnHitungOngkir.Click += BtnHitungOngkir_Click;
        pnlSummary.Controls.Add(btnHitungOngkir);

        lblShippingNote = new Label
        {
            Text      = "Belum dihitung",
            Font      = AppTheme.FontSmall,
            ForeColor = AppTheme.TextMuted,
            Location  = new Point(0, y + 52),
            AutoSize  = false,
            Width     = 220,
            Height    = 16
        };
        pnlSummary.Controls.Add(lblShippingNote);
        y += 72;

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
        AppTheme.StyleNumericUpDown(numTax);
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
        AppTheme.StyleNumericUpDown(numPPh);
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
    }

    private TextBox AddInfoField(Panel parent, ref int y, string label, string placeholder = "")
    {
        var lbl = AppTheme.MakeLabel(label, AppTheme.FontSmall, AppTheme.TextSecondary);
        lbl.Location = new Point(0, y);
        parent.Controls.Add(lbl);
        y += 17;
        var tb = new TextBox
        {
            Location        = new Point(0, y),
            Width           = 200,
            PlaceholderText = placeholder,
            Font            = AppTheme.FontBase,
            Anchor          = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        AppTheme.StyleTextBox(tb);
        parent.Controls.Add(tb);
        y += 30;
        return tb;
    }

    /// <summary>Builds one margin tier row: title + NumericUpDown + value label.</summary>
    private void BuildMarginTierRow(
        Panel parent, ref int y, int tier, decimal initialValue,
        ref Label lblTitle, ref NumericUpDown num, ref Label lblAmt,
        EventHandler onChanged)
    {
        lblTitle = AppTheme.MakeLabel($"Tier {tier} (%):", AppTheme.FontSmall, AppTheme.TextSecondary);
        lblTitle.Location = new Point(0, y);
        lblTitle.AutoSize = true;
        parent.Controls.Add(lblTitle);
        y += 18;

        num = new NumericUpDown
        {
            Location = new Point(0, y), Width = 200,
            Minimum = -100, Maximum = 500, DecimalPlaces = 1, Value = initialValue,
            Font = AppTheme.FontBase, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        AppTheme.StyleNumericUpDown(num);
        num.ValueChanged += onChanged;
        parent.Controls.Add(num);
        y += 28;

        AddSummaryRow(parent, ref y, $"  → Nilai Tier {tier}:", ref lblAmt);
        y += 2;
    }

    private void UpdateMarginTitles()
    {
        static string Fmt(decimal v) => v == 0 ? "0%" : (v > 0 ? $"+{v:N1}%" : $"{v:N1}%");
        if (lblMargin1Title != null) lblMargin1Title.Text = $"Tier 1 {Fmt(_margin1Percent)}:";
        if (lblMargin2Title != null) lblMargin2Title.Text = $"Tier 2 {Fmt(_margin2Percent)}:";
        if (lblMargin3Title != null) lblMargin3Title.Text = $"Tier 3 {Fmt(_margin3Percent)}:";
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
        await LoadCategoriesAsync();   // loads both categories and vendors
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

        var vendors = (await _productRepo.GetAllVendorsAsync()).ToList();
        cmbVendor.Items.Clear();
        cmbVendor.Items.Add("— Semua Merk —");
        foreach (var v in vendors) cmbVendor.Items.Add(v);
        cmbVendor.SelectedIndex = 0;
    }

    private async Task LoadProductsAsync(string? search = null, string? category = null, string? vendor = null)
    {
        IEnumerable<Product> products;

        if (!string.IsNullOrWhiteSpace(search))
            products = await _productRepo.SearchAsync(search);
        else if (!string.IsNullOrWhiteSpace(category) && category != "— Semua Kategori —")
            products = await _productRepo.GetByCategoryAsync(category);
        else
            products = await _productRepo.GetAllAsync();

        // Apply additional filters in memory
        if (!string.IsNullOrWhiteSpace(category) && category != "— Semua Kategori —")
            products = products.Where(p => p.Category == category);

        if (!string.IsNullOrWhiteSpace(vendor) && vendor != "— Semua Merk —")
            products = products.Where(p => p.Vendor == vendor);

        dgvProducts.Rows.Clear();
        foreach (var p in products)
        {
            var displayName = p.StockStatus == 2 ? $"{p.ProductName}  [Indent]" : p.ProductName;
            var vendorShort = p.Vendor?.Replace("Schneider Electric", "Schneider") ?? "";
            var yearStr     = p.PriceYear.HasValue ? p.PriceYear.Value.ToString() : "—";
            dgvProducts.Rows.Add(p.ReferenceCode, displayName, vendorShort, p.Price, yearStr, p.ProductId);
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

        TryLoad("DefaultMarginPercent", numMargin1, ref _margin1Percent);
        TryLoad("DefaultTaxPercent",    numTax,    ref _taxPercent);
    }

    private async void CmbFilter_Changed(object? sender, EventArgs e) =>
        await LoadProductsAsync(txtSearch.Text, cmbCategory.SelectedItem?.ToString(), cmbVendor.SelectedItem?.ToString());

    private async void TxtSearch_TextChanged(object? sender, EventArgs e) =>
        await LoadProductsAsync(txtSearch.Text, cmbCategory.SelectedItem?.ToString(), cmbVendor.SelectedItem?.ToString());

    // ── Tambah Produk ─────────────────────────────────────────────────────
    private async void BtnAddProduct_Click(object? sender, EventArgs e)
    {
        // Pass existing categories so the combo is pre-filled
        var categories = _context.Products
            .Select(p => p.Category)
            .Distinct()
            .OrderBy(c => c)
            .ToList();

        using var dlg = new ProductEditDialog(null, categories);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        // Check for duplicate reference code
        if (_context.Products.Any(p => p.ReferenceCode == dlg.OutReferenceCode))
        {
            MessageBox.Show(
                $"Kode Referensi \"{dlg.OutReferenceCode}\" sudah ada di database.\nGunakan kode yang berbeda.",
                "Kode Duplikat", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var product = new Product
        {
            Category       = dlg.OutCategory,
            ReferenceCode  = dlg.OutReferenceCode,
            ProductName    = dlg.OutProductName,
            Specifications = dlg.OutSpecifications,
            Price          = dlg.OutPrice,
            PriceYear      = dlg.OutPriceYear,
            StockStatus    = dlg.OutStockStatus,
            Vendor         = dlg.OutVendor,
            LastUpdated    = DateTime.UtcNow
        };

        try
        {
            _context.Products.Add(product);
            await _context.SaveChangesAsync();

            // Refresh product list and category/vendor dropdowns
            await LoadCategoriesAsync();
            await LoadProductsAsync(txtSearch.Text,
                cmbCategory.SelectedItem?.ToString(),
                cmbVendor.SelectedItem?.ToString());

            SetStatus($"Produk \"{product.ProductName}\" berhasil ditambahkan.");
            MessageBox.Show(
                $"Produk \"{product.ProductName}\" berhasil ditambahkan ke database.",
                "Berhasil", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Gagal menyimpan produk:\n{ex.Message}", "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // ── Export Produk ─────────────────────────────────────────────────────
    private async void BtnExportProducts_Click(object? sender, EventArgs e)
    {
        // Determine which products to export
        bool filtered = dgvProducts.Rows.Count > 0 &&
                        (!string.IsNullOrWhiteSpace(txtSearch.Text) ||
                         (cmbCategory.SelectedItem?.ToString() is string c && c != "— Semua Kategori —") ||
                         (cmbVendor.SelectedItem?.ToString()   is string v && v != "— Semua Merk —"));

        string scope = "semua";
        if (filtered)
        {
            var pick = MessageBox.Show(
                $"Ekspor {dgvProducts.Rows.Count} produk yang sedang ditampilkan,\n" +
                "atau semua produk di database?\n\n" +
                "  [Yes] = Yang ditampilkan\n  [No]  = Semua produk",
                "Pilih Cakupan Export",
                MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (pick == DialogResult.Cancel) return;
            scope = pick == DialogResult.Yes ? "filtered" : "semua";
        }

        using var sfd = new SaveFileDialog
        {
            Title       = "Simpan Data Produk",
            Filter      = "Excel (*.xlsx)|*.xlsx|CSV (*.csv)|*.csv",
            FilterIndex = 1,
            FileName    = $"Produk_{DateTime.Now:yyyyMMdd}"
        };
        if (sfd.ShowDialog() != DialogResult.OK) return;

        try
        {
            List<Product> products;
            if (scope == "filtered")
            {
                // Collect product IDs shown in the grid
                var ids = dgvProducts.Rows
                    .Cast<DataGridViewRow>()
                    .Select(r => r.Cells["ColProductId"].Value is int id ? id : 0)
                    .Where(id => id > 0)
                    .ToHashSet();
                products = _context.Products
                    .Where(p => ids.Contains(p.ProductId))
                    .OrderBy(p => p.Category).ThenBy(p => p.ProductName)
                    .ToList();
            }
            else
            {
                products = await Task.Run(() =>
                    _context.Products
                        .OrderBy(p => p.Category).ThenBy(p => p.ProductName)
                        .ToList());
            }

            var ext = Path.GetExtension(sfd.FileName).ToLowerInvariant();
            if (ext == ".xlsx")
                ExportToExcel(sfd.FileName, products);
            else
                ExportToCsv(sfd.FileName, products);

            SetStatus($"Export selesai — {products.Count} produk disimpan ke {Path.GetFileName(sfd.FileName)}");
            if (MessageBox.Show(
                    $"Berhasil mengekspor {products.Count} produk.\n\nBuka file sekarang?",
                    "Export Selesai", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(sfd.FileName) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Gagal mengekspor:\n{ex.Message}", "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static void ExportToCsv(string path, List<Product> products)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("category,reference_code,product_name,specifications,price,price_year,stock_status,vendor");
        foreach (var p in products)
        {
            static string Q(string? s) => s == null ? "" : $"\"{s.Replace("\"", "\"\"")}\"";
            sb.AppendLine(string.Join(",",
                Q(p.Category),
                Q(p.ReferenceCode),
                Q(p.ProductName),
                Q(p.Specifications),
                p.Price.ToString("0"),
                p.PriceYear.HasValue ? p.PriceYear.Value.ToString() : "",
                p.StockStatus.ToString(),
                Q(p.Vendor)));
        }
        File.WriteAllText(path, sb.ToString(), System.Text.Encoding.UTF8);
    }

    private static void ExportToExcel(string path, List<Product> products)
    {
        using var wb = new ClosedXML.Excel.XLWorkbook();
        var ws = wb.Worksheets.Add("Produk");

        // Header row
        string[] headers = { "category","reference_code","product_name","specifications",
                              "price","price_year","stock_status","vendor" };
        for (int c = 0; c < headers.Length; c++)
        {
            var cell = ws.Cell(1, c + 1);
            cell.Value = headers[c];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.FromHtml("#1e40af");
            cell.Style.Font.FontColor       = ClosedXML.Excel.XLColor.White;
        }

        // Data rows
        for (int r = 0; r < products.Count; r++)
        {
            var p = products[r];
            int row = r + 2;
            ws.Cell(row, 1).Value = p.Category;
            ws.Cell(row, 2).Value = p.ReferenceCode;
            ws.Cell(row, 3).Value = p.ProductName;
            ws.Cell(row, 4).Value = p.Specifications ?? "";
            ws.Cell(row, 5).Value = (double)p.Price;
            ws.Cell(row, 6).Value = p.PriceYear.HasValue ? p.PriceYear.Value.ToString() : "";
            ws.Cell(row, 7).Value = p.StockStatus;
            ws.Cell(row, 8).Value = p.Vendor ?? "";

            // Price column: number format
            ws.Cell(row, 5).Style.NumberFormat.Format = "#,##0";
            // Alternating row color
            if (r % 2 == 1)
                ws.Row(row).Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.FromHtml("#f8fafc");
        }

        ws.Columns().AdjustToContents();
        // Freeze header row
        ws.SheetView.FreezeRows(1);
        wb.SaveAs(path);
    }

    private void DgvProducts_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex >= 0 && e.ColumnIndex == dgvProducts.Columns["ColPrice"].Index && e.Value is decimal price)
        {
            e.Value = FormatRupiah(price);
            e.FormattingApplied = true;
        }
    }

    // ── Tambah Grup ───────────────────────────────────────────────────────

    private void BtnTambahGrup_Click(object? sender, EventArgs e)
    {
        var section = cmbTargetSection.SelectedItem?.ToString();
        if (string.IsNullOrEmpty(section)) return;

        if (_activeSections.Contains(section))
        {
            SetStatus($"Grup '{section}' sudah ada. Pilih produk lalu klik [+] atau klik 2×.");
            return;
        }

        _activeSections.Add(section);
        RefreshItemsGrid();
        SetStatus($"Grup '{section}' ditambahkan.  Klik 2× produk atau seret ke grid untuk mengisi.");
    }

    // ── Drag from product list ────────────────────────────────────────────

    private void DgvProducts_MouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            _dragStartPoint    = e.Location;
            _isDraggingProduct = false;
        }
    }

    private void DgvProducts_MouseMove(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || _isDraggingProduct) return;
        var dx = Math.Abs(e.X - _dragStartPoint.X);
        var dy = Math.Abs(e.Y - _dragStartPoint.Y);
        if (dx < SystemInformation.DragSize.Width && dy < SystemInformation.DragSize.Height) return;

        var hit = dgvProducts.HitTest(_dragStartPoint.X, _dragStartPoint.Y);
        if (hit.RowIndex < 0) return;

        _isDraggingProduct = true;
        dgvProducts.DoDragDrop(hit.RowIndex, DragDropEffects.Copy);
        _isDraggingProduct = false;
    }

    private void DgvItems_DragEnter(object? sender, DragEventArgs e)
    {
        e.Effect = e.Data?.GetDataPresent(typeof(int)) == true
            ? DragDropEffects.Copy
            : DragDropEffects.None;
    }

    private void DgvItems_DragDrop(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetData(typeof(int)) is not int rowIdx) return;
        if (rowIdx < 0 || rowIdx >= dgvProducts.Rows.Count) return;
        AddProductRowToEstimation(dgvProducts.Rows[rowIdx]);
    }

    private void DgvProducts_CellDoubleClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0) return;
        AddProductRowToEstimation(dgvProducts.Rows[e.RowIndex]);
    }

    /// <summary>
    /// Core add-to-estimation logic shared by double-click and the "+" button.
    /// If the same product already exists in the target section, increments qty.
    /// </summary>
    private void AddProductRowToEstimation(DataGridViewRow row)
    {
        var refCode = row.Cells["ColRef"].Value?.ToString() ?? "";
        var name    = row.Cells["ColName"].Value?.ToString() ?? "";
        var price   = row.Cells["ColPrice"].Value is decimal p ? p : 0m;
        var prodId  = row.Cells["ColProductId"].Value is int id ? id : 0;
        var vendor  = row.Cells["ColVendor"].Value?.ToString() ?? "";

        var targetSection = cmbTargetSection.SelectedItem?.ToString() ?? "Material Utama";

        // Same product in same section → increment qty
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
                Vendor        = vendor,
                Quantity      = 1,
                Adj1Percent   = 0m,
                Adj2Percent   = 0m,
                Adj3Percent   = 0m,
                Section       = targetSection
            });
        }

        RefreshItemsGrid();
        RecalcSummary();
        SetStatus($"Ditambahkan: {name}  →  {targetSection}");
    }

    private void DgvItems_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0) return;
        // Format Adj column: display "+10.0%" / "-5.0%" / "—"
        foreach (var colName in new[] { "ColItemAdj1", "ColItemAdj2", "ColItemAdj3" })
        {
            if (e.ColumnIndex == dgvItems.Columns[colName].Index && e.Value is decimal adj)
            {
                e.Value             = adj == 0 ? "—" : (adj > 0 ? $"+{adj:N1}%" : $"{adj:N1}%");
                e.FormattingApplied = true;
                break;
            }
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
        else if (e.ColumnIndex == dgvItems.Columns["ColItemSatuan"].Index)
        {
            var satuan = row.Cells["ColItemSatuan"].Value?.ToString() ?? "pcs";
            item.Satuan = string.IsNullOrWhiteSpace(satuan) ? "pcs" : satuan.Trim();
            _refreshing = false; // no grid refresh needed for Satuan change
            return;
        }
        else if (e.ColumnIndex == dgvItems.Columns["ColItemAdj1"].Index)
        {
            if (decimal.TryParse(row.Cells["ColItemAdj1"].Value?.ToString(), out var adj))
                item.Adj1Percent = Math.Clamp(adj, -99m, 500m);
        }
        else if (e.ColumnIndex == dgvItems.Columns["ColItemAdj2"].Index)
        {
            if (decimal.TryParse(row.Cells["ColItemAdj2"].Value?.ToString(), out var adj))
                item.Adj2Percent = Math.Clamp(adj, -99m, 500m);
        }
        else if (e.ColumnIndex == dgvItems.Columns["ColItemAdj3"].Index)
        {
            if (decimal.TryParse(row.Cells["ColItemAdj3"].Value?.ToString(), out var adj))
                item.Adj3Percent = Math.Clamp(adj, -99m, 500m);
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

    private void BtnHitungOngkir_Click(object? sender, EventArgs e)
    {
        using var dlg = new ShippingCalculatorDialog(_shippingCost, _shippingNote);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        _shippingCost = dlg.ResultCost;
        _shippingNote = dlg.ResultNote;

        lblShippingNote.Text = string.IsNullOrEmpty(_shippingNote)
            ? "Dihitung manual"
            : _shippingNote.Length > 40 ? _shippingNote[..37] + "…" : _shippingNote;

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

    private void BtnDashboard_Click(object? sender, EventArgs e)
    {
        var user = CurrentUser ?? new PanelCalculator.Core.Models.User
        {
            Username     = "?",
            PasswordHash = "",
            FullName     = "Pengguna",
            Role         = "Operator",
            IsActive     = true
        };
        using var dlg = new DashboardForm(_context, user);
        dlg.ShowDialog(this);
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
        _activeSections.Clear();
        _shippingCost = 0m;
        _shippingNote = "";
        if (lblShippingNote != null) lblShippingNote.Text = "Belum dihitung";

        // Clear client info fields
        txtNomorSurat.Clear();
        txtClientName.Clear();
        txtContactPhone.Clear();
        txtCompany.Clear();
        txtAddress.Clear();
        txtProjectName.Clear();
        txtNotes.Clear();
        dtpCreatedDate.Value  = DateTime.Today;
        dtpEstOrderDate.Value = DateTime.Today.AddDays(14);

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

    /// <summary>Called by ShellForm when user clicks the Riwayat nav item.</summary>
    public void OpenHistoryFromShell() => BtnHistory_Click(null, EventArgs.Empty);

    private async void BtnSave_Click(object? sender, EventArgs e)
    {
        if (_currentItems.Count == 0)
        {
            MessageBox.Show("Tambahkan minimal satu produk sebelum menyimpan.", "Perhatian",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        // Validate required field
        if (string.IsNullOrWhiteSpace(txtClientName.Text))
        {
            MessageBox.Show("Nama Klien tidak boleh kosong.", "Perhatian",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            txtClientName.Focus();
            return;
        }

        var today     = DateTime.Now;
        var todayStr  = today.ToString("yyyyMMdd");
        var prefix    = $"EST-{todayStr}-";

        // Use UTC boundaries so the date range matches CreatedDate (stored in UTC)
        var utcStart  = today.Date.ToUniversalTime();
        var utcEnd    = today.Date.AddDays(1).ToUniversalTime();
        var existing  = await _estimationRepo.GetByDateRangeAsync(utcStart, utcEnd);

        // MAX-based sequence: safe even when previous estimations are deleted
        var maxSeq = existing
            .Select(e =>
            {
                if (!e.EstimationNumber.StartsWith(prefix)) return 0;
                return int.TryParse(e.EstimationNumber[prefix.Length..], out int n) ? n : 0;
            })
            .DefaultIfEmpty(0)
            .Max();

        var estNumber = $"{prefix}{(maxSeq + 1):D3}";

        var (subtotal, _, _, _, totalMarginAmt, ppnAmt, pphAmt, total) = CalcAll();

        var estimation = new Estimation
        {
            EstimationNumber  = estNumber,
            NomorSurat        = txtNomorSurat.Text.Trim().NullIfEmpty(),
            ClientName        = txtClientName.Text.Trim(),
            ContactPhone      = txtContactPhone.Text.Trim(),
            Company           = txtCompany.Text.Trim(),
            Address           = txtAddress.Text.Trim(),
            ProjectName       = txtProjectName.Text.Trim(),
            Notes             = txtNotes.Text.Trim(),
            SubTotal          = subtotal,
            MarginPercent     = _margin1Percent,
            Margin2Percent    = _margin2Percent,
            Margin3Percent    = _margin3Percent,
            Margin            = totalMarginAmt,
            ShippingCost      = _shippingCost,
            Tax               = ppnAmt,
            PPhPercent        = _pphPercent,
            PPh               = pphAmt,
            TotalPrice        = total,
            Status            = "Draft",
            CreatedDate       = dtpCreatedDate.Value.ToUniversalTime(),
            EstimatedOrderDate = dtpEstOrderDate.Value.ToUniversalTime()
        };

        estimation.Details = _currentItems.Select(item => new EstimationDetail
        {
            ProductId      = item.ProductId,
            Quantity       = item.Quantity,
            Satuan         = item.Satuan,
            UnitPrice      = item.UnitPrice,
            AdjPercent     = item.Adj1Percent,
            Adj2Percent    = item.Adj2Percent,
            Adj3Percent    = item.Adj3Percent,
            Section        = item.Section,
            LineTotalPrice = item.LineTotal
        }).ToList();

        await _estimationRepo.AddAsync(estimation);
        MessageBox.Show($"Estimasi {estNumber} berhasil disimpan!", "Tersimpan", MessageBoxButtons.OK, MessageBoxIcon.Information);
        SetStatus($"✓ Disimpan: {estNumber} untuk {txtClientName.Text.Trim()}");
    }

    private void BtnExport_Click(object? sender, EventArgs e)
    {
        if (_currentItems.Count == 0)
        {
            MessageBox.Show("Tidak ada item untuk di-export.", "Perhatian", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        // ── Format selection ──────────────────────────────────────────────
        var fmt = MessageBox.Show(
            "Pilih format export PDF:\n\n" +
            "  [Yes]    Surat Resmi  — format surat penawaran formal (2 halaman)\n" +
            "  [No]     Modern       — format digital tabel berwarna\n" +
            "  [Cancel] Batal",
            "Format Export PDF",
            MessageBoxButtons.YesNoCancel,
            MessageBoxIcon.Question);
        if (fmt == DialogResult.Cancel) return;
        bool useLetter = fmt == DialogResult.Yes;

        using var sfd = new SaveFileDialog
        {
            Title      = "Simpan Penawaran PDF",
            Filter     = "PDF Files (*.pdf)|*.pdf",
            FileName   = useLetter
                ? $"SuratPenawaran_{DateTime.Now:yyyyMMdd_HHmm}.pdf"
                : $"Penawaran_{DateTime.Now:yyyyMMdd_HHmm}.pdf",
            DefaultExt = "pdf"
        };
        if (sfd.ShowDialog() != DialogResult.OK) return;

        try
        {
            var settings = _context.Settings.ToDictionary(s => s.SettingKey, s => s.SettingValue ?? "");
            var (subtotal, _, _, _, totalMarginAmt, ppnAmt, pphAmt, total) = CalcAll();

            if (useLetter)
            {
                // ── Surat Resmi (formal letter) ───────────────────────────
                var letterItems = _currentItems
                    .Select(i => new PdfLetterExport.LineItem(
                        i.ReferenceCode, i.ProductName, i.Vendor, i.Section,
                        i.Quantity, i.Satuan, i.UnitPrice, i.LineTotal))
                    .ToList();

                PdfLetterExport.Generate(
                    outputPath:       sfd.FileName,
                    estimationNumber: !string.IsNullOrWhiteSpace(txtNomorSurat.Text) ? txtNomorSurat.Text.Trim() : $"DRAFT-{DateTime.Now:yyyyMMdd-HHmm}",
                    clientName:       !string.IsNullOrWhiteSpace(txtClientName.Text) ? txtClientName.Text.Trim() : "—",
                    contactPhone:     !string.IsNullOrWhiteSpace(txtContactPhone.Text) ? txtContactPhone.Text.Trim() : null,
                    company:          !string.IsNullOrWhiteSpace(txtCompany.Text)      ? txtCompany.Text.Trim()      : null,
                    address:          !string.IsNullOrWhiteSpace(txtAddress.Text)      ? txtAddress.Text.Trim()      : null,
                    perihal:          !string.IsNullOrWhiteSpace(txtProjectName.Text)  ? txtProjectName.Text.Trim()  : null,
                    createdDate:      dtpCreatedDate.Value,
                    notes:            txtNotes.Text.Trim(),
                    items:            letterItems,
                    subtotal:         subtotal,
                    margin1Percent:   _margin1Percent,
                    margin2Percent:   _margin2Percent,
                    margin3Percent:   _margin3Percent,
                    marginAmount:     totalMarginAmt,
                    shippingCost:     _shippingCost,
                    taxPercent:       _taxPercent,
                    taxAmount:        ppnAmt,
                    pphPercent:       _pphPercent,
                    pphAmount:        pphAmt,
                    total:            total,
                    settings:         settings);
            }
            else
            {
                // ── Format Modern (existing colorful table) ───────────────
                var lineItems = _currentItems
                    .Select(i => new PdfQuotationExport.LineItem(
                        i.ReferenceCode, i.ProductName, i.Section, i.Quantity,
                        i.Satuan, i.UnitPrice, i.Adj1Percent, i.LineTotal))
                    .ToList();

                PdfQuotationExport.Generate(
                    outputPath:       sfd.FileName,
                    estimationNumber: $"DRAFT-{DateTime.Now:yyyyMMdd-HHmm}",
                    clientName:       !string.IsNullOrWhiteSpace(txtClientName.Text) ? txtClientName.Text.Trim() : "—",
                    contactPhone:     !string.IsNullOrWhiteSpace(txtContactPhone.Text) ? txtContactPhone.Text.Trim() : null,
                    company:          !string.IsNullOrWhiteSpace(txtCompany.Text)      ? txtCompany.Text.Trim()      : null,
                    address:          !string.IsNullOrWhiteSpace(txtAddress.Text)      ? txtAddress.Text.Trim()      : null,
                    createdDate:      dtpCreatedDate.Value,
                    notes:            txtNotes.Text.Trim(),
                    items:            lineItems,
                    subtotal:         subtotal,
                    marginPercent:    _margin1Percent,
                    marginAmount:     totalMarginAmt,
                    shippingCost:     _shippingCost,
                    taxPercent:       _taxPercent,
                    taxAmount:        ppnAmt,
                    pphPercent:       _pphPercent,
                    pphAmount:        pphAmt,
                    total:            total,
                    settings:         settings);
            }

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
        using var form = new SettingsForm(_context) { CurrentUser = CurrentUser };
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

        // Only render sections that were explicitly added by user OR already have items
        var sectionsToRender = Sections
            .Where(s => _activeSections.Contains(s) || _currentItems.Any(i => i.Section == s))
            .ToList();

        foreach (var section in sectionsToRender)
        {
            // Items belonging to this section (with their original list index)
            var sectionItems = _currentItems
                .Select((item, idx) => (item, idx))
                .Where(x => x.item.Section == section)
                .ToList();

            var hdrBg    = SectionHeaderColor(section);
            var rowBg    = SectionRowColor(section);
            var secTotal = sectionItems.Sum(x => x.item.LineTotal);

            // ── Section header row ────────────────────────────────────────
            int hIdx = dgvItems.Rows.Add();
            var hRow = dgvItems.Rows[hIdx];

            hRow.Cells["ColItemRef"].Value   = $"▶  {section.ToUpper()}";
            hRow.Cells["ColItemTotal"].Value = FormatRupiah(secTotal);

            hRow.DefaultCellStyle.BackColor = hdrBg;
            hRow.DefaultCellStyle.ForeColor = SectionHeaderForeColor(section);
            hRow.DefaultCellStyle.Font      = AppTheme.FontBold;
            hRow.ReadOnly = true;
            hRow.Tag      = -1; // sentinel: not an item row

            // ── Placeholder for empty (but active) section ───────────────
            if (sectionItems.Count == 0)
            {
                int pIdx = dgvItems.Rows.Add("", "  ← klik 2×  produk  atau  seret ke sini",
                    "", "", "", "", "", "", "", "");
                var pRow = dgvItems.Rows[pIdx];
                pRow.ReadOnly = true;
                pRow.DefaultCellStyle.BackColor = SectionRowColor(section);
                pRow.DefaultCellStyle.ForeColor = Color.FromArgb(
                    SectionHeaderForeColor(section).R / 2,
                    SectionHeaderForeColor(section).G / 2,
                    SectionHeaderForeColor(section).B / 2);
                pRow.DefaultCellStyle.Font = new Font("Segoe UI", 8f, FontStyle.Italic);
                pRow.Tag = -1;
                continue;
            }

            // ── Item rows ─────────────────────────────────────────────────
            foreach (var (item, itemIdx) in sectionItems)
            {
                int rIdx = dgvItems.Rows.Add(
                    item.ReferenceCode,
                    item.ProductName,
                    item.Section,
                    item.Quantity,
                    item.Satuan,
                    FormatRupiah(item.UnitPrice),
                    item.Adj1Percent,        // raw decimal → formatted via CellFormatting
                    item.Adj2Percent,
                    item.Adj3Percent,
                    FormatRupiah(item.LineTotal)
                );
                var iRow = dgvItems.Rows[rIdx];
                iRow.Tag = itemIdx; // store _currentItems index for event handlers
                iRow.DefaultCellStyle.BackColor = rowBg;
                iRow.DefaultCellStyle.ForeColor = AppTheme.Text1;
            }
        }

        _refreshing = false;
    }

    /// <summary>
    /// Core calculation with 3-tier sequential margin:
    ///   subtotal         = Σ item.LineTotal (per-item adj already included)
    ///   after1..3        = compounding tiers (×1.m1 ×1.m2 ×1.m3)
    ///   preTax           = after3 + shipping
    ///   ppnAmt           = preTax × ppnPct / 100
    ///   pphAmt           = preTax × pphPct / 100  (withheld, deducted from total)
    ///   total            = preTax + ppnAmt - pphAmt
    /// </summary>
    private (decimal subtotal,
             decimal tier1Amt, decimal tier2Amt, decimal tier3Amt, decimal totalMarginAmt,
             decimal ppnAmt, decimal pphAmt, decimal total) CalcAll()
    {
        var subtotal = _currentItems.Sum(i => i.LineTotal);
        var (t1, t2, t3, totalMargin, afterMargin) =
            _calcService.ApplyMargin3Tier(subtotal, _margin1Percent, _margin2Percent, _margin3Percent);
        var preTax = afterMargin + _shippingCost;
        var ppnAmt = preTax * _taxPercent  / 100m;
        var pphAmt = preTax * _pphPercent / 100m;
        var total  = preTax + ppnAmt - pphAmt;
        return (subtotal, t1, t2, t3, totalMargin, ppnAmt, pphAmt, total);
    }

    private void RecalcSummary()
    {
        var (subtotal, t1Amt, t2Amt, t3Amt, totalMargin, ppnAmt, pphAmt, total) = CalcAll();

        UpdateMarginTitles();
        lblSubTotal.Text      = FormatRupiah(subtotal);
        lblMarginAmt1.Text    = FormatRupiah(t1Amt);
        lblMarginAmt2.Text    = FormatRupiah(t2Amt);
        lblMarginAmt3.Text    = FormatRupiah(t3Amt);
        lblTotalMarginAmt.Text= FormatRupiah(totalMargin);
        lblShipping.Text      = FormatRupiah(_shippingCost);
        lblTax.Text           = FormatRupiah(ppnAmt);
        lblPPh.Text           = FormatRupiah(pphAmt);
        lblTotal.Text         = FormatRupiah(total);
    }

    private void LoadEstimationIntoCalculator(Estimation est)
    {
        _currentItems.Clear();
        _activeSections.Clear();
        // Restore active sections from loaded items so the grid groups are preserved
        _activeSections.AddRange(est.Details
            .Select(d => string.IsNullOrWhiteSpace(d.Section) ? "Material Utama" : d.Section)
            .Distinct()
            .OrderBy(s => Array.IndexOf(Sections, s)));
        foreach (var d in est.Details)
        {
            _currentItems.Add(new EstimationLineItem
            {
                ProductId     = d.ProductId,
                ReferenceCode = d.Product?.ReferenceCode ?? "",
                ProductName   = d.Product?.ProductName ?? "",
                UnitPrice     = d.UnitPrice,
                Vendor        = d.Product?.Vendor ?? "",
                Quantity      = d.Quantity,
                Satuan        = string.IsNullOrWhiteSpace(d.Satuan) ? "pcs" : d.Satuan,
                Adj1Percent   = d.AdjPercent,
                Adj2Percent   = d.Adj2Percent,
                Adj3Percent   = d.Adj3Percent,
                Section       = string.IsNullOrWhiteSpace(d.Section) ? "Material Utama" : d.Section
            });
        }

        // Restore overall percentages stored in the estimation
        try
        {
            numMargin1.Value  = Math.Clamp(est.MarginPercent,  numMargin1.Minimum, numMargin1.Maximum);
            numMargin2.Value  = Math.Clamp(est.Margin2Percent, numMargin2.Minimum, numMargin2.Maximum);
            numMargin3.Value  = Math.Clamp(est.Margin3Percent, numMargin3.Minimum, numMargin3.Maximum);
            _margin1Percent   = numMargin1.Value;
            _margin2Percent   = numMargin2.Value;
            _margin3Percent   = numMargin3.Value;
            numTax.Value      = Math.Clamp(est.Tax > 0 && est.SubTotal > 0
                ? Math.Round(est.Tax / (est.SubTotal + est.Margin + est.ShippingCost) * 100, 1)
                : 11m, numTax.Minimum, numTax.Maximum);
            _taxPercent       = numTax.Value;
            _shippingCost = est.ShippingCost;
            _shippingNote = "";
            if (lblShippingNote != null)
                lblShippingNote.Text = est.ShippingCost > 0
                    ? "Rp " + est.ShippingCost.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("id-ID"))
                    : "Belum dihitung";
            numPPh.Value      = Math.Clamp(est.PPhPercent, numPPh.Minimum, numPPh.Maximum);
            _pphPercent       = numPPh.Value;
        }
        catch { }

        // Restore client info fields
        txtNomorSurat.Text   = est.NomorSurat   ?? "";
        txtClientName.Text   = est.ClientName;
        txtContactPhone.Text = est.ContactPhone ?? "";
        txtCompany.Text      = est.Company      ?? "";
        txtAddress.Text      = est.Address      ?? "";
        txtProjectName.Text  = est.ProjectName  ?? "";
        txtNotes.Text        = est.Notes        ?? "";
        try
        {
            dtpCreatedDate.Value  = est.CreatedDate.ToLocalTime();
            dtpEstOrderDate.Value = est.EstimatedOrderDate.HasValue
                ? est.EstimatedOrderDate.Value.ToLocalTime()
                : DateTime.Today.AddDays(14);
        }
        catch { }

        RefreshItemsGrid();
        RecalcSummary();
        SetStatus($"Loaded: {est.EstimationNumber} — {est.ClientName}");
    }

    // ── Section color helpers (dark-pro palette) ─────────────────────────
    private static Color SectionHeaderColor(string section) => section switch
    {
        "Material Utama"     => Color.FromArgb(15,  22,  55),  // navy
        "Material Pendukung" => Color.FromArgb(32,  22,   8),  // amber
        "Material Lainnya"   => Color.FromArgb( 8,  28,  16),  // green
        "Box"                => Color.FromArgb(38,  20,   6),  // orange
        "Incoming"           => Color.FromArgb(28,  16,  48),  // purple
        "Outgoing"           => Color.FromArgb(48,  10,  16),  // rose
        "Trailer"            => Color.FromArgb( 6,  30,  38),  // cyan
        "Karoseri"           => Color.FromArgb(40,  34,   6),  // yellow
        "Jasa"               => Color.FromArgb(38,  10,  34),  // fuchsia
        _                    => AppTheme.Bg2
    };

    private static Color SectionRowColor(string section) => section switch
    {
        "Material Utama"     => AppTheme.Bg1,
        "Material Pendukung" => Color.FromArgb(14,  12,   6),
        "Material Lainnya"   => Color.FromArgb( 7,  13,  10),
        "Box"                => Color.FromArgb(16,  10,   4),
        "Incoming"           => Color.FromArgb(10,   6,  20),
        "Outgoing"           => Color.FromArgb(20,   5,   8),
        "Trailer"            => Color.FromArgb( 4,  14,  18),
        "Karoseri"           => Color.FromArgb(17,  14,   4),
        "Jasa"               => Color.FromArgb(16,   5,  14),
        _                    => AppTheme.Bg1
    };

    private static Color SectionHeaderForeColor(string section) => section switch
    {
        "Material Utama"     => Color.FromArgb(125, 210, 255), // sky-blue
        "Material Pendukung" => Color.FromArgb(251, 191,  36), // amber-300
        "Material Lainnya"   => Color.FromArgb( 52, 211, 153), // emerald-300
        "Box"                => Color.FromArgb(253, 186, 116), // orange-300
        "Incoming"           => Color.FromArgb(196, 181, 253), // violet-300
        "Outgoing"           => Color.FromArgb(253, 164, 175), // rose-300
        "Trailer"            => Color.FromArgb(103, 232, 249), // cyan-300
        "Karoseri"           => Color.FromArgb(253, 224,  71), // yellow-300
        "Jasa"               => Color.FromArgb(240, 171, 252), // fuchsia-300
        _                    => AppTheme.Text2
    };

    // ── Lanjutkan Nomor Surat ─────────────────────────────────────────────
    /// <summary>Fills txtNomorSurat with the most recent estimation's NomorSurat so the
    /// user can edit it for the new letter (e.g. increment the counter).</summary>
    private async Task LanjutkanNomorSuratAsync()
    {
        try
        {
            var all = await _estimationRepo.GetAllWithDetailsAsync();
            var last = all.OrderByDescending(e => e.CreatedDate)
                          .FirstOrDefault(e => !string.IsNullOrWhiteSpace(e.NomorSurat));
            if (last == null)
            {
                SetStatus("Belum ada nomor surat sebelumnya.");
                return;
            }
            txtNomorSurat.Text = last.NomorSurat!;
            txtNomorSurat.Focus();
            txtNomorSurat.SelectAll();
            SetStatus($"Nomor surat diisi dari: {last.EstimationNumber}");
        }
        catch (Exception ex)
        {
            SetStatus($"Gagal memuat nomor surat: {ex.Message}");
        }
    }

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

    /// <summary>Unit of measurement, manually entered (e.g. pcs, set, meter, rol)</summary>
    public string  Satuan        { get; set; } = "pcs";

    /// <summary>Per-item adjustment tier 1: positive = markup %, negative = diskon %</summary>
    public decimal Adj1Percent { get; set; } = 0m;

    /// <summary>Per-item adjustment tier 2</summary>
    public decimal Adj2Percent { get; set; } = 0m;

    /// <summary>Per-item adjustment tier 3</summary>
    public decimal Adj3Percent { get; set; } = 0m;

    /// <summary>Vendor / merek produk, e.g. "Schneider". Populated from Product.Vendor.</summary>
    public string Vendor { get; set; } = "";

    /// <summary>Material Utama | Material Pendukung | Material Lainnya</summary>
    public string Section { get; set; } = "Material Utama";

    /// <summary>Cascading 3-tier: UnitPrice × (1+Adj1/100) × (1+Adj2/100) × (1+Adj3/100)</summary>
    public decimal EffectiveUnitPrice =>
        UnitPrice * (1m + Adj1Percent / 100m) * (1m + Adj2Percent / 100m) * (1m + Adj3Percent / 100m);
    public decimal LineTotal => EffectiveUnitPrice * Quantity;
}
