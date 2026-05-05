using PanelCalculator.Core.Models;
using PanelCalculator.Core.Services;
using PanelCalculator.Data;
using PanelCalculator.Data.Repositories;
using PanelCalculator.WinForms.Theme;

namespace PanelCalculator.WinForms.Forms;

/// <summary>
/// Application shell: top title bar + left sidebar navigation + content area.
/// Hosts DashboardPanel, MainForm (embedded), and UserManagementForm.
/// </summary>
public class ShellForm : Form
{
    private readonly PanelCalculatorContext    _context;
    private readonly IProductRepository        _productRepo;
    private readonly IEstimationRepository     _estimationRepo;
    private readonly ICalculationService       _calcService;

    public User CurrentUser { get; set; } = null!;

    // ── Layout ───────────────────────────────────────────────────────────
    private Panel  pnlSidebar   = null!;
    private Panel  pnlContent   = null!;

    // ── Sidebar buttons ──────────────────────────────────────────────────
    private Button btnNavDashboard  = null!;
    private Button btnNavCalc       = null!;
    private Button btnNavHistory    = null!;
    private Button btnNavUsers      = null!;
    private Button btnNavSettings   = null!;
    private Button btnNavLogout     = null!;
    private Button? _activeNav      = null;

    // ── Cached panels ────────────────────────────────────────────────────
    private Panel?              _dashboardPanel  = null;
    private MainForm?           _calcForm        = null;
    private UserManagementForm? _usersForm       = null;

    public ShellForm(
        PanelCalculatorContext    context,
        IProductRepository        productRepo,
        IEstimationRepository     estimationRepo,
        ICalculationService       calcService)
    {
        _context        = context;
        _productRepo    = productRepo;
        _estimationRepo = estimationRepo;
        _calcService    = calcService;

        BuildUI();
    }

    // ════════════════════════════════════════════════════════════════════
    //  BUILD SHELL
    // ════════════════════════════════════════════════════════════════════
    private void BuildUI()
    {
        Text            = "Kalkulator Panel Tritunggal Swarna";
        Size            = new Size(1380, 840);
        MinimumSize     = new Size(1150, 700);
        StartPosition   = FormStartPosition.CenterScreen;
        BackColor       = AppTheme.Background;

        // ── Top title bar ────────────────────────────────────────────────
        var pnlTopBar = new Panel
        {
            Dock      = DockStyle.Top,
            Height    = 48,
            BackColor = AppTheme.Primary
        };
        pnlTopBar.Paint += (s, e) =>
        {
            using var pen = new Pen(Color.FromArgb(29, 78, 216));
            e.Graphics.DrawLine(pen, 0, pnlTopBar.Height - 1, pnlTopBar.Width, pnlTopBar.Height - 1);
        };

        var lblAppTitle = new Label
        {
            Text      = "⚡  Kalkulator Panel Tritunggal Swarna",
            Font      = new Font("Segoe UI", 12f, FontStyle.Bold),
            ForeColor = Color.White,
            AutoSize  = false,
            Dock      = DockStyle.Left,
            Width     = 400,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding   = new Padding(16, 0, 0, 0)
        };

        var lblUserInfo = new Label
        {
            Text      = "",     // set in ShellForm_Load
            Font      = AppTheme.FontBase,
            ForeColor = Color.FromArgb(190, 215, 255),
            AutoSize  = false,
            Dock      = DockStyle.Right,
            Width     = 260,
            TextAlign = ContentAlignment.MiddleRight,
            Padding   = new Padding(0, 0, 16, 0)
        };

        pnlTopBar.Controls.Add(lblUserInfo);
        pnlTopBar.Controls.Add(lblAppTitle);

        // ── Sidebar ──────────────────────────────────────────────────────
        pnlSidebar = new Panel
        {
            Dock      = DockStyle.Left,
            Width     = 210,
            BackColor = Color.FromArgb(30, 41, 59),   // slate-800
            Padding   = new Padding(0, 8, 0, 8)
        };
        pnlSidebar.Paint += (s, e) =>
        {
            using var pen = new Pen(Color.FromArgb(51, 65, 85));
            e.Graphics.DrawLine(pen, pnlSidebar.Width - 1, 0, pnlSidebar.Width - 1, pnlSidebar.Height);
        };

        // ── Content area ─────────────────────────────────────────────────
        pnlContent = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.Background };

        // ── Build sidebar nav buttons ─────────────────────────────────────
        btnNavDashboard = MakeNavBtn("🏠  Dashboard",    0);
        btnNavCalc      = MakeNavBtn("🧮  Kalkulator",   1);
        btnNavHistory   = MakeNavBtn("📋  Riwayat",      2);
        btnNavUsers     = MakeNavBtn("👥  Pengguna",     3);
        btnNavSettings  = MakeNavBtn("⚙  Pengaturan",   4);
        btnNavLogout    = MakeNavBtn("🚪  Keluar",       5, isBottom: true);

        btnNavDashboard.Click += (s, e) => Navigate("Dashboard");
        btnNavCalc.Click      += (s, e) => Navigate("Calc");
        btnNavHistory.Click   += (s, e) => Navigate("History");
        btnNavUsers.Click     += (s, e) => Navigate("Users");
        btnNavSettings.Click  += (s, e) => Navigate("Settings");
        btnNavLogout.Click    += BtnLogout_Click;

        pnlSidebar.Controls.AddRange(new Control[]
        {
            btnNavDashboard, btnNavCalc, btnNavHistory, btnNavUsers, btnNavSettings, btnNavLogout
        });

        Controls.Add(pnlContent);
        Controls.Add(pnlSidebar);
        Controls.Add(pnlTopBar);

        Load += (s, e) =>
        {
            lblUserInfo.Text = $"{CurrentUser.FullName}  ({CurrentUser.Role})";
            // Hide user management for non-admins
            btnNavUsers.Visible = (CurrentUser.Role == "Admin");
            Navigate("Dashboard");
        };
    }

    // ════════════════════════════════════════════════════════════════════
    //  NAV BUTTON FACTORY
    // ════════════════════════════════════════════════════════════════════
    private Button MakeNavBtn(string text, int index, bool isBottom = false)
    {
        var btn = new Button
        {
            Text      = text,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.Transparent,
            ForeColor = Color.FromArgb(148, 163, 184),  // slate-400
            Font      = new Font("Segoe UI", 9.5f, FontStyle.Regular),
            TextAlign = ContentAlignment.MiddleLeft,
            Padding   = new Padding(16, 0, 0, 0),
            Cursor    = Cursors.Hand,
            Height    = 44,
            Width     = 210,
            Left      = 0,
        };
        btn.FlatAppearance.BorderSize     = 0;
        btn.FlatAppearance.MouseOverBackColor  = Color.FromArgb(51, 65, 85);
        btn.FlatAppearance.MouseDownBackColor  = Color.FromArgb(71, 85, 105);

        if (!isBottom)
            btn.Top = 8 + index * 48;
        else
        {
            btn.Dock = DockStyle.Bottom;
        }

        return btn;
    }

    // ════════════════════════════════════════════════════════════════════
    //  NAVIGATION
    // ════════════════════════════════════════════════════════════════════
    private void SetActiveNav(Button btn)
    {
        // Reset previous
        if (_activeNav != null)
        {
            _activeNav.BackColor = Color.Transparent;
            _activeNav.ForeColor = Color.FromArgb(148, 163, 184);
            _activeNav.Font      = new Font("Segoe UI", 9.5f, FontStyle.Regular);
        }
        btn.BackColor = Color.FromArgb(37, 99, 235);   // primary blue
        btn.ForeColor = Color.White;
        btn.Font      = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        _activeNav    = btn;
    }

    private void Navigate(string page)
    {
        // Hide all hosted panels/forms
        foreach (Control c in pnlContent.Controls)
            c.Visible = false;

        switch (page)
        {
            case "Dashboard":
                SetActiveNav(btnNavDashboard);
                ShowDashboard();
                break;

            case "Calc":
                SetActiveNav(btnNavCalc);
                ShowCalcForm();
                break;

            case "History":
                SetActiveNav(btnNavHistory);
                ShowCalcForm();   // reuse calc form, open history dialog
                OpenHistoryDialog();
                break;

            case "Users":
                if (CurrentUser.Role != "Admin") return;
                SetActiveNav(btnNavUsers);
                ShowUsersForm();
                break;

            case "Settings":
                SetActiveNav(btnNavSettings);
                ShowCalcForm();   // settings lives inside calc form for now
                break;
        }
    }

    // ── Dashboard ────────────────────────────────────────────────────────
    private void ShowDashboard()
    {
        if (_dashboardPanel == null)
            _dashboardPanel = BuildDashboard();

        if (!pnlContent.Controls.Contains(_dashboardPanel))
            pnlContent.Controls.Add(_dashboardPanel);

        _dashboardPanel.Dock    = DockStyle.Fill;
        _dashboardPanel.Visible = true;
        RefreshDashboard(_dashboardPanel);
    }

    private Panel BuildDashboard()
    {
        var pnl = new Panel { BackColor = AppTheme.Background, Padding = new Padding(24) };

        var flow = new FlowLayoutPanel
        {
            Dock      = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents  = false,
            AutoScroll    = true,
            Padding       = new Padding(0)
        };

        // ── Greeting ─────────────────────────────────────────────────────
        var lblGreet = new Label
        {
            Text      = "",     // filled in Refresh
            Font      = AppTheme.FontTitle,
            ForeColor = AppTheme.TextPrimary,
            AutoSize  = true,
            Margin    = new Padding(0, 0, 0, 4)
        };
        lblGreet.Name = "lblGreet";

        var lblDate = AppTheme.MakeLabel(
            DateTime.Now.ToString("dddd, dd MMMM yyyy",
                System.Globalization.CultureInfo.GetCultureInfo("id-ID")),
            AppTheme.FontBase, AppTheme.TextSecondary);
        lblDate.Margin = new Padding(0, 0, 0, 24);

        // ── Stats row ─────────────────────────────────────────────────────
        var pnlStats = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents  = false,
            AutoSize      = true,
            Margin        = new Padding(0, 0, 0, 24)
        };
        pnlStats.Name = "pnlStats";

        flow.Controls.AddRange(new Control[] { lblGreet, lblDate, pnlStats });
        pnl.Controls.Add(flow);
        return pnl;
    }

    private void RefreshDashboard(Panel pnl)
    {
        try
        {
            var flow    = (FlowLayoutPanel)pnl.Controls[0];
            var lblGreet = flow.Controls.Find("lblGreet", false).FirstOrDefault() as Label;
            var pnlStats = flow.Controls.Find("pnlStats", false).FirstOrDefault() as FlowLayoutPanel;
            if (lblGreet != null)
                lblGreet.Text = $"Selamat datang, {CurrentUser.FullName} 👋";

            if (pnlStats == null) return;
            pnlStats.Controls.Clear();

            // Stats
            var estimations  = _context.Estimations.ToList();
            var products     = _context.Products.Count();
            var users        = _context.Users.Count();

            int total   = estimations.Count;
            int draft   = estimations.Count(e => e.Status == "Draft");
            int won     = estimations.Count(e => e.Status == "Won");
            int sent    = estimations.Count(e => e.Status == "Sent");
            decimal revenue = estimations.Where(e => e.Status == "Won").Sum(e => e.TotalPrice);

            pnlStats.Controls.Add(MakeStat("📊  Total Estimasi",  total.ToString(),          AppTheme.Primary));
            pnlStats.Controls.Add(MakeStat("✏  Draft",           draft.ToString(),          AppTheme.TextSecondary));
            pnlStats.Controls.Add(MakeStat("📤  Terkirim",        sent.ToString(),           AppTheme.Warning));
            pnlStats.Controls.Add(MakeStat("✅  Menang",          won.ToString(),            AppTheme.Success));
            pnlStats.Controls.Add(MakeStat("💰  Revenue Won",     FormatRp(revenue),         AppTheme.Success));
            pnlStats.Controls.Add(MakeStat("📦  Produk",          products.ToString(),       AppTheme.Primary));

            // Recent estimations
            var recent = estimations.OrderByDescending(e => e.CreatedDate).Take(10).ToList();
            if (recent.Any())
            {
                var lblRecent = AppTheme.MakeLabel("Estimasi Terbaru", AppTheme.FontBold, AppTheme.TextPrimary);
                lblRecent.Margin = new Padding(0, 8, 0, 8);
                flow.Controls.Remove(flow.Controls.Find("lblRecent", false).FirstOrDefault()!);
                flow.Controls.Remove(flow.Controls.Find("dgvRecent", false).FirstOrDefault()!);
                lblRecent.Name = "lblRecent";

                var dgv = new DataGridView
                {
                    Name            = "dgvRecent",
                    ReadOnly        = true,
                    AllowUserToAddRows = false,
                    Height          = 280,
                    Width           = 860,
                    Margin          = new Padding(0)
                };
                AppTheme.StyleGrid(dgv);
                dgv.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "No. Estimasi",  FillWeight = 20 });
                dgv.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Klien",         FillWeight = 30 });
                dgv.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Tanggal",       FillWeight = 16 });
                dgv.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Status",        FillWeight = 12 });
                dgv.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Total",         FillWeight = 22,
                    DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleRight } });

                foreach (var est in recent)
                {
                    int ri = dgv.Rows.Add(
                        est.EstimationNumber,
                        est.ClientName,
                        est.CreatedDate.ToLocalTime().ToString("dd MMM yyyy"),
                        est.Status,
                        FormatRp(est.TotalPrice));
                    dgv.Rows[ri].Cells[3].Style.ForeColor = est.Status switch
                    {
                        "Won"      => AppTheme.Success,
                        "Lost"     => AppTheme.Danger,
                        "Approved" => AppTheme.Primary,
                        "Sent"     => AppTheme.Warning,
                        _          => AppTheme.TextSecondary
                    };
                }

                flow.Controls.Add(lblRecent);
                flow.Controls.Add(dgv);
            }
        }
        catch { /* ignore on empty db */ }
    }

    private static Panel MakeStat(string label, string value, Color accent)
    {
        var card = new Panel
        {
            Size      = new Size(180, 90),
            BackColor = Color.White,
            Margin    = new Padding(0, 0, 16, 0),
            Padding   = new Padding(14, 10, 14, 10)
        };
        card.Paint += (s, e) =>
        {
            using var pen = new Pen(AppTheme.Border);
            var r = new Rectangle(0, 0, card.Width - 1, card.Height - 1);
            e.Graphics.DrawRectangle(pen, r);
            // left accent bar
            using var brush = new SolidBrush(accent);
            e.Graphics.FillRectangle(brush, 0, 0, 4, card.Height);
        };

        var lblVal = new Label
        {
            Text      = value,
            Font      = new Font("Segoe UI", 15f, FontStyle.Bold),
            ForeColor = AppTheme.TextPrimary,
            AutoSize  = false,
            Size      = new Size(152, 36),
            Location  = new Point(14, 14),
            TextAlign = ContentAlignment.MiddleLeft
        };
        var lblLbl = new Label
        {
            Text      = label,
            Font      = AppTheme.FontSmall,
            ForeColor = AppTheme.TextSecondary,
            AutoSize  = false,
            Size      = new Size(152, 20),
            Location  = new Point(14, 52)
        };
        card.Controls.AddRange(new Control[] { lblVal, lblLbl });
        return card;
    }

    // ── Calculator (embedded) ─────────────────────────────────────────────
    private void ShowCalcForm()
    {
        if (_calcForm == null)
        {
            _calcForm = new MainForm(_context, _productRepo, _estimationRepo, _calcService);
            _calcForm.TopLevel       = false;
            _calcForm.FormBorderStyle = FormBorderStyle.None;
            _calcForm.Dock           = DockStyle.Fill;
            _calcForm.Visible        = false;
            pnlContent.Controls.Add(_calcForm);
            _calcForm.Show();
        }
        _calcForm.Visible = true;
    }

    private void OpenHistoryDialog()
    {
        _calcForm?.OpenHistoryFromShell();
    }

    // ── Users management ──────────────────────────────────────────────────
    private void ShowUsersForm()
    {
        if (_usersForm == null)
        {
            _usersForm = new UserManagementForm(_context);
            _usersForm.TopLevel        = false;
            _usersForm.FormBorderStyle = FormBorderStyle.None;
            _usersForm.Dock            = DockStyle.Fill;
            _usersForm.Visible         = false;
            pnlContent.Controls.Add(_usersForm);
            _usersForm.Show();
        }
        _usersForm.Visible = true;
        _usersForm.ReloadAsync();
    }

    // ── Logout ────────────────────────────────────────────────────────────
    private void BtnLogout_Click(object? sender, EventArgs e)
    {
        var confirm = MessageBox.Show("Keluar dari aplikasi?", "Konfirmasi",
            MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (confirm == DialogResult.Yes)
            Application.Restart();
    }

    private static string FormatRp(decimal v)
        => "Rp " + v.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("id-ID"));
}
