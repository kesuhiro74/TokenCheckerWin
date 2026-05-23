using System.Drawing;
using System.Windows.Forms;

namespace TokenChecker.App;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        using var notifyIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "TokenCheckerWin",
            Visible = true,
            ContextMenuStrip = new ContextMenuStrip()
        };

        notifyIcon.ContextMenuStrip.Items.Add("Exit", null, (_, _) => Application.Exit());

        Application.Run();
    }
}
