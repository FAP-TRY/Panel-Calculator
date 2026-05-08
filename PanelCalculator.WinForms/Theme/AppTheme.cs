using PanelCalculator.WinForms.Controls;

namespace PanelCalculator.WinForms.Theme;

/// <summary>
/// Dark Pro design tokens — navy-blue palette with electric-blue brand &amp; cyan accent.
/// Mirrors the CSS token system from tokens.css.
/// </summary>
public static class AppTheme
{
    // ════════════════════════════════════════════════════════
    //  PALETTE — background scale (dark navy)
    // ════════════════════════════════════════════════════════

    /// <summary>#060912 — canvas / app background</summary>
    public static readonly Color Bg0      = Color.FromArgb(  6,   9,  18);
    /// <summary>#0b1020 — panel surface</summary>
    public static readonly Color Bg1      = Color.FromArgb( 11,  16,  32);
    /// <summary>#111733 — raised surface / row hover</summary>
    public static readonly Color Bg2      = Color.FromArgb( 17,  23,  51);
    /// <summary>#1a2247 — selected row / active state</summary>
    public static readonly Color Bg3      = Color.FromArgb( 26,  34,  71);
    /// <summary>#0f1530 — card / modal / elevated surface</summary>
    public static readonly Color BgElev   = Color.FromArgb( 15,  21,  48);
    /// <summary>#0d1430 — topbar / header gradient start</summary>
    public static readonly Color BgHeader = Color.FromArgb( 13,  20,  48);

    // ════════════════════════════════════════════════════════
    //  BORDERS
    // ════════════════════════════════════════════════════════

    /// <summary>Subtle border (solid approx of rgba(120,150,220,0.10))</summary>
    public static readonly Color Border1      = Color.FromArgb( 20,  27,  56);
    /// <summary>Normal border (solid approx of rgba(120,150,220,0.18))</summary>
    public static readonly Color Border2      = Color.FromArgb( 28,  36,  68);
    /// <summary>Strong border (solid approx of rgba(140,170,240,0.30))</summary>
    public static readonly Color BorderStrong = Color.FromArgb( 40,  52,  90);
    /// <summary>Semi-transparent normal border for controls that support alpha</summary>
    public static readonly Color Border2Alpha = Color.FromArgb( 46, 120, 150, 220);

    // ════════════════════════════════════════════════════════
    //  BRAND — electric blue
    // ════════════════════════════════════════════════════════

    /// <summary>#6e94ff — code / accent text</summary>
    public static readonly Color Brand300 = Color.FromArgb(110, 148, 255);
    /// <summary>#4a78ff</summary>
    public static readonly Color Brand400 = Color.FromArgb( 74, 120, 255);
    /// <summary>#2a5cff — primary action colour</summary>
    public static readonly Color Brand500 = Color.FromArgb( 42,  92, 255);
    /// <summary>#1c46e6 — pressed / darker variant</summary>
    public static readonly Color Brand600 = Color.FromArgb( 28,  70, 230);

    // ════════════════════════════════════════════════════════
    //  CYAN ACCENT
    // ════════════════════════════════════════════════════════

    /// <summary>#7df0ff — lighter cyan, selected-row text</summary>
    public static readonly Color Cyan300 = Color.FromArgb(125, 240, 255);
    /// <summary>#2cdcff — main cyan accent, section titles</summary>
    public static readonly Color Cyan400 = Color.FromArgb( 44, 220, 255);
    /// <summary>#00c2ff</summary>
    public static readonly Color Cyan500 = Color.FromArgb(  0, 194, 255);

    // ════════════════════════════════════════════════════════
    //  STATUS COLOURS
    // ════════════════════════════════════════════════════════

    public static readonly Color Success400 = Color.FromArgb( 45, 212, 154);  // #2dd49a
    public static readonly Color Success500 = Color.FromArgb( 16, 185, 129);  // #10b981
    public static readonly Color Warning400 = Color.FromArgb(255, 181,  71);  // #ffb547
    public static readonly Color Warning500 = Color.FromArgb(245, 158,  11);  // #f59e0b
    public static readonly Color Danger400  = Color.FromArgb(255, 107, 107);  // #ff6b6b
    public static readonly Color Danger500  = Color.FromArgb(239,  68,  68);  // #ef4444

    // ════════════════════════════════════════════════════════
    //  TEXT
    // ════════════════════════════════════════════════════════

    /// <summary>#eef2ff — primary text</summary>
    public static readonly Color Text1     = Color.FromArgb(238, 242, 255);
    /// <summary>#aab5d6 — secondary text</summary>
    public static readonly Color Text2     = Color.FromArgb(170, 181, 214);
    /// <summary>#6e7a9e — tertiary / placeholder</summary>
    public static readonly Color Text3     = Color.FromArgb(110, 122, 158);
    /// <summary>#4a5576 — muted / de-emphasised</summary>
    public static readonly Color TextMutedColor = Color.FromArgb( 74,  85, 118);

    // ════════════════════════════════════════════════════════
    //  LEGACY ALIASES  (keeps all existing form code compiling)
    // ════════════════════════════════════════════════════════

    public static Color Background    => Bg0;
    public static Color SidebarBg     => Bg1;
    public static Color CardBg        => BgElev;
    public static Color Primary       => Brand500;
    public static Color PrimaryHover  => Brand600;
    public static Color Success       => Success500;
    public static Color SuccessHover  => Success400;
    public static Color Danger        => Danger500;
    public static Color Warning       => Warning500;
    public static Color TextPrimary   => Text1;
    public static Color TextSecondary => Text2;
    public static Color TextMuted     => Text3;
    public static Color Border        => Border2;
    public static Color BorderFocus   => Brand400;
    public static Color GridHeader    => BgElev;
    public static Color GridAltRow    => Bg2;
    public static Color TotalBg       => Bg3;
    public static Color TotalText     => Cyan300;

    // ════════════════════════════════════════════════════════
    //  STATUS TAG COLOURS  (status → fg, bg)
    // ════════════════════════════════════════════════════════

    public static (Color fg, Color bg) GetStatusColor(string? status) => status?.Trim() switch
    {
        "Approved" => (Success400, Color.FromArgb(8, 32, 22)),
        "Draft"    => (Text3,      Color.FromArgb(18, 20, 36)),
        _          => (Text3,      Color.FromArgb(18, 20, 36))
    };

    // ════════════════════════════════════════════════════════
    //  FONTS
    // ════════════════════════════════════════════════════════

    public static readonly Font FontBase    = new Font("Segoe UI", 9f,  FontStyle.Regular);
    public static readonly Font FontSmall   = new Font("Segoe UI", 8f,  FontStyle.Regular);
    public static readonly Font FontBold    = new Font("Segoe UI", 9f,  FontStyle.Bold);
    public static readonly Font FontLarge   = new Font("Segoe UI", 11f, FontStyle.Bold);
    public static readonly Font FontTitle   = new Font("Segoe UI", 14f, FontStyle.Bold);
    public static readonly Font FontTotal   = new Font("Segoe UI", 18f, FontStyle.Bold);
    public static readonly Font FontLabel   = new Font("Segoe UI", 8f,  FontStyle.Regular);
    public static readonly Font FontMono    = new Font("Consolas",  8f,  FontStyle.Regular);
    /// <summary>Uppercase caption / section label (7 pt bold).</summary>
    public static readonly Font FontCaption = new Font("Segoe UI", 7f,  FontStyle.Bold);
    public static readonly Font FontCode    = new Font("Consolas",  8.5f, FontStyle.Regular);

    // ════════════════════════════════════════════════════════
    //  BUTTON HELPERS
    // ════════════════════════════════════════════════════════

    /// <summary>Generic flat button with custom bg/fg.</summary>
    public static void StyleButton(Button btn, Color bgColor, Color textColor)
    {
        btn.FlatStyle = FlatStyle.Flat;
        btn.BackColor = bgColor;
        btn.ForeColor = textColor;
        btn.FlatAppearance.BorderSize  = 0;
        btn.FlatAppearance.MouseOverBackColor = Lighten(bgColor, 18);
        btn.Font   = FontBold;
        btn.Cursor = Cursors.Hand;
        btn.Padding = new Padding(8, 4, 8, 4);
    }

    public static void StyleButtonPrimary(Button btn) => StyleButton(btn, Brand500, Color.White);

    public static void StyleButtonSuccess(Button btn) => StyleButton(btn, Success500, Color.White);

    public static void StyleButtonDanger(Button btn) => StyleButton(btn, Danger500, Color.White);

    /// <summary>Ghost / secondary button — dark background, subtle border.</summary>
    public static void StyleButtonGhost(Button btn)
    {
        btn.FlatStyle = FlatStyle.Flat;
        btn.BackColor = Bg2;
        btn.ForeColor = Text2;
        btn.FlatAppearance.BorderSize  = 1;
        btn.FlatAppearance.BorderColor = Border2;
        btn.FlatAppearance.MouseOverBackColor = Bg3;
        btn.Font   = FontBold;
        btn.Cursor = Cursors.Hand;
        btn.Padding = new Padding(8, 4, 8, 4);
    }

    // ════════════════════════════════════════════════════════
    //  INPUT HELPERS
    // ════════════════════════════════════════════════════════

    public static void StyleTextBox(TextBox tb)
    {
        tb.BorderStyle = BorderStyle.None;   // RoundedPanel wrapper owns the border
        tb.Font        = FontBase;
        tb.BackColor   = Bg2;
        tb.ForeColor   = Text1;

        // Wrap in a rounded-corner panel the first time this TextBox gets a parent
        tb.ParentChanged += TextBoxParentChanged;
    }

    // ── rounded-wrapper helpers ───────────────────────────────────────────

    private static void TextBoxParentChanged(object? sender, EventArgs e)
    {
        if (sender is not TextBox tb) return;
        var parent = tb.Parent;
        if (parent == null || parent is RoundedPanel) return; // already wrapped or detached

        tb.ParentChanged -= TextBoxParentChanged;   // fire once only
        WrapInRoundedPanel(tb);
    }

    private static void WrapInRoundedPanel(TextBox tb)
    {
        var parent     = tb.Parent!;
        int origIndex  = parent.Controls.GetChildIndex(tb);
        var origLoc    = tb.Location;
        var origSize   = tb.Size;
        var origAnchor = tb.Anchor;
        var origDock   = tb.Dock;
        bool isDocked  = origDock != DockStyle.None;

        // Create the visual wrapper
        var wrapper = new RoundedPanel
        {
            CornerRadius  = 6,
            FillColor     = Bg2,
            BorderNormal  = BorderStrong,
            BorderFocused = Brand400,
            Padding       = Padding.Empty,
        };

        if (isDocked)
        {
            // For DockStyle.Top / Fill etc — keep the dock, add a few pixels of height
            wrapper.Dock   = origDock;
            wrapper.Height = tb.PreferredHeight + 8;
        }
        else
        {
            // Absolute-positioned — same location / size / anchor (no layout shift)
            wrapper.Location = origLoc;
            wrapper.Size     = origSize;
            wrapper.Anchor   = origAnchor;
        }

        // Move the TextBox inside the wrapper
        parent.Controls.Remove(tb);
        tb.Anchor   = AnchorStyles.None;
        tb.Dock     = DockStyle.None;
        tb.Location = Point.Empty;

        if (tb.Multiline)
        {
            // Multi-line: fill the wrapper with a small inset so the text
            // doesn't run into the rounded corners.
            tb.Dock = DockStyle.Fill;
            wrapper.Padding = new Padding(4);
        }
        else
        {
            // Single-line: stretch horizontally, center vertically.
            // We keep Dock = None so WinForms respects the auto-sized height.
            tb.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            void PositionTb()
            {
                const int HP = 6;   // horizontal padding inside the rounded rect
                tb.Left  = HP;
                tb.Width = Math.Max(4, wrapper.ClientSize.Width - HP * 2);
                tb.Top   = Math.Max(0, (wrapper.ClientSize.Height - tb.Height) / 2);
            }
            PositionTb();
            wrapper.SizeChanged += (_, _) => PositionTb();
        }

        wrapper.Controls.Add(tb);
        parent.Controls.Add(wrapper);
        parent.Controls.SetChildIndex(wrapper, origIndex);

        // Focus ring: glow the border when the inner TextBox has focus
        tb.GotFocus  += (_, _) => wrapper.SetFocused(true);
        tb.LostFocus += (_, _) => wrapper.SetFocused(false);
    }

    public static void StyleComboBox(ComboBox cb)
    {
        cb.FlatStyle = FlatStyle.Flat;
        cb.Font      = FontBase;
        cb.BackColor = Bg2;
        cb.ForeColor = Text1;
    }

    public static void StyleNumericUpDown(NumericUpDown nud)
    {
        nud.BackColor   = Bg2;
        nud.ForeColor   = Text1;
        nud.Font        = FontBase;
        nud.BorderStyle = BorderStyle.FixedSingle;
    }

    // ════════════════════════════════════════════════════════
    //  DATA GRID HELPER
    // ════════════════════════════════════════════════════════

    public static void StyleGrid(DataGridView dgv)
    {
        dgv.BackgroundColor = Bg1;
        dgv.BorderStyle     = BorderStyle.None;
        dgv.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        dgv.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
        dgv.GridColor       = Border2;
        dgv.RowHeadersVisible = false;
        dgv.SelectionMode   = DataGridViewSelectionMode.FullRowSelect;
        dgv.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        dgv.AllowUserToResizeRows = false;
        dgv.MultiSelect     = false;
        dgv.ReadOnly        = false;
        dgv.AllowUserToAddRows = false;
        dgv.Font            = FontBase;
        dgv.EnableHeadersVisualStyles = false;

        // Column headers
        dgv.ColumnHeadersDefaultCellStyle.BackColor          = BgElev;
        dgv.ColumnHeadersDefaultCellStyle.ForeColor          = Text3;
        dgv.ColumnHeadersDefaultCellStyle.Font               = FontCaption;
        dgv.ColumnHeadersDefaultCellStyle.Padding            = new Padding(8, 6, 8, 6);
        dgv.ColumnHeadersDefaultCellStyle.SelectionBackColor = BgElev;
        dgv.ColumnHeadersDefaultCellStyle.SelectionForeColor = Text3;
        dgv.ColumnHeadersHeight = 38;

        // Default cell style
        dgv.DefaultCellStyle.BackColor          = Bg1;
        dgv.DefaultCellStyle.ForeColor          = Text2;
        dgv.DefaultCellStyle.SelectionBackColor = Bg3;
        dgv.DefaultCellStyle.SelectionForeColor = Cyan300;
        dgv.DefaultCellStyle.Padding            = new Padding(6, 4, 6, 4);
        dgv.RowTemplate.Height = 34;

        // Alternating rows
        dgv.AlternatingRowsDefaultCellStyle.BackColor          = Bg2;
        dgv.AlternatingRowsDefaultCellStyle.ForeColor          = Text2;
        dgv.AlternatingRowsDefaultCellStyle.SelectionBackColor = Bg3;
        dgv.AlternatingRowsDefaultCellStyle.SelectionForeColor = Cyan300;
    }

    // ════════════════════════════════════════════════════════
    //  FACTORY HELPERS
    // ════════════════════════════════════════════════════════

    public static Label MakeLabel(string text, Font? font = null, Color? color = null)
        => new Label { Text = text, Font = font ?? FontBase, ForeColor = color ?? Text1, AutoSize = true };

    /// <summary>Uppercase cyan section-title label (like kp-section-title).</summary>
    public static Label MakeSectionTitle(string text)
        => new Label
        {
            Text      = text.ToUpper(),
            Font      = FontCaption,
            ForeColor = Cyan400,
            AutoSize  = true
        };

    /// <summary>Elevated dark card panel.</summary>
    public static Panel MakeCard()
        => new Panel { BackColor = BgElev, Padding = new Padding(16) };

    /// <summary>Horizontal rule separator.</summary>
    public static Panel MakeSeparator()
        => new Panel { Height = 1, BackColor = Border2, Dock = DockStyle.Top };

    // ════════════════════════════════════════════════════════
    //  UTILITY
    // ════════════════════════════════════════════════════════

    /// <summary>Lightens a colour by adding <paramref name="amount"/> to each channel.</summary>
    private static Color Lighten(Color c, int amount)
        => Color.FromArgb(
            c.A,
            Math.Min(255, c.R + amount),
            Math.Min(255, c.G + amount),
            Math.Min(255, c.B + amount));
}
