namespace TokenChecker.App;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        var showStatusRequested = args.Any(arg => string.Equals(arg, "--show-status", StringComparison.OrdinalIgnoreCase));
        var showSettingsRequested = args.Any(arg => string.Equals(arg, "--show-settings", StringComparison.OrdinalIgnoreCase));

        // One tray instance per session. A second launch wakes the running
        // instance (surfacing its status or settings window) and then exits, so
        // we never end up with two tray icons.
        using var guard = new SingleInstanceGuard();
        if (!guard.IsPrimary)
        {
            guard.SignalPrimary(showSettingsRequested);
            return;
        }

        ApplicationConfiguration.Initialize();
        var context = new TrayApplicationContext();

        // Constructing the first WinForms object installs the WinForms
        // SynchronizationContext on this STA thread; hand it to the guard so its
        // background listener can marshal surface requests onto the UI thread.
        guard.ShowStatusRequested += context.ShowStatusForm;
        guard.ShowSettingsRequested += context.ShowSettingsForm;
        guard.StartListening(SynchronizationContext.Current!);

        // Open the status window on launch when the user has the "show on
        // startup" setting enabled (the default, so first-time users see it) or
        // when explicitly requested with --show-status. A single one-shot Idle
        // handler avoids opening it twice.
        if (showStatusRequested || context.ShouldShowOnStartup)
        {
            Application.Idle += ShowStatusFormOnce;

            void ShowStatusFormOnce(object? sender, EventArgs e)
            {
                Application.Idle -= ShowStatusFormOnce;
                context.ShowStatusForm();
            }
        }
        if (showSettingsRequested)
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
