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

        // Resolve and apply the color theme BEFORE Initialize() (which also applies
        // the DpiUnaware override). The custom-drawn windows read UsageTheme's active
        // palette; the standard-control settings dialog is themed by SetColorMode.
        // Applied at startup only — a theme change in settings takes effect on the
        // next launch.
        // Resolve language and theme from the saved settings (one load), and apply
        // both BEFORE Initialize() — windows read the active Strings table / palette
        // at construction. Applied at startup only; changing either needs a restart.
        var startupSettings = new SettingsStore().Load();
        Strings.Apply(LanguageResolver.ResolveJapanese(startupSettings.Language));
        var dark = WindowsTheme.ResolveDark(startupSettings.Theme);
        UsageTheme.Apply(dark);
        Application.SetColorMode(dark ? SystemColorMode.Dark : SystemColorMode.Classic);

        ApplicationConfiguration.Initialize();
        var context = new TrayApplicationContext();

        // Constructing the first WinForms object installs the WinForms
        // SynchronizationContext on this STA thread; hand it to the guard so its
        // background listener can marshal surface requests onto the UI thread.
        guard.ShowStatusRequested += context.ShowStatusForm;
        guard.ShowSettingsRequested += context.ShowSettingsForm;
        guard.StartListening(SynchronizationContext.Current!);

        // The context opens the windows whose display method is "Always" on its own
        // first-idle pass. --show-status additionally forces the status window
        // forward (a one-shot Idle handler avoids opening it twice).
        if (showStatusRequested)
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
