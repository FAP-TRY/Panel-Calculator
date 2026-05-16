using PanelCalculator.Core.Models;
using PanelCalculator.Core.Services;
using PanelCalculator.Data;
using PanelCalculator.Data.Repositories;
using PanelCalculator.WinForms.Services;
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
    private Button   _btnUpdate  = null!;   // update-available notification

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
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode       = AutoScaleMode.Dpi;

        Text          = "Kalkulator Panel Tritunggal Swarna";
        MinimumSize   = new Size(1150, 700);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor     = AppTheme.Background;

        // Adapt initial window size to screen — start maximized on small screens
        var screen = Screen.FromPoint(Cursor.Position).WorkingArea;
        if (screen.Width < 1400 || screen.Height < 800)
            WindowState = FormWindowState.Maximized;
        else
            Size = new Size(Math.Min(1380, screen.Width - 40), Math.Min(840, screen.Height - 40));

        // ── Top title bar ────────────────────────────────────────────────
        var pnlTopBar = new Panel
        {
            Dock      = DockStyle.Top,
            Height    = 48,
            BackColor = AppTheme.BgHeader
        };
        pnlTopBar.Paint += (s, e) =>
        {
            using var pen = new Pen(AppTheme.Border2);
            e.Graphics.DrawLine(pen, 0, pnlTopBar.Height - 1, pnlTopBar.Width, pnlTopBar.Height - 1);
        };

        // ── Logo image in top bar ────────────────────────────────────────
        var pbTopLogo = new PictureBox
        {
            Size     = new Size(32, 32),
            Location = new Point(12, (pnlTopBar.Height - 32) / 2),
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.Transparent,
        };
        using (var stream = typeof(ShellForm).Assembly
                   .GetManifestResourceStream("PanelCalculator.WinForms.Assets.logo.png"))
        {
            if (stream != null)
                pbTopLogo.Image = Image.FromStream(stream);
        }

        var lblAppTitle = new Label
        {
            Text      = "",
            AutoSize  = false,
            Dock      = DockStyle.Left,
            Width     = 52,
        };

        // ── Right-side buttons in top bar ────────────────────────────────
        var pnlRight = new Panel
        {
            Dock      = DockStyle.Right,
            Width     = 240,
            BackColor = Color.Transparent
        };

        // user info label  (no Dock — absolutely positioned in Layout so buttons stay on top)
        var lblUserInfo = new Label
        {
            Text      = "",     // set on Load
            Font      = AppTheme.FontBase,
            ForeColor = AppTheme.Text2,
            AutoSize  = false,
            TextAlign = ContentAlignment.MiddleRight,
            Padding   = new Padding(0, 0, 8, 0)
        };

        // Logout button
        var btnLogout = new Button
        {
            Text      = "🚪 Keluar",
            Width     = 100,
            Height    = 30,
            FlatStyle = FlatStyle.Flat,
            BackColor = AppTheme.Bg2,
            ForeColor = AppTheme.Text2,
            Font      = AppTheme.FontSmall,
            Cursor    = Cursors.Hand
        };
        btnLogout.FlatAppearance.BorderSize = 0;
        btnLogout.FlatAppearance.MouseOverBackColor = AppTheme.Danger500;  // red on hover
        btnLogout.Click += BtnLogout_Click;

        // Update-available notification button (hidden until an update is found)
        _btnUpdate = new Button
        {
            Text      = "",
            Width     = 0,   // invisible until needed
            Height    = 30,
            Visible   = false,
            FlatStyle = FlatStyle.Flat,
            BackColor = AppTheme.Success500,
            ForeColor = Color.White,
            Font      = AppTheme.FontSmall,
            Cursor    = Cursors.Hand
        };
        _btnUpdate.FlatAppearance.BorderSize = 0;
        _btnUpdate.FlatAppearance.MouseOverBackColor = AppTheme.Success400;
        _btnUpdate.Click += BtnUpdate_Click;

        // Version label (shows current version on hover area)
        var lblVersion = new Label
        {
            Text      = $"v{UpdateService.AppVersion}",
            Font      = AppTheme.FontSmall,
            ForeColor = AppTheme.Text3,
            AutoSize  = false,
            TextAlign = ContentAlignment.MiddleRight,
            Width     = 56
        };

        // Position: logout → update → version → userinfo
        pnlRight.Layout += (s, e) =>
        {
            int cx = pnlRight.Width - 8;

            btnLogout.Top  = (pnlRight.Height - btnLogout.Height) / 2;
            btnLogout.Left = cx - btnLogout.Width;

            if (_btnUpdate.Visible)
            {
                _btnUpdate.Top  = (pnlRight.Height - _btnUpdate.Height) / 2;
                _btnUpdate.Left = btnLogout.Left - _btnUpdate.Width - 6;
                lblVersion.SetBounds(_btnUpdate.Left - lblVersion.Width - 4, 0,
                    lblVersion.Width, pnlRight.Height);
            }
            else
            {
                lblVersion.SetBounds(btnLogout.Left - lblVersion.Width - 4, 0,
                    lblVersion.Width, pnlRight.Height);
            }

            lblUserInfo.SetBounds(0, 0,
                Math.Max(4, lblVersion.Left - 4), pnlRight.Height);
        };

        pnlRight.Controls.AddRange(new Control[]
            { lblUserInfo, lblVersion, _btnUpdate, btnLogout });

        pnlTopBar.Controls.Add(pnlRight);
        pnlTopBar.Controls.Add(lblAppTitle);
        pnlTopBar.Controls.Add(pbTopLogo);

        // ── Content area ─────────────────────────────────────────────────
        pnlContent = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.Background };

        Controls.Add(pnlContent);
        Controls.Add(pnlTopBar);

        Load += (s, e) =>
        {
            lblUserInfo.Text = $"{CurrentUser.FullName}  ({CurrentUser.Role})";
            ShowCalcForm();

            // Background update check — never blocks the UI
            _ = CheckForUpdateAsync();
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

    // ── Auto-update ───────────────────────────────────────────────────────

    private UpdateService.ReleaseInfo? _pendingUpdate;

    private async Task CheckForUpdateAsync()
    {
        try
        {
            var info = await UpdateService.CheckAsync();
            if (info == null) return;

            _pendingUpdate = info;

            // Show notification on UI thread
            if (IsHandleCreated)
                Invoke(() =>
                {
                    _btnUpdate.Text    = $"🔄 Update v{info.Version}";
                    _btnUpdate.Width   = 130;
                    _btnUpdate.Visible = true;
                    _btnUpdate.Parent?.PerformLayout();
                });
        }
        catch { /* non-fatal */ }
    }

    private async void BtnUpdate_Click(object? sender, EventArgs e)
    {
        if (_pendingUpdate == null) return;

        var confirm = MessageBox.Show(
            $"Pembaruan  v{_pendingUpdate.Version}  tersedia!\n\n" +
            $"Aplikasi akan didownload, ditutup, dan diperbarui secara otomatis.\n" +
            $"Data di database Anda tidak akan terpengaruh.\n\n" +
            $"Lanjutkan update sekarang?",
            "Update Tersedia",
            MessageBoxButtons.YesNo, MessageBoxIcon.Information,
            MessageBoxDefaultButton.Button1);

        if (confirm != DialogResult.Yes) return;

        // Progress dialog — ControlBox=false removes the X button so user cannot
        // accidentally close it mid-download (which would cause silent autoclose later)
        using var prog = new Form
        {
            Text            = "Mengunduh Pembaruan...",
            Size            = new Size(440, 150),
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition   = FormStartPosition.CenterParent,
            MaximizeBox     = false,
            MinimizeBox     = false,
            ControlBox      = false,   // ← removes X button entirely
            BackColor       = AppTheme.Background
        };
        var bar = new ProgressBar { Dock = DockStyle.Top, Height = 28, Minimum = 0, Maximum = 100 };
        var lbl = new Label
        {
            Text      = "Memulai download...",
            Dock      = DockStyle.Fill,
            Font      = AppTheme.FontBase,
            ForeColor = AppTheme.Text2,
            TextAlign = ContentAlignment.MiddleCenter
        };
        prog.Controls.Add(lbl);
        prog.Controls.Add(bar);
        prog.Show(this);

        // Progress<T> already marshals the callback to the UI thread via
        // SynchronizationContext — no Application.DoEvents() needed (and DoEvents
        // is dangerous here because it can process unrelated close/resize events
        // that put the form into an inconsistent state mid-download).
        var progress = new Progress<(int Percent, string Status)>(p =>
        {
            if (prog.IsDisposed) return;
            bar.Value = Math.Clamp(p.Percent, 0, 100);
            lbl.Text  = p.Status;
        });

        try
        {
            _btnUpdate.Enabled = false;
            await UpdateService.DownloadAndApplyAsync(_pendingUpdate, progress);
            // DownloadAndApplyAsync calls Application.Exit() on success → never reaches here
        }
        catch (Exception ex)
        {
            if (!prog.IsDisposed) prog.Close();
            _btnUpdate.Enabled = true;
            MessageBox.Show(
                $"Download gagal:\n{ex.Message}\n\nSilakan download manual dari:\n{_pendingUpdate.HtmlUrl}",
                "Update Gagal", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

}
