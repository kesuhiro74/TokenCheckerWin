using System.Windows.Forms;

namespace TokenChecker.App;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        var context = new TrayApplicationContext();
        if (args.Any(arg => string.Equals(arg, "--show-status", StringComparison.OrdinalIgnoreCase)))
        {
            Application.Idle += ShowStatusFormOnce;

            void ShowStatusFormOnce(object? sender, EventArgs e)
            {
                Application.Idle -= ShowStatusFormOnce;
                context.ShowStatusForm();
            }
        }
        Application.Run(context);
    }
}
