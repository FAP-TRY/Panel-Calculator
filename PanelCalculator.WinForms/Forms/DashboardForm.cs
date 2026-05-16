using Microsoft.EntityFrameworkCore;
using PanelCalculator.Core.Models;
using PanelCalculator.Data;
using PanelCalculator.Data.Repositories;
using PanelCalculator.WinForms.Theme;

namespace PanelCalculator.WinForms.Forms;

/// <summary>
/// Kanban pipeline estimasi.
/// Kolom: Draft → Approved
/// </summary>
public class DashboardForm : Form
{
    private readonly PanelCalculatorContext  _context;
    private readonly User                    _currentUser;

    // Definisi kolom pipeline
    private static readonly (string Status, string Title, Color Accent)[] Stages =
    {
        ("Antri Dihitung",  "Antri Dihitung",  Color.FromArgb(217, 119,   6)),  // amber
        ("Draft",           "Draft",           Color.FromArgb(100, 116, 139)),  // slate
        ("Tunggu Approved", "Tunggu Approved", Color.FromArgb( 37,  99, 235)),  // blue
        ("Approved",        "Approved",        Color.FromArgb(  5, 150, 105)),  // emerald
    };

    private const int ColPadding  = 10;

    // One Panel per column (holds cards, Dock=Top stacking)
    private readonly Panel[] _colFlows = new Panel[Stages.Length];
    private readonly Label[] _colCount = new Label[Stages.Length];

    // Action requested by caller (open estimation)
    public Estimation? SelectedEstimation { get; private set; }

    private Label lblSummary = null!;

    public DashboardForm(PanelCalculatorContext context, User currentUser)
    {
        _context     = context;
        _currentUser = currentUser;
        BuildUI();
    }

    // ════════════════════════════════════════════════════════════════════
    private void BuildUI()
    {
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode       = AutoScaleMode.Dpi;
        Text          = "Pipeline Estimasi";
        Size          = new Size(1500, 720);
        MinimumSize   = new Size(1100, 580);
        StartPosition = FormStartPosition.CenterParent;
        BackColor     = AppTheme.Background;

        // ── Top bar ───────────────────────────────────────────────────────
        var pnlTop = new Panel
        {
            Dock      = DockStyle.Top,
            Height    = 56,
            BackColor = AppTheme.BgHeader
        };
        pnlTop.Paint += (s, e) =>
        {
            using var pen = new Pen(AppTheme.Border2);
            e.Graphics.DrawLine(pen, 0, pnlTop.Height - 1, pnlTop.Width, pnlTop.Height - 1);
        };

        var lblTitle = AppTheme.MakeLabel(
            $"📋  Pipeline Estimasi  —  {_currentUser.FullName}",
            AppTheme.FontLarge, AppTheme.TextPrimary);
        lblTitle.Location = new Point(20, 14);
        lblTitle.AutoSize = true;

        lblSummary = AppTheme.MakeLabel("", AppTheme.FontSmall, AppTheme.TextSecondary);
        lblSummary.AutoSize  = false;
        lblSummary.Dock      = DockStyle.Right;
        lblSummary.Width     = 560;
        lblSummary.TextAlign = ContentAlignment.MiddleRight;
        lblSummary.Padding   = new Padding(0, 0, 16, 0);

        var btnRefresh = new Button { Text = "🔄 Refresh", Width = 100, Height = 30, Dock = DockStyle.Right };
        AppTheme.StyleButton(btnRefresh, AppTheme.Bg2, AppTheme.Text2);
        btnRefresh.Click += (s, e) => LoadData();

        pnlTop.Controls.Add(lblSummary);
        pnlTop.Controls.Add(btnRefresh);
        pnlTop.Controls.Add(lblTitle);

        // ── Kanban columns: TableLayoutPanel fills the whole window ──────
        var tblCols = new TableLayoutPanel
        {
            Dock        = DockStyle.Fill,
            ColumnCount = Stages.Length,
            RowCount    = 1,
            BackColor   = AppTheme.Bg0,
            Padding     = new Padding(8, 8, 8, 8),
            CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        };
        tblCols.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        for (int i = 0; i < Stages.Length; i++)
            tblCols.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / Stages.Length));

        for (int i = 0; i < Stages.Length; i++)
        {
            var col = BuildColumn(i, Stages[i]);
            tblCols.Controls.Add(col, i, 0);
        }

        Controls.Add(tblCols);
        Controls.Add(pnlTop);

        Load += (s, e) => LoadData();
    }

    // ════════════════════════════════════════════════════════════════════
    //  BUILD ONE COLUMN
    // ════════════════════════════════════════════════════════════════════
    private Panel BuildColumn(int idx, (string Status, string Title, Color Accent) stage)
    {
        var col = new Panel
        {
            Dock      = DockStyle.Fill,
            BackColor = AppTheme.Bg1,
            Padding   = new Padding(ColPadding),
            Margin    = new Padding(idx == 0 ? 0 : 6, 0, 0, 0)
        };
        col.Paint += (s, e) =>
        {
            using var pen = new Pen(AppTheme.Border2);
            e.Graphics.DrawRectangle(pen, 0, 0, col.Width - 1, col.Height - 1);
            using var brush = new SolidBrush(stage.Accent);
            e.Graphics.FillRectangle(brush, 0, 0, col.Width, 3);
        };

        // ── Header ────────────────────────────────────────────────────────
        var pnlHeader = new Panel { Dock = DockStyle.Top, Height = 54, BackColor = Color.Transparent };

        var lblColTitle = new Label
        {
            Text = stage.Title, Font = AppTheme.FontBold, ForeColor = AppTheme.TextPrimary,
            Location = new Point(0, 4), AutoSize = false, Width = 220, Height = 22
        };
        _colCount[idx] = new Label
        {
            Text = "0", Font = AppTheme.FontSmall, ForeColor = AppTheme.TextMuted,
            Location = new Point(0, 26), AutoSize = true
        };

        var headerControls = new List<Control> { lblColTitle, _colCount[idx] };

        // "＋ Tambah" button — right-aligned using Anchor
        if (idx == 0)
        {
            var btnAdd = new Button
            {
                Text      = "＋ Tambah",
                Anchor    = AnchorStyles.Top | AnchorStyles.Right,
                Width     = 80, Height    = 26,
                Font      = new Font("Segoe UI", 8f),
                FlatStyle = FlatStyle.Flat,
                BackColor = stage.Accent, ForeColor = Color.White,
                Cursor    = Cursors.Hand
            };
            // position on Layout so it sticks to right edge of header
            pnlHeader.Layout += (s, e) =>
                btnAdd.Location = new Point(pnlHeader.Width - btnAdd.Width - 4, 14);
            btnAdd.FlatAppearance.BorderSize = 0;
            btnAdd.Click += (s, e) => OpenAddQueueDialog();
            headerControls.Add(btnAdd);
        }
        pnlHeader.Controls.AddRange(headerControls.ToArray());

        // ── Scrollable card area ──────────────────────────────────────────
        // Uses a plain Panel with Dock=Top cards; AutoScroll handles overflow.
        _colFlows[idx] = new Panel
        {
            Dock       = DockStyle.Fill,
            AutoScroll = true,
            BackColor  = Color.Transparent,
            Padding    = new Padding(0, 4, 0, 4)
        };

        col.Controls.Add(_colFlows[idx]);
        col.Controls.Add(pnlHeader);
        return col;
    }

    // ════════════════════════════════════════════════════════════════════
    //  LOAD DATA
    // ════════════════════════════════════════════════════════════════════
    private void LoadData()
    {
        try
        {
            var ests = _context.Estimations
                .AsNoTracking()
                .Include(e => e.Details)
                .OrderByDescending(e => e.CreatedDate)
                .ToList();

            // Update summary
            var total  = ests.Count;
            var antri  = ests.Count(e => e.Status == "Antri Dihitung");
            var draft  = ests.Count(e => e.Status == "Draft");
            var tunggu = ests.Count(e => e.Status == "Tunggu Approved");
            var ok     = ests.Count(e => e.Status == "Approved");
            lblSummary.Text = $"Total: {total}  |  Antri: {antri}  |  Draft: {draft}  |  Tunggu: {tunggu}  |  Approved: {ok}";

            // Clear columns
            foreach (var f in _colFlows) f.Controls.Clear();

            // Distribute to columns
            var stageSet = new HashSet<string>(Stages.Select(s => s.Status));
            foreach (var est in ests)
            {
                int colIdx = Array.FindIndex(Stages, s => s.Status == est.Status);
                if (colIdx < 0) continue;   // status lama (Draft, Sent, dll) → tidak ditampilkan

                var card = BuildCard(est, colIdx);
                _colFlows[colIdx].Controls.Add(card);
            }

            // Update counts
            for (int i = 0; i < Stages.Length; i++)
            {
                int cnt = _colFlows[i].Controls.Count;
                _colCount[i].Text = cnt == 0 ? "Kosong" : $"{cnt} estimasi";
            }

        }
        catch (Exception ex)
        {
            lblSummary.Text = "Gagal memuat: " + ex.Message;
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  BUILD CARD
    // ════════════════════════════════════════════════════════════════════
    private Panel BuildCard(Estimation est, int colIdx)
    {
        var accent = Stages[colIdx].Accent;

        // Dock=Top so the card stretches to full column width automatically.
        // Height 128 = 120 content + 8 invisible bottom gap.
        var card = new Panel
        {
            Dock      = DockStyle.Top,
            Height    = 128,
            BackColor = AppTheme.BgElev,
            Cursor    = Cursors.Hand,
            Tag       = est
        };
        card.Paint += (s, e) =>
        {
            using var pen = new Pen(AppTheme.Border2);
            e.Graphics.DrawRectangle(pen, 0, 0, card.Width - 1, card.Height - 9);  // leave gap visible
            using var brush = new SolidBrush(accent);
            e.Graphics.FillRectangle(brush, 0, 0, 3, card.Height - 9);
        };

        // EST number
        var lblNo = new Label
        {
            Text      = est.EstimationNumber,
            Font      = new Font("Segoe UI", 7.5f, FontStyle.Regular),
            ForeColor = AppTheme.TextMuted,
            Location  = new Point(10, 6),
            AutoSize  = true
        };

        // Client name (bold) – stretches with card width
        var lblClient = new Label
        {
            Text      = est.ClientName,
            Font      = AppTheme.FontBold,
            ForeColor = AppTheme.TextPrimary,
            Location  = new Point(10, 22),
            AutoSize  = false,
            Anchor    = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            Width     = 300,
            Height    = 18
        };

        // Company
        var lblCompany = new Label
        {
            Text      = est.Company ?? "",
            Font      = AppTheme.FontSmall,
            ForeColor = AppTheme.TextSecondary,
            Location  = new Point(10, 40),
            AutoSize  = false,
            Anchor    = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            Width     = 300,
            Height    = 15
        };

        // Project name
        var lblProject = new Label
        {
            Text      = est.ProjectName ?? "",
            Font      = AppTheme.FontSmall,
            ForeColor = AppTheme.TextMuted,
            Location  = new Point(10, 55),
            AutoSize  = false,
            Anchor    = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            Width     = 300,
            Height    = 15
        };

        // Total
        var lblTotal = new Label
        {
            Text      = FmtRp(est.TotalPrice),
            Font      = new Font("Segoe UI", 9f, FontStyle.Bold),
            ForeColor = accent,
            Location  = new Point(10, 72),
            AutoSize  = true
        };

        // Date
        var lblDate = new Label
        {
            Text      = est.CreatedDate.ToLocalTime().ToString("dd MMM yyyy"),
            Font      = new Font("Segoe UI", 7.5f),
            ForeColor = AppTheme.TextMuted,
            Location  = new Point(10, 90),
            AutoSize  = true
        };

        // ── Action buttons (right-aligned via Layout event) ────────────
        var btnBuka = new Button
        {
            Text      = "Buka",
            Width     = 50, Height = 22,
            Font      = new Font("Segoe UI", 7.5f),
            FlatStyle = FlatStyle.Flat,
            BackColor = AppTheme.Primary,
            ForeColor = Color.White,
            Cursor    = Cursors.Hand
        };
        btnBuka.FlatAppearance.BorderSize = 0;
        btnBuka.Click += (s, e) =>
        {
            SelectedEstimation = est;
            DialogResult       = DialogResult.OK;
            Close();
        };

        bool isLast = colIdx >= Stages.Length - 1;
        var btnMaju = new Button
        {
            Text      = isLast ? "—" : "→ Maju",
            Width     = 55, Height = 22,
            Font      = new Font("Segoe UI", 7.5f),
            FlatStyle = FlatStyle.Flat,
            BackColor = isLast ? AppTheme.Bg2 : AppTheme.Success500,
            ForeColor = isLast ? AppTheme.Text3 : Color.White,
            Cursor    = isLast ? Cursors.Default : Cursors.Hand,
            Enabled   = !isLast
        };
        btnMaju.FlatAppearance.BorderSize = 0;
        if (!isLast)
        {
            int nextIdx    = colIdx + 1;
            string nextSts = Stages[nextIdx].Status;
            btnMaju.Click += (s, e) => MoveToStage(est, nextSts);
        }

        // Keep buttons pinned to bottom-right as card width changes
        card.Layout += (s, e) =>
        {
            btnBuka.Location = new Point(card.Width - 116, 90);
            btnMaju.Location = new Point(card.Width -  61, 90);
        };

        card.Controls.AddRange(new Control[]
        {
            lblNo, lblClient, lblCompany, lblProject, lblTotal, lblDate, btnBuka, btnMaju
        });

        // Tooltip
        var tip = new ToolTip();
        tip.SetToolTip(card, $"{est.EstimationNumber}\n{est.ClientName}\n{FmtRp(est.TotalPrice)}");

        return card;
    }

    // ════════════════════════════════════════════════════════════════════
    //  MOVE TO NEXT STAGE
    // ════════════════════════════════════════════════════════════════════
    private void MoveToStage(Estimation est, string newStatus)
    {
        try
        {
            // Refresh from DB to avoid concurrency issues
            var tracked = _context.Estimations.Find(est.EstimationId);
            if (tracked == null) return;

            tracked.Status = newStatus;
            _context.SaveChanges();
            LoadData();

            SetStatus($"'{est.ClientName}' dipindahkan ke '{newStatus}'.");
        }
        catch (Exception ex)
        {
            MessageBox.Show("Gagal memindahkan: " + ex.Message, "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  TAMBAH ANTRI HITUNG
    // ════════════════════════════════════════════════════════════════════
    private void OpenAddQueueDialog()
    {
        using var dlg = new AddQueueDialog();
        if (dlg.ShowDialog() != DialogResult.OK) return;

        try
        {
            // Auto-generate estimation number
            var today  = DateTime.Now;
            var same   = _context.Estimations
                .Where(e => e.CreatedDate >= today.Date.ToUniversalTime()
                         && e.CreatedDate <  today.Date.AddDays(1).ToUniversalTime())
                .Count();
            var seq    = (same + 1).ToString("D3");
            var estNo  = $"EST-{today:yyyyMMdd}-{seq}";

            var est = new Estimation
            {
                EstimationNumber   = estNo,
                ClientName         = dlg.ClientName,
                Company            = dlg.CompanyName,
                Notes              = dlg.Notes,
                EstimatedOrderDate = dlg.EstOrderDate,
                Status             = "Antri Dihitung",
                CreatedDate        = today.ToUniversalTime(),
                SubTotal           = 0,
                Margin             = 0,
                MarginPercent      = 0,
                ShippingCost       = 0,
                Tax                = 0,
                TotalPrice         = 0
            };

            _context.Estimations.Add(est);
            _context.SaveChanges();

            LoadData();
            SetStatus($"'{dlg.ClientName}' ditambahkan ke Antri Dihitung ({estNo}).");
        }
        catch (Exception ex)
        {
            MessageBox.Show("Gagal menyimpan: " + ex.Message, "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void SetStatus(string msg)
    {
        // flash status in summary label briefly
        var old = lblSummary.Text;
        lblSummary.ForeColor = AppTheme.Success;
        lblSummary.Text      = "✔ " + msg;
        var t = new System.Windows.Forms.Timer { Interval = 2500 };
        t.Tick += (s, e) =>
        {
            t.Stop();
            lblSummary.ForeColor = AppTheme.TextSecondary;
            lblSummary.Text      = old;
        };
        t.Start();
    }

    private static string FmtRp(decimal v)
        => "Rp " + v.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("id-ID"));
}

// ════════════════════════════════════════════════════════════════════════
//  Dialog: Tambah Estimasi
// ════════════════════════════════════════════════════════════════════════
public class AddQueueDialog : Form
{
    public string   ClientName  { get; private set; } = "";
    public string?  CompanyName { get; private set; }
    public string?  Notes       { get; private set; }
    public DateTime? EstOrderDate { get; private set; }

    private TextBox        _txtClient  = null!;
    private TextBox        _txtCompany = null!;
    private TextBox        _txtNotes   = null!;
    private DateTimePicker _dtpOrder   = null!;
    private CheckBox       _chkDate    = null!;

    public AddQueueDialog()
    {
        Text            = "Tambah Estimasi Draft";
        Size            = new Size(420, 340);
        MinimumSize     = new Size(380, 320);
        StartPosition   = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox     = false;
        BackColor       = AppTheme.Background;

        // ── Fields ────────────────────────────────────────────────────
        var lblClient = AppTheme.MakeLabel("Nama Klien *", AppTheme.FontSmall, AppTheme.TextSecondary);
        lblClient.Location = new Point(20, 20); lblClient.AutoSize = true;
        _txtClient = new TextBox { Location = new Point(20, 38), Width = 360, PlaceholderText = "Nama klien..." };
        AppTheme.StyleTextBox(_txtClient);

        var lblCompany = AppTheme.MakeLabel("Perusahaan", AppTheme.FontSmall, AppTheme.TextSecondary);
        lblCompany.Location = new Point(20, 72); lblCompany.AutoSize = true;
        _txtCompany = new TextBox { Location = new Point(20, 90), Width = 360, PlaceholderText = "Nama perusahaan (opsional)..." };
        AppTheme.StyleTextBox(_txtCompany);

        var lblNotes = AppTheme.MakeLabel("Keterangan / Deskripsi Pekerjaan", AppTheme.FontSmall, AppTheme.TextSecondary);
        lblNotes.Location = new Point(20, 124); lblNotes.AutoSize = true;
        _txtNotes = new TextBox
        {
            Location    = new Point(20, 142),
            Width       = 360,
            Height      = 54,
            Multiline   = true,
            ScrollBars  = ScrollBars.Vertical,
            PlaceholderText = "Deskripsi pekerjaan yang perlu dihitung..."
        };
        AppTheme.StyleTextBox(_txtNotes);

        _chkDate = new CheckBox
        {
            Text      = "Estimasi tanggal order:",
            Location  = new Point(20, 204),
            AutoSize  = true,
            Font      = AppTheme.FontSmall,
            ForeColor = AppTheme.Text2
        };
        _dtpOrder = new DateTimePicker
        {
            Location = new Point(180, 200),
            Width    = 200,
            Format   = DateTimePickerFormat.Short,
            Enabled  = false
        };
        _chkDate.CheckedChanged += (s, e) => _dtpOrder.Enabled = _chkDate.Checked;

        // ── Buttons ───────────────────────────────────────────────────
        var btnOk = new Button { Text = "＋ Tambahkan", Location = new Point(20, 244), Width = 150, Height = 36 };
        AppTheme.StyleButton(btnOk, AppTheme.Primary, Color.White);
        btnOk.Click += (s, e) =>
        {
            if (string.IsNullOrWhiteSpace(_txtClient.Text))
            {
                MessageBox.Show("Nama klien tidak boleh kosong.", "Perhatian",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _txtClient.Focus();
                return;
            }
            ClientName   = _txtClient.Text.Trim();
            CompanyName  = string.IsNullOrWhiteSpace(_txtCompany.Text) ? null : _txtCompany.Text.Trim();
            Notes        = string.IsNullOrWhiteSpace(_txtNotes.Text)   ? null : _txtNotes.Text.Trim();
            EstOrderDate = _chkDate.Checked ? _dtpOrder.Value.ToUniversalTime() : null;
            DialogResult = DialogResult.OK;
            Close();
        };

        var btnCancel = new Button { Text = "Batal", Location = new Point(182, 244), Width = 90, Height = 36 };
        AppTheme.StyleButton(btnCancel, AppTheme.Bg2, AppTheme.Text2);
        btnCancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };

        Controls.AddRange(new Control[]
        {
            lblClient, _txtClient,
            lblCompany, _txtCompany,
            lblNotes, _txtNotes,
            _chkDate, _dtpOrder,
            btnOk, btnCancel
        });
    }
}
