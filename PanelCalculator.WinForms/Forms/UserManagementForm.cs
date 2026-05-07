using PanelCalculator.Core.Models;
using PanelCalculator.Data;
using PanelCalculator.WinForms.Theme;
using System.Security.Cryptography;
using System.Text;

namespace PanelCalculator.WinForms.Forms;

/// <summary>Admin-only user management: list, add, edit, deactivate users.</summary>
public class UserManagementForm : Form
{
    private readonly PanelCalculatorContext _context;

    private DataGridView dgv     = null!;
    private List<User>   _users  = new();

    public UserManagementForm(PanelCalculatorContext context)
    {
        _context = context;
        BuildUI();
    }

    private void BuildUI()
    {
        Text            = "Manajemen Pengguna";
        BackColor       = AppTheme.Background;
        FormBorderStyle = FormBorderStyle.None;
        Dock            = DockStyle.Fill;

        // ── Top bar ───────────────────────────────────────────────────────
        var pnlTop = new Panel
        {
            Dock      = DockStyle.Top,
            Height    = 56,
            BackColor = AppTheme.BgHeader,
            Padding   = new Padding(16, 10, 16, 10)
        };
        pnlTop.Paint += (s, e) =>
        {
            using var pen = new Pen(AppTheme.Border2);
            e.Graphics.DrawLine(pen, 0, pnlTop.Height - 1, pnlTop.Width, pnlTop.Height - 1);
        };

        var lblHead = AppTheme.MakeLabel("👥  Manajemen Pengguna", AppTheme.FontLarge, AppTheme.TextPrimary);
        lblHead.Location  = new Point(16, 14);
        lblHead.AutoSize  = true;

        var btnAdd = new Button { Text = "➕ Tambah", Location = new Point(0, 10), Width = 120, Height = 36, Dock = DockStyle.Right };
        AppTheme.StyleButton(btnAdd, AppTheme.Primary, Color.White);
        btnAdd.Margin = new Padding(0, 0, 8, 0);
        btnAdd.Click += BtnAdd_Click;

        pnlTop.Controls.AddRange(new Control[] { lblHead, btnAdd });

        // ── Grid ──────────────────────────────────────────────────────────
        dgv = new DataGridView { Dock = DockStyle.Fill };
        AppTheme.StyleGrid(dgv);
        dgv.ReadOnly = true;
        dgv.Columns.Add(new DataGridViewTextBoxColumn { Name = "ColId",       HeaderText = "ID",         FillWeight = 5  });
        dgv.Columns.Add(new DataGridViewTextBoxColumn { Name = "ColUsername",  HeaderText = "Username",   FillWeight = 18 });
        dgv.Columns.Add(new DataGridViewTextBoxColumn { Name = "ColFullName",  HeaderText = "Nama Lengkap", FillWeight = 28 });
        dgv.Columns.Add(new DataGridViewTextBoxColumn { Name = "ColRole",      HeaderText = "Role",       FillWeight = 12 });
        dgv.Columns.Add(new DataGridViewTextBoxColumn { Name = "ColActive",    HeaderText = "Status",     FillWeight = 10 });
        dgv.Columns.Add(new DataGridViewTextBoxColumn { Name = "ColCreated",   HeaderText = "Dibuat",     FillWeight = 16 });
        dgv.Columns.Add(new DataGridViewTextBoxColumn { Name = "ColLastLogin", HeaderText = "Login Terakhir", FillWeight = 16 });

        // ── Bottom action bar ─────────────────────────────────────────────
        var pnlBottom = new Panel
        {
            Dock      = DockStyle.Bottom,
            Height    = 56,
            BackColor = Color.White,
            Padding   = new Padding(16, 10, 16, 10)
        };
        pnlBottom.Paint += (s, e) =>
        {
            using var pen = new Pen(AppTheme.Border);
            e.Graphics.DrawLine(pen, 0, 0, pnlBottom.Width, 0);
        };

        var btnEdit = new Button { Text = "✏ Edit", Location = new Point(16, 10), Width = 110, Height = 36 };
        AppTheme.StyleButton(btnEdit, AppTheme.Bg2, AppTheme.Text2);
        btnEdit.Click += BtnEdit_Click;

        var btnToggle = new Button { Text = "🔒 Nonaktifkan", Location = new Point(138, 10), Width = 140, Height = 36 };
        AppTheme.StyleButton(btnToggle, AppTheme.Warning, Color.White);
        btnToggle.Click += BtnToggle_Click;

        var btnResetPw = new Button { Text = "🔑 Reset Password", Location = new Point(290, 10), Width = 150, Height = 36 };
        AppTheme.StyleButton(btnResetPw, AppTheme.Danger, Color.White);
        btnResetPw.Click += BtnResetPw_Click;

        var lblHint = AppTheme.MakeLabel("Klik dua kali untuk mengedit.", AppTheme.FontSmall, AppTheme.TextMuted);
        lblHint.Location = new Point(460, 20);

        pnlBottom.Controls.AddRange(new Control[] { btnEdit, btnToggle, btnResetPw, lblHint });

        Controls.Add(dgv);
        Controls.Add(pnlTop);
        Controls.Add(pnlBottom);

        dgv.CellDoubleClick += (s, e) => { if (e.RowIndex >= 0) EditUser(GetSelectedUser()); };
    }

    // ════════════════════════════════════════════════════════════════════
    public void ReloadAsync()
    {
        try
        {
            _users = _context.Users.OrderBy(u => u.UserId).ToList();
            dgv.Rows.Clear();
            foreach (var u in _users)
            {
                int ri = dgv.Rows.Add(
                    u.UserId,
                    u.Username,
                    u.FullName,
                    u.Role,
                    u.IsActive ? "Aktif" : "Nonaktif",
                    u.CreatedDate.ToLocalTime().ToString("dd MMM yyyy"),
                    u.LastLoginDate.HasValue
                        ? u.LastLoginDate.Value.ToLocalTime().ToString("dd MMM yyyy HH:mm")
                        : "—");
                dgv.Rows[ri].DefaultCellStyle.ForeColor = u.IsActive
                    ? AppTheme.TextPrimary
                    : AppTheme.TextMuted;
            }
        }
        catch { /* ignore */ }
    }

    private User? GetSelectedUser()
    {
        if (dgv.CurrentRow == null) return null;
        if (dgv.CurrentRow.Cells["ColId"].Value is not int id) return null;
        return _users.FirstOrDefault(u => u.UserId == id);
    }

    // ── Add ───────────────────────────────────────────────────────────────
    private void BtnAdd_Click(object? sender, EventArgs e)
    {
        using var dlg = new UserEditDialog(null);
        if (dlg.ShowDialog() != DialogResult.OK) return;

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
            ReloadAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show("Gagal menyimpan: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // ── Edit ──────────────────────────────────────────────────────────────
    private void BtnEdit_Click(object? sender, EventArgs e) => EditUser(GetSelectedUser());

    private void EditUser(User? user)
    {
        if (user == null) return;
        using var dlg = new UserEditDialog(user);
        if (dlg.ShowDialog() != DialogResult.OK) return;

        user.FullName = dlg.FullName;
        user.Role     = dlg.Role;
        if (!string.IsNullOrWhiteSpace(dlg.NewPassword))
            user.PasswordHash = HashPassword(dlg.NewPassword);

        try
        {
            _context.SaveChanges();
            ReloadAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show("Gagal menyimpan: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // ── Toggle active ─────────────────────────────────────────────────────
    private void BtnToggle_Click(object? sender, EventArgs e)
    {
        var user = GetSelectedUser();
        if (user == null) return;
        if (user.Username == "admin")
        {
            MessageBox.Show("Akun admin tidak dapat dinonaktifkan.", "Peringatan", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        var action = user.IsActive ? "menonaktifkan" : "mengaktifkan";
        var confirm = MessageBox.Show($"Yakin ingin {action} akun '{user.Username}'?", "Konfirmasi",
            MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (confirm != DialogResult.Yes) return;

        user.IsActive = !user.IsActive;
        _context.SaveChanges();
        ReloadAsync();
    }

    // ── Reset password ────────────────────────────────────────────────────
    private void BtnResetPw_Click(object? sender, EventArgs e)
    {
        var user = GetSelectedUser();
        if (user == null) return;

        using var dlg = new PasswordResetDialog(user.Username);
        if (dlg.ShowDialog() != DialogResult.OK) return;

        user.PasswordHash = HashPassword(dlg.NewPassword);
        _context.SaveChanges();
        MessageBox.Show("Password berhasil diubah.", "Sukses", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private static string HashPassword(string pw)
    {
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(pw))).ToLower();
    }
}

// ════════════════════════════════════════════════════════════════════════
//  UserEditDialog
// ════════════════════════════════════════════════════════════════════════
public class UserEditDialog : Form
{
    public string Username    { get; private set; } = "";
    public string FullName    { get; private set; } = "";
    public string Role        { get; private set; } = "Operator";
    public string NewPassword { get; private set; } = "";

    private TextBox  txtUsername = null!;
    private TextBox  txtFullName = null!;
    private ComboBox cmbRole     = null!;
    private TextBox  txtPassword = null!;
    private TextBox  txtConfirm  = null!;
    private readonly bool _isEdit;

    public UserEditDialog(User? existing)
    {
        _isEdit = existing != null;
        Text            = _isEdit ? "Edit Pengguna" : "Tambah Pengguna";
        Size            = new Size(380, _isEdit ? 340 : 400);
        StartPosition   = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox     = false;
        BackColor       = AppTheme.Background;

        int y = 16;

        // Username
        Add(AppTheme.MakeLabel("Username", AppTheme.FontSmall, AppTheme.TextSecondary), 20, y); y += 18;
        txtUsername = new TextBox { Location = new Point(20, y), Width = 320, Text = existing?.Username ?? "" };
        AppTheme.StyleTextBox(txtUsername);
        txtUsername.ReadOnly = _isEdit;
        Controls.Add(txtUsername); y += 36;

        // Full name
        Add(AppTheme.MakeLabel("Nama Lengkap", AppTheme.FontSmall, AppTheme.TextSecondary), 20, y); y += 18;
        txtFullName = new TextBox { Location = new Point(20, y), Width = 320, Text = existing?.FullName ?? "" };
        AppTheme.StyleTextBox(txtFullName);
        Controls.Add(txtFullName); y += 36;

        // Role
        Add(AppTheme.MakeLabel("Role", AppTheme.FontSmall, AppTheme.TextSecondary), 20, y); y += 18;
        cmbRole = new ComboBox { Location = new Point(20, y), Width = 320, DropDownStyle = ComboBoxStyle.DropDownList };
        AppTheme.StyleComboBox(cmbRole);
        cmbRole.Items.AddRange(new[] { "Admin", "Operator" });
        cmbRole.SelectedItem = existing?.Role ?? "Operator";
        Controls.Add(cmbRole); y += 36;

        // Password
        var pwLabel = _isEdit ? "Password Baru (kosongkan jika tidak diganti)" : "Password *";
        Add(AppTheme.MakeLabel(pwLabel, AppTheme.FontSmall, AppTheme.TextSecondary), 20, y); y += 18;
        txtPassword = new TextBox { Location = new Point(20, y), Width = 320, UseSystemPasswordChar = true };
        AppTheme.StyleTextBox(txtPassword);
        Controls.Add(txtPassword); y += 36;

        if (!_isEdit)
        {
            Add(AppTheme.MakeLabel("Konfirmasi Password *", AppTheme.FontSmall, AppTheme.TextSecondary), 20, y); y += 18;
            txtConfirm = new TextBox { Location = new Point(20, y), Width = 320, UseSystemPasswordChar = true };
            AppTheme.StyleTextBox(txtConfirm);
            Controls.Add(txtConfirm); y += 36;
        }
        else
        {
            txtConfirm = new TextBox(); // dummy
        }

        y += 4;
        var btnOk = new Button { Text = "Simpan", Location = new Point(20, y), Width = 140, Height = 34 };
        AppTheme.StyleButton(btnOk, AppTheme.Primary, Color.White);
        btnOk.Click += BtnOk_Click;

        var btnCancel = new Button { Text = "Batal", Location = new Point(172, y), Width = 140, Height = 34 };
        AppTheme.StyleButton(btnCancel, Color.FromArgb(229, 231, 235), AppTheme.TextPrimary);
        btnCancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };

        Controls.AddRange(new Control[] { btnOk, btnCancel });
        Height = y + 80;
    }

    private void Add(Control c, int x, int y) { c.Location = new Point(x, y); Controls.Add(c); }

    private void BtnOk_Click(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(txtUsername.Text)) { Warn("Username tidak boleh kosong."); return; }
        if (string.IsNullOrWhiteSpace(txtFullName.Text)) { Warn("Nama lengkap tidak boleh kosong."); return; }
        if (!_isEdit && string.IsNullOrEmpty(txtPassword.Text)) { Warn("Password tidak boleh kosong."); return; }
        if (!_isEdit && txtPassword.Text != txtConfirm.Text)    { Warn("Password tidak cocok.");       return; }
        if (!_isEdit && txtPassword.Text.Length < 6)            { Warn("Password minimal 6 karakter."); return; }
        if (!string.IsNullOrEmpty(txtPassword.Text) && txtPassword.Text.Length < 6)
        { Warn("Password minimal 6 karakter."); return; }

        Username    = txtUsername.Text.Trim();
        FullName    = txtFullName.Text.Trim();
        Role        = cmbRole.SelectedItem?.ToString() ?? "Operator";
        NewPassword = txtPassword.Text;
        DialogResult = DialogResult.OK;
        Close();
    }

    private void Warn(string msg) =>
        MessageBox.Show(msg, "Validasi", MessageBoxButtons.OK, MessageBoxIcon.Warning);
}

// ════════════════════════════════════════════════════════════════════════
//  PasswordResetDialog
// ════════════════════════════════════════════════════════════════════════
public class PasswordResetDialog : Form
{
    public string NewPassword { get; private set; } = "";

    private TextBox txtPassword = null!;
    private TextBox txtConfirm  = null!;

    public PasswordResetDialog(string username)
    {
        Text            = $"Reset Password — {username}";
        Size            = new Size(360, 240);
        StartPosition   = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox     = false;
        BackColor       = AppTheme.Background;

        var lbl1 = AppTheme.MakeLabel("Password Baru *", AppTheme.FontSmall, AppTheme.TextSecondary);
        lbl1.Location = new Point(20, 20);
        txtPassword = new TextBox { Location = new Point(20, 38), Width = 300, UseSystemPasswordChar = true };
        AppTheme.StyleTextBox(txtPassword);

        var lbl2 = AppTheme.MakeLabel("Konfirmasi Password *", AppTheme.FontSmall, AppTheme.TextSecondary);
        lbl2.Location = new Point(20, 80);
        txtConfirm = new TextBox { Location = new Point(20, 98), Width = 300, UseSystemPasswordChar = true };
        AppTheme.StyleTextBox(txtConfirm);

        var btnOk = new Button { Text = "Simpan", Location = new Point(20, 140), Width = 130, Height = 34 };
        AppTheme.StyleButton(btnOk, AppTheme.Primary, Color.White);
        btnOk.Click += (s, e) =>
        {
            if (string.IsNullOrEmpty(txtPassword.Text)) { Warn("Password tidak boleh kosong."); return; }
            if (txtPassword.Text.Length < 6)            { Warn("Minimal 6 karakter."); return; }
            if (txtPassword.Text != txtConfirm.Text)    { Warn("Password tidak cocok."); return; }
            NewPassword  = txtPassword.Text;
            DialogResult = DialogResult.OK;
            Close();
        };

        var btnCancel = new Button { Text = "Batal", Location = new Point(162, 140), Width = 130, Height = 34 };
        AppTheme.StyleButton(btnCancel, Color.FromArgb(229, 231, 235), AppTheme.TextPrimary);
        btnCancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };

        Controls.AddRange(new Control[] { lbl1, txtPassword, lbl2, txtConfirm, btnOk, btnCancel });
    }

    private void Warn(string msg) =>
        MessageBox.Show(msg, "Validasi", MessageBoxButtons.OK, MessageBoxIcon.Warning);
}
