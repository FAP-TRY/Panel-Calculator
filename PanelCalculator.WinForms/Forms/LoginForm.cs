using PanelCalculator.Core.Models;
using PanelCalculator.Data;
using PanelCalculator.WinForms.Theme;
using System.Security.Cryptography;
using System.Text;

namespace PanelCalculator.WinForms.Forms;

public class LoginForm : Form
{
    private readonly PanelCalculatorContext _context;

    public bool LoginSuccess { get; private set; }
    public User? LoggedInUser { get; private set; }

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
        Text            = "Kalkulator Panel Tritunggal Swarna";
        Size            = new Size(420, 500);
        StartPosition   = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox     = false;
        BackColor       = AppTheme.Background;

        // ── Top banner ───────────────────────────────────────────────
        var pnlBanner = new Panel
        {
            Dock      = DockStyle.Top,
            Height    = 140,
            BackColor = AppTheme.Primary
        };

        var lblAppName = new Label
        {
            Text      = "Kalkulator Panel",
            Font      = new Font("Segoe UI", 18f, FontStyle.Bold),
            ForeColor = Color.White,
            AutoSize  = false,
            TextAlign = ContentAlignment.MiddleCenter,
            Dock      = DockStyle.Fill
        };

        var lblSubtitle = new Label
        {
            Text      = "Tritunggal Swarna",
            Font      = new Font("Segoe UI", 11f, FontStyle.Regular),
            ForeColor = Color.FromArgb(190, 215, 255),
            AutoSize  = false,
            TextAlign = ContentAlignment.TopCenter,
            Dock      = DockStyle.Bottom,
            Height    = 30
        };

        pnlBanner.Controls.Add(lblAppName);
        pnlBanner.Controls.Add(lblSubtitle);

        // ── Login card ───────────────────────────────────────────────
        var pnlCard = new Panel
        {
            Padding   = new Padding(32, 24, 32, 24),
            BackColor = Color.White,
            Dock      = DockStyle.Fill
        };

        var lblTitle = AppTheme.MakeLabel("Masuk ke Sistem", AppTheme.FontLarge, AppTheme.TextPrimary);
        lblTitle.Location  = new Point(32, 20);
        lblTitle.AutoSize  = true;

        var lblUser = AppTheme.MakeLabel("Username", AppTheme.FontSmall, AppTheme.TextSecondary);
        lblUser.Location = new Point(32, 66);

        txtUsername = new TextBox { Location = new Point(32, 84), Width = 320, Height = 32, PlaceholderText = "Masukkan username" };
        AppTheme.StyleTextBox(txtUsername);
        txtUsername.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; txtPassword.Focus(); } };

        var lblPass = AppTheme.MakeLabel("Password", AppTheme.FontSmall, AppTheme.TextSecondary);
        lblPass.Location = new Point(32, 128);

        txtPassword = new TextBox { Location = new Point(32, 146), Width = 320, Height = 32, UseSystemPasswordChar = true, PlaceholderText = "Masukkan password" };
        AppTheme.StyleTextBox(txtPassword);
        txtPassword.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; DoLogin(); } };

        lblError = new Label
        {
            Location  = new Point(32, 192),
            Size      = new Size(320, 24),
            ForeColor = AppTheme.Danger,
            Font      = AppTheme.FontSmall,
            Text      = "",
            Visible   = false
        };

        btnLogin = new Button { Location = new Point(32, 226), Width = 320, Height = 40, Text = "Masuk" };
        AppTheme.StyleButton(btnLogin, AppTheme.Primary, Color.White);
        btnLogin.Font   = new Font("Segoe UI", 10f, FontStyle.Bold);
        btnLogin.Click += (s, e) => DoLogin();

        var lblVersion = AppTheme.MakeLabel("v1.0  •  © 2026 Tritunggal Swarna", AppTheme.FontSmall, AppTheme.TextMuted);
        lblVersion.Location = new Point(32, 290);
        lblVersion.AutoSize = true;

        pnlCard.Controls.AddRange(new Control[] { lblTitle, lblUser, txtUsername, lblPass, txtPassword, lblError, btnLogin, lblVersion });

        Controls.Add(pnlCard);
        Controls.Add(pnlBanner);

        AcceptButton = btnLogin;
    }

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
            var hash            = HashPassword(password);
            var usernameLower   = username.ToLower();
            // Username: case-insensitive | Password hash: exact match
            var user = _context.Users.FirstOrDefault(u =>
                u.Username.ToLower() == usernameLower && u.PasswordHash == hash && u.IsActive);

            if (user == null)
            {
                ShowError("Username atau password salah, atau akun tidak aktif.");
                txtPassword.Clear();
                txtPassword.Focus();
                return;
            }

            // Update last login
            user.LastLoginDate = DateTime.UtcNow;
            _context.SaveChanges();

            LoggedInUser  = user;
            LoginSuccess  = true;
            DialogResult  = DialogResult.OK;
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

    public static string HashPassword(string password)
    {
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(password));
        return Convert.ToHexString(bytes).ToLower();
    }
}
