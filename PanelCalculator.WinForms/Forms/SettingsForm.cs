using PanelCalculator.Core.Models;
using PanelCalculator.Data;
using PanelCalculator.Data.DataSeeding;
using PanelCalculator.WinForms.Theme;

namespace PanelCalculator.WinForms.Forms;

public class SettingsForm : Form
{
    private readonly PanelCalculatorContext _context;

    private TextBox txtCompanyName    = null!;
    private TextBox txtCompanyAddress = null!;
    private TextBox txtCompanyPhone   = null!;
    private NumericUpDown numMargin   = null!;
    private NumericUpDown numTax      = null!;
    private NumericUpDown numShipping = null!;
    private Label lblProductCount     = null!;

    public SettingsForm(PanelCalculatorContext context)
    {
        _context = context;
        BuildUI();
    }

    private void BuildUI()
    {
        Text = "Pengaturan";
        Size = new Size(500, 640);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        BackColor = AppTheme.Background;

        var lblTitle = AppTheme.MakeLabel("Pengaturan Aplikasi", AppTheme.FontTitle, AppTheme.TextPrimary);
        lblTitle.Location = new Point(20, 16);

        int y = 60;

        void AddField(string label, ref TextBox field, string placeholder = "")
        {
            var lbl = AppTheme.MakeLabel(label, AppTheme.FontSmall, AppTheme.TextSecondary);
            lbl.Location = new Point(20, y);
            Controls.Add(lbl);
            y += 18;
            field = new TextBox { Location = new Point(20, y), Width = 440, PlaceholderText = placeholder };
            AppTheme.StyleTextBox(field);
            Controls.Add(field);
            y += 32;
        }

        void AddNumField(string label, ref NumericUpDown num, decimal min, decimal max, decimal inc = 1, int decimals = 0)
        {
            var lbl = AppTheme.MakeLabel(label, AppTheme.FontSmall, AppTheme.TextSecondary);
            lbl.Location = new Point(20, y);
            Controls.Add(lbl);
            y += 18;
            num = new NumericUpDown
            {
                Location = new Point(20, y), Width = 200,
                Minimum = min, Maximum = max, Increment = inc, DecimalPlaces = decimals, Font = AppTheme.FontBase
            };
            Controls.Add(num);
            y += 36;
        }

        // ── Section: Perusahaan ──────────────────────────────
        AddField("Nama Perusahaan", ref txtCompanyName, "PT Electrical Supplies");
        AddField("Alamat Perusahaan", ref txtCompanyAddress, "Jakarta, Indonesia");
        AddField("No. Telepon", ref txtCompanyPhone, "+62-21-xxxx-xxxx");

        // ── Separator ────────────────────────────────────────
        y += 4;
        Controls.Add(new Panel { Location = new Point(20, y), Width = 440, Height = 1, BackColor = AppTheme.Border });
        y += 12;

        // ── Section: Default Kalkulasi ───────────────────────
        AddNumField("Default Margin (%)", ref numMargin, 0, 200, 1, 1);
        AddNumField("Default PPN (%)", ref numTax, 0, 50, 1, 1);
        AddNumField("Default Ongkos Kirim (Rp)", ref numShipping, 0, 100_000_000, 10000);

        // ── Tombol Simpan ────────────────────────────────────
        var btnSave = new Button { Text = "Simpan Pengaturan", Location = new Point(20, y + 8), Width = 180, Height = 36 };
        AppTheme.StyleButton(btnSave, AppTheme.Success, Color.White);
        btnSave.Click += BtnSave_Click;

        var btnClose = new Button { Text = "Tutup", Location = new Point(215, y + 8), Width = 110, Height = 36 };
        AppTheme.StyleButton(btnClose, Color.FromArgb(229, 231, 235), AppTheme.TextPrimary);
        btnClose.Click += (s, e) => Close();

        y += 56;

        // ── Separator ────────────────────────────────────────
        Controls.Add(new Panel { Location = new Point(20, y), Width = 440, Height = 1, BackColor = AppTheme.Border });
        y += 12;

        // ── Section: Import Produk ───────────────────────────
        var lblImportTitle = AppTheme.MakeLabel("Import Data Produk", AppTheme.FontBold, AppTheme.TextPrimary);
        lblImportTitle.Location = new Point(20, y);
        y += 24;

        lblProductCount = AppTheme.MakeLabel("...", AppTheme.FontSmall, AppTheme.TextSecondary);
        lblProductCount.Location = new Point(20, y);
        y += 22;

        var btnImport = new Button { Text = "📂 Import dari CSV", Location = new Point(20, y), Width = 180, Height = 36 };
        AppTheme.StyleButton(btnImport, AppTheme.Primary, Color.White);
        btnImport.Click += BtnImport_Click;

        var lblImportHint = AppTheme.MakeLabel(
            "Format: category, reference_code, product_name, specifications, price, stock_status, vendor",
            AppTheme.FontSmall, AppTheme.TextMuted);
        lblImportHint.Location = new Point(20, y + 42);
        lblImportHint.MaximumSize = new Size(440, 0);
        lblImportHint.AutoSize = true;

        Controls.AddRange(new Control[] { lblTitle, btnSave, btnClose, lblImportTitle, lblProductCount, btnImport, lblImportHint });
        Load += (s, e) => { LoadSettings(); UpdateProductCount(); };
    }

    private void UpdateProductCount()
    {
        try
        {
            var count = _context.Products.Count();
            lblProductCount.Text = count > 0
                ? $"Database: {count} produk tersedia"
                : "Database: belum ada produk — import CSV untuk mulai";
            lblProductCount.ForeColor = count > 0 ? AppTheme.Success : AppTheme.Warning;
        }
        catch { }
    }

    private void LoadSettings()
    {
        var settings = _context.Settings.ToDictionary(s => s.SettingKey, s => s.SettingValue ?? "");
        txtCompanyName.Text    = Get(settings, "CompanyName");
        txtCompanyAddress.Text = Get(settings, "CompanyAddress");
        txtCompanyPhone.Text   = Get(settings, "CompanyPhone");

        if (decimal.TryParse(Get(settings, "DefaultMarginPercent"), out var m))   numMargin.Value   = m;
        if (decimal.TryParse(Get(settings, "DefaultTaxPercent"), out var t))      numTax.Value      = t;
        if (decimal.TryParse(Get(settings, "DefaultShippingCost"), out var ship)) numShipping.Value = ship;
    }

    private void BtnSave_Click(object? sender, EventArgs e)
    {
        void Upsert(string key, string value)
        {
            var s = _context.Settings.Find(key);
            if (s == null) _context.Settings.Add(new AppSettings { SettingKey = key, SettingValue = value });
            else { s.SettingValue = value; s.LastUpdated = DateTime.UtcNow; _context.Settings.Update(s); }
        }

        Upsert("CompanyName",           txtCompanyName.Text.Trim());
        Upsert("CompanyAddress",        txtCompanyAddress.Text.Trim());
        Upsert("CompanyPhone",          txtCompanyPhone.Text.Trim());
        Upsert("DefaultMarginPercent",  numMargin.Value.ToString());
        Upsert("DefaultTaxPercent",     numTax.Value.ToString());
        Upsert("DefaultShippingCost",   numShipping.Value.ToString());

        _context.SaveChanges();
        MessageBox.Show("Pengaturan berhasil disimpan.", "Tersimpan", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private async void BtnImport_Click(object? sender, EventArgs e)
    {
        using var ofd = new OpenFileDialog
        {
            Title  = "Pilih file CSV produk",
            Filter = "CSV Files (*.csv)|*.csv|All Files (*.*)|*.*"
        };
        if (ofd.ShowDialog() != DialogResult.OK) return;

        var btn = (Button)sender!;
        btn.Enabled = false;
        btn.Text = "⏳ Mengimport...";

        try
        {
            var seeder = new ProductSeeder(_context);
            int count = await seeder.SeedFromCsvAsync(ofd.FileName);
            UpdateProductCount();
            MessageBox.Show($"Berhasil import {count} produk ke database.", "Import Selesai",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Gagal import:\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            btn.Enabled = true;
            btn.Text = "📂 Import dari CSV";
        }
    }

    private static string Get(Dictionary<string, string> d, string key)
        => d.TryGetValue(key, out var v) ? v : "";
}
