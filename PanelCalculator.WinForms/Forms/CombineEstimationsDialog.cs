using PanelCalculator.Core.Models;
using PanelCalculator.Core.Services;
using PanelCalculator.WinForms.Theme;

namespace PanelCalculator.WinForms.Forms;

/// <summary>
/// Dialog untuk meng-compose Surat Penawaran Multi-Panel.
/// Menampilkan daftar panel yang dipilih (read-only), warning kalau
/// customer info beda, input ongkir gabungan + nomor surat + pilihan format.
/// </summary>
public class CombineEstimationsDialog : Form
{
    public enum PdfFormat { Formal, Modern }

    public decimal   CombinedShippingCost { get; private set; } = 0m;
    public string    NomorSurat           { get; private set; } = "";
    public PdfFormat SelectedFormat       { get; private set; } = PdfFormat.Formal;

    private readonly List<Estimation> _estimations;

    public CombineEstimationsDialog(IEnumerable<Estimation> estimations)
    {
        _estimations = estimations.ToList();
        BuildUI();
    }

    private void BuildUI()
    {
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode       = AutoScaleMode.Dpi;
        Text            = "Surat Penawaran Multi-Panel";
        Size            = new Size(640, 580);
        StartPosition   = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox     = false;
        MinimizeBox     = false;
        BackColor       = AppTheme.Background;
        Padding         = new Padding(16);

        int x = 20, w = 580;
        int y = 16;

        var lblTitle = AppTheme.MakeLabel(
            $"Gabungkan {_estimations.Count} Estimasi → Satu Surat Penawaran",
            AppTheme.FontLarge, AppTheme.TextPrimary);
        lblTitle.Location = new Point(x, y); lblTitle.AutoSize = true;
        Controls.Add(lblTitle); y += 30;

        var lblInfo = AppTheme.MakeLabel(
            "PPN 11% akan dihitung SATU KALI dari total semua panel (bukan per panel).",
            AppTheme.FontSmall, AppTheme.TextSecondary);
        lblInfo.Location = new Point(x, y); lblInfo.AutoSize = true;
        Controls.Add(lblInfo); y += 22;

        // ── Warning customer mismatch (kalau ada) ──────────────────────
        var mismatch = CombinedQuotationCalculator.CheckCustomerInfoMismatch(_estimations);
        if (!string.IsNullOrWhiteSpace(mismatch))
        {
            var warn = new Label
            {
                Text      = "⚠ Customer info berbeda antar estimasi.\n" +
                            "PDF akan tetap dibuat memakai data dari panel pertama.\n" +
                            "Detail perbedaan:\n" + mismatch,
                Location  = new Point(x, y),
                Width     = w,
                Height    = 72,
                ForeColor = AppTheme.Danger,
                BackColor = AppTheme.Bg2,
                Padding   = new Padding(8),
                Font      = AppTheme.FontSmall,
            };
            Controls.Add(warn);
            y += 80;
        }

        // ── List panel yang dipilih ─────────────────────────────────────
        Controls.Add(AppTheme.MakeLabel("Panel yang dipilih:",
            AppTheme.FontSmall, AppTheme.TextSecondary)
            .Apply(c => c.Location = new Point(x, y)));
        y += 18;

        var lst = new ListBox
        {
            Location  = new Point(x, y),
            Width     = w,
            Height    = 110,
            BackColor = AppTheme.Bg1,
            ForeColor = AppTheme.Text1,
            Font      = AppTheme.FontBase,
        };
        int i = 0;
        foreach (var e in _estimations)
        {
            i++;
            var label = $"Panel #{i}  •  {e.EstimationNumber}  •  " +
                        $"{(string.IsNullOrWhiteSpace(e.ProjectName) ? "—" : e.ProjectName)}  •  " +
                        $"Subtotal+Margin: Rp {(e.SubTotal + e.Margin):N0}";
            lst.Items.Add(label);
        }
        Controls.Add(lst);
        y += 120;

        // ── Nomor surat ─────────────────────────────────────────────────
        Controls.Add(AppTheme.MakeLabel("Nomor Surat *", AppTheme.FontSmall, AppTheme.TextSecondary)
            .Apply(c => c.Location = new Point(x, y))); y += 18;
        var txtNomor = new TextBox
        {
            Location = new Point(x, y),
            Width    = w,
            Text     = $"EST-COMBINED-{DateTime.Now:yyyyMMdd}-{DateTime.Now:HHmm}",
        };
        AppTheme.StyleTextBox(txtNomor);
        Controls.Add(txtNomor); y += 34;

        // ── Ongkir gabungan ─────────────────────────────────────────────
        Controls.Add(AppTheme.MakeLabel("Ongkos Kirim Gabungan (Rp)",
            AppTheme.FontSmall, AppTheme.TextSecondary)
            .Apply(c => c.Location = new Point(x, y))); y += 18;
        var txtOngkir = new TextBox
        {
            Location = new Point(x, y),
            Width    = 200,
            Text     = "0",
        };
        AppTheme.StyleTextBox(txtOngkir);
        Controls.Add(txtOngkir); y += 34;

        // ── Format ──────────────────────────────────────────────────────
        Controls.Add(AppTheme.MakeLabel("Format PDF:",
            AppTheme.FontSmall, AppTheme.TextSecondary)
            .Apply(c => c.Location = new Point(x, y))); y += 18;

        var rbFormal = new RadioButton
        {
            Text      = "Surat Formal (kop surat resmi)",
            Location  = new Point(x, y),
            Width     = 280,
            Checked   = true,
            ForeColor = AppTheme.Text1,
            BackColor = AppTheme.Background,
        };
        var rbModern = new RadioButton
        {
            Text      = "Modern (warna-warni per section)",
            Location  = new Point(x + 290, y),
            Width     = 280,
            ForeColor = AppTheme.Text1,
            BackColor = AppTheme.Background,
        };
        Controls.Add(rbFormal);
        Controls.Add(rbModern);
        y += 34;

        // ── Buttons ─────────────────────────────────────────────────────
        var btnOk = new Button { Text = "📄 Generate PDF", Location = new Point(x, y), Width = 200, Height = 36 };
        AppTheme.StyleButton(btnOk, AppTheme.Success, Color.White);
        btnOk.Click += (s, ev) =>
        {
            // Validasi nomor surat
            if (string.IsNullOrWhiteSpace(txtNomor.Text))
            {
                MessageBox.Show("Nomor surat wajib diisi.", "Perhatian",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            // Validasi ongkir
            var raw = (txtOngkir.Text ?? "0").Replace(".", "").Replace(",", "").Trim();
            if (!decimal.TryParse(raw, System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.InvariantCulture, out var ong) || ong < 0)
            {
                MessageBox.Show("Ongkos kirim harus berupa angka non-negatif.", "Perhatian",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            CombinedShippingCost = ong;
            NomorSurat           = txtNomor.Text.Trim();
            SelectedFormat       = rbModern.Checked ? PdfFormat.Modern : PdfFormat.Formal;
            DialogResult         = DialogResult.OK;
            Close();
        };

        var btnCancel = new Button { Text = "Batal", Location = new Point(x + 210, y), Width = 140, Height = 36 };
        AppTheme.StyleButton(btnCancel, AppTheme.Bg2, AppTheme.Text2);
        btnCancel.Click += (s, ev) => { DialogResult = DialogResult.Cancel; Close(); };

        Controls.Add(btnOk);
        Controls.Add(btnCancel);

        Height       = y + 90;
        AcceptButton = btnOk;
        CancelButton = btnCancel;
    }
}

/// <summary>Tiny helper untuk one-liner Location set di fluent style.</summary>
internal static class ControlFluentExt
{
    public static T Apply<T>(this T ctl, Action<T> action) where T : Control
    {
        action(ctl);
        return ctl;
    }
}
