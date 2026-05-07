using System.Drawing.Drawing2D;

namespace PanelCalculator.WinForms.Controls;

/// <summary>
/// A Panel that paints an anti-aliased rounded-corner border and fill.
/// Used as a transparent visual wrapper around TextBox / NumericUpDown
/// controls so they appear to have smooth rounded borders.
/// </summary>
internal sealed class RoundedPanel : Panel
{
    // ── backing fields ────────────────────────────────────────────────────
    private bool  _focused;
    private int   _radius  = 6;
    private Color _fill    = Color.Transparent;
    private Color _borderN = Color.Gray;
    private Color _borderF = Color.DodgerBlue;

    // ── properties ────────────────────────────────────────────────────────
    public int   CornerRadius  { get => _radius;  set { _radius  = value; Invalidate(); UpdateRegion(); } }
    public Color FillColor     { get => _fill;    set { _fill    = value; Invalidate(); } }
    public Color BorderNormal  { get => _borderN; set { _borderN = value; Invalidate(); } }
    public Color BorderFocused { get => _borderF; set { _borderF = value; Invalidate(); } }

    /// <summary>Switch between normal and focus-highlight border colour.</summary>
    public void SetFocused(bool focused)
    {
        if (_focused == focused) return;
        _focused = focused;
        Invalidate();
    }

    // ── painting ──────────────────────────────────────────────────────────
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        // Inset by 1 so the pen stays fully inside the control bounds
        var rect = new Rectangle(1, 1, Width - 2, Height - 2);
        using var path = BuildPath(rect, _radius);

        using (var br = new SolidBrush(_fill))
            g.FillPath(br, path);

        using (var pen = new Pen(_focused ? _borderF : _borderN, 1.5f))
            g.DrawPath(pen, path);
    }

    // ── region clipping ───────────────────────────────────────────────────
    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        UpdateRegion();
    }

    private void UpdateRegion()
    {
        if (Width <= 0 || Height <= 0) return;
        using var path = BuildPath(new Rectangle(0, 0, Width, Height), _radius);
        Region = new Region(path);
    }

    // ── rounded-rect path helper ─────────────────────────────────────────
    private static GraphicsPath BuildPath(Rectangle r, int radius)
    {
        int d  = radius * 2;
        var gp = new GraphicsPath();
        gp.AddArc(r.X,         r.Y,          d, d, 180, 90);
        gp.AddArc(r.Right - d, r.Y,          d, d, 270, 90);
        gp.AddArc(r.Right - d, r.Bottom - d, d, d,   0, 90);
        gp.AddArc(r.X,         r.Bottom - d, d, d,  90, 90);
        gp.CloseFigure();
        return gp;
    }

    // WS_EX_COMPOSITED: composited drawing reduces flicker when child
    // controls redraw inside the rounded area.
    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x02000000;
            return cp;
        }
    }
}
