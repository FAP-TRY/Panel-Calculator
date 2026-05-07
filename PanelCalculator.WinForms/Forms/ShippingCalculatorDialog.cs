using PanelCalculator.WinForms.Theme;

namespace PanelCalculator.WinForms.Forms;

/// <summary>
/// Dialog kalkulator ongkos kirim.
/// Mendukung 4 metode: Manual, Per Kg, Kubikasi (volumetric), Per Unit.
/// Sesuai standar ekspedisi Indonesia (JNE JTR, J&T Cargo, SiCepat, dll).
/// </summary>
public class ShippingCalculatorDialog : Form
{
    // ── Output ───────────────────────────────────────────────────────────
    public decimal ResultCost { get; private set; }
    public string  ResultNote { get; private set; } = "";

    // ── Header fields ────────────────────────────────────────────────────
    private TextBox  txtEkspedisi = null!;
    private TextBox  txtTujuan    = null!;
    private ComboBox cmbMetode    = null!;

    // ── Method panels ────────────────────────────────────────────────────
    private Panel pnlManual   = null!;
    private Panel pnlPerKg    = null!;
    private Panel pnlKubikasi = null!;
    private Panel pnlPerUnit  = null!;

    // Correct content heights for each panel (calculated from AddPanelRow/AddInfoLabel calls)
    // Manual:   1 label(18) + 1 spinner(28)                                        = 60
    // PerKg:    3 rows(46×3=138) + 2 info labels(18×2=36)                          = 174 → 180
    // Kubikasi: 1 row(46) + dim-label(18) + P/L/T row(46) + pembagi(48) +
    //           2 rows(92) + 3 info labels(54)                                      = 304 → 310
    // PerUnit:  2 rows(92) + 1 info label(18)                                       = 110 → 116
    private static readonly int[] PanelHeights = { 60, 180, 310, 116 };

    // Manual
    private NumericUpDown numManualTotal = null!;

    // Per Kg
    private NumericUpDown numKgBerat      = null!;
    private NumericUpDown numKgBeratMin   = null!;
    private NumericUpDown numKgTarif      = null!;
    private Label         lblKgTagihan    = null!;
    private Label         lblKgTotal      = null!;

    // Kubikasi
    private NumericUpDown numKubBeratAktual = null!;
    private NumericUpDown numKubPanjang     = null!;
    private NumericUpDown numKubLebar       = null!;
    private NumericUpDown numKubTinggi      = null!;
    private ComboBox      cmbKubPembagi     = null!;
    private NumericUpDown numKubTarif       = null!;
    private NumericUpDown numKubSurcharge   = null!;
    private Label         lblKubVolume      = null!;
    private Label         lblKubTagihan     = null!;
    private Label         lblKubTotal       = null!;

    // Per Unit
    private NumericUpDown numUnitJumlah = null!;
    private NumericUpDown numUnitTarif  = null!;
    private Label         lblUnitTotal  = null!;

    // Footer (stored as fields so RepositionFooter can move them)
    private NumericUpDown numMinTagihan  = null!;
    private Label         lblHasil       = null!;
    private Panel         _sepAfterPanel = null!;
    private Panel         _sepAfterMin   = null!;
    private Label         _lblMinTitle   = null!;
    private Label         _lblHasilTitle = null!;
    private Button        _btnTerapkan   = null!;
    private Button        _btnBatal      = null!;

    // Layout tracking
    private int   _panelStartY;
    private Panel _scrollPanel = null!;

    // ── Inputs from caller ───────────────────────────────────────────────
    private readonly decimal _initialCost;
    private readonly string  _initialNote;

    public ShippingCalculatorDialog(decimal currentCost = 0, string currentNote = "")
    {
        _initialCost = currentCost;
        _initialNote = currentNote;
        BuildUI();
    }

    // ════════════════════════════════════════════════════════════════════
    private void BuildUI()
    {
        Text            = "Kalkulator Ongkos Kirim";
        Size            = new Size(440, 500);   // height adjusted by RepositionFooter
        MinimumSize     = new Size(440, 360);
        StartPosition   = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox     = false;
        MinimizeBox     = false;
        BackColor       = AppTheme.Background;

        _scrollPanel = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(16, 12, 16, 8) };

        int y = 0;

        // ── Header: Ekspedisi & Tujuan ────────────────────────────────
        AddLabel(_scrollPanel, "Ekspedisi:", ref y);
        txtEkspedisi = AddTextBox(_scrollPanel, ref y, "Contoh: JNE JTR, J&T Cargo...");
        AddGap(ref y, 8);

        AddLabel(_scrollPanel, "Tujuan Pengiriman:", ref y);
        txtTujuan = AddTextBox(_scrollPanel, ref y, "Contoh: Bandung → Jakarta");
        AddGap(ref y, 12);

        // ── Metode ────────────────────────────────────────────────────
        AddLabel(_scrollPanel, "Metode Perhitungan:", ref y);
        cmbMetode = new ComboBox
        {
            Location      = new Point(16, y),
            Width         = 390,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font          = AppTheme.FontBase
        };
        AppTheme.StyleComboBox(cmbMetode);
        cmbMetode.Items.AddRange(new[] { "Manual (input langsung)", "Per Kg", "Kubikasi (Volumetric)", "Per Unit" });
        cmbMetode.SelectedIndex = 0;
        cmbMetode.SelectedIndexChanged += Metode_Changed;
        _scrollPanel.Controls.Add(cmbMetode);
        y += 32;

        AddSeparator(_scrollPanel, ref y);

        // ── Method Panels (stacked, shown/hidden) ─────────────────────
        pnlManual   = BuildManualPanel();
        pnlPerKg    = BuildPerKgPanel();
        pnlKubikasi = BuildKubikasiPanel();
        pnlPerUnit  = BuildPerUnitPanel();

        _panelStartY = y;   // remember where panels begin

        foreach (var (pnl, h) in new[] {
            (pnlManual, PanelHeights[0]), (pnlPerKg, PanelHeights[1]),
            (pnlKubikasi, PanelHeights[2]), (pnlPerUnit, PanelHeights[3]) })
        {
            pnl.Location = new Point(16, y);
            pnl.Width    = 390;
            pnl.Height   = h;
            pnl.Visible  = false;
            _scrollPanel.Controls.Add(pnl);
        }
        pnlManual.Visible = true;

        // Footer controls — positioned dynamically via RepositionFooter()
        _sepAfterPanel = new Panel { Height = 1, Width = 390, BackColor = AppTheme.Border };
        _scrollPanel.Controls.Add(_sepAfterPanel);

        _lblMinTitle = AppTheme.MakeLabel("Minimum Tagihan (Rp) — isi 0 jika tidak ada:",
                                          AppTheme.FontSmall, AppTheme.TextSecondary);
        _lblMinTitle.AutoSize = true;
        _scrollPanel.Controls.Add(_lblMinTitle);

        numMinTagihan = new NumericUpDown
        {
            Width = 200, Minimum = 0, Maximum = 100_000_000, Increment = 10000,
            Value = 0, Font = AppTheme.FontBase, ThousandsSeparator = true
        };
        numMinTagihan.ValueChanged += (s, e) => RecalcAll();
        _scrollPanel.Controls.Add(numMinTagihan);

        _sepAfterMin = new Panel { Height = 1, Width = 390, BackColor = AppTheme.Border };
        _scrollPanel.Controls.Add(_sepAfterMin);

        _lblHasilTitle = new Label
        {
            Text = "TOTAL ONGKOS KIRIM", Font = AppTheme.FontBold,
            ForeColor = AppTheme.TextSecondary, AutoSize = true
        };
        _scrollPanel.Controls.Add(_lblHasilTitle);

        lblHasil = new Label
        {
            Text = "Rp 0", Font = new Font("Segoe UI", 18f, FontStyle.Bold),
            ForeColor = AppTheme.Primary, AutoSize = true
        };
        _scrollPanel.Controls.Add(lblHasil);

        _btnTerapkan = new Button { Text = "✔ Terapkan", Width = 140, Height = 36 };
        AppTheme.StyleButton(_btnTerapkan, AppTheme.Primary, Color.White);
        _btnTerapkan.Click += BtnTerapkan_Click;

        _btnBatal = new Button { Text = "Batal", Width = 90, Height = 36 };
        AppTheme.StyleButton(_btnBatal, AppTheme.Bg2, AppTheme.Text2);
        _btnBatal.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };

        _scrollPanel.Controls.AddRange(new Control[] { _btnTerapkan, _btnBatal });
        Controls.Add(_scrollPanel);

        // Position footer correctly for the default method (Manual = 60px)
        RepositionFooter(PanelHeights[0]);

        // Restore previous values if any
        RestoreInitial();
        RecalcAll();
    }

    /// <summary>
    /// Moves all footer controls to sit just below the active method panel,
    /// then auto-resizes the form height to eliminate empty space at the bottom.
    /// </summary>
    private void RepositionFooter(int panelHeight)
    {
        const int X = 16;  // left margin — matches right margin for symmetry
        int y = _panelStartY + panelHeight + 12;   // 12px gap after panel

        _sepAfterPanel.Location = new Point(X, y); y += 9;
        _lblMinTitle.Location   = new Point(X, y); y += 18;
        numMinTagihan.Location  = new Point(X, y); y += 32;
        _sepAfterMin.Location   = new Point(X, y); y += 9;
        _lblHasilTitle.Location = new Point(X, y); y += 22;
        lblHasil.Location       = new Point(X, y); y += 40;
        _btnTerapkan.Location   = new Point(X, y);
        _btnBatal.Location      = new Point(X + _btnTerapkan.Width + 10, y);
        y += _btnTerapkan.Height;   // bottom of buttons in scroll-panel coords

        // ── Auto-resize form to exactly fit content ──────────────────────
        // y is now the bottom of the last visible control (in scroll-panel coords).
        // Add 20px bottom breathing room.  The scroll panel's Padding.Top = 12
        // adds a visual top inset, so total needed client height = 12 + y + 20.
        int neededClientH = 12 + y + 20;
        int frameH        = Height - ClientSize.Height;   // title bar + borders
        int screenH       = Screen.FromControl(this).WorkingArea.Height;
        Height = Math.Max(360, Math.Min(neededClientH + frameH, screenH - 60));
    }

    // ════════════════════════════════════════════════════════════════════
    //  METHOD PANELS
    // ════════════════════════════════════════════════════════════════════

    private Panel BuildManualPanel()
    {
        var p = new Panel { Height = PanelHeights[0], BackColor = Color.Transparent };
        int y = 0;
        var lbl = AppTheme.MakeLabel("Total Ongkos Kirim (Rp):", AppTheme.FontSmall, AppTheme.TextSecondary);
        lbl.Location = new Point(0, y); lbl.AutoSize = true; p.Controls.Add(lbl); y += 20;

        numManualTotal = new NumericUpDown
        {
            Location = new Point(0, y), Width = 200,
            Minimum  = 0, Maximum = 100_000_000, Increment = 10000,
            Value    = 0, Font = AppTheme.FontBase, ThousandsSeparator = true
        };
        numManualTotal.ValueChanged += (s, e) => RecalcAll();
        p.Controls.Add(numManualTotal);
        return p;
    }

    private Panel BuildPerKgPanel()
    {
        var p = new Panel { Height = PanelHeights[1], BackColor = Color.Transparent };
        int y = 0;

        AddPanelRow(p, "Berat Aktual (kg):", ref y, out numKgBerat, 1, 10000, 1, 1);
        numKgBerat.DecimalPlaces = 1;
        AddPanelRow(p, "Berat Minimum (kg) — jika < min, pakai min:", ref y, out numKgBeratMin, 0, 1000, 1, 10);
        AddPanelRow(p, "Tarif per Kg (Rp):", ref y, out numKgTarif, 0, 1_000_000, 500, 5000);

        lblKgTagihan = AddInfoLabel(p, "Berat tagihan: — kg", ref y, AppTheme.TextSecondary);
        lblKgTotal   = AddInfoLabel(p, "Subtotal: —", ref y, AppTheme.Primary);

        numKgBerat.ValueChanged    += (s, e) => RecalcAll();
        numKgBeratMin.ValueChanged += (s, e) => RecalcAll();
        numKgTarif.ValueChanged    += (s, e) => RecalcAll();
        return p;
    }

    private Panel BuildKubikasiPanel()
    {
        var p = new Panel { Height = PanelHeights[2], BackColor = Color.Transparent };
        int y = 0;

        // berat aktual
        AddPanelRow(p, "Berat Aktual (kg):", ref y, out numKubBeratAktual, 0, 10000, 1, 0);
        numKubBeratAktual.DecimalPlaces = 1;

        // dimensi label
        var lblDim = AppTheme.MakeLabel("Dimensi (cm):", AppTheme.FontSmall, AppTheme.TextSecondary);
        lblDim.Location = new Point(0, y); lblDim.AutoSize = true; p.Controls.Add(lblDim); y += 18;

        // P L T on one row
        void DimBox(string hint, int left, out NumericUpDown num)
        {
            var l = AppTheme.MakeLabel(hint, AppTheme.FontSmall, AppTheme.TextSecondary);
            l.Location = new Point(left, y); l.AutoSize = true; p.Controls.Add(l);
            num = new NumericUpDown { Location = new Point(left, y + 16), Width = 80,
                Minimum = 0, Maximum = 9999, Increment = 1, Value = 0,
                Font = AppTheme.FontBase, ThousandsSeparator = false };
            num.ValueChanged += (s, e) => RecalcAll();
            p.Controls.Add(num);
        }
        DimBox("P (cm)", 0,   out numKubPanjang);
        DimBox("L (cm)", 92,  out numKubLebar);
        DimBox("T (cm)", 184, out numKubTinggi);
        y += 46;

        // Pembagi
        var lblPembagi = AppTheme.MakeLabel("Pembagi (mode transportasi):", AppTheme.FontSmall, AppTheme.TextSecondary);
        lblPembagi.Location = new Point(0, y); lblPembagi.AutoSize = true; p.Controls.Add(lblPembagi); y += 18;
        cmbKubPembagi = new ComboBox { Location = new Point(0, y), Width = 260, DropDownStyle = ComboBoxStyle.DropDownList, Font = AppTheme.FontBase };
        AppTheme.StyleComboBox(cmbKubPembagi);
        cmbKubPembagi.Items.AddRange(new[] { "5.000  —  Darat/Trucking (standar Indonesia)", "6.000  —  Udara/Air Freight" });
        cmbKubPembagi.SelectedIndex = 0;
        cmbKubPembagi.SelectedIndexChanged += (s, e) => RecalcAll();
        p.Controls.Add(cmbKubPembagi);
        y += 30;

        AddPanelRow(p, "Tarif per Kg-Kubikasi (Rp):", ref y, out numKubTarif, 0, 1_000_000, 500, 5000);
        AddPanelRow(p, "Surcharge berat >250 kg (Rp):", ref y, out numKubSurcharge, 0, 10_000_000, 50000, 0);

        lblKubVolume  = AddInfoLabel(p, "Berat kubikasi: — kg", ref y, AppTheme.TextSecondary);
        lblKubTagihan = AddInfoLabel(p, "Berat tagihan (maks): — kg", ref y, AppTheme.TextSecondary);
        lblKubTotal   = AddInfoLabel(p, "Subtotal: —", ref y, AppTheme.Primary);

        numKubBeratAktual.ValueChanged += (s, e) => RecalcAll();
        numKubTarif.ValueChanged       += (s, e) => RecalcAll();
        numKubSurcharge.ValueChanged   += (s, e) => RecalcAll();
        return p;
    }

    private Panel BuildPerUnitPanel()
    {
        var p = new Panel { Height = PanelHeights[3], BackColor = Color.Transparent };
        int y = 0;
        AddPanelRow(p, "Jumlah Unit:", ref y, out numUnitJumlah, 1, 10000, 1, 1);
        AddPanelRow(p, "Tarif per Unit (Rp):", ref y, out numUnitTarif, 0, 100_000_000, 10000, 0);
        lblUnitTotal = AddInfoLabel(p, "Subtotal: —", ref y, AppTheme.Primary);
        numUnitJumlah.ValueChanged += (s, e) => RecalcAll();
        numUnitTarif.ValueChanged  += (s, e) => RecalcAll();
        return p;
    }

    // ════════════════════════════════════════════════════════════════════
    //  CALCULATION
    // ════════════════════════════════════════════════════════════════════
    private void RecalcAll()
    {
        decimal result = 0;
        string  note   = "";

        switch (cmbMetode.SelectedIndex)
        {
            case 0: // Manual
                result = numManualTotal.Value;
                note   = "Manual";
                break;

            case 1: // Per Kg
            {
                decimal aktual = numKgBerat.Value;
                decimal min    = numKgBeratMin.Value;
                decimal tagihan = Math.Max(aktual, min);
                decimal tarif  = numKgTarif.Value;
                decimal sub    = tagihan * tarif;

                lblKgTagihan.Text = $"Berat tagihan: {tagihan:N1} kg  (aktual {aktual:N1} kg, min {min:N0} kg)";
                lblKgTotal.Text   = $"Subtotal: {FormatRp(sub)}";

                result = sub;
                note   = $"Per Kg | {tagihan:N1} kg × Rp {tarif:N0}/kg";
                break;
            }

            case 2: // Kubikasi
            {
                decimal beratAktual = numKubBeratAktual.Value;
                decimal p = numKubPanjang.Value;
                decimal l = numKubLebar.Value;
                decimal t = numKubTinggi.Value;
                decimal pembagi = cmbKubPembagi.SelectedIndex == 0 ? 5000m : 6000m;
                decimal tarif   = numKubTarif.Value;
                decimal surcharge = numKubSurcharge.Value;

                decimal volume   = p * l * t;
                decimal beratKub = volume > 0 ? Math.Round(volume / pembagi, 1) : 0;
                decimal tagihan  = Math.Max(beratAktual, beratKub);

                // Auto-suggest surcharge (JNE JTR rules)
                if (surcharge == 0 && tagihan >= 250)
                {
                    decimal suggested = tagihan switch
                    {
                        >= 500 => 600_000,
                        >= 400 => 300_000,
                        >= 300 => 200_000,
                        _      => 150_000
                    };
                    numKubSurcharge.Value = suggested;
                    surcharge = suggested;
                }
                else if (tagihan < 250)
                {
                    // reset surcharge suggestion when weight drops below threshold
                }

                decimal sub = tagihan * tarif + surcharge;

                lblKubVolume.Text  = $"Berat kubikasi: {beratKub:N1} kg  ({p:N0}×{l:N0}×{t:N0} ÷ {pembagi:N0})";
                lblKubTagihan.Text = $"Berat tagihan (maks): {tagihan:N1} kg";
                lblKubTotal.Text   = $"Subtotal: {FormatRp(tagihan * tarif)}" +
                                     (surcharge > 0 ? $"  + surcharge {FormatRp(surcharge)}" : "");

                result = sub;
                note   = $"Kubikasi | {tagihan:N1} kg × Rp {tarif:N0}/kg" +
                         (surcharge > 0 ? $" + surcharge" : "");
                break;
            }

            case 3: // Per Unit
            {
                decimal qty   = numUnitJumlah.Value;
                decimal tarif = numUnitTarif.Value;
                decimal sub   = qty * tarif;
                lblUnitTotal.Text = $"Subtotal: {FormatRp(sub)}";
                result = sub;
                note   = $"Per Unit | {qty:N0} unit × Rp {tarif:N0}";
                break;
            }
        }

        // Apply minimum charge
        decimal minCharge = numMinTagihan.Value;
        if (minCharge > 0 && result < minCharge)
            result = minCharge;

        // Build note
        var eksp   = txtEkspedisi.Text.Trim();
        var tujuan = txtTujuan.Text.Trim();
        string fullNote = string.Join(" | ", new[] { eksp, tujuan, note }.Where(s => !string.IsNullOrEmpty(s)));
        ResultNote = fullNote;

        lblHasil.Text = FormatRp(result);
        ResultCost    = result;
    }

    // ════════════════════════════════════════════════════════════════════
    //  EVENTS
    // ════════════════════════════════════════════════════════════════════
    private void Metode_Changed(object? sender, EventArgs e)
    {
        pnlManual.Visible   = cmbMetode.SelectedIndex == 0;
        pnlPerKg.Visible    = cmbMetode.SelectedIndex == 1;
        pnlKubikasi.Visible = cmbMetode.SelectedIndex == 2;
        pnlPerUnit.Visible  = cmbMetode.SelectedIndex == 3;

        // Reposition footer to sit directly below the newly visible panel
        RepositionFooter(PanelHeights[cmbMetode.SelectedIndex]);

        RecalcAll();
    }

    private void BtnTerapkan_Click(object? sender, EventArgs e)
    {
        RecalcAll();
        DialogResult = DialogResult.OK;
        Close();
    }

    // ════════════════════════════════════════════════════════════════════
    //  RESTORE
    // ════════════════════════════════════════════════════════════════════
    private void RestoreInitial()
    {
        // Parse note to restore fields
        if (!string.IsNullOrEmpty(_initialNote))
        {
            var parts = _initialNote.Split('|', StringSplitOptions.TrimEntries);
            foreach (var part in parts)
            {
                if (part.StartsWith("Per Kg"))   { cmbMetode.SelectedIndex = 1; break; }
                if (part.StartsWith("Kubikasi")) { cmbMetode.SelectedIndex = 2; break; }
                if (part.StartsWith("Per Unit")) { cmbMetode.SelectedIndex = 3; break; }
            }
        }

        // Restore manual total if manual mode and there's a previous cost
        if (cmbMetode.SelectedIndex == 0 && _initialCost > 0)
            numManualTotal.Value = Math.Min(_initialCost, numManualTotal.Maximum);
    }

    // ════════════════════════════════════════════════════════════════════
    //  HELPERS
    // ════════════════════════════════════════════════════════════════════
    private static string FormatRp(decimal v)
        => "Rp " + v.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("id-ID"));

    private void AddLabel(Panel p, string text, ref int y)
    {
        var lbl = AppTheme.MakeLabel(text, AppTheme.FontSmall, AppTheme.TextSecondary);
        lbl.Location = new Point(16, y); lbl.AutoSize = true; p.Controls.Add(lbl); y += 18;
    }

    private TextBox AddTextBox(Panel p, ref int y, string placeholder)
    {
        var tb = new TextBox { Location = new Point(16, y), Width = 390, PlaceholderText = placeholder, Font = AppTheme.FontBase };
        AppTheme.StyleTextBox(tb);
        tb.TextChanged += (s, e) => RecalcAll();
        p.Controls.Add(tb); y += 30;
        return tb;
    }

    private void AddSeparator(Panel p, ref int y)
    {
        var sep = new Panel { Location = new Point(16, y), Height = 1, Width = 390, BackColor = AppTheme.Border };
        p.Controls.Add(sep); y += 9;
    }

    private void AddGap(ref int y, int px) => y += px;

    private static void AddPanelRow(Panel p, string label, ref int y,
        out NumericUpDown num, decimal min, decimal max, decimal inc, decimal def)
    {
        var lbl = AppTheme.MakeLabel(label, AppTheme.FontSmall, AppTheme.TextSecondary);
        lbl.Location = new Point(0, y); lbl.AutoSize = true; p.Controls.Add(lbl); y += 18;
        num = new NumericUpDown
        {
            Location = new Point(0, y), Width = 200,
            Minimum  = min, Maximum = max, Increment = inc, Value = def,
            Font     = AppTheme.FontBase, ThousandsSeparator = true
        };
        p.Controls.Add(num); y += 28;
    }

    private static Label AddInfoLabel(Panel p, string text, ref int y, Color color)
    {
        var lbl = new Label { Text = text, Font = AppTheme.FontSmall, ForeColor = color,
            Location = new Point(0, y), AutoSize = true };
        p.Controls.Add(lbl); y += 18;
        return lbl;
    }
}
