using System.Windows.Forms;

namespace TokenChecker.App;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        var context = new TrayApplicationContext();

        // Open the status window on launch when the user has the "show on
        // startup" setting enabled (the default, so first-time users see it) or
        // when explicitly requested with --show-status. A single one-shot Idle
        // handler avoids opening it twice.
        var showStatusRequested = args.Any(arg => string.Equals(arg, "--show-status", StringComparison.OrdinalIgnoreCase));
        if (showStatusRequested || context.ShouldShowOnStartup)
        {
            Application.Idle += ShowStatusFormOnce;

            void ShowStatusFormOnce(object? sender, EventArgs e)
            {
                Application.Idle -= ShowStatusFormOnce;
                context.ShowStatusForm();
            }
        }
        if (args.Any(arg => string.Equals(arg, "--show-settings", StringComparison.OrdinalIgnoreCase)))
        {
            Application.Idle += ShowSettingsFormOnce;

            void ShowSettingsFormOnce(object? sender, EventArgs e)
            {
                Application.Idle -= ShowSettingsFormOnce;
                context.ShowSettingsForm();
            }
        }
        Application.Run(context);
    }
}
