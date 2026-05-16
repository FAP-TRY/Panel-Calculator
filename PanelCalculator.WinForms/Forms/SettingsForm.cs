using ClosedXML.Excel;
using PanelCalculator.Core.Models;
using PanelCalculator.Data;
using PanelCalculator.Data.DataSeeding;
using PanelCalculator.WinForms.Services;
using PanelCalculator.WinForms.Theme;
using System.Security.Cryptography;
using System.Text;

namespace PanelCalculator.WinForms.Forms;

public class SettingsForm : Form
{
    private readonly PanelCalculatorContext _context;

    private Label        lblProductCount  = null!;
    private Label        lblLastSync      = null!;
    private Button       btnSync          = null!;
    private DataGridView dgvUsers         = null!;
    private List<User>   _users           = new();
    private string       _lastImportPath  = "";

    // Role-visibility
    private Panel  _pnlUserSection  = null!;
    private Button _btnClose        = null!;

    // Update section
    private Label  _lblUpdateResult = null!;

    /// <summary>Set by the caller (MainForm) before ShowDialog so the form
    /// can hide the user-management section for non-Admin users.</summary>
    public User? CurrentUser { get; set; }

    public SettingsForm(PanelCalculatorContext context)
    {
        _context = context;
        BuildUI();
    }

    // ════════════════════════════════════════════════════════════════════
    private void BuildUI()
    {
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode       = AutoScaleMode.Dpi;
        Text            = "Pengaturan";
        Size            = new Size(720, 680);
        MinimumSize     = new Size(640, 560);
        StartPosition   = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox     = false;
        BackColor       = AppTheme.Background;

        // ── Title ────────────────────────────────────────────────────────
        var pnlHead = new Panel { Dock = DockStyle.Top, Height = 52, BackColor = AppTheme.BgHeader };
        pnlHead.Paint += (s, e) =>
        {
            using var pen = new Pen(AppTheme.Border2);
            e.Graphics.DrawLine(pen, 0, pnlHead.Height - 1, pnlHead.Width, pnlHead.Height - 1);
        };
        var lblTitle = AppTheme.MakeLabel("⚙  Pengaturan", AppTheme.FontLarge, AppTheme.TextPrimary);
        lblTitle.Location = new Point(20, 13);
        lblTitle.AutoSize = true;
        pnlHead.Controls.Add(lblTitle);

        // ── Body ─────────────────────────────────────────────────────────
        var pnlBody = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(20, 14, 20, 14) };

        // ─────────────────────────────────────────────────────────────────
        //  SECTION 1: Import Data Produk
        // ─────────────────────────────────────────────────────────────────
        var lblImportTitle = AppTheme.MakeLabel("📦  Import Data Produk", AppTheme.FontBold, AppTheme.TextPrimary);
        lblImportTitle.Location = new Point(20, 0);
        lblImportTitle.AutoSize = true;

        lblProductCount = AppTheme.MakeLabel("...", AppTheme.FontSmall, AppTheme.TextSecondary);
        lblProductCount.Location = new Point(20, 24);
        lblProductCount.AutoSize = true;

        var btnImport = new Button { Text = "📂 Import CSV / Excel", Location = new Point(20, 46), Width = 192, Height = 34 };
        AppTheme.StyleButton(btnImport, AppTheme.Primary, Color.White);
        btnImport.Click += BtnImport_Click;

        btnSync = new Button { Text = "🔄 Sync Ulang", Location = new Point(220, 46), Width = 140, Height = 34, Enabled = false };
        AppTheme.StyleButton(btnSync, AppTheme.Success500, Color.White);
        btnSync.Click += BtnSync_Click;

        lblLastSync = AppTheme.MakeLabel("Belum ada riwayat import.", AppTheme.FontSmall, AppTheme.TextMuted);
        lblLastSync.Location = new Point(20, 88);
        lblLastSync.AutoSize = true;

        var lblHint = new Label
        {
            Text      = "Format kolom: category · reference_code · product_name · specifications · price · price_year · stock_status · vendor",
            Font      = AppTheme.FontSmall,
            ForeColor = AppTheme.TextMuted,
            Location  = new Point(20, 108),
            AutoSize  = false,
            Width     = 640,
            Height    = 28
        };

        var sep = new Panel { Location = new Point(20, 140), Height = 1, Width = 640, BackColor = AppTheme.Border };

        // ─────────────────────────────────────────────────────────────────
        //  SECTION 2: Tentang Aplikasi & Pembaruan
        // ─────────────────────────────────────────────────────────────────
        var lblUpdateTitle = AppTheme.MakeLabel("🔄  Tentang Aplikasi & Pembaruan", AppTheme.FontBold, AppTheme.TextPrimary);
        lblUpdateTitle.Location = new Point(20, 152);
        lblUpdateTitle.AutoSize = true;

        var lblCurrentVersion = AppTheme.MakeLabel(
            $"Versi aplikasi saat ini:  v{UpdateService.AppVersion}",
            AppTheme.FontSmall, AppTheme.TextSecondary);
        lblCurrentVersion.Location = new Point(20, 174);
        lblCurrentVersion.AutoSize = true;

        var btnCheckUpdate = new Button
        {
            Text     = "🔍 Cek Update",
            Location = new Point(20, 198),
            Width    = 140,
            Height   = 32
        };
        AppTheme.StyleButton(btnCheckUpdate, AppTheme.Primary, Color.White);
        btnCheckUpdate.Click += BtnCheckUpdate_Click;

        _lblUpdateResult = AppTheme.MakeLabel("", AppTheme.FontSmall, AppTheme.TextSecondary);
        _lblUpdateResult.Location = new Point(170, 207);
        _lblUpdateResult.AutoSize = true;

        var sep2 = new Panel { Location = new Point(20, 242), Height = 1, Width = 640, BackColor = AppTheme.Border };

        // ─────────────────────────────────────────────────────────────────
        //  SECTION 3: Manajemen Pengguna (Admin only — hidden for Operators)
        // ─────────────────────────────────────────────────────────────────
        _pnlUserSection = new Panel
        {
            Location    = new Point(20, 254),
            Size        = new Size(640, 352),
            BackColor   = Color.Transparent
        };

        var lblUserTitle = AppTheme.MakeLabel("👥  Manajemen Pengguna", AppTheme.FontBold, AppTheme.TextPrimary);
        lblUserTitle.Location = new Point(0, 0);    // was (0, 152)
        lblUserTitle.AutoSize = true;

        var lblSec = AppTheme.MakeLabel(
            "Hanya pengguna terdaftar yang dapat login. Tambahkan atau nonaktifkan akun di sini.",
            AppTheme.FontSmall, AppTheme.TextSecondary);
        lblSec.Location = new Point(0, 20);         // was (0, 172)
        lblSec.AutoSize = true;

        dgvUsers = new DataGridView
        {
            Location           = new Point(0, 44),  // was (0, 196)
            Height             = 258,
            Width              = 640,
            Anchor             = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            ReadOnly           = true,
            AllowUserToAddRows = false
        };
        AppTheme.StyleGrid(dgvUsers);
        dgvUsers.Columns.Add(new DataGridViewTextBoxColumn { Name = "ColId",       HeaderText = "ID",             FillWeight = 5  });
        dgvUsers.Columns.Add(new DataGridViewTextBoxColumn { Name = "ColUsername",  HeaderText = "Username",       FillWeight = 18 });
        dgvUsers.Columns.Add(new DataGridViewTextBoxColumn { Name = "ColFullName",  HeaderText = "Nama Lengkap",   FillWeight = 28 });
        dgvUsers.Columns.Add(new DataGridViewTextBoxColumn { Name = "ColRole",      HeaderText = "Role",           FillWeight = 12 });
        dgvUsers.Columns.Add(new DataGridViewTextBoxColumn { Name = "ColActive",    HeaderText = "Status",         FillWeight = 10 });
        dgvUsers.Columns.Add(new DataGridViewTextBoxColumn { Name = "ColCreated",   HeaderText = "Dibuat",         FillWeight = 14 });
        dgvUsers.Columns.Add(new DataGridViewTextBoxColumn { Name = "ColLastLogin", HeaderText = "Login Terakhir", FillWeight = 18 });
        dgvUsers.CellDoubleClick += (s, e) => { if (e.RowIndex >= 0) EditUser(GetSelectedUser()); };

        const int bY = 310;                         // was 462; offset by -152
        var btnAddUser = new Button { Text = "➕ Tambah User", Location = new Point(0, bY),   Width = 140, Height = 32 };
        AppTheme.StyleButton(btnAddUser, AppTheme.Primary, Color.White);
        btnAddUser.Click += BtnAddUser_Click;

        var btnEditUser = new Button { Text = "✏ Edit",          Location = new Point(148, bY), Width = 90,  Height = 32 };
        AppTheme.StyleButton(btnEditUser, AppTheme.Bg2, AppTheme.Text2);
        btnEditUser.Click += (s, e) => EditUser(GetSelectedUser());

        var btnToggle = new Button { Text = "🔒 Nonaktifkan",    Location = new Point(246, bY), Width = 130, Height = 32 };
        AppTheme.StyleButton(btnToggle, AppTheme.Warning, Color.White);
        btnToggle.Click += BtnToggle_Click;

        var btnResetPw = new Button { Text = "🔑 Reset Password", Location = new Point(384, bY), Width = 148, Height = 32 };
        AppTheme.StyleButton(btnResetPw, AppTheme.Danger, Color.White);
        btnResetPw.Click += BtnResetPw_Click;

        var lblHint2 = AppTheme.MakeLabel("Klik 2× untuk edit.", AppTheme.FontSmall, AppTheme.TextMuted);
        lblHint2.Location = new Point(540, bY + 8);
        lblHint2.AutoSize = true;

        _pnlUserSection.Controls.AddRange(new Control[]
        {
            lblUserTitle, lblSec, dgvUsers,
            btnAddUser, btnEditUser, btnToggle, btnResetPw, lblHint2
        });

        // ── "Tutup" button — always visible; positioned by ApplyRoleVisibility ──
        _btnClose = new Button { Text = "Tutup", Width = 90, Height = 32 };
        AppTheme.StyleButton(_btnClose, AppTheme.Bg2, AppTheme.Text2);
        _btnClose.Click += (s, e) => Close();

        pnlBody.Controls.AddRange(new Control[]
        {
            lblImportTitle, lblProductCount, btnImport, btnSync, lblLastSync, lblHint, sep,
            lblUpdateTitle, lblCurrentVersion, btnCheckUpdate, _lblUpdateResult, sep2,
            _pnlUserSection, _btnClose
        });
        Controls.Add(pnlBody);
        Controls.Add(pnlHead);

        Load += (s, e) =>
        {
            UpdateProductCount();
            ReloadUsers();
            LoadLastImportPath();
            ApplyRoleVisibility();
        };
    }

    // ════════════════════════════════════════════════════════════════════
    //  ROLE VISIBILITY
    // ════════════════════════════════════════════════════════════════════
    private void ApplyRoleVisibility()
    {
        bool isAdmin = CurrentUser?.Role == "Admin";

        _pnlUserSection.Visible = isAdmin;

        if (isAdmin)
        {
            // Import + Update + User management
            // _pnlUserSection at y=254, height=352 → bottom at 606
            _btnClose.Location = new Point(570, 254 + 352 + 10);  // y = 616
            Size        = new Size(720, 740);
            MinimumSize = new Size(640, 620);
        }
        else
        {
            // Import + Update sections only
            _btnClose.Location = new Point(570, 260);
            Size        = new Size(720, 380);
            MinimumSize = new Size(640, 320);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  UPDATE
    // ════════════════════════════════════════════════════════════════════
    private async void BtnCheckUpdate_Click(object? sender, EventArgs e)
    {
        var btn = (Button)sender!;
        btn.Enabled            = false;
        btn.Text               = "⏳ Memeriksa...";
        _lblUpdateResult.Text  = "";

        try
        {
            var info = await UpdateService.CheckAsync();

            if (info == null)
            {
                _lblUpdateResult.Text      = "✔ Sudah versi terbaru";
                _lblUpdateResult.ForeColor = AppTheme.Success;
            }
            else
            {
                _lblUpdateResult.Text      = $"⬆ v{info.Version} tersedia!";
                _lblUpdateResult.ForeColor = Color.FromArgb(251, 191, 36); // amber

                var confirm = MessageBox.Show(
                    $"Pembaruan  v{info.Version}  tersedia!\n\n" +
                    $"Aplikasi akan diunduh lalu diperbarui secara otomatis.\n" +
                    $"Data di database Anda tidak terpengaruh.\n\n" +
                    $"Lanjutkan update sekarang?",
                    "Update Tersedia",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Information,
                    MessageBoxDefaultButton.Button1);

                if (confirm == DialogResult.Yes)
                    await DoUpdateFromSettings(info);
            }
        }
        catch
        {
            _lblUpdateResult.Text      = "✘ Gagal memeriksa — cek koneksi internet";
            _lblUpdateResult.ForeColor = AppTheme.Danger;
        }
        finally
        {
            btn.Enabled = true;
            btn.Text    = "🔍 Cek Update";
        }
    }

    private async Task DoUpdateFromSettings(UpdateService.ReleaseInfo info)
    {
        using var prog = new Form
        {
            Text            = "Mengunduh Pembaruan...",
            Size            = new Size(420, 140),
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition   = FormStartPosition.CenterParent,
            MaximizeBox     = false, MinimizeBox = false,
            BackColor       = AppTheme.Background
        };
        var bar = new ProgressBar { Dock = DockStyle.Top, Height = 24, Minimum = 0, Maximum = 100 };
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

        var progress = new Progress<(int Percent, string Status)>(p =>
        {
            if (!prog.IsDisposed)
            {
                bar.Value = Math.Clamp(p.Percent, 0, 100);
                lbl.Text  = p.Status;
                Application.DoEvents();
            }
        });

        try
        {
            await UpdateService.DownloadAndApplyAsync(info, progress);
            // Application exits inside DownloadAndApplyAsync — line below never reached
        }
        catch (Exception ex)
        {
            prog.Close();
            MessageBox.Show(
                $"Download gagal:\n{ex.Message}\n\nSilakan download manual dari:\n{info.HtmlUrl}",
                "Update Gagal", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  IMPORT – Settings persistence
    // ════════════════════════════════════════════════════════════════════
    private void UpdateProductCount()
    {
        try
        {
            var count = _context.Products.Count();
            lblProductCount.Text = count > 0
                ? $"Database: {count} produk tersedia"
                : "Database: belum ada produk — import file untuk mulai";
            lblProductCount.ForeColor = count > 0 ? AppTheme.Success : AppTheme.Warning;
        }
        catch { }
    }

    private void LoadLastImportPath()
    {
        try
        {
            var pathSetting = _context.Settings.Find("LastImportPath");
            var timeSetting = _context.Settings.Find("LastImportTime");

            if (pathSetting?.SettingValue is string path && File.Exists(path))
            {
                _lastImportPath = path;
                var timeStr = timeSetting?.SettingValue ?? "";
                lblLastSync.Text = $"Terakhir: {Path.GetFileName(path)}" + (timeStr.Length > 0 ? $"  ·  {timeStr}" : "");
                lblLastSync.ForeColor = AppTheme.Success;
                btnSync.Enabled = true;
            }
            else if (pathSetting?.SettingValue is string missing)
            {
                lblLastSync.Text      = $"File tidak ditemukan: {Path.GetFileName(missing)}";
                lblLastSync.ForeColor = AppTheme.Warning;
            }
        }
        catch { }
    }

    private void SaveLastImportPath(string path)
    {
        try
        {
            UpsertSetting("LastImportPath", path);
            UpsertSetting("LastImportTime", DateTime.Now.ToString("dd MMM yyyy HH:mm"));
            _context.SaveChanges();
        }
        catch { }
    }

    private void UpsertSetting(string key, string value)
    {
        var s = _context.Settings.Find(key);
        if (s == null) _context.Settings.Add(new AppSettings { SettingKey = key, SettingValue = value });
        else           s.SettingValue = value;
    }

    // ════════════════════════════════════════════════════════════════════
    //  IMPORT – Button handlers
    // ════════════════════════════════════════════════════════════════════
    private async void BtnImport_Click(object? sender, EventArgs e)
    {
        using var ofd = new OpenFileDialog
        {
            Title  = "Pilih file produk (CSV atau Excel)",
            Filter = "Spreadsheet & CSV|*.xlsx;*.xls;*.csv|Excel (*.xlsx;*.xls)|*.xlsx;*.xls|CSV (*.csv)|*.csv|Semua File|*.*"
        };
        if (ofd.ShowDialog() != DialogResult.OK) return;
        await RunImport(ofd.FileName, (Button)sender!);
    }

    private async void BtnSync_Click(object? sender, EventArgs e)
    {
        if (string.IsNullOrEmpty(_lastImportPath) || !File.Exists(_lastImportPath))
        {
            MessageBox.Show("File sumber tidak ditemukan.\nGunakan tombol Import untuk memilih file baru.",
                "File Tidak Ada", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            btnSync.Enabled = false;
            return;
        }
        if (MessageBox.Show($"Update database dari:\n{_lastImportPath}\n\nLanjutkan?",
                "Konfirmasi Sync", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

        await RunImport(_lastImportPath, btnSync);
    }

    // ════════════════════════════════════════════════════════════════════
    //  IMPORT – Core orchestrator
    // ════════════════════════════════════════════════════════════════════
    private async Task RunImport(string filePath, Button callerBtn)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        string vendorOverride = "";

        // ── For Excel: decide vendor BEFORE touching UI ───────────────────
        if (ext is ".xlsx" or ".xls")
        {
            var hdrKeys     = GetExcelHeaderKeys(filePath);
            bool hasVendor  = hdrKeys.Any(h =>
                h.Contains("vendor") || h.Contains("merk") || h.Contains("brand") ||
                h.Contains("supplier") || h.Contains("merek"));

            if (!hasVendor)
            {
                var existing = _context.Products
                    .Where(p => p.Vendor != null && p.Vendor != "")
                    .Select(p => p.Vendor!).Distinct().OrderBy(v => v).ToList();

                var chosen = ShowVendorDialog(existing);
                if (chosen == null) return;     // user cancelled
                vendorOverride = chosen;
            }
        }

        // ── Disable UI, run import ────────────────────────────────────────
        var origText      = callerBtn.Text;
        callerBtn.Enabled = false;
        callerBtn.Text    = "⏳ Mengimport...";
        btnSync.Enabled   = false;

        try
        {
            int count;
            if (ext is ".xlsx" or ".xls")
            {
                var records = await Task.Run(() => ReadExcelRecords(filePath, vendorOverride));
                count = await new ProductSeeder(_context).SeedFromRecordsAsync(records);
            }
            else
            {
                count = await new ProductSeeder(_context).SeedFromCsvAsync(filePath);
            }

            _lastImportPath = filePath;
            SaveLastImportPath(filePath);

            lblLastSync.Text      = $"Terakhir: {Path.GetFileName(filePath)}  ·  {DateTime.Now:dd MMM yyyy HH:mm}";
            lblLastSync.ForeColor = AppTheme.Success;
            btnSync.Enabled       = true;
            UpdateProductCount();

            MessageBox.Show($"Berhasil import / update {count} produk dari:\n{Path.GetFileName(filePath)}",
                "Import Selesai", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Gagal import:\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            callerBtn.Enabled = true;
            callerBtn.Text    = origText;
            btnSync.Enabled   = !string.IsNullOrEmpty(_lastImportPath) && File.Exists(_lastImportPath);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  IMPORT – Inline vendor-select dialog
    // ════════════════════════════════════════════════════════════════════
    private string? ShowVendorDialog(List<string> existingVendors)
    {
        using var f = new Form
        {
            Text            = "Pilih Vendor",
            Size            = new Size(400, 195),
            StartPosition   = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox     = false,
            MinimizeBox     = false,
            BackColor       = AppTheme.Background
        };

        var lbl = AppTheme.MakeLabel(
            "File Excel ini tidak memiliki kolom Vendor.\nPilih atau ketik nama vendor untuk semua produk di file ini:",
            AppTheme.FontSmall, AppTheme.TextPrimary);
        lbl.Location = new Point(16, 14);
        lbl.Width    = 360;
        lbl.Height   = 36;

        var cmb = new ComboBox
        {
            Location       = new Point(16, 58),
            Width          = 352,
            Font           = AppTheme.FontBase,
            DropDownStyle  = ComboBoxStyle.DropDown
        };
        foreach (var v in existingVendors) cmb.Items.Add(v);

        var btnOK = new Button { Text = "Import", Location = new Point(184, 112), Width = 90, Height = 32, DialogResult = DialogResult.OK };
        AppTheme.StyleButton(btnOK, AppTheme.Primary, Color.White);

        var btnCancel = new Button { Text = "Batal", Location = new Point(282, 112), Width = 86, Height = 32, DialogResult = DialogResult.Cancel };
        AppTheme.StyleButton(btnCancel, AppTheme.Bg2, AppTheme.Text2);

        f.AcceptButton = btnOK;
        f.CancelButton = btnCancel;
        f.Controls.AddRange(new Control[] { lbl, cmb, btnOK, btnCancel });

        return f.ShowDialog(this) == DialogResult.OK ? cmb.Text.Trim() : null;
    }

    // ════════════════════════════════════════════════════════════════════
    //  IMPORT – Excel parsing (runs on background thread)
    // ════════════════════════════════════════════════════════════════════

    /// <summary>Quickly reads only the first row of the Excel to get header names.</summary>
    private static List<string> GetExcelHeaderKeys(string path)
    {
        try
        {
            using var wb = new XLWorkbook(path);
            var ws = wb.Worksheet(1);
            return ws.Row(1).CellsUsed()
                     .Select(c => NormKey(c.GetString()))
                     .Where(k => k.Length > 0)
                     .ToList();
        }
        catch { return new List<string>(); }
    }

    /// <summary>
    /// Main Excel reader. Handles two formats:
    ///  1) Flat (same columns as CSV: category, reference_code, product_name, …)
    ///  2) Pricelist layout (e.g. Schneider PDF → Excel):
    ///       Section header rows (green/merged) give the product family name.
    ///       Spec columns (Sensitivitas, Kutub, Pengenal A, …) may use merged cells.
    ///       No product_name or vendor column; those are derived.
    /// </summary>
    private static List<ProductSeeder.ProductCsvRecord> ReadExcelRecords(
        string path, string vendorOverride)
    {
        using var wb = new XLWorkbook(path);
        var ws      = wb.Worksheet(1);
        int lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;

        // ── Find the header row (first row containing a reference-code column) ──
        int hdrRow = FindHeaderRow(ws, lastRow);

        // ── Map all column headers ────────────────────────────────────────────
        var colMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);   // normKey → colNum
        foreach (var cell in ws.Row(hdrRow).CellsUsed())
        {
            var k = NormKey(cell.GetString());
            if (!string.IsNullOrEmpty(k) && !colMap.ContainsKey(k))
                colMap[k] = cell.Address.ColumnNumber;
        }

        int Col(params string[] aliases)
        {
            foreach (var a in aliases)
                if (colMap.TryGetValue(a, out var c)) return c;
            // also try Contains match for partial keys like "pengenal" → "pengenala"
            foreach (var a in aliases)
                foreach (var (k, v) in colMap)
                    if (k.StartsWith(a) || k.Contains(a)) return v;
            return -1;
        }

        // ── Essential columns ─────────────────────────────────────────────────
        int colRef   = Col("referensi", "referencecode", "kode", "code", "ref", "itemcode");
        int colPrice = Col("hargarp", "harga", "price", "unitprice");
        int colYear  = Col("priceyear", "tahunharga", "tahun", "year", "thn");
        int colSS    = Col("ss", "stockstatus", "stok", "stock");
        int colCat   = Col("category", "kategori", "cat");
        int colName  = Col("productname", "namaproduct", "nama", "product", "name");
        int colVend  = Col("vendor", "merk", "brand", "supplier", "merek");

        // ── Detect format ─────────────────────────────────────────────────────
        // Pricelist mode: has spec-type columns (kutub/sensitivitas/pengenal) and NO product-name col
        bool isPricelist = colName < 0 &&
                           colMap.Keys.Any(k =>
                               k.Contains("kutub") || k.Contains("sensitivitas") ||
                               k.Contains("pengenal") || k.Contains("pole") ||
                               k.Contains("sens"));

        // ── Collect spec columns (pricelist mode) ────────────────────────────
        // All columns except essential ones → build specification string from these
        var essentialCols = new HashSet<int?> { colRef, colPrice, colYear, colSS, colCat, colVend };
        var specColList   = colMap
            .Where(kv => !essentialCols.Contains(kv.Value))
            .OrderBy(kv => kv.Value)
            .ToList();   // (normKey, colNum) in left-to-right order

        // ── Parse data rows ───────────────────────────────────────────────────
        var result      = new List<ProductSeeder.ProductCsvRecord>();
        var lastSeen    = new Dictionary<int, string>();   // colNum → last non-empty value (merged-cell trick)
        string family   = "";      // current product family from section header
        string familyCat = "";     // category derived from family name

        for (int r = hdrRow + 1; r <= lastRow; r++)
        {
            string Get(int col) => col > 0 ? ws.Cell(r, col).GetString().Trim() : "";

            var refCode = Get(colRef);

            if (string.IsNullOrWhiteSpace(refCode))
            {
                // Might be a section-header row (product family).
                // Look for non-empty text in the first few columns.
                for (int c = 1; c <= Math.Min(colRef > 0 ? colRef - 1 : 6, 6); c++)
                {
                    var txt = ws.Cell(r, c).GetString().Trim();
                    if (txt.Length > 2)
                    {
                        family    = txt;
                        familyCat = FamilyToCategory(txt);
                        lastSeen.Clear();   // reset merge tracking at each new section
                        break;
                    }
                }
                continue;
            }

            // ── Get price ──────────────────────────────────────────────────
            decimal price = 0;
            if (colPrice > 0)
            {
                var pc = ws.Cell(r, colPrice);
                if (pc.DataType == XLDataType.Number)
                    price = (decimal)pc.GetDouble();
                else
                {
                    var raw = pc.GetString().Replace("Rp", "").Replace(" ", "")
                               .Replace(".", "").Replace(",", "").Trim();
                    decimal.TryParse(raw, out price);
                }
            }

            // ── Get stock status ───────────────────────────────────────────
            int stockStatus = 1;
            if (colSS > 0)
            {
                var sc = ws.Cell(r, colSS);
                if (sc.DataType == XLDataType.Number)
                    stockStatus = (int)sc.GetDouble();
                else
                    int.TryParse(sc.GetString().Trim(), out stockStatus);
                stockStatus = Math.Clamp(stockStatus, 1, 2);
            }

            // ── Get price year ─────────────────────────────────────────────
            int? priceYear = null;
            if (colYear > 0)
            {
                var yc = ws.Cell(r, colYear);
                if (yc.DataType == XLDataType.Number)
                    priceYear = (int)yc.GetDouble();
                else if (int.TryParse(yc.GetString().Trim(), out var yi) && yi > 1990 && yi < 2100)
                    priceYear = yi;
            }

            // ── Get vendor ─────────────────────────────────────────────────
            string vendor = vendorOverride;
            if (string.IsNullOrEmpty(vendor) && colVend > 0)
                vendor = Get(colVend);

            if (isPricelist)
            {
                // ── Pricelist mode: build spec string from spec columns ────
                // Track last-seen for merged cells (empty = use last seen value for that column)
                var specParts = new List<string>();
                foreach (var (colKey, colNum) in specColList)
                {
                    var raw = ws.Cell(r, colNum).GetString().Trim();
                    if (!string.IsNullOrEmpty(raw))
                        lastSeen[colNum] = raw;

                    var val = lastSeen.TryGetValue(colNum, out var prev) ? prev : "";
                    if (string.IsNullOrEmpty(val)) continue;

                    specParts.Add(FormatSpecValue(colKey, val));
                }

                var specStr  = string.Join(" ", specParts.Where(p => p.Length > 0));
                var prodName = string.IsNullOrEmpty(family) ? refCode
                             : string.IsNullOrEmpty(specStr) ? family
                             : $"{family} - {specStr}";

                var cat = string.IsNullOrEmpty(familyCat) ? "Other" : familyCat;

                result.Add(new ProductSeeder.ProductCsvRecord
                {
                    Category       = cat,
                    ReferenceCode  = refCode,
                    ProductName    = prodName.Length > 250 ? prodName[..250] : prodName,
                    Specifications = specStr.Length > 0 ? specStr : null,
                    Price          = price,
                    PriceYear      = priceYear,
                    StockStatus    = stockStatus,
                    Vendor         = vendor.NullIfEmpty()
                });
            }
            else
            {
                // ── Flat mode: standard column mapping ────────────────────
                var cat  = colCat  > 0 ? Get(colCat)  : family.Length > 0 ? familyCat : "Other";
                var name = colName > 0 ? Get(colName) : refCode;

                if (string.IsNullOrWhiteSpace(cat))  cat  = "Other";
                if (string.IsNullOrWhiteSpace(name)) name = refCode;

                // Spec: combine all non-essential columns that have values
                var specParts = new List<string>();
                foreach (var (colKey, colNum) in specColList)
                {
                    if (colNum == colName || colNum == colCat) continue;
                    var v = Get(colNum);
                    if (!string.IsNullOrEmpty(v)) specParts.Add(v);
                }
                var specStr = specParts.Count > 0 ? string.Join(" ", specParts) : null;

                result.Add(new ProductSeeder.ProductCsvRecord
                {
                    Category       = cat,
                    ReferenceCode  = refCode,
                    ProductName    = name.Length > 250 ? name[..250] : name,
                    Specifications = specStr,
                    Price          = price,
                    PriceYear      = priceYear,
                    StockStatus    = stockStatus,
                    Vendor         = vendor.NullIfEmpty()
                });
            }
        }

        return result;
    }

    // ── Find the row whose cells include a reference-code-style column ────────
    private static int FindHeaderRow(IXLWorksheet ws, int maxRow)
    {
        for (int r = 1; r <= Math.Min(maxRow, 15); r++)
        {
            var keys = ws.Row(r).CellsUsed()
                         .Select(c => NormKey(c.GetString()))
                         .ToList();

            bool hasRef = keys.Any(k =>
                k is "referensi" || k.Contains("referencecode") || k is "ref" ||
                k is "itemcode" || k is "kode" || k is "code");

            if (hasRef) return r;
        }
        return 1;
    }

    // ── Normalize header key: lowercase, letters+digits only ─────────────────
    private static string NormKey(string s)
        => new string(s.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    // ── Derive category from product family name ──────────────────────────────
    private static string FamilyToCategory(string family)
    {
        var t = family.ToLowerInvariant();
        if (t.Contains("rccb") || t.Contains("rcbo") || t.Contains("elcb") || t.Contains("residual") || t.Contains("iid")) return "RCCB";
        if (t.Contains("acb")  || t.Contains("air circuit") || t.Contains("masterpact") || t.Contains("nw")) return "ACB";
        if (t.Contains("mccb") || t.Contains("molded") || t.Contains("gopact") || t.Contains("cvs") ||
            t.Contains("nsx")  || t.Contains("nm1")    || t.Contains("nm8")    || t.Contains("nc100")) return "MCCB";
        if (t.Contains("mcb")  || t.Contains("miniature") || t.Contains("easy9") || t.Contains("domae") ||
            t.Contains("nxb")  || t.Contains("nb1")    || t.Contains("nb3")    || t.Contains("nb4")) return "MCB";
        if (t.Contains("kontaktor") || t.Contains("contactor") || t.Contains("tesys") ||
            t.Contains("lc1")  || t.Contains("lc3")    || t.Contains("nc1")    || t.Contains("nc2")) return "Kontaktor";
        if (t.Contains("motor cb") || t.Contains("motor circuit") || t.Contains("gv2") || t.Contains("gv3")) return "Motor CB";
        if (t.Contains("surge") || t.Contains("spd") || t.Contains("lightning") || t.Contains("arrester")) return "Surge Arrester";
        if (t.Contains("vsd")   || t.Contains("inverter") || t.Contains("variable speed") || t.Contains("nv")) return "VSD";
        if (t.Contains("ats")   || t.Contains("transfer switch") || t.Contains("nz7")) return "ATS";
        if (t.Contains("busbar") || t.Contains("isobar") || t.Contains("linergy")) return "Busbar";
        if (t.Contains("box")   || t.Contains("pragma") || t.Contains("enclosure")) return "Box";
        return "Other";
    }

    // ── Format a spec column value based on the column's semantic type ─────────
    private static string FormatSpecValue(string colKey, string val)
    {
        if (string.IsNullOrWhiteSpace(val)) return "";

        // Kutub / Poles  →  "2" or "2 Kutub" → "2P"
        if (colKey.Contains("kutub") || colKey.Contains("pole"))
        {
            var digits = new string(val.Where(char.IsDigit).ToArray());
            return digits.Length > 0 ? digits + "P" : val.Trim();
        }

        // Pengenal (A) / Arus / Ampere rating  →  "16" or "16 A" → "16A"
        if (colKey.StartsWith("pengenal") || colKey is "in" or "ina" or "ia" ||
            colKey.Contains("arus") || colKey.Contains("rating") || colKey.Contains("amp"))
        {
            var numStr = new string(val.Where(c => char.IsDigit(c) || c == '.').ToArray());
            if (numStr.Length > 0 && decimal.TryParse(numStr, out _))
                return numStr + "A";
            return val.Trim();
        }

        // Sensitivitas / Sensitivity  →  "30 mA" → "30mA"
        if (colKey.Contains("sensitivitas") || colKey.Contains("sensitivity") || colKey.StartsWith("sens"))
            return val.Trim().Replace(" ", "");

        // Tegangan (V) / Voltage  →  "220" → "220V"
        if (colKey.Contains("tegangan") || colKey.Contains("voltage") || colKey.Contains("volt"))
        {
            var numStr = new string(val.Where(c => char.IsDigit(c) || c == '.').ToArray());
            if (numStr.Length > 0 && decimal.TryParse(numStr, out _))
                return numStr + "V";
            return val.Trim();
        }

        // Daya (kW) / Power  →  "7.5" → "7.5kW"
        if (colKey.Contains("daya") || colKey.Contains("power") || colKey.Contains("kw"))
        {
            var numStr = new string(val.Where(c => char.IsDigit(c) || c == '.').ToArray());
            if (numStr.Length > 0 && decimal.TryParse(numStr, out _))
                return numStr + "kW";
            return val.Trim();
        }

        // Default: return as-is
        return val.Trim();
    }

    // ════════════════════════════════════════════════════════════════════
    //  USER MANAGEMENT
    // ════════════════════════════════════════════════════════════════════
    private void ReloadUsers()
    {
        try
        {
            _users = _context.Users.OrderBy(u => u.UserId).ToList();
            dgvUsers.Rows.Clear();
            foreach (var u in _users)
            {
                int ri = dgvUsers.Rows.Add(
                    u.UserId,
                    u.Username,
                    u.FullName,
                    u.Role,
                    u.IsActive ? "✔ Aktif" : "✘ Nonaktif",
                    u.CreatedDate.ToLocalTime().ToString("dd MMM yyyy"),
                    u.LastLoginDate.HasValue
                        ? u.LastLoginDate.Value.ToLocalTime().ToString("dd MMM yyyy HH:mm")
                        : "—");
                dgvUsers.Rows[ri].DefaultCellStyle.ForeColor =
                    u.IsActive ? AppTheme.TextPrimary : AppTheme.TextMuted;
                dgvUsers.Rows[ri].Cells["ColRole"].Style.ForeColor =
                    u.Role == "Admin" ? AppTheme.Primary : AppTheme.TextSecondary;
            }
        }
        catch { }
    }

    private User? GetSelectedUser()
    {
        if (dgvUsers.CurrentRow == null) return null;
        var raw = dgvUsers.CurrentRow.Cells["ColId"].Value;
        int id  = raw is int i ? i : Convert.ToInt32(raw);
        return _users.FirstOrDefault(u => u.UserId == id);
    }

    private void BtnAddUser_Click(object? sender, EventArgs e)
    {
        using var dlg = new UserEditDialog(null);
        if (dlg.ShowDialog() != DialogResult.OK) return;

        if (_context.Users.Any(u => u.Username.ToLower() == dlg.Username.ToLower()))
        {
            MessageBox.Show($"Username '{dlg.Username}' sudah digunakan.", "Duplikat",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var user = new User
        {
            Username     = dlg.Username,
            FullName     = dlg.FullName,
            Role         = dlg.Role,
            PasswordHash = HashPassword(dlg.NewPassword),
            IsActive     = true,
            CreatedDate  = DateTime.UtcNow
        };
        _context.Users.Add(user);
        try
        {
            _context.SaveChanges();
            ReloadUsers();
            MessageBox.Show($"User '{user.FullName}' berhasil ditambahkan.", "Berhasil",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Gagal menyimpan: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void EditUser(User? user)
    {
        if (user == null) return;
        using var dlg = new UserEditDialog(user);
        if (dlg.ShowDialog() != DialogResult.OK) return;

        user.FullName = dlg.FullName;
        user.Role     = dlg.Role;
        if (!string.IsNullOrWhiteSpace(dlg.NewPassword))
            user.PasswordHash = HashPassword(dlg.NewPassword);

        try { _context.SaveChanges(); ReloadUsers(); }
        catch (Exception ex)
        {
            MessageBox.Show("Gagal menyimpan: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void BtnToggle_Click(object? sender, EventArgs e)
    {
        var user = GetSelectedUser();
        if (user == null) return;
        if (user.Username.ToLower() == "admin")
        {
            MessageBox.Show("Akun 'admin' tidak dapat dinonaktifkan.", "Peringatan",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var aksi = user.IsActive ? "menonaktifkan" : "mengaktifkan";
        if (MessageBox.Show($"Yakin ingin {aksi} akun '{user.Username}'?", "Konfirmasi",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

        user.IsActive = !user.IsActive;
        _context.SaveChanges();
        ReloadUsers();
    }

    private void BtnResetPw_Click(object? sender, EventArgs e)
    {
        var user = GetSelectedUser();
        if (user == null) return;

        using var dlg = new PasswordResetDialog(user.Username);
        if (dlg.ShowDialog() != DialogResult.OK) return;

        user.PasswordHash = HashPassword(dlg.NewPassword);
        _context.SaveChanges();
        MessageBox.Show($"Password '{user.Username}' berhasil diubah.", "Berhasil",
            MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private static string HashPassword(string pw)
    {
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(pw))).ToLower();
    }
}

// ── String extension ──────────────────────────────────────────────────────────
internal static class StringExts
{
    public static string? NullIfEmpty(this string? s)
        => string.IsNullOrWhiteSpace(s) ? null : s;
}
