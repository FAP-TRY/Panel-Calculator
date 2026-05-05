using PanelCalculator.Core.Models;
using PanelCalculator.Core.Services;
using PanelCalculator.Data;
using PanelCalculator.Data.Repositories;
using PanelCalculator.WinForms.Theme;

namespace PanelCalculator.WinForms.Forms;

/// <summary>
/// Application shell: top title bar + full-window content area (MainForm embedded).
/// No sidebar. Navigation lives in MainForm's toolbar.
/// </summary>
public class ShellForm : Form
{
    private readonly PanelCalculatorContext    _context;
    private readonly IProductRepository        _productRepo;
    private readonly IEstimationRepository     _estimationRepo;
    private readonly ICalculationService       _calcService;

    public User CurrentUser   { get; set; } = null!;

    /// <summary>True when the user explicitly clicked "Keluar" — Program.cs
    /// uses this to decide whether to show the login screen again.</summary>
    public bool WantsRelogin  { get; private set; }

    private Panel    pnlContent  = null!;
    private MainForm? _calcForm  = null;

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

    private void BuildUI()
    {
        Text          = "Kalkulator Panel Tritunggal Swarna";
        Size          = new Size(1380, 840);
        MinimumSize   = new Size(1150, 700);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor     = AppTheme.Background;

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
            Width     = 420,
            TextAlign = ContentAlignment.TopLeft,
            Padding   = new Padding(16, 12, 0, 0)
        };

        // ── Right-side buttons in top bar ────────────────────────────────
        var pnlRight = new Panel
        {
            Dock      = DockStyle.Right,
            Width     = 360,
            BackColor = Color.Transparent
        };

        // user info label  (no Dock — absolutely positioned in Layout so buttons stay on top)
        var lblUserInfo = new Label
        {
            Text      = "",     // set on Load
            Font      = AppTheme.FontBase,
            ForeColor = Color.FromArgb(190, 215, 255),
            AutoSize  = false,
            TextAlign = ContentAlignment.MiddleRight,
            Padding   = new Padding(0, 0, 8, 0)
        };

        // Pengguna button (admin only; hidden for operators)
        var btnUsers = new Button
        {
            Text      = "👥 Pengguna",
            Width     = 110,
            Height    = 30,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(55, 65, 81),
            ForeColor = Color.White,
            Font      = AppTheme.FontSmall,
            Cursor    = Cursors.Hand,
            Visible   = false   // shown on Load if Admin
        };
        btnUsers.FlatAppearance.BorderSize = 0;
        btnUsers.FlatAppearance.MouseOverBackColor = Color.FromArgb(75, 85, 99);
        btnUsers.Click += BtnUsers_Click;

        // Logout button
        var btnLogout = new Button
        {
            Text      = "🚪 Keluar",
            Width     = 100,
            Height    = 30,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(55, 65, 81),
            ForeColor = Color.White,
            Font      = AppTheme.FontSmall,
            Cursor    = Cursors.Hand
        };
        btnLogout.FlatAppearance.BorderSize = 0;
        btnLogout.FlatAppearance.MouseOverBackColor = Color.FromArgb(220, 38, 38);  // red on hover
        btnLogout.Click += BtnLogout_Click;

        // position buttons
        pnlRight.Layout += (s, e) =>
        {
            int cx = pnlRight.Width - 8;
            btnLogout.Top  = (pnlRight.Height - btnLogout.Height) / 2;
            btnLogout.Left = cx - btnLogout.Width;

            btnUsers.Top  = (pnlRight.Height - btnUsers.Height) / 2;
            btnUsers.Left = btnLogout.Left - btnUsers.Width - 6;

            lblUserInfo.SetBounds(0, 0, btnUsers.Left - 4, pnlRight.Height);
        };

        pnlRight.Controls.AddRange(new Control[] { lblUserInfo, btnUsers, btnLogout });

        pnlTopBar.Controls.Add(pnlRight);
        pnlTopBar.Controls.Add(lblAppTitle);

        // ── Content area ─────────────────────────────────────────────────
        pnlContent = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.Background };

        Controls.Add(pnlContent);
        Controls.Add(pnlTopBar);

        Load += (s, e) =>
        {
            lblUserInfo.Text    = $"{CurrentUser.FullName}  ({CurrentUser.Role})";
            btnUsers.Visible    = CurrentUser.Role == "Admin";
            ShowCalcForm();
        };
    }

    // ── Embed MainForm ────────────────────────────────────────────────────
    private void ShowCalcForm()
    {
        if (_calcForm == null)
        {
            _calcForm = new MainForm(_context, _productRepo, _estimationRepo, _calcService);
            _calcForm.CurrentUser      = CurrentUser;
            _calcForm.TopLevel         = false;
            _calcForm.FormBorderStyle  = FormBorderStyle.None;
            _calcForm.Dock             = DockStyle.Fill;
            _calcForm.Visible          = false;
            pnlContent.Controls.Add(_calcForm);
            _calcForm.Show();
        }
        _calcForm.CurrentUser = CurrentUser;
        _calcForm.Visible     = true;
    }

    // ── Users button ──────────────────────────────────────────────────────
    private void BtnUsers_Click(object? sender, EventArgs e)
    {
        if (CurrentUser.Role != "Admin") return;
        using var form = new UserManagementForm(_context);
        form.ShowDialog(this);
    }

    // ── Logout ────────────────────────────────────────────────────────────
    private void BtnLogout_Click(object? sender, EventArgs e)
    {
        var confirm = MessageBox.Show(
            $"Keluar dari akun  \"{CurrentUser.FullName}\"?\n\nAnda akan kembali ke halaman login.",
            "Konfirmasi Keluar",
            MessageBoxButtons.YesNo, MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button2);   // "No" is default (safer)

        if (confirm == DialogResult.Yes)
        {
            WantsRelogin = true;
            Close();
        }
    }

}
