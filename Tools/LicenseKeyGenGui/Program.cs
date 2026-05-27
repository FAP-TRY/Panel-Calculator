using System;
using System.Windows.Forms;

namespace PanelCalculator.Tools.LicenseKeyGenGui;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}
