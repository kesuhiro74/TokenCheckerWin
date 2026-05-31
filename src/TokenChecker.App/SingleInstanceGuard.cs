namespace TokenChecker.App;

// Keeps the tray app to a single instance per Windows session and lets a second
// launch surface the already-running instance instead of starting a second tray
// icon. A named Mutex is the gate; two named auto-reset events carry the one bit
// we need (show the status window vs. the settings window) from the second
// process to the primary, where a background thread marshals the request onto
// the UI thread.
//
// Privacy: the Mutex/event names are fixed constants with no user data, so this
// writes nothing identifying anywhere (honouring the project's privacy rule).
internal sealed class SingleInstanceGuard : IDisposable
{
    // Session-local names (no "Global\" prefix): each interactive Windows
    // session gets its own primary, matching the per-user settings/last_usage
    // files. Fast User Switching / RDP users don't block each other.
    private const string MutexName = "TokenCheckerWin.SingleInstance";
    private const string ShowStatusEventName = "TokenCheckerWin.ShowStatus";
    private const string ShowSettingsEventName = "TokenCheckerWin.ShowSettings";

    private const int StatusIndex = 0;
    private const int SettingsIndex = 1;
    private const int StopIndex = 2;

    private readonly Mutex _mutex;
    private readonly bool _isPrimary;

    // Created only in the primary; the events exist the moment the gate is taken
    // so a second launch racing startup can still signal us.
    private readonly EventWaitHandle? _showStatus;
    private readonly EventWaitHandle? _showSettings;
    private readonly EventWaitHandle? _stop;

    private SynchronizationContext? _ui;
    private Thread? _listener;
    private volatile bool _disposed;

    public bool IsPrimary => _isPrimary;

    // Raised on the UI thread (via SynchronizationContext.Post) when another
    // instance asks the primary to surface a window.
    public event Action? ShowStatusRequested;
    public event Action? ShowSettingsRequested;

    public SingleInstanceGuard()
    {
        // No ownership is acquired and we never WaitOne, so there is no
        // AbandonedMutexException to worry about — the Mutex is used purely as a
        // named existence check. A crash closes the handle and the OS reaps the
        // object, so the next launch becomes the primary cleanly.
        _mutex = new Mutex(initiallyOwned: false, MutexName, out _isPrimary);

        if (_isPrimary)
        {
            _showStatus = new EventWaitHandle(false, EventResetMode.AutoReset, ShowStatusEventName);
            _showSettings = new EventWaitHandle(false, EventResetMode.AutoReset, ShowSettingsEventName);
            _stop = new EventWaitHandle(false, EventResetMode.ManualReset);
        }
    }

    // Called by a SECONDARY instance to wake the primary, then it exits.
    public void SignalPrimary(bool showSettings)
    {
        var name = showSettings ? ShowSettingsEventName : ShowStatusEventName;
        try
        {
            if (EventWaitHandle.TryOpenExisting(name, out var handle))
            {
                using (handle)
                {
                    handle.Set();
                }
            }
        }
        catch
        {
            // The primary is racing shutdown (its events are gone). Nothing to
            // surface; the second instance simply exits.
        }
    }

    // Called once by the PRIMARY after the WinForms SynchronizationContext has
    // been installed (i.e. after the first WinForms object is constructed).
    public void StartListening(SynchronizationContext uiContext)
    {
        if (!_isPrimary || _listener is not null)
        {
            return;
        }

        _ui = uiContext;
        _listener = new Thread(ListenLoop)
        {
            IsBackground = true,
            Name = "TokenChecker.SingleInstanceListener"
        };
        _listener.Start();
    }

    private void ListenLoop()
    {
        var handles = new WaitHandle[] { _showStatus!, _showSettings!, _stop! };
        while (!_disposed)
        {
            int index;
            try
            {
                index = WaitHandle.WaitAny(handles);
            }
            catch (ObjectDisposedException)
            {
                return; // disposed during shutdown
            }

            if (_disposed || index == StopIndex)
            {
                return;
            }

            // Post (not Send) so this background thread never blocks on a busy
            // UI thread (e.g. while a modal dialog is up). The posted call runs
            // at a clean point in the primary's message loop; ShowStatusForm /
            // ShowSettingsForm already early-return when the context is disposed,
            // so a late signal during shutdown is harmless.
            var callback = index == SettingsIndex
                ? new SendOrPostCallback(_ => ShowSettingsRequested?.Invoke())
                : new SendOrPostCallback(_ => ShowStatusRequested?.Invoke());
            try
            {
                _ui?.Post(callback, null);
            }
            catch
            {
                // UI context torn down mid-shutdown; ignore.
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            _stop?.Set();
        }
        catch (ObjectDisposedException)
        {
        }

        _listener?.Join(TimeSpan.FromSeconds(1));
        _showStatus?.Dispose();
        _showSettings?.Dispose();
        _stop?.Dispose();
        _mutex.Dispose();
    }
}
