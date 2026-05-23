using System.Drawing;
using System.Windows.Forms;
using TokenChecker.Core;
using TokenChecker.Core.Providers;

namespace TokenChecker.App;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly UsageAggregator _aggregator;
    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _contextMenu;
    private readonly ToolStripMenuItem _refreshMenuItem;
    private readonly StatusForm _statusForm;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly CancellationTokenSource _shutdown = new();
    private UsageSnapshot? _lastSuccessfulSnapshot;
    private bool _disposed;

    public TrayApplicationContext()
    {
        _aggregator = new UsageAggregator(new IUsageProvider[]
        {
            new ClaudeUsageProvider(),
            new CodexUsageProvider()
        });

        _statusForm = new StatusForm();
        _refreshMenuItem = new ToolStripMenuItem("今すぐ更新", null, async (_, _) => await RefreshAsync().ConfigureAwait(true));

        var exitMenuItem = new ToolStripMenuItem("終了", null, (_, _) => ExitThread());
        _contextMenu = new ContextMenuStrip();
        _contextMenu.Items.Add(_refreshMenuItem);
        _contextMenu.Items.Add(new ToolStripSeparator());
        _contextMenu.Items.Add(exitMenuItem);

        _notifyIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "TokenCheckerWin",
            Visible = true,
            ContextMenuStrip = _contextMenu
        };
        _notifyIcon.MouseUp += NotifyIconOnMouseUp;

        _statusForm.FormClosing += (_, args) =>
        {
            if (args.CloseReason == CloseReason.UserClosing)
            {
                args.Cancel = true;
                _statusForm.Hide();
            }
        };

        Application.Idle += OnApplicationIdle;
    }

    private void OnApplicationIdle(object? sender, EventArgs args)
    {
        Application.Idle -= OnApplicationIdle;
        _ = RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        if (_disposed || _shutdown.IsCancellationRequested)
        {
            return;
        }

        if (!await _refreshLock.WaitAsync(0, _shutdown.Token).ConfigureAwait(true))
        {
            return;
        }

        try
        {
            _refreshMenuItem.Enabled = false;
            _statusForm.SetLoading();
            _notifyIcon.Text = "TokenCheckerWin 更新中";

            UsageSnapshot snapshot;
            try
            {
                snapshot = await _aggregator.CaptureAsync(_shutdown.Token).ConfigureAwait(true);
                _lastSuccessfulSnapshot = snapshot;
            }
            catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
            {
                return;
            }
            catch
            {
                snapshot = new UsageSnapshot(
                    DateTimeOffset.UtcNow,
                    new[]
                    {
                        new ServiceUsage("Claude", ProviderStatus.Error, "Usage data could not be read.", Array.Empty<RateLimitWindow>()),
                        new ServiceUsage("Codex", ProviderStatus.Error, "Usage data could not be read.", Array.Empty<RateLimitWindow>())
                    });
            }

            if (_disposed || _shutdown.IsCancellationRequested)
            {
                return;
            }

            _statusForm.UpdateSnapshot(snapshot, _lastSuccessfulSnapshot);
            _notifyIcon.Text = TrimTooltip(BuildTooltip(_lastSuccessfulSnapshot ?? snapshot));
        }
        finally
        {
            if (!_disposed)
            {
                _refreshMenuItem.Enabled = true;
            }

            _refreshLock.Release();
        }
    }

    private void NotifyIconOnMouseUp(object? sender, MouseEventArgs args)
    {
        if (args.Button != MouseButtons.Left)
        {
            return;
        }

        if (_statusForm.Visible)
        {
            _statusForm.Activate();
            return;
        }

        _statusForm.StartPosition = FormStartPosition.Manual;
        _statusForm.Location = GetStatusFormLocation();
        _statusForm.Show();
        _statusForm.Activate();
    }

    private Point GetStatusFormLocation()
    {
        var area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1024, 768);
        return new Point(
            Math.Max(area.Left, area.Right - _statusForm.Width - 16),
            Math.Max(area.Top, area.Bottom - _statusForm.Height - 16));
    }

    private static string BuildTooltip(UsageSnapshot snapshot)
    {
        var codex = snapshot.Services.FirstOrDefault(service => service.ServiceName == "Codex");
        if (codex is null)
        {
            return "Codex unavailable";
        }

        if (codex.Status != ProviderStatus.Available)
        {
            return $"Codex {codex.Status}";
        }

        var shortWindow = FindWindow(codex, 300);
        var weeklyWindow = FindWindow(codex, 10080);

        return $"Codex {FormatPercent(shortWindow)} / Weekly {FormatPercent(weeklyWindow)}";
    }

    private static RateLimitWindow? FindWindow(ServiceUsage service, long durationMins)
        => service.Windows.FirstOrDefault(window => window.WindowDurationMins == durationMins);

    private static string FormatPercent(RateLimitWindow? window)
        => window?.UsedPercent is null
            ? "n/a"
            : $"{Math.Round(window.UsedPercent.Value)}%";

    private static string TrimTooltip(string value)
        => value.Length <= 63 ? value : value[..63];

    protected override void ExitThreadCore()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _shutdown.Cancel();
        Application.Idle -= OnApplicationIdle;
        _notifyIcon.MouseUp -= NotifyIconOnMouseUp;
        _notifyIcon.Visible = false;
        _notifyIcon.ContextMenuStrip = null;
        _notifyIcon.Dispose();
        _contextMenu.Dispose();
        _statusForm.Dispose();
        _shutdown.Dispose();
        _refreshLock.Dispose();
        base.ExitThreadCore();
    }
}
