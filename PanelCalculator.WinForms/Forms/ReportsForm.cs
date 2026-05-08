using Microsoft.EntityFrameworkCore;
using PanelCalculator.Data;
using PanelCalculator.WinForms.Theme;

namespace PanelCalculator.WinForms.Forms;

public class ReportsForm : Form
{
    private readonly PanelCalculatorContext _context;
    private Panel pnlContent = null!;

    public ReportsForm(PanelCalculatorContext context)
    {
        _context = context;
        BuildUI();
    }

    private void BuildUI()
    {
        Text = "Laporan & Analitik";
        Size = new Size(1000, 650);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = AppTheme.Background;

        // Sidebar
        var sidebar = new Panel { Dock = DockStyle.Left, Width = 200, BackColor = AppTheme.SidebarBg, Padding = new Padding(12) };
        sidebar.Paint += (s, e) =>
        {
            using var pen = new Pen(AppTheme.Border);
            e.Graphics.DrawLine(pen, sidebar.Width - 1, 0, sidebar.Width - 1, sidebar.Height);
        };

        var lblNav = AppTheme.MakeLabel("LAPORAN", AppTheme.FontSmall, AppTheme.TextMuted);
        lblNav.Dock = DockStyle.Top;
        lblNav.Height = 36;

        var btnSummary  = MakeNavButton("📊  Ringkasan Penjualan");
        var btnPipeline = MakeNavButton("🎯  Pipeline Status");

        btnSummary.Click  += (s, e) => ShowSummaryReport();
        btnPipeline.Click += (s, e) => ShowPipelineReport();

        sidebar.Controls.Add(btnPipeline);
        sidebar.Controls.Add(btnSummary);
        sidebar.Controls.Add(lblNav);

        pnlContent = new Panel { Dock = DockStyle.Fill, Padding = new Padding(20), BackColor = AppTheme.Background };

        Controls.Add(pnlContent);
        Controls.Add(sidebar);

        Load += (s, e) => ShowSummaryReport();
    }

    private Button MakeNavButton(string text)
    {
        var btn = new Button
        {
            Text = text,
            Dock = DockStyle.Top,
            Height = 40,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(8, 0, 0, 0)
        };
        AppTheme.StyleButton(btn, Color.Transparent, AppTheme.Text2);
        btn.FlatAppearance.MouseOverBackColor = AppTheme.Bg2;
        return btn;
    }

    private void ShowSummaryReport()
    {
        pnlContent.Controls.Clear();

        // Single query — no lazy loading needed
        var estimations = _context.Estimations.AsNoTracking().ToList();
        var approved = estimations.Where(e => e.Status == "Approved").ToList();
        var draft    = estimations.Where(e => e.Status == "Draft").ToList();

        var totalApproved = approved.Sum(e => e.TotalPrice);
        var totalDraft    = draft.Sum(e => e.TotalPrice);
        var avgDeal       = approved.Count > 0 ? totalApproved / approved.Count : 0;

        var title = AppTheme.MakeLabel("Ringkasan Penjualan", AppTheme.FontTitle, AppTheme.TextPrimary);
        title.Dock = DockStyle.Top;
        title.Height = 44;

        var pnlCards = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, Height = 110,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false, BackColor = Color.Transparent
        };
        pnlCards.Controls.Add(MakeKpiCard("Total Approved",   Fmt(totalApproved), AppTheme.Success));
        pnlCards.Controls.Add(MakeKpiCard("Total Draft",      Fmt(totalDraft),    AppTheme.Primary));
        pnlCards.Controls.Add(MakeKpiCard("Total Estimasi",   estimations.Count.ToString(), AppTheme.TextSecondary));
        pnlCards.Controls.Add(MakeKpiCard("Rata-rata Deal",   Fmt(avgDeal),       AppTheme.TextPrimary));

        var dgv = new DataGridView { Dock = DockStyle.Fill };
        AppTheme.StyleGrid(dgv);
        dgv.ReadOnly = true;
        dgv.Columns.Add("ColNo",     "No. Estimasi");
        dgv.Columns.Add("ColClient", "Klien");
        dgv.Columns.Add("ColDate",   "Tanggal");
        dgv.Columns.Add("ColStatus", "Status");
        dgv.Columns.Add("ColTotal",  "Total");

        foreach (var est in estimations.OrderByDescending(e => e.CreatedDate))
        {
            var rowIdx = dgv.Rows.Add(
                est.EstimationNumber,
                est.ClientName,
                est.CreatedDate.ToLocalTime().ToString("dd MMM yyyy"),
                est.Status,
                Fmt(est.TotalPrice));
            var (fg, _) = AppTheme.GetStatusColor(est.Status);
            dgv.Rows[rowIdx].Cells["ColStatus"].Style.ForeColor = fg;
        }

        pnlContent.Controls.Add(dgv);
        pnlContent.Controls.Add(pnlCards);
        pnlContent.Controls.Add(title);
    }

    private void ShowPipelineReport()
    {
        pnlContent.Controls.Clear();

        var title = AppTheme.MakeLabel("Pipeline Status", AppTheme.FontTitle, AppTheme.TextPrimary);
        title.Dock = DockStyle.Top;
        title.Height = 44;

        var statuses    = new[] { "Antri Dihitung", "Draft", "Tunggu Approved", "Approved" };
        var estimations = _context.Estimations.AsNoTracking().ToList();

        var pnlCards = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, Height = 110,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false, BackColor = Color.Transparent
        };

        foreach (var status in statuses)
        {
            var items = estimations.Where(e => e.Status == status).ToList();
            var value = items.Sum(e => e.TotalPrice);
            var (color, _) = AppTheme.GetStatusColor(status);
            pnlCards.Controls.Add(MakeKpiCard($"{status} ({items.Count})", Fmt(value), color));
        }

        pnlContent.Controls.Add(pnlCards);
        pnlContent.Controls.Add(title);
    }

    private Panel MakeKpiCard(string label, string value, Color accent)
    {
        var card = new Panel
        {
            Width = 175, Height = 90,
            Margin = new Padding(0, 0, 12, 0),
            BackColor = AppTheme.BgElev
        };
        var lbl = new Label
        {
            Text = label, Font = AppTheme.FontSmall, ForeColor = AppTheme.TextSecondary,
            Location = new Point(12, 14), AutoSize = true
        };
        var val = new Label
        {
            Text = value, Font = AppTheme.FontLarge, ForeColor = accent,
            Location = new Point(12, 36), AutoSize = true, MaximumSize = new Size(150, 0)
        };
        // Fixed GDI leak: use 'using' inside Paint
        card.Paint += (s, e) =>
        {
            using var brush = new SolidBrush(accent);
            e.Graphics.FillRectangle(brush, 0, 0, 4, card.Height);
        };
        card.Controls.AddRange(new Control[] { lbl, val });
        return card;
    }

    private static string Fmt(decimal v)
        => "Rp " + v.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("id-ID"));
}
