using System;
using System.Diagnostics;
using PanelCalculator.Core.Security;
using PanelCalculator.Data;
using PanelCalculator.Data.Security;
using PanelCalculator.WinForms.Theme;

namespace PanelCalculator.WinForms.Forms;

/// <summary>
/// Modal shown at startup when the app has no valid license stored in
/// AppSettings. Walks the user through:
///   1. Reading their hardware fingerprint (auto-copied to clipboard on click)
///   2. Contacting PT TTS via WhatsApp (deep-link) to receive a license
///   3. Pasting the license string back into the textbox
///   4. Activating — on success the license is persisted in AppSettings.
///
/// Also exposed from SettingsForm as "Re-activate" for the hardware-change
/// reactivation flow.
/// </summary>
public class ActivationForm : Form
{
    /// <summary>Optional support phone number shown in the WhatsApp deep-link.
    /// Defaults to a placeholder — PT TTS should update before public release.</summary>
    public string SupportWhatsAppNumber { get; set; } = "628XXXXXXXXXX";

    private readonly PanelCalculatorContext _context;
    private TextBox _txtFingerprint = null!;
    private TextBox _txtLicenseKey  = null!;
    private Label   _lblStatus      = null!;
    private Button  _btnActivate    = null!;
    private string  _fingerprintDisplay = "";

    /// <summary>True if the user successfully activated (license saved).</summary>
    public bool ActivationSuccess { get; private set; }

    public ActivationForm(PanelCalculatorContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        BuildUI();
    }

    private void BuildUI()
    {
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode       = AutoScaleMode.Dpi;
        Text                = "Aktivasi Kalkulator Panel";
        Size                = new Size(560, 600);
        StartPosition       = FormStartPosition.CenterScreen;
        FormBorderStyle     = FormBorderStyle.FixedDialog;
        MaximizeBox         = false;
        MinimizeBox         = false;
        BackColor           = AppTheme.Bg0;

        try { _fingerprintDisplay = MachineKeyProvider.GetHardwareFingerprintDisplay(); }
        catch (Exception ex) { _fingerprintDisplay = $"(error: {ex.Message})"; }

        int margin = 24;
        int width  = 512 - margin * 2;
        int y      = 18;

        var lblTitle = AppTheme.MakeLabel("🔐  Aktivasi Aplikasi", AppTheme.FontTitle, AppTheme.Text1);
        lblTitle.Location = new Point(margin, y); y += 36;

        var lblSub = AppTheme.MakeLabel(
            "Aplikasi ini perlu diaktivasi sekali untuk komputer ini.",
            AppTheme.FontBase, AppTheme.Text2);
        lblSub.Location = new Point(margin, y);
        lblSub.AutoSize = false;
        lblSub.Size     = new Size(width, 20);
        Controls.Add(lblSub); y += 24;

        // ── Separator ────────────────────────────────────────────────────
        var sep1 = new Panel { Location = new Point(margin, y), Size = new Size(width, 1), BackColor = AppTheme.Border2 };
        Controls.Add(sep1); y += 16;

        // ── Step 1: hardware fingerprint ─────────────────────────────────
        var lblStep1 = AppTheme.MakeLabel("LANGKAH 1  ·  HARDWARE ID KOMPUTER INI", AppTheme.FontCaption, AppTheme.Cyan400);
        lblStep1.Location = new Point(margin, y); y += 18;

        var lblStep1Body = AppTheme.MakeLabel(
            "Salin kode di bawah ini, lalu kirim ke PT Tritunggal Swarna via WhatsApp.",
            AppTheme.FontSmall, AppTheme.Text2);
        lblStep1Body.Location = new Point(margin, y);
        lblStep1Body.AutoSize = false;
        lblStep1Body.Size     = new Size(width, 18);
        Controls.Add(lblStep1Body); y += 22;

        _txtFingerprint = new TextBox
        {
            Location    = new Point(margin, y),
            Width       = width - 130,
            Text        = _fingerprintDisplay,
            ReadOnly    = true,
            Font        = new Font("Consolas", 14f, FontStyle.Bold),
            BorderStyle = BorderStyle.FixedSingle,
            BackColor   = AppTheme.Bg2,
            ForeColor   = AppTheme.Cyan300,
            TextAlign   = HorizontalAlignment.Center,
        };
        Controls.Add(_txtFingerprint);

        var btnCopy = new Button
        {
            Text     = "📋 Salin",
            Location = new Point(margin + width - 122, y),
            Width    = 120,
            Height   = _txtFingerprint.PreferredHeight + 2,
        };
        AppTheme.StyleButtonGhost(btnCopy);
        btnCopy.Click += (_, _) => CopyFingerprintToClipboard();
        Controls.Add(btnCopy);
        y += _txtFingerprint.PreferredHeight + 12;

        var btnWa = new Button
        {
            Text     = "💬 Hubungi PT TTS via WhatsApp",
            Location = new Point(margin, y),
            Width    = width,
            Height   = 36,
        };
        AppTheme.StyleButton(btnWa, AppTheme.Success500, Color.White);
        btnWa.Click += (_, _) => OpenWhatsAppChat();
        Controls.Add(btnWa);
        y += 50;

        var sep2 = new Panel { Location = new Point(margin, y), Size = new Size(width, 1), BackColor = AppTheme.Border2 };
        Controls.Add(sep2); y += 16;

        // ── Step 2: paste license ────────────────────────────────────────
        var lblStep2 = AppTheme.MakeLabel("LANGKAH 2  ·  TEMPELKAN KODE AKTIVASI", AppTheme.FontCaption, AppTheme.Cyan400);
        lblStep2.Location = new Point(margin, y); y += 18;

        var lblStep2Body = AppTheme.MakeLabel(
            "PT TTS akan mengirim kode aktivasi (panjang ~150 karakter). Tempelkan di sini:",
            AppTheme.FontSmall, AppTheme.Text2);
        lblStep2Body.Location = new Point(margin, y);
        lblStep2Body.AutoSize = false;
        lblStep2Body.Size     = new Size(width, 18);
        Controls.Add(lblStep2Body); y += 22;

        _txtLicenseKey = new TextBox
        {
            Location    = new Point(margin, y),
            Width       = width,
            Height      = 80,
            Multiline   = true,
            ScrollBars  = ScrollBars.Vertical,
            Font        = new Font("Consolas", 9.5f, FontStyle.Regular),
            BorderStyle = BorderStyle.FixedSingle,
            BackColor   = AppTheme.Bg2,
            ForeColor   = AppTheme.Text1,
            PlaceholderText = "Contoh: K7F3M-B9P2X-N4QT8-...",
        };
        Controls.Add(_txtLicenseKey);
        y += 92;

        _lblStatus = new Label
        {
            Location  = new Point(margin, y),
            Size      = new Size(width, 36),
            Font      = AppTheme.FontSmall,
            ForeColor = AppTheme.Text3,
            Text      = "",
            AutoSize  = false,
        };
        Controls.Add(_lblStatus);
        y += 40;

        // ── Buttons ──────────────────────────────────────────────────────
        _btnActivate = new Button
        {
            Text     = "✅  Aktifkan",
            Location = new Point(margin, y),
            Width    = width - 130,
            Height   = 40,
            Font     = new Font("Segoe UI", 10f, FontStyle.Bold),
        };
        AppTheme.StyleButtonPrimary(_btnActivate);
        _btnActivate.Click += (_, _) => DoActivate();
        Controls.Add(_btnActivate);

        var btnCancel = new Button
        {
            Text     = "Keluar",
            Location = new Point(margin + width - 122, y),
            Width    = 120,
            Height   = 40,
        };
        AppTheme.StyleButtonGhost(btnCancel);
        btnCancel.Click += (_, _) => Close();
        Controls.Add(btnCancel);

        AcceptButton = _btnActivate;
        CancelButton = btnCancel;
    }

    // ─────────────────────────────────────────────────────────────────────
    private void CopyFingerprintToClipboard()
    {
        try
        {
            Clipboard.SetText(_fingerprintDisplay);
            ShowStatus("Hardware ID disalin ke clipboard. Tempelkan ke pesan WhatsApp Anda.", AppTheme.Success400);
        }
        catch (Exception ex)
        {
            ShowStatus("Gagal menyalin: " + ex.Message, AppTheme.Danger400);
        }
    }

    private void OpenWhatsAppChat()
    {
        try
        {
            var msg = Uri.EscapeDataString(
                "Halo PT Tritunggal Swarna, saya ingin aktivasi Kalkulator Panel.\n" +
                "Hardware ID komputer saya: " + _fingerprintDisplay);
            var url = $"https://wa.me/{SupportWhatsAppNumber}?text={msg}";

            // Use UseShellExecute so Windows resolves the protocol.
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ShowStatus("Tidak bisa membuka WhatsApp: " + ex.Message, AppTheme.Danger400);
        }
    }

    private void DoActivate()
    {
        var key = (_txtLicenseKey.Text ?? "").Trim();
        if (key.Length == 0)
        {
            ShowStatus("Tempelkan kode aktivasi terlebih dahulu.", AppTheme.Warning400);
            return;
        }

        _btnActivate.Enabled = false;
        try
        {
            var fpBytes = MachineKeyProvider.GetHardwareFingerprintBytes();
            var result  = LicenseService.ValidateLicense(key, fpBytes);

            if (!result.IsValid)
            {
                ShowStatus(MapErrorMessage(result), AppTheme.Danger400);
                return;
            }

            // Persist the (canonicalised) license string so future startups
            // don't trip on whitespace / dash inconsistencies.
            PersistLicense(key);

            ActivationSuccess    = true;
            DialogResult = DialogResult.OK;

            MessageBox.Show(
                $"Aktivasi berhasil!\n\nLisensi atas nama: {result.CustomerName}\nTerima kasih telah menggunakan Kalkulator Panel TTS.",
                "Aktivasi Berhasil",
                MessageBoxButtons.OK, MessageBoxIcon.Information);

            Close();
        }
        catch (Exception ex)
        {
            ShowStatus("Terjadi error saat aktivasi: " + ex.Message, AppTheme.Danger400);
        }
        finally
        {
            _btnActivate.Enabled = true;
        }
    }

    private static string MapErrorMessage(LicenseValidationResult r) => r.Status switch
    {
        LicenseValidationStatus.InvalidSignature =>
            "Kode aktivasi tidak valid (tanda tangan salah). Pastikan tidak ada karakter yang tertukar.",
        LicenseValidationStatus.WrongHardware =>
            "Kode aktivasi ini diterbitkan untuk komputer lain. Hubungi PT TTS untuk reaktivasi.",
        LicenseValidationStatus.Malformed =>
            "Format kode aktivasi tidak dikenali. Pastikan Anda menempel seluruh teks tanpa terpotong.\n(" + r.Reason + ")",
        LicenseValidationStatus.Expired =>
            "Kode aktivasi sudah kedaluwarsa.",
        _ => "Aktivasi gagal: " + r.Reason
    };

    private void PersistLicense(string licenseKey)
    {
        var setting = _context.Settings.FirstOrDefault(s => s.SettingKey == LicenseSettings.LicenseKeySettingName);
        if (setting == null)
        {
            _context.Settings.Add(new Core.Models.AppSettings
            {
                SettingKey   = LicenseSettings.LicenseKeySettingName,
                SettingValue = licenseKey,
                LastUpdated  = DateTime.UtcNow,
            });
        }
        else
        {
            setting.SettingValue = licenseKey;
            setting.LastUpdated  = DateTime.UtcNow;
        }
        _context.SaveChanges();
    }

    private void ShowStatus(string message, Color color)
    {
        _lblStatus.Text      = message;
        _lblStatus.ForeColor = color;
    }
}

/// <summary>Shared constants for the license-related settings keys.</summary>
internal static class LicenseSettings
{
    /// <summary>AppSettings row that stores the activated license key.</summary>
    public const string LicenseKeySettingName = "license_key";
}
