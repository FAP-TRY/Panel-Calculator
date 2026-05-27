using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text.Json;
using System.Windows.Forms;
using PanelCalculator.Tools.LicenseKeyGen;

namespace PanelCalculator.Tools.LicenseKeyGenGui;

/// <summary>
/// Single-window GUI for issuing customer licenses. Sales/admin flow:
///   1. Customer kirim WA: 16-char hardware fingerprint dari ActivationForm.
///   2. Admin buka tool ini, paste fingerprint + isi nama customer.
///   3. Klik "Generate License" → kode license muncul di kotak besar.
///   4. Klik "Copy License" lalu paste ke chat WhatsApp customer.
///      (Atau klik "Buka WhatsApp" untuk pre-fill chat dengan license).
///
/// Tool ini INTERNAL — jangan di-distribusikan ke customer.
/// </summary>
public sealed class MainForm : Form
{
    // ── Settings persistence ─────────────────────────────────────────────
    // We remember the private-key path so admin doesn't browse every time.
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "PanelCalculator-IssuerTool",
        "settings.json");

    private sealed class Settings
    {
        public string PrivateKeyPath { get; set; } = LicenseIssuer.DefaultKeyPath;
        public string WhatsAppNumber { get; set; } = ""; // optional, e.g. "628123456789"
    }

    private Settings _settings = LoadSettings();

    // ── Controls ────────────────────────────────────────────────────────
    private readonly TextBox  _txtFingerprint   = new();
    private readonly TextBox  _txtCustomerName  = new();
    private readonly TextBox  _txtKeyPath       = new();
    private readonly Button   _btnBrowseKey     = new();
    private readonly Button   _btnGenerateKey   = new();
    private readonly Button   _btnIssue         = new();
    private readonly TextBox  _txtLicense       = new();
    private readonly Button   _btnCopy          = new();
    private readonly Button   _btnSendWa        = new();
    private readonly Label    _lblStatus        = new();

    public MainForm()
    {
        // ── Window setup ────────────────────────────────────────────────
        Text          = "License Issuer — PT Tritunggal Swarna (Internal)";
        ClientSize    = new Size(720, 620);
        StartPosition = FormStartPosition.CenterScreen;
        Font          = new Font("Segoe UI", 9F);
        BackColor     = Color.White;
        MaximizeBox   = false;
        FormBorderStyle = FormBorderStyle.FixedSingle;

        int y = 20;
        const int LBL_W = 180, FIELD_X = 200, FIELD_W = 480;

        // Title
        Controls.Add(new Label
        {
            Text = "🔑  License Issuer Panel Calculator",
            Font = new Font("Segoe UI Semibold", 14F),
            Location = new Point(20, y),
            AutoSize = true,
            ForeColor = Color.FromArgb(37, 99, 235),
        });
        y += 35;

        Controls.Add(new Label
        {
            Text = "Tool internal — generate license untuk customer berdasarkan hardware ID yang mereka kirim via WhatsApp.",
            Location = new Point(20, y),
            Size = new Size(680, 30),
            ForeColor = Color.Gray,
        });
        y += 40;

        // ── Fingerprint input ───────────────────────────────────────────
        AddLabel("Hardware Fingerprint", y, LBL_W);
        _txtFingerprint.Location = new Point(FIELD_X, y);
        _txtFingerprint.Size     = new Size(FIELD_W, 28);
        _txtFingerprint.Font     = new Font("Consolas", 11F);
        _txtFingerprint.PlaceholderText = "Contoh: D8F8-BEB8-E346-4A5A";
        _txtFingerprint.CharacterCasing = CharacterCasing.Upper;
        Controls.Add(_txtFingerprint);
        y += 50;

        // ── Customer name ───────────────────────────────────────────────
        AddLabel("Nama Customer / PT", y, LBL_W);
        _txtCustomerName.Location = new Point(FIELD_X, y);
        _txtCustomerName.Size     = new Size(FIELD_W, 28);
        _txtCustomerName.Font     = new Font("Segoe UI", 10F);
        _txtCustomerName.PlaceholderText = "Contoh: PT Sumber Makmur Sejahtera";
        Controls.Add(_txtCustomerName);
        y += 50;

        // ── Private key path ────────────────────────────────────────────
        AddLabel("File Private Key", y, LBL_W);
        _txtKeyPath.Location = new Point(FIELD_X, y);
        _txtKeyPath.Size     = new Size(FIELD_W - 90, 28);
        _txtKeyPath.Font     = new Font("Consolas", 9F);
        _txtKeyPath.Text     = _settings.PrivateKeyPath;
        Controls.Add(_txtKeyPath);

        _btnBrowseKey.Text     = "📂 Pilih";
        _btnBrowseKey.Location = new Point(FIELD_X + FIELD_W - 85, y - 1);
        _btnBrowseKey.Size     = new Size(85, 30);
        _btnBrowseKey.Click   += BrowseKey_Click;
        Controls.Add(_btnBrowseKey);
        y += 35;

        _btnGenerateKey.Text     = "Belum punya keypair? Klik di sini untuk generate (one-time setup)";
        _btnGenerateKey.Location = new Point(FIELD_X, y);
        _btnGenerateKey.Size     = new Size(FIELD_W, 30);
        _btnGenerateKey.FlatStyle = FlatStyle.Flat;
        _btnGenerateKey.BackColor = Color.FromArgb(241, 245, 249);
        _btnGenerateKey.ForeColor = Color.FromArgb(71, 85, 105);
        _btnGenerateKey.Font     = new Font("Segoe UI", 8.5F);
        _btnGenerateKey.Click   += GenerateKey_Click;
        Controls.Add(_btnGenerateKey);
        y += 50;

        // ── Issue button ────────────────────────────────────────────────
        _btnIssue.Text      = "⚡  GENERATE LICENSE";
        _btnIssue.Location  = new Point(20, y);
        _btnIssue.Size      = new Size(680, 50);
        _btnIssue.FlatStyle = FlatStyle.Flat;
        _btnIssue.BackColor = Color.FromArgb(37, 99, 235);
        _btnIssue.ForeColor = Color.White;
        _btnIssue.Font      = new Font("Segoe UI Semibold", 12F);
        _btnIssue.Click    += Issue_Click;
        Controls.Add(_btnIssue);
        y += 65;

        // ── License output ──────────────────────────────────────────────
        Controls.Add(new Label
        {
            Text = "License Key (siap dikirim ke customer):",
            Location = new Point(20, y),
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 9F),
        });
        y += 22;

        _txtLicense.Location  = new Point(20, y);
        _txtLicense.Size      = new Size(680, 130);
        _txtLicense.Multiline = true;
        _txtLicense.ReadOnly  = true;
        _txtLicense.ScrollBars = ScrollBars.Vertical;
        _txtLicense.Font      = new Font("Consolas", 10F);
        _txtLicense.BackColor = Color.FromArgb(248, 250, 252);
        _txtLicense.WordWrap  = true;
        Controls.Add(_txtLicense);
        y += 140;

        // ── Copy + WhatsApp buttons ─────────────────────────────────────
        _btnCopy.Text      = "📋  Copy License";
        _btnCopy.Location  = new Point(20, y);
        _btnCopy.Size      = new Size(335, 40);
        _btnCopy.FlatStyle = FlatStyle.Flat;
        _btnCopy.BackColor = Color.FromArgb(241, 245, 249);
        _btnCopy.Font      = new Font("Segoe UI", 10F);
        _btnCopy.Enabled   = false;
        _btnCopy.Click    += Copy_Click;
        Controls.Add(_btnCopy);

        _btnSendWa.Text      = "💬  Buka WhatsApp & Kirim";
        _btnSendWa.Location  = new Point(365, y);
        _btnSendWa.Size      = new Size(335, 40);
        _btnSendWa.FlatStyle = FlatStyle.Flat;
        _btnSendWa.BackColor = Color.FromArgb(34, 197, 94);
        _btnSendWa.ForeColor = Color.White;
        _btnSendWa.Font      = new Font("Segoe UI", 10F);
        _btnSendWa.Enabled   = false;
        _btnSendWa.Click    += SendWa_Click;
        Controls.Add(_btnSendWa);
        y += 50;

        // ── Status ──────────────────────────────────────────────────────
        _lblStatus.Location = new Point(20, y);
        _lblStatus.Size     = new Size(680, 20);
        _lblStatus.ForeColor = Color.FromArgb(71, 85, 105);
        _lblStatus.Font     = new Font("Segoe UI", 8.5F);
        _lblStatus.Text     = "Siap. Isi 3 field di atas lalu klik GENERATE LICENSE.";
        Controls.Add(_lblStatus);

        // ── Enter key di fingerprint → fokus next ───────────────────────
        _txtFingerprint.KeyDown += (s, e) =>
        {
            if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; _txtCustomerName.Focus(); }
        };
        _txtCustomerName.KeyDown += (s, e) =>
        {
            if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Issue_Click(s!, e); }
        };
    }

    private void AddLabel(string text, int y, int width)
    {
        Controls.Add(new Label
        {
            Text = text + " :",
            Location = new Point(20, y + 4),
            Size = new Size(width, 22),
            Font = new Font("Segoe UI Semibold", 9F),
            TextAlign = ContentAlignment.MiddleLeft,
        });
    }

    // ── Event handlers ──────────────────────────────────────────────────

    private void BrowseKey_Click(object? sender, EventArgs e)
    {
        using var dlg = new OpenFileDialog
        {
            Filter = "Private key (*.key)|*.key|All files (*.*)|*.*",
            Title  = "Pilih file private key (license-private.key)",
            InitialDirectory = File.Exists(_txtKeyPath.Text)
                ? Path.GetDirectoryName(_txtKeyPath.Text)!
                : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        };
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            _txtKeyPath.Text = dlg.FileName;
            SaveSettings();
        }
    }

    private void GenerateKey_Click(object? sender, EventArgs e)
    {
        var confirm = MessageBox.Show(this,
            "Generate keypair Ed25519 baru?\n\n" +
            "WAJIB dilakukan SEKALI saja saat pertama kali pakai app.\n\n" +
            "Setelah generate:\n" +
            "  • File private key disimpan di komputer Anda (JANGAN HILANG)\n" +
            "  • Public key di-tampilkan untuk di-paste ke source code aplikasi\n" +
            "  • Aplikasi harus di-rebuild + di-release ulang\n\n" +
            "Kalau Anda sudah pernah generate sebelumnya, JANGAN klik OK\n" +
            "(akan gagal karena tidak boleh overwrite key lama).\n\n" +
            "Lanjut generate?",
            "Generate Keypair", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
        if (confirm != DialogResult.OK) return;

        using var dlg = new FolderBrowserDialog
        {
            Description = "Pilih folder untuk simpan private key (di luar repo!)",
            UseDescriptionForTitle = true,
            SelectedPath = Path.GetDirectoryName(_settings.PrivateKeyPath) ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            var result = LicenseIssuer.GenerateKeyPair(dlg.SelectedPath);
            _txtKeyPath.Text = result.PrivateKeyPath;
            SaveSettings();

            Clipboard.SetText(result.PublicKeyBase64);
            MessageBox.Show(this,
                "Keypair berhasil di-generate!\n\n" +
                $"Private key disimpan di:\n{result.PrivateKeyPath}\n\n" +
                $"Public key (sudah ter-copy ke clipboard):\n{result.PublicKeyBase64}\n\n" +
                "LANGKAH SELANJUTNYA:\n" +
                "1. Buka file: PanelCalculator.Core/Security/LicenseService.cs\n" +
                "2. Ganti nilai constant PublicKeyBase64 dengan string di atas\n" +
                "3. Rebuild aplikasi (Tools/build-release-singlefile.ps1)\n" +
                "4. Re-release ke customer\n\n" +
                "BACKUP file private key ke flashdisk / Google Drive sekarang juga.",
                "Keypair berhasil", MessageBoxButtons.OK, MessageBoxIcon.Information);
            SetStatus("Keypair generated. Public key sudah di-copy ke clipboard.", Color.Green);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Gagal generate keypair", MessageBoxButtons.OK, MessageBoxIcon.Error);
            SetStatus("Gagal generate keypair: " + ex.Message, Color.Red);
        }
    }

    private void Issue_Click(object? sender, EventArgs e)
    {
        _txtLicense.Text = "";
        _btnCopy.Enabled = false;
        _btnSendWa.Enabled = false;

        try
        {
            var result = LicenseIssuer.Issue(
                _txtFingerprint.Text,
                _txtCustomerName.Text,
                _txtKeyPath.Text);

            // Save the last-used key path for next launch
            _settings.PrivateKeyPath = _txtKeyPath.Text;
            SaveSettings();

            _txtLicense.Text = result.LicenseKey;
            _btnCopy.Enabled = true;
            _btnSendWa.Enabled = true;
            SetStatus(
                $"✓ License berhasil di-generate untuk '{result.CustomerName}' " +
                $"(fingerprint {result.HardwareFingerprintDisplay}, {result.LicenseLength} karakter).",
                Color.Green);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Gagal generate license",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            SetStatus("✗ " + ex.Message, Color.Red);
        }
    }

    private void Copy_Click(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_txtLicense.Text)) return;
        Clipboard.SetText(_txtLicense.Text);
        SetStatus("✓ License ter-copy ke clipboard. Paste ke chat WhatsApp customer (Ctrl+V).", Color.Green);
    }

    private void SendWa_Click(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_txtLicense.Text)) return;

        // Customer-friendly message template (admin can edit before send via WA web)
        var msg =
            "Halo, berikut kode aktivasi untuk Panel Calculator:\n\n" +
            _txtLicense.Text + "\n\n" +
            "Cara aktivasi:\n" +
            "1. Buka aplikasi Panel Calculator\n" +
            "2. Di layar 'Aktivasi Kalkulator Panel', PASTE kode di atas ke kotak besar\n" +
            "3. Klik tombol 'Aktifkan'\n\n" +
            "Terima kasih.\n— Tim PT Tritunggal Swarna";

        var encoded = Uri.EscapeDataString(msg);

        // No phone — opens "send to" chooser. If admin has saved a default number, prefill.
        var url = string.IsNullOrWhiteSpace(_settings.WhatsAppNumber)
            ? $"https://wa.me/?text={encoded}"
            : $"https://wa.me/{_settings.WhatsAppNumber}?text={encoded}";

        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
            SetStatus("✓ WhatsApp dibuka. Pilih chat customer, lalu kirim.", Color.Green);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                "Tidak bisa buka WhatsApp web. Copy manual saja:\n\n" + url,
                "WhatsApp", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            SetStatus("Gagal buka WhatsApp: " + ex.Message, Color.OrangeRed);
        }
    }

    private void SetStatus(string text, Color color)
    {
        _lblStatus.Text      = text;
        _lblStatus.ForeColor = color;
    }

    // ── Settings persistence (private key path remembered between runs) ──

    private static Settings LoadSettings()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                return JsonSerializer.Deserialize<Settings>(json) ?? new Settings();
            }
        }
        catch { /* ignore — fall through to defaults */ }
        return new Settings();
    }

    private void SaveSettings()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            _settings.PrivateKeyPath = _txtKeyPath.Text;
            File.WriteAllText(SettingsPath,
                JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* best-effort */ }
    }
}
