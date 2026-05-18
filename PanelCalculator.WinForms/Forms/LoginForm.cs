using PanelCalculator.Core.Models;
using PanelCalculator.Core.Security;
using PanelCalculator.Data;
using PanelCalculator.WinForms.Theme;

namespace PanelCalculator.WinForms.Forms;

public class LoginForm : Form
{
    private readonly PanelCalculatorContext _context;

    public bool  LoginSuccess  { get; private set; }
    public User? LoggedInUser  { get; private set; }

    private TextBox txtUsername = null!;
    private TextBox txtPassword = null!;
    private Label   lblError    = null!;
    private Button  btnLogin    = null!;

    public LoginForm(PanelCalculatorContext context)
    {
        _context = context;
        BuildUI();
    }

    private void BuildUI()
    {
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode       = AutoScaleMode.Dpi;
        Text            = "Kalkulator Panel Tritunggal Swarna";
        Size            = new Size(440, 560);
        StartPosition   = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox     = false;
        BackColor       = AppTheme.Bg0;

        // ── Background gradient painted on the form ───────────────────────
        Paint += (s, e) =>
        {
            var g = e.Graphics;
            // Radial glow top-left (brand blue)
            using var pathTop = new System.Drawing.Drawing2D.GraphicsPath();
            pathTop.AddEllipse(-80, -80, 380, 380);
            using var brushTop = new System.Drawing.Drawing2D.PathGradientBrush(pathTop);
            brushTop.CenterColor   = Color.FromArgb(55, 42, 92, 255);
            brushTop.SurroundColors = new[] { Color.Transparent };
            g.FillPath(brushTop, pathTop);

            // Radial glow bottom-right (cyan)
            using var pathBot = new System.Drawing.Drawing2D.GraphicsPath();
            pathBot.AddEllipse(Width - 160, Height - 160, 300, 300);
            using var brushBot = new System.Drawing.Drawing2D.PathGradientBrush(pathBot);
            brushBot.CenterColor    = Color.FromArgb(40, 0, 194, 255);
            brushBot.SurroundColors = new[] { Color.Transparent };
            g.FillPath(brushBot, pathBot);
        };

        // ── Card panel ────────────────────────────────────────────────────
        var card = new Panel
        {
            Size      = new Size(364, 460),
            BackColor = AppTheme.BgElev,
        };
        // Centre card within form
        Resize += (s, e) =>
        {
            card.Location = new Point(
                (ClientSize.Width  - card.Width)  / 2,
                (ClientSize.Height - card.Height) / 2);
        };
        card.Location = new Point(38, 50);

        // Card border (custom paint)
        card.Paint += (s, e) =>
        {
            var g  = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var rect = new Rectangle(0, 0, card.Width - 1, card.Height - 1);
            using var pen = new Pen(AppTheme.BorderStrong);
            int r = 14;
            DrawRoundRect(g, pen, rect, r);
        };

        int y = 36;

        // ── Logo mark ─────────────────────────────────────────────────────
        const int logoSize = 80;
        var pbLogo = new PictureBox
        {
            Size     = new Size(logoSize, logoSize),
            Location = new Point((card.Width - logoSize) / 2, y),
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.Transparent,
        };
        using (var stream = typeof(LoginForm).Assembly
                   .GetManifestResourceStream("PanelCalculator.WinForms.Assets.logo.png"))
        {
            if (stream != null)
                pbLogo.Image = Image.FromStream(stream);
        }
        card.Controls.Add(pbLogo);
        y += logoSize + 10;

        // App title gradient text
        var lblTitle = new Label
        {
            Text      = "Kalkulator Panel",
            Font      = new Font("Segoe UI", 20f, FontStyle.Bold),
            ForeColor = AppTheme.Text1,
            TextAlign = ContentAlignment.MiddleCenter,
            AutoSize  = false,
            Size      = new Size(card.Width, 32),
            Location  = new Point(0, y),
        };
        card.Controls.Add(lblTitle);
        y += 36;

        var lblSub = new Label
        {
            Text      = "Tritunggal Swarna · Engineering Suite",
            Font      = AppTheme.FontSmall,
            ForeColor = AppTheme.Text3,
            TextAlign = ContentAlignment.MiddleCenter,
            AutoSize  = false,
            Size      = new Size(card.Width, 18),
            Location  = new Point(0, y),
        };
        card.Controls.Add(lblSub);
        y += 30;

        // Separator
        var sep = new Panel { Location = new Point(24, y), Size = new Size(card.Width - 48, 1), BackColor = AppTheme.Border2 };
        card.Controls.Add(sep);
        y += 18;

        // ── "Masuk ke Sistem" sub-heading ─────────────────────────────────
        var lblSignIn = new Label
        {
            Text      = "🔐  Masuk ke Sistem",
            Font      = AppTheme.FontBold,
            ForeColor = AppTheme.Text1,
            Location  = new Point(24, y),
            AutoSize  = true,
        };
        card.Controls.Add(lblSignIn);
        y += 22;

        var lblCred = new Label
        {
            Text      = "Gunakan kredensial yang diberikan administrator",
            Font      = AppTheme.FontSmall,
            ForeColor = AppTheme.Text3,
            Location  = new Point(24, y),
            AutoSize  = true,
        };
        card.Controls.Add(lblCred);
        y += 26;

        // Username
        var lblUser = AppTheme.MakeLabel("USERNAME", AppTheme.FontCaption, AppTheme.Text3);
        lblUser.Location = new Point(24, y);
        card.Controls.Add(lblUser);
        y += 16;

        txtUsername = new TextBox
        {
            Location        = new Point(24, y),
            Width           = card.Width - 48,
            PlaceholderText = "Masukkan username",
        };
        AppTheme.StyleTextBox(txtUsername);
        txtUsername.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; txtPassword.Focus(); } };
        card.Controls.Add(txtUsername);
        y += 34;

        // Password
        var lblPass = AppTheme.MakeLabel("PASSWORD", AppTheme.FontCaption, AppTheme.Text3);
        lblPass.Location = new Point(24, y);
        card.Controls.Add(lblPass);
        y += 16;

        txtPassword = new TextBox
        {
            Location              = new Point(24, y),
            Width                 = card.Width - 48,
            UseSystemPasswordChar = true,
            PlaceholderText       = "Masukkan password",
        };
        AppTheme.StyleTextBox(txtPassword);
        txtPassword.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; DoLogin(); } };
        card.Controls.Add(txtPassword);
        y += 36;

        // Error label
        lblError = new Label
        {
            Location  = new Point(24, y),
            Size      = new Size(card.Width - 48, 20),
            ForeColor = AppTheme.Danger400,
            Font      = AppTheme.FontSmall,
            Text      = "",
            Visible   = false,
        };
        card.Controls.Add(lblError);
        y += 22;

        // Login button
        btnLogin = new Button
        {
            Location = new Point(24, y),
            Width    = card.Width - 48,
            Height   = 44,
            Text     = "Masuk  →",
            Font     = new Font("Segoe UI", 10f, FontStyle.Bold),
        };
        AppTheme.StyleButton(btnLogin, AppTheme.Brand500, Color.White);
        btnLogin.Click += (s, e) => DoLogin();
        card.Controls.Add(btnLogin);
        y += 54;

        // Footer
        var lblVersion = new Label
        {
            Text      = "v1.0  ·  © 2026 Tritunggal Swarna",
            Font      = AppTheme.FontSmall,
            ForeColor = AppTheme.TextMutedColor,
            TextAlign = ContentAlignment.MiddleCenter,
            AutoSize  = false,
            Size      = new Size(card.Width, 18),
            Location  = new Point(0, y),
        };
        card.Controls.Add(lblVersion);

        Controls.Add(card);
        AcceptButton = btnLogin;
    }

    // ── Round-rect drawing helper ─────────────────────────────────────────
    private static void DrawRoundRect(Graphics g, Pen pen, Rectangle r, int radius)
    {
        int d = radius * 2;
        using var path = new System.Drawing.Drawing2D.GraphicsPath();
        path.AddArc(r.X,               r.Y,               d, d, 180, 90);
        path.AddArc(r.Right - d,       r.Y,               d, d, 270, 90);
        path.AddArc(r.Right - d,       r.Bottom - d,      d, d,   0, 90);
        path.AddArc(r.X,               r.Bottom - d,      d, d,  90, 90);
        path.CloseFigure();
        g.DrawPath(pen, path);
    }

    // ── Login logic ───────────────────────────────────────────────────────
    private void DoLogin()
    {
        lblError.Visible = false;
        var username = txtUsername.Text.Trim();
        var password = txtPassword.Text;

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
        {
            ShowError("Username dan password tidak boleh kosong.");
            return;
        }

        try
        {
            var usernameLower = username.ToLower();
            // Lookup by username only — password verification (BCrypt or legacy
            // SHA-256) is done in-process via PasswordHasher so we can detect
            // the legacy format and silently upgrade it.
            var user = _context.Users.FirstOrDefault(u =>
                u.Username.ToLower() == usernameLower && u.IsActive);

            if (user == null || !PasswordHasher.Verify(password, user.PasswordHash, out var needsUpgrade))
            {
                ShowError("Username atau password salah, atau akun tidak aktif.");
                txtPassword.Clear();
                txtPassword.Focus();
                return;
            }

            // Silently migrate legacy SHA-256 hash → BCrypt now that we have
            // the plaintext password in memory. Customer never sees this.
            if (needsUpgrade)
            {
                user.PasswordHash = PasswordHasher.Hash(password);
            }

            user.LastLoginDate = DateTime.UtcNow;
            _context.SaveChanges();

            LoggedInUser = user;
            LoginSuccess = true;
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            ShowError($"Error: {ex.Message}");
        }
    }

    private void ShowError(string msg)
    {
        lblError.Text    = msg;
        lblError.Visible = true;
    }
}
