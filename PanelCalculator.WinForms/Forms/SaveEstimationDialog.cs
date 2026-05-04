using PanelCalculator.WinForms.Theme;

namespace PanelCalculator.WinForms.Forms;

public class SaveEstimationDialog : Form
{
    public string ClientName { get; private set; } = "";
    public string Notes { get; private set; } = "";

    private TextBox txtClient = null!;
    private TextBox txtNotes  = null!;

    public SaveEstimationDialog()
    {
        BuildUI();
    }

    private void BuildUI()
    {
        Text = "Simpan Estimasi";
        Size = new Size(420, 280);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        BackColor = AppTheme.Background;
        Padding = new Padding(20);

        var lblTitle = AppTheme.MakeLabel("Simpan Estimasi Baru", AppTheme.FontLarge, AppTheme.TextPrimary);
        lblTitle.Location = new Point(20, 16);

        var lblClient = AppTheme.MakeLabel("Nama Klien *", AppTheme.FontSmall, AppTheme.TextSecondary);
        lblClient.Location = new Point(20, 56);

        txtClient = new TextBox
        {
            Location = new Point(20, 76),
            Width = 360,
            PlaceholderText = "Contoh: PT. ABC Indonesia"
        };
        AppTheme.StyleTextBox(txtClient);

        var lblNotes = AppTheme.MakeLabel("Catatan (opsional)", AppTheme.FontSmall, AppTheme.TextSecondary);
        lblNotes.Location = new Point(20, 114);

        txtNotes = new TextBox
        {
            Location = new Point(20, 134),
            Width = 360,
            Height = 60,
            Multiline = true,
            PlaceholderText = "Catatan tambahan untuk estimasi ini..."
        };
        AppTheme.StyleTextBox(txtNotes);

        var btnOk = new Button { Text = "Simpan", Location = new Point(20, 210), Width = 170, Height = 36 };
        AppTheme.StyleButton(btnOk, AppTheme.Success, Color.White);
        btnOk.Click += (s, e) =>
        {
            if (string.IsNullOrWhiteSpace(txtClient.Text))
            {
                MessageBox.Show("Nama klien tidak boleh kosong.", "Perhatian", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            ClientName = txtClient.Text.Trim();
            Notes = txtNotes.Text.Trim();
            DialogResult = DialogResult.OK;
            Close();
        };

        var btnCancel = new Button { Text = "Batal", Location = new Point(205, 210), Width = 170, Height = 36 };
        AppTheme.StyleButton(btnCancel, Color.FromArgb(229, 231, 235), AppTheme.TextPrimary);
        btnCancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };

        Controls.AddRange(new Control[] { lblTitle, lblClient, txtClient, lblNotes, txtNotes, btnOk, btnCancel });
    }
}
