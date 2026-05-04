using PanelCalculator.Data;

namespace PanelCalculator.WinForms.Forms;

public partial class MainForm : Form
{
    private readonly PanelCalculatorContext _context;

    public MainForm(PanelCalculatorContext context)
    {
        _context = context;
        InitializeComponent();
        Text = "Panel Calculator v1.0";
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(1200, 700);
    }

    private void InitializeComponent()
    {
        // TODO: Initialize UI components
        // This is a placeholder - will be fully implemented in Phase 2
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(800, 450);
        Name = "MainForm";
        Text = "Panel Calculator";
    }
}
