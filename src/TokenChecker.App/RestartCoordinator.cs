using System.Diagnostics;
using System.Globalization;

namespace TokenChecker.App;

// Coordinates a clean self-restart so the startup-only settings (UI language and
// theme) actually take effect. A plain relaunch is useless here: the
// SingleInstanceGuard would just surface the still-running instance and exit. So
// the replacement process is handed the current PID and waits for this instance
// to terminate (freeing the named mutex) before it claims the single-instance
// gate. The pure helpers (RequiresRestart / TryParseAwaitExitPid) are unit-tested;
// the process plumbing is thin and integration-only.
internal static class RestartCoordinator
{
    private const string AwaitExitFlag = "--await-exit";

    // Language and theme are applied only at process startup (see Program.Main),
    // so only a change to one of them needs a restart to show.
    public static bool RequiresRestart(AppSettings before, AppSettings after)
        => before.Language != after.Language || before.Theme != after.Theme;

    // Parses "--await-exit <pid>" passed to a relaunched instance. Returns null
    // when the flag is absent or malformed (so a normal launch is unaffected).
    public static int? TryParseAwaitExitPid(string[] args)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], AwaitExitFlag, StringComparison.OrdinalIgnoreCase)
                && int.TryParse(args[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var pid)
                && pid > 0)
            {
                return pid;
            }
        }

        return null;
    }

    // Called by a relaunched instance BEFORE it takes the single-instance gate:
    // wait (bounded) for the previous instance to terminate so the mutex is free
    // and this process becomes the primary. Any failure (already gone, no access)
    // just means there is nothing to wait for.
    public static void WaitForPreviousExit(int pid, TimeSpan timeout)
    {
        try
        {
            using var previous = Process.GetProcessById(pid);
            previous.WaitForExit((int)timeout.TotalMilliseconds);
        }
        catch
        {
            // Process already exited or is not accessible — nothing to wait for.
        }
    }

    // Starts a replacement instance that will wait for THIS process to exit and
    // then claim the single-instance gate. Returns true when the new process was
    // launched (the caller should then exit the current instance); false when it
    // could not start (the caller must stay running so the user is never left
    // without the app — the change still applies on the next manual restart).
    public static bool LaunchReplacement()
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe))
        {
            return false;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = exe,
                Arguments = $"{AwaitExitFlag} {Environment.ProcessId.ToString(CultureInfo.InvariantCulture)}",
                UseShellExecute = false,
            });
            return true;
        }
        catch
        {
            return false;
        }
    }
}
