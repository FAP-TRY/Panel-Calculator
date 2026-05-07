using PanelCalculator.WinForms.Theme;

namespace PanelCalculator.WinForms.Forms;

public class SaveEstimationDialog : Form
{
    public string  ClientName   { get; private set; } = "";
    public string  ContactPhone { get; private set; } = "";
    public string  Company      { get; private set; } = "";
    public string  Address      { get; private set; } = "";
    public string  Notes        { get; private set; } = "";

    private TextBox txtClient  = null!;
    private TextBox txtPhone   = null!;
    private TextBox txtCompany = null!;
    private TextBox txtAddress = null!;
    private TextBox txtNotes   = null!;

    public SaveEstimationDialog()
    {
        BuildUI();
    }

    private void BuildUI()
    {
        Text            = "Simpan Estimasi";
        Size            = new Size(440, 440);
        StartPosition   = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox     = false;
        MinimizeBox     = false;
        BackColor       = AppTheme.Background;

        int x = 20, w = 380;
        int y = 16;

        var lblTitle = AppTheme.MakeLabel("Simpan Estimasi Baru", AppTheme.FontLarge, AppTheme.TextPrimary);
        lblTitle.Location = new Point(x, y); y += 36;

        // ── Nama Klien ──────────────────────────────────────────────────
        Add(AppTheme.MakeLabel("Nama Klien *", AppTheme.FontSmall, AppTheme.TextSecondary), x, y); y += 18;
        txtClient = new TextBox { Location = new Point(x, y), Width = w, PlaceholderText = "Contoh: Budi Santoso" };
        AppTheme.StyleTextBox(txtClient);
        Controls.Add(txtClient); y += 34;

        // ── No Kontak ───────────────────────────────────────────────────
        Add(AppTheme.MakeLabel("No Kontak", AppTheme.FontSmall, AppTheme.TextSecondary), x, y); y += 18;
        txtPhone = new TextBox { Location = new Point(x, y), Width = w, PlaceholderText = "08xx-xxxx-xxxx" };
        AppTheme.StyleTextBox(txtPhone);
        Controls.Add(txtPhone); y += 34;

        // ── Perusahaan ──────────────────────────────────────────────────
        Add(AppTheme.MakeLabel("Perusahaan", AppTheme.FontSmall, AppTheme.TextSecondary), x, y); y += 18;
        txtCompany = new TextBox { Location = new Point(x, y), Width = w, PlaceholderText = "Nama perusahaan / instansi" };
        AppTheme.StyleTextBox(txtCompany);
        Controls.Add(txtCompany); y += 34;

        // ── Alamat ──────────────────────────────────────────────────────
        Add(AppTheme.MakeLabel("Alamat", AppTheme.FontSmall, AppTheme.TextSecondary), x, y); y += 18;
        txtAddress = new TextBox { Location = new Point(x, y), Width = w, PlaceholderText = "Alamat pengiriman / perusahaan" };
        AppTheme.StyleTextBox(txtAddress);
        Controls.Add(txtAddress); y += 34;

        // ── Catatan ─────────────────────────────────────────────────────
        Add(AppTheme.MakeLabel("Catatan (opsional)", AppTheme.FontSmall, AppTheme.TextSecondary), x, y); y += 18;
        txtNotes = new TextBox
        {
            Location        = new Point(x, y),
            Width           = w,
            Height          = 52,
            Multiline       = true,
            PlaceholderText = "Catatan tambahan..."
        };
        AppTheme.StyleTextBox(txtNotes);
        Controls.Add(txtNotes); y += 60;

        // ── Buttons ─────────────────────────────────────────────────────
        var btnOk = new Button { Text = "Simpan", Location = new Point(x, y), Width = 180, Height = 36 };
        AppTheme.StyleButton(btnOk, AppTheme.Success, Color.White);
        btnOk.Click += BtnOk_Click;

        var btnCancel = new Button { Text = "Batal", Location = new Point(x + 190, y), Width = 180, Height = 36 };
        AppTheme.StyleButton(btnCancel, AppTheme.Bg2, AppTheme.Text2);
        btnCancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };

        Controls.AddRange(new Control[] { lblTitle, btnOk, btnCancel });

        Height = y + 80;
        AcceptButton = btnOk;
    }

    private void Add(Control c, int x, int y) { c.Location = new Point(x, y); Controls.Add(c); }

    private void BtnOk_Click(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(txtClient.Text))
        {
            MessageBox.Show("Nama klien tidak boleh kosong.", "Perhatian",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            txtClient.Focus();
            return;
        }
        ClientName   = txtClient.Text.Trim();
        ContactPhone = txtPhone.Text.Trim();
        Company      = txtCompany.Text.Trim();
        Address      = txtAddress.Text.Trim();
        Notes        = txtNotes.Text.Trim();
        DialogResult = DialogResult.OK;
        Close();
    }
}
