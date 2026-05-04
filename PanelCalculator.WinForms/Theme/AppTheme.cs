namespace PanelCalculator.WinForms.Theme;

public static class AppTheme
{
    // Colors
    public static readonly Color Background     = Color.FromArgb(245, 247, 250);
    public static readonly Color SidebarBg      = Color.FromArgb(255, 255, 255);
    public static readonly Color CardBg         = Color.FromArgb(255, 255, 255);
    public static readonly Color Primary        = Color.FromArgb(37, 99, 235);   // Blue
    public static readonly Color PrimaryHover   = Color.FromArgb(29, 78, 216);
    public static readonly Color Success        = Color.FromArgb(22, 163, 74);   // Green
    public static readonly Color SuccessHover   = Color.FromArgb(21, 128, 61);
    public static readonly Color Danger         = Color.FromArgb(220, 38, 38);   // Red
    public static readonly Color Warning        = Color.FromArgb(234, 179, 8);   // Yellow
    public static readonly Color TextPrimary    = Color.FromArgb(17, 24, 39);
    public static readonly Color TextSecondary  = Color.FromArgb(107, 114, 128);
    public static readonly Color TextMuted      = Color.FromArgb(156, 163, 175);
    public static readonly Color Border         = Color.FromArgb(229, 231, 235);
    public static readonly Color BorderFocus    = Color.FromArgb(37, 99, 235);
    public static readonly Color GridHeader     = Color.FromArgb(249, 250, 251);
    public static readonly Color GridAltRow     = Color.FromArgb(250, 251, 252);
    public static readonly Color TotalBg        = Color.FromArgb(239, 246, 255);
    public static readonly Color TotalText      = Color.FromArgb(30, 64, 175);

    // Fonts
    public static readonly Font FontBase        = new Font("Segoe UI", 9f, FontStyle.Regular);
    public static readonly Font FontSmall       = new Font("Segoe UI", 8f, FontStyle.Regular);
    public static readonly Font FontBold        = new Font("Segoe UI", 9f, FontStyle.Bold);
    public static readonly Font FontLarge       = new Font("Segoe UI", 11f, FontStyle.Bold);
    public static readonly Font FontTitle       = new Font("Segoe UI", 14f, FontStyle.Bold);
    public static readonly Font FontTotal       = new Font("Segoe UI", 16f, FontStyle.Bold);
    public static readonly Font FontLabel       = new Font("Segoe UI", 8f, FontStyle.Regular);

    // Apply flat styling to a Button
    public static void StyleButton(Button btn, Color bgColor, Color textColor)
    {
        btn.FlatStyle = FlatStyle.Flat;
        btn.BackColor = bgColor;
        btn.ForeColor = textColor;
        btn.FlatAppearance.BorderSize = 0;
        btn.Font = FontBold;
        btn.Cursor = Cursors.Hand;
        btn.Padding = new Padding(8, 4, 8, 4);
    }

    // Apply flat styling to a TextBox
    public static void StyleTextBox(TextBox tb)
    {
        tb.BorderStyle = BorderStyle.FixedSingle;
        tb.Font = FontBase;
        tb.BackColor = Color.White;
        tb.ForeColor = TextPrimary;
    }

    // Apply flat styling to a ComboBox
    public static void StyleComboBox(ComboBox cb)
    {
        cb.FlatStyle = FlatStyle.Flat;
        cb.Font = FontBase;
        cb.BackColor = Color.White;
        cb.ForeColor = TextPrimary;
    }

    // Apply styling to DataGridView
    public static void StyleGrid(DataGridView dgv)
    {
        dgv.BackgroundColor = Color.White;
        dgv.BorderStyle = BorderStyle.None;
        dgv.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        dgv.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
        dgv.GridColor = Border;
        dgv.RowHeadersVisible = false;
        dgv.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        dgv.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        dgv.AllowUserToResizeRows = false;
        dgv.MultiSelect = false;
        dgv.ReadOnly = false;
        dgv.AllowUserToAddRows = false;
        dgv.Font = FontBase;

        // Header style
        dgv.ColumnHeadersDefaultCellStyle.BackColor = GridHeader;
        dgv.ColumnHeadersDefaultCellStyle.ForeColor = TextSecondary;
        dgv.ColumnHeadersDefaultCellStyle.Font = FontBold;
        dgv.ColumnHeadersDefaultCellStyle.Padding = new Padding(8, 6, 8, 6);
        dgv.ColumnHeadersHeight = 38;

        // Row style
        dgv.DefaultCellStyle.ForeColor = TextPrimary;
        dgv.DefaultCellStyle.SelectionBackColor = Color.FromArgb(219, 234, 254);
        dgv.DefaultCellStyle.SelectionForeColor = Primary;
        dgv.DefaultCellStyle.Padding = new Padding(6, 4, 6, 4);
        dgv.RowTemplate.Height = 34;

        // Alternating row
        dgv.AlternatingRowsDefaultCellStyle.BackColor = GridAltRow;
    }

    // Create a styled label
    public static Label MakeLabel(string text, Font? font = null, Color? color = null)
    {
        return new Label
        {
            Text = text,
            Font = font ?? FontBase,
            ForeColor = color ?? TextPrimary,
            AutoSize = true
        };
    }

    // Create a styled panel as card
    public static Panel MakeCard()
    {
        return new Panel
        {
            BackColor = CardBg,
            Padding = new Padding(16)
        };
    }
}
