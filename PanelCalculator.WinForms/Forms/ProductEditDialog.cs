using PanelCalculator.Core.Models;
using PanelCalculator.WinForms.Theme;

namespace PanelCalculator.WinForms.Forms;

/// <summary>
/// Dialog untuk menambah atau mengedit produk secara manual.
/// Field sesuai format CSV: category, reference_code, product_name,
/// specifications, price, price_year, stock_status, vendor.
///
/// MARGIN RULE: pnlBody uses absolute positioning.
/// Panel.Padding does NOT offset absolute children, so we always declare
///   const int M = 20;   // left / right margin
/// and position every control at new Point(M, y).
/// </summary>
public class ProductEditDialog : Form
{
    // ── Output properties (read by caller after ShowDialog == OK) ────────
    public string  OutCategory       { get; private set; } = "";
    public string  OutReferenceCode  { get; private set; } = "";
    public string  OutProductName    { get; private set; } = "";
    public string? OutSpecifications { get; private set; }
    public decimal OutPrice          { get; private set; }
    public int?    OutPriceYear      { get; private set; }
    public int     OutStockStatus    { get; private set; } = 1;
    public string? OutVendor         { get; private set; }

    // ── Input controls ───────────────────────────────────────────────────
    private ComboBox      cmbCategory = null!;
    private TextBox       txtRef      = null!;
    private TextBox       txtName     = null!;
    private TextBox       txtSpec     = null!;
    private NumericUpDown numPrice    = null!;
    private TextBox       txtYear     = null!;
    private ComboBox      cmbStock    = null!;
    private TextBox       txtVendor   = null!;

    private readonly Product?      _editing;
    private readonly List<string>  _categories;

    public ProductEditDialog(Product? editing, List<string> categories)
    {
        _editing    = editing;
        _categories = categories;
        BuildUI();
        if (_editing != null) PopulateForEdit(_editing);
    }

    // ════════════════════════════════════════════════════════════════════
    private void BuildUI()
    {
        Text            = _editing == null ? "Tambah Produk Baru" : "Edit Produk";
        Size            = new Size(500, 490);
        MinimumSize     = new Size(460, 460);
        StartPosition   = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox     = false;
        MinimizeBox     = false;
        BackColor       = AppTheme.Background;

        // ── Header ───────────────────────────────────────────────────────
        var pnlHead = new Panel { Dock = DockStyle.Top, Height = 50, BackColor = Color.White };
        pnlHead.Paint += (s, e) =>
        {
            using var pen = new Pen(AppTheme.Border);
            e.Graphics.DrawLine(pen, 0, pnlHead.Height - 1, pnlHead.Width, pnlHead.Height - 1);
        };
        var icon = _editing == null ? "➕" : "✏";
        var lblTitle = AppTheme.MakeLabel(
            $"{icon}  {(_editing == null ? "Tambah Produk Baru" : "Edit Produk")}",
            AppTheme.FontLarge, AppTheme.TextPrimary);
        lblTitle.Location = new Point(20, 13);
        lblTitle.AutoSize = true;
        pnlHead.Controls.Add(lblTitle);

        // ── Body ─────────────────────────────────────────────────────────
        // MARGIN RULE: Panel.Padding does NOT move absolute-positioned
        // children. Use explicit M for every control's X coordinate.
        const int M   = 20;   // left margin (= right margin target)
        const int LH  = 20;   // label row height
        const int FH  = 30;   // field height
        const int GAP = 10;   // vertical gap between groups
        // fw = usable content width inside the 500-px form
        // Client ≈ 484px (FixedDialog border ~16px). 484 - 2*M = 444.
        const int fw  = 444;

        var pnlBody = new Panel { Dock = DockStyle.Fill, AutoScroll = true };

        int y = 14;  // top padding (manual, since Panel.Padding is skipped)

        // Helper — creates a secondary label at (M, top)
        void MkLbl(string text, int top, int? overrideX = null)
        {
            var lbl = AppTheme.MakeLabel(text, AppTheme.FontSmall, AppTheme.TextSecondary);
            lbl.Location = new Point(overrideX ?? M, top);
            lbl.AutoSize = true;
            pnlBody.Controls.Add(lbl);
        }

        // ── 1. Kategori ───────────────────────────────────────────────────
        MkLbl("Kategori *", y);
        y += LH;
        cmbCategory = new ComboBox
        {
            Location      = new Point(M, y),
            Width         = fw,
            DropDownStyle = ComboBoxStyle.DropDown,
            Font          = AppTheme.FontBase
        };
        AppTheme.StyleComboBox(cmbCategory);
        foreach (var c in _categories) cmbCategory.Items.Add(c);
        foreach (var def in new[] { "MCB","MCCB","RCCB","ACB","Kontaktor",
                                    "Motor CB","Busbar","Box","Accessories","Other" })
            if (!cmbCategory.Items.Contains(def)) cmbCategory.Items.Add(def);
        pnlBody.Controls.Add(cmbCategory);
        y += FH + GAP;

        // ── 2. Kode Referensi ─────────────────────────────────────────────
        MkLbl("Kode Referensi *  (unik, contoh: A9K14116)", y);
        y += LH;
        txtRef = new TextBox { Location = new Point(M, y), Width = fw, Font = AppTheme.FontBase };
        AppTheme.StyleTextBox(txtRef);
        txtRef.CharacterCasing = CharacterCasing.Upper;
        pnlBody.Controls.Add(txtRef);
        y += FH + GAP;

        // ── 3. Nama Produk ────────────────────────────────────────────────
        MkLbl("Nama Produk *", y);
        y += LH;
        txtName = new TextBox { Location = new Point(M, y), Width = fw, Font = AppTheme.FontBase };
        AppTheme.StyleTextBox(txtName);
        pnlBody.Controls.Add(txtName);
        y += FH + GAP;

        // ── 4. Spesifikasi ────────────────────────────────────────────────
        MkLbl("Spesifikasi  (contoh: 16A 2P 30mA)", y);
        y += LH;
        txtSpec = new TextBox { Location = new Point(M, y), Width = fw, Font = AppTheme.FontBase };
        AppTheme.StyleTextBox(txtSpec);
        pnlBody.Controls.Add(txtSpec);
        y += FH + GAP;

        // ── 5. Harga (Rp) + Tahun Harga side-by-side ─────────────────────
        const int yearW = 104;
        const int hargaW = fw - yearW - 12;   // gap of 12 between the two fields
        MkLbl("Harga (Rp) *", y);
        MkLbl("Tahun Harga", y, M + hargaW + 12);   // right label, same row
        y += LH;

        numPrice = new NumericUpDown
        {
            Location           = new Point(M, y),
            Width              = hargaW,
            Height             = FH,
            Minimum            = 0,
            Maximum            = 999_999_999,
            DecimalPlaces      = 0,
            ThousandsSeparator = true,
            Font               = AppTheme.FontBase
        };
        pnlBody.Controls.Add(numPrice);

        txtYear = new TextBox
        {
            Location        = new Point(M + hargaW + 12, y),
            Width           = yearW,
            Font            = AppTheme.FontBase,
            MaxLength       = 4,
            PlaceholderText = "2025"
        };
        AppTheme.StyleTextBox(txtYear);
        pnlBody.Controls.Add(txtYear);
        y += FH + GAP;

        // ── 6. Status Stok ────────────────────────────────────────────────
        MkLbl("Status Stok *", y);
        y += LH;
        cmbStock = new ComboBox
        {
            Location      = new Point(M, y),
            Width         = 220,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font          = AppTheme.FontBase
        };
        AppTheme.StyleComboBox(cmbStock);
        cmbStock.Items.Add("1 — Stock (tersedia)");
        cmbStock.Items.Add("2 — Indent (pesan dulu)");
        cmbStock.SelectedIndex = 0;
        pnlBody.Controls.Add(cmbStock);
        y += FH + GAP;

        // ── 7. Vendor / Merk ──────────────────────────────────────────────
        MkLbl("Vendor / Merk  (opsional)", y);
        y += LH;
        txtVendor = new TextBox { Location = new Point(M, y), Width = fw, Font = AppTheme.FontBase };
        AppTheme.StyleTextBox(txtVendor);
        pnlBody.Controls.Add(txtVendor);
        y += FH + GAP + 8;

        // Spacer so AutoScroll knows the content height
        pnlBody.Controls.Add(new Panel { Location = new Point(M, y), Height = 1, Width = 1 });

        // ── Footer ────────────────────────────────────────────────────────
        var pnlFoot = new Panel { Dock = DockStyle.Bottom, Height = 52, BackColor = Color.White };
        pnlFoot.Paint += (s, e) =>
        {
            using var pen = new Pen(AppTheme.Border);
            e.Graphics.DrawLine(pen, 0, 0, pnlFoot.Width, 0);
        };

        var btnSave = new Button
        {
            Text         = "💾 Simpan",
            Width        = 110,
            Height       = 34,
            DialogResult = DialogResult.None
        };
        AppTheme.StyleButton(btnSave, AppTheme.Primary, Color.White);
        btnSave.Click += BtnSave_Click;

        var btnCancel = new Button
        {
            Text         = "Batal",
            Width        = 80,
            Height       = 34,
            DialogResult = DialogResult.Cancel
        };
        AppTheme.StyleButton(btnCancel, Color.FromArgb(229, 231, 235), AppTheme.TextPrimary);

        pnlFoot.Layout += (s, e) =>
        {
            int rx      = pnlFoot.Width - M;
            btnCancel.Top  = (pnlFoot.Height - btnCancel.Height) / 2;
            btnCancel.Left = rx - btnCancel.Width;
            btnSave.Top    = btnCancel.Top;
            btnSave.Left   = btnCancel.Left - btnSave.Width - 8;
        };
        pnlFoot.Controls.AddRange(new Control[] { btnSave, btnCancel });

        CancelButton = btnCancel;
        Controls.Add(pnlBody);
        Controls.Add(pnlFoot);
        Controls.Add(pnlHead);
    }

    // ════════════════════════════════════════════════════════════════════
    private void PopulateForEdit(Product p)
    {
        cmbCategory.Text       = p.Category;
        txtRef.Text            = p.ReferenceCode;
        txtRef.Enabled         = false;    // PK — tidak boleh diubah
        txtName.Text           = p.ProductName;
        txtSpec.Text           = p.Specifications ?? "";
        numPrice.Value         = Math.Clamp(p.Price, 0, 999_999_999);
        txtYear.Text           = p.PriceYear.HasValue ? p.PriceYear.Value.ToString() : "";
        cmbStock.SelectedIndex = p.StockStatus == 2 ? 1 : 0;
        txtVendor.Text         = p.Vendor ?? "";
    }

    // ════════════════════════════════════════════════════════════════════
    private void BtnSave_Click(object? sender, EventArgs e)
    {
        var cat = cmbCategory.Text.Trim();
        var rfc = txtRef.Text.Trim();
        var nm  = txtName.Text.Trim();

        if (string.IsNullOrWhiteSpace(cat))
        { Warn("Kategori wajib diisi.", cmbCategory); return; }
        if (string.IsNullOrWhiteSpace(rfc))
        { Warn("Kode Referensi wajib diisi.", txtRef); return; }
        if (string.IsNullOrWhiteSpace(nm))
        { Warn("Nama Produk wajib diisi.", txtName); return; }

        int? yr = null;
        if (!string.IsNullOrWhiteSpace(txtYear.Text))
        {
            if (!int.TryParse(txtYear.Text.Trim(), out var yv) || yv < 1990 || yv > 2100)
            { Warn("Tahun Harga harus 4 digit yang valid (contoh: 2025).", txtYear); return; }
            yr = yv;
        }

        OutCategory       = cat;
        OutReferenceCode  = rfc;
        OutProductName    = nm;
        OutSpecifications = string.IsNullOrWhiteSpace(txtSpec.Text) ? null : txtSpec.Text.Trim();
        OutPrice          = numPrice.Value;
        OutPriceYear      = yr;
        OutStockStatus    = cmbStock.SelectedIndex == 1 ? 2 : 1;
        OutVendor         = string.IsNullOrWhiteSpace(txtVendor.Text) ? null : txtVendor.Text.Trim();

        DialogResult = DialogResult.OK;
        Close();
    }

    private void Warn(string msg, Control focus)
    {
        MessageBox.Show(msg, "Validasi", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        focus.Focus();
    }
}
