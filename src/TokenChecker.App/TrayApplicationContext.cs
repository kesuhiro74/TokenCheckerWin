using System.Drawing;
using System.Windows.Forms;
using TokenChecker.Core;
using TokenChecker.Core.Providers;

namespace TokenChecker.App;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly UsageAggregator _aggregator;
    private readonly SettingsStore _settingsStore;
    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _contextMenu;
    private readonly ToolStripMenuItem _refreshMenuItem;
    private readonly ToolStripMenuItem _settingsMenuItem;
    private readonly StatusForm _statusForm;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly System.Windows.Forms.Timer _refreshTimer = new();
    private readonly Dictionary<string, ServiceUsage> _lastSuccessfulServices = new(StringComparer.OrdinalIgnoreCase);
    private AppSettings _settings;
    private bool _disposed;
    private Icon? _currentTrayIcon;

    public TrayApplicationContext()
    {
        _settingsStore = new SettingsStore();
        _settings = _settingsStore.Load();
        AutoStartManager.Apply(_settings.AutoStartEnabled);

        _aggregator = new UsageAggregator(new IUsageProvider[]
        {
            new ClaudeUsageProvider(),
            new CodexUsageProvider()
        });

        _statusForm = new StatusForm();
        _statusForm.ApplySettings(_settings);
        _refreshMenuItem = new ToolStripMenuItem("今すぐ更新", null, async (_, _) => await RefreshAsync().ConfigureAwait(true));
        _settingsMenuItem = new ToolStripMenuItem("設定", null, (_, _) => ShowSettings());

        var exitMenuItem = new ToolStripMenuItem("終了", null, (_, _) => ExitThread());
        _contextMenu = new ContextMenuStrip();
        _contextMenu.Items.Add(_refreshMenuItem);
        _contextMenu.Items.Add(_settingsMenuItem);
        _contextMenu.Items.Add(new ToolStripSeparator());
        _contextMenu.Items.Add(exitMenuItem);

        _notifyIcon = new NotifyIcon
        {
            Text = "TokenCheckerWin",
            Visible = true,
            ContextMenuStrip = _contextMenu
        };
        ApplyTrayIcon(null, null, TrayIconRenderer.OverallState.Loading);
        _notifyIcon.MouseUp += NotifyIconOnMouseUp;

        _statusForm.FormClosing += (_, args) =>
        {
            if (args.CloseReason == CloseReason.UserClosing)
            {
                args.Cancel = true;
                SaveStatusFormLocation();
                _statusForm.Hide();
            }
        };

        _refreshTimer.Tick += async (_, _) => await RefreshAsync().ConfigureAwait(true);
        ApplyRefreshInterval();
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

        if (!await _refreshLock.WaitAsync(0).ConfigureAwait(true))
        {
            return;
        }

        try
        {
            _refreshMenuItem.Enabled = false;
            _statusForm.SetLoading();
            _notifyIcon.Text = "TokenCheckerWin 更新中";
            ApplyTrayIcon(null, null, TrayIconRenderer.OverallState.Loading);

            UsageSnapshot snapshot;
            try
            {
                snapshot = await _aggregator.CaptureAsync(_shutdown.Token).ConfigureAwait(true);
                UpdateLastSuccessfulServices(snapshot);
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

            var fallbackSnapshot = BuildFallbackSnapshot(snapshot.CapturedAtUtc);
            _statusForm.UpdateSnapshot(snapshot, fallbackSnapshot);
            var iconSnapshot = fallbackSnapshot ?? snapshot;
            var state = TrayIconRenderer.DetermineState(iconSnapshot, out var claudePercent, out var codexPercent);
            ApplyTrayIcon(claudePercent, codexPercent, state);
            _notifyIcon.Text = TrimTooltip(BuildTooltip(iconSnapshot));
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

    private void UpdateLastSuccessfulServices(UsageSnapshot snapshot)
    {
        foreach (var service in snapshot.Services)
        {
            if (service.Status == ProviderStatus.Available && service.Windows.Count > 0)
            {
                _lastSuccessfulServices[service.ServiceName] = service;
            }
        }
    }

    private UsageSnapshot? BuildFallbackSnapshot(DateTimeOffset capturedAtUtc)
    {
        if (_lastSuccessfulServices.Count == 0)
        {
            return null;
        }

        return new UsageSnapshot(capturedAtUtc, _lastSuccessfulServices.Values.ToArray());
    }

    private void NotifyIconOnMouseUp(object? sender, MouseEventArgs args)
    {
        if (args.Button != MouseButtons.Left)
        {
            return;
        }

        ShowStatusForm();
    }

    public void ShowStatusForm()
    {
        if (_disposed)
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
        if (_settings.StatusFormLocation is not null)
        {
            var saved = _settings.StatusFormLocation.Value.ToPoint();
            if (TryClampToVisibleScreen(saved, _statusForm.Size, out var visibleLocation))
            {
                return visibleLocation;
            }
        }

        var area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1024, 768);
        return new Point(
            Math.Max(area.Left, area.Right - _statusForm.Width - 16),
            Math.Max(area.Top, area.Bottom - _statusForm.Height - 16));
    }

    private static bool TryClampToVisibleScreen(Point location, Size size, out Point visibleLocation)
    {
        foreach (var screen in Screen.AllScreens)
        {
            var area = screen.WorkingArea;
            var formBounds = new Rectangle(location, size);
            if (!area.IntersectsWith(formBounds))
            {
                continue;
            }

            visibleLocation = new Point(
                Math.Clamp(location.X, area.Left, Math.Max(area.Left, area.Right - size.Width)),
                Math.Clamp(location.Y, area.Top, Math.Max(area.Top, area.Bottom - size.Height)));
            return true;
        }

        visibleLocation = Point.Empty;
        return false;
    }

    private void ShowSettings()
    {
        if (_disposed)
        {
            return;
        }

        using var form = new SettingsForm(_settings);
        if (form.ShowDialog(_statusForm.Visible ? _statusForm : null) != DialogResult.OK)
        {
            return;
        }

        _settings = form.ToSettings(_settings);
        _statusForm.ApplySettings(_settings);
        _settingsStore.Save(_settings);
        AutoStartManager.Apply(_settings.AutoStartEnabled);
        ApplyRefreshInterval();
        _ = RefreshAsync();
    }

    private void ApplyRefreshInterval()
    {
        _refreshTimer.Stop();
        _refreshTimer.Interval = Math.Max(1, _settings.RefreshIntervalSeconds) * 1000;
        if (!_disposed)
        {
            _refreshTimer.Start();
        }
    }

    private void SaveStatusFormLocation()
    {
        if (!_statusForm.Visible || _statusForm.WindowState != FormWindowState.Normal)
        {
            return;
        }

        _settings.StatusFormLocation = FormLocation.FromPoint(_statusForm.Location);
        _settingsStore.Save(_settings);
    }

    private static string BuildTooltip(UsageSnapshot snapshot)
    {
        var claude = snapshot.Services.FirstOrDefault(service => service.ServiceName == "Claude");
        var codex = snapshot.Services.FirstOrDefault(service => service.ServiceName == "Codex");
        return $"TokenCheckerWin\nClaude: {FormatTooltipService(claude)}\nCodex: {FormatTooltipService(codex)}";
    }

    private static string FormatTooltipService(ServiceUsage? service)
    {
        if (service is null)
        {
            return "未取得";
        }

        if (service.Status != ProviderStatus.Available)
        {
            return ProviderStatusPresenter.BadgeText(service.Status);
        }

        var shortWindow = FindWindow(service, 300);
        var weeklyWindow = FindWindow(service, 10080);
        return $"5h {FormatPercent(shortWindow)} / Weekly {FormatPercent(weeklyWindow)}";
    }

    private static RateLimitWindow? FindWindow(ServiceUsage service, long durationMins)
        => service.Windows.FirstOrDefault(window => window.WindowDurationMins == durationMins);

    private static string FormatPercent(RateLimitWindow? window)
        => window?.UsedPercent is null
            ? "n/a"
            : $"{Math.Round(window.UsedPercent.Value)}%";

    private static string TrimTooltip(string value)
        => value.Length <= 127 ? value : value[..127];

    private void ApplyTrayIcon(double? claudePercent, double? codexPercent, TrayIconRenderer.OverallState state)
    {
        if (_disposed)
        {
            return;
        }

        var newIcon = TrayIconRenderer.CreateIcon(claudePercent, codexPercent, state);
        _notifyIcon.Icon = newIcon;
        _currentTrayIcon?.Dispose();
        _currentTrayIcon = newIcon;
    }

    protected override void ExitThreadCore()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _shutdown.Cancel();
        SaveStatusFormLocation();
        _refreshTimer.Stop();
        Application.Idle -= OnApplicationIdle;
        _notifyIcon.MouseUp -= NotifyIconOnMouseUp;
        _notifyIcon.Visible = false;
        _notifyIcon.ContextMenuStrip = null;
        _notifyIcon.Icon = null;
        _notifyIcon.Dispose();
        _currentTrayIcon?.Dispose();
        _currentTrayIcon = null;
        _contextMenu.Dispose();
        _statusForm.Dispose();
        _refreshTimer.Dispose();
        _shutdown.Dispose();
        _refreshLock.Dispose();
        base.ExitThreadCore();
    }
}
