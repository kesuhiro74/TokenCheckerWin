using System.Drawing;
using System.Windows.Forms;
using TokenChecker.Core;
using TokenChecker.Core.Providers;
using TokenChecker.Core.Providers.GitHubCopilot;

namespace TokenChecker.App;

internal sealed class TrayApplicationContext : ApplicationContext
{
    // Per-window state (the Claude/Codex status window and the GitHub Copilot
    // window each get their own tray icon, on/off, and display method).
    private sealed class PopupSlot
    {
        public PopupSlot(Form form, NotifyIcon icon, Action activate, Func<Point> resolveLocation, Action saveLocation)
        {
            Form = form;
            Icon = icon;
            Activate = activate;
            ResolveLocation = resolveLocation;
            SaveLocation = saveLocation;
        }

        public Form Form { get; }
        public NotifyIcon Icon { get; }
        public Action Activate { get; }
        public Func<Point> ResolveLocation { get; }
        public Action SaveLocation { get; }
        public WindowDisplayMode Mode { get; set; }
        public bool Enabled { get; set; }
        public bool Pinned { get; set; }
        public Point LastIconHoverPos { get; set; }
        public int LastIconHoverTick { get; set; }
        public Icon? CurrentImage { get; set; }
    }

    // Rebuilt when settings change so only enabled windows/services have providers
    // (no Claude/Codex calls when the status window is off; no billing endpoint
    // when the Copilot window is off).
    private UsageAggregator _aggregator;
    private readonly SettingsStore _settingsStore;
    private readonly LastUsageStore _lastUsageStore;

    // One tray icon per window, plus a control icon shown ONLY when both windows
    // are off so the settings/exit menu is always reachable.
    private readonly NotifyIcon _statusIcon;
    private readonly NotifyIcon _copilotIcon;
    private readonly NotifyIcon _controlIcon;
    private readonly Icon _controlIconImage;

    private readonly ContextMenuStrip _contextMenu;
    private readonly ToolStripMenuItem _refreshMenuItem;
    private readonly ToolStripMenuItem _displayModeMenuItem;
    private readonly ToolStripMenuItem _modeNormalItem;
    private readonly ToolStripMenuItem _modeCompactItem;
    private readonly ToolStripMenuItem _modeMinimumItem;
    private readonly ToolStripMenuItem _copilotDisplayMenuItem;
    private readonly ToolStripMenuItem _copilotAlwaysItem;
    private readonly ToolStripMenuItem _copilotHoverItem;
    private readonly ToolStripMenuItem _settingsMenuItem;

    private readonly StatusForm _statusForm;
    private readonly CopilotWindow _copilotWindow;
    private readonly PopupSlot _status;
    private readonly PopupSlot _copilot;
    private readonly PopupSlot[] _slots;

    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly System.Windows.Forms.Timer _refreshTimer = new();
    // Fade-in for a hover-previewed window, and a shared leave-poll that hides a
    // hovered (un-pinned) window once the cursor leaves it.
    private readonly System.Windows.Forms.Timer _fadeInTimer = new();
    private readonly System.Windows.Forms.Timer _hoverLeaveTimer = new();
    private Form? _fadeTarget;
    // Outside-click dismissal for a pinned HoverPreview window (either popup):
    // briefly suppress the Deactivate-driven hide around an icon's own click and
    // while the shared context menu is open, so those interactions don't fight it.
    private int _suppressPopupDeactivateUntil;
    private bool _contextMenuOpen;

    private readonly Dictionary<string, ServiceUsage> _lastSuccessfulServices = new(StringComparer.OrdinalIgnoreCase);
    private readonly CopilotUsageTracker _copilotTracker = new();
    // Last computed Copilot today's-burn percent (of the monthly allowance), cached
    // from UpdateCopilotWindow's tracker.Observe so UpdateTrayIcons can drive the
    // burn warning mark without re-running Observe (which would skew the 09:00
    // baseline). Null until the first fresh Available sample produces insights.
    private double? _copilotTodayPercent;
    private readonly AuthCommandService _authService = new();
    private AppSettings _settings;
    private bool _disposed;
    private UsageSnapshot? _lastSnapshot;
    private SettingsForm? _settingsForm;

    public AuthCommandService AuthService => _authService;

    public ProviderStatus GetServiceStatus(string serviceName)
        => _lastSnapshot?.Services.FirstOrDefault(s => string.Equals(s.ServiceName, serviceName, StringComparison.OrdinalIgnoreCase))?.Status
            ?? ProviderStatus.Unknown;

    public Task RefreshUsageAsync() => RefreshAsync();

    public TrayApplicationContext()
    {
        _settingsStore = new SettingsStore();
        _settings = _settingsStore.Load();
        _lastUsageStore = new LastUsageStore();
        SeedLastSuccessfulFromStore();
        AutoStartManager.Apply(_settings.AutoStartEnabled);

        _aggregator = BuildAggregator(_settings);

        _statusForm = new StatusForm();
        _statusForm.ApplySettings(_settings);
        _copilotWindow = new CopilotWindow();
        _copilotWindow.ApplyAccent(_settings.CopilotAccentColor());

        // ----- Tray icons --------------------------------------------------
        _contextMenu = new ContextMenuStrip();
        _statusIcon = new NotifyIcon { Text = "TokenCheckerWin", ContextMenuStrip = _contextMenu, Visible = false };
        _copilotIcon = new NotifyIcon { Text = "GitHub Copilot", ContextMenuStrip = _contextMenu, Visible = false };
        _controlIconImage = TrayIconRenderer.CreateIcon(null, null, TrayIconRenderer.OverallState.Unknown);
        _controlIcon = new NotifyIcon { Text = "TokenCheckerWin", ContextMenuStrip = _contextMenu, Icon = _controlIconImage, Visible = false };

        _status = new PopupSlot(
            _statusForm, _statusIcon,
            activate: () => _statusForm.Activate(),
            resolveLocation: GetStatusFormLocation,
            saveLocation: SaveStatusFormLocation);
        _copilot = new PopupSlot(
            _copilotWindow, _copilotIcon,
            activate: () => { _copilotWindow.Activate(); _copilotWindow.ClearFocus(); },
            resolveLocation: GetCopilotWindowLocation,
            saveLocation: SaveCopilotWindowLocation);
        _slots = [_status, _copilot];

        // Wired after the slots exist (the handlers reference them). Esc/close
        // un-pins and hides rather than disposing the form — EXCEPT an Always
        // ("常時表示") window, which stays visible (see DismissViaUserGesture). The
        // FormClosing always cancels (we never destroy the window here).
        _statusForm.FormClosing += (_, args) =>
        {
            if (args.CloseReason == CloseReason.UserClosing)
            {
                args.Cancel = true;
                DismissViaUserGesture(_status);
            }
        };
        _statusForm.HideRequested += (_, _) => DismissViaUserGesture(_status);
        _copilotWindow.FormClosing += (_, args) =>
        {
            if (args.CloseReason == CloseReason.UserClosing)
            {
                args.Cancel = true;
                DismissViaUserGesture(_copilot);
            }
        };
        _copilotWindow.HideRequested += (_, _) => DismissViaUserGesture(_copilot);

        _statusIcon.MouseUp += (_, e) => { if (e.Button == MouseButtons.Left) OnIconClick(_status); };
        _statusIcon.MouseMove += (_, _) => OnIconMouseMove(_status);
        _copilotIcon.MouseUp += (_, e) => { if (e.Button == MouseButtons.Left) OnIconClick(_copilot); };
        _copilotIcon.MouseMove += (_, _) => OnIconMouseMove(_copilot);
        _controlIcon.MouseUp += (_, e) => { if (e.Button == MouseButtons.Left) ShowSettings(); };

        // Outside-click dismissal for a window pinned via a HoverPreview tray click:
        // the window is activated when shown by a click, so clicking outside it
        // deactivates it -> hide + unpin. Both popups behave identically here.
        _statusForm.Deactivate += (_, _) => OnPopupDeactivated(_status);
        _copilotWindow.Deactivate += (_, _) => OnPopupDeactivated(_copilot);
        // Interacting with ANY tray icon (left toggle OR right-click to open the SHARED
        // context menu) deactivates a popup. Suppress the dismissal on each icon's
        // MouseDown — the earliest signal, ahead of both the Deactivate and the menu's
        // Opening — so tray/menu actions never hide a pinned window. A plain click
        // elsewhere (desktop / another app) is NOT suppressed, so it still hides.
        void SuppressPopupDismiss(object? _, MouseEventArgs __) => _suppressPopupDeactivateUntil = Environment.TickCount + 500;
        _statusIcon.MouseDown += SuppressPopupDismiss;
        _copilotIcon.MouseDown += SuppressPopupDismiss;
        _controlIcon.MouseDown += SuppressPopupDismiss;

        // ----- Context menu (5 items): 今すぐ更新 / Claude·Codex 表示モード /
        // GitHub Copilot 表示モード / 設定 / 終了. Login, first-time-setup, and the
        // window-show items live in the settings dialog instead.
        _refreshMenuItem = new ToolStripMenuItem(Strings.T("今すぐ更新"), null, async (_, _) => await RefreshAsync().ConfigureAwait(true));

        _modeNormalItem = new ToolStripMenuItem(Strings.T("通常モード"), null, (_, _) => SetDisplayMode(DisplayMode.Normal));
        _modeCompactItem = new ToolStripMenuItem(Strings.T("コンパクトモード"), null, (_, _) => SetDisplayMode(DisplayMode.Compact));
        _modeMinimumItem = new ToolStripMenuItem(Strings.T("ミニマムモード"), null, (_, _) => SetDisplayMode(DisplayMode.Minimum));
        _displayModeMenuItem = new ToolStripMenuItem(Strings.T("Claude/Codexステータス表示モード"));
        _displayModeMenuItem.DropDownItems.Add(_modeNormalItem);
        _displayModeMenuItem.DropDownItems.Add(_modeCompactItem);
        _displayModeMenuItem.DropDownItems.Add(_modeMinimumItem);

        _copilotAlwaysItem = new ToolStripMenuItem(Strings.T("常時表示"), null, (_, _) => SetCopilotDisplayMode(WindowDisplayMode.Always));
        _copilotHoverItem = new ToolStripMenuItem(Strings.T("ホバー表示"), null, (_, _) => SetCopilotDisplayMode(WindowDisplayMode.HoverPreview));
        _copilotDisplayMenuItem = new ToolStripMenuItem(Strings.T("GitHubCopilot表示モード"));
        _copilotDisplayMenuItem.DropDownItems.Add(_copilotAlwaysItem);
        _copilotDisplayMenuItem.DropDownItems.Add(_copilotHoverItem);

        _settingsMenuItem = new ToolStripMenuItem(Strings.T("設定"), null, (_, _) => ShowSettings());
        var exitMenuItem = new ToolStripMenuItem(Strings.T("終了"), null, (_, _) => ExitThread());

        _contextMenu.Items.Add(_refreshMenuItem);
        _contextMenu.Items.Add(_displayModeMenuItem);
        _contextMenu.Items.Add(_copilotDisplayMenuItem);
        _contextMenu.Items.Add(_settingsMenuItem);
        _contextMenu.Items.Add(exitMenuItem);
        _contextMenu.Opening += (_, _) => { _contextMenuOpen = true; SyncContextMenuState(); };
        _contextMenu.Closed += (_, _) => _contextMenuOpen = false;

        _fadeInTimer.Interval = 30;
        _fadeInTimer.Tick += (_, _) => FadeInStep();
        _hoverLeaveTimer.Interval = 150;
        _hoverLeaveTimer.Tick += (_, _) => HoverLeaveCheck();

        UpdateTrayIcons(null, loading: true);
        SyncContextMenuState();

        _refreshTimer.Tick += async (_, _) => await RefreshAsync().ConfigureAwait(true);
        ApplyRefreshInterval();
        Application.Idle += OnApplicationIdle;
    }

    private void OnApplicationIdle(object? sender, EventArgs args)
    {
        Application.Idle -= OnApplicationIdle;
        // Show the windows that are enabled with the Always display method.
        ApplyWindowModes();
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
            _copilotWindow.SetLoading();
            UpdateTrayIcons(null, loading: true);

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
                // Whole-capture failure fallback: only the services whose providers
                // are actually enabled (a disabled window never shows an error row).
                var errorServices = new List<ServiceUsage>();
                if (_settings.ClaudeProviderEnabled)
                {
                    errorServices.Add(new ServiceUsage("Claude", ProviderStatus.Error, "Usage data could not be read.", Array.Empty<RateLimitWindow>()));
                }

                if (_settings.CodexProviderEnabled)
                {
                    errorServices.Add(new ServiceUsage("Codex", ProviderStatus.Error, "Usage data could not be read.", Array.Empty<RateLimitWindow>()));
                }

                if (_settings.CopilotProviderEnabled)
                {
                    errorServices.Add(new ServiceUsage(AppSettings.CopilotServiceName, ProviderStatus.Error, "Usage data could not be read.", Array.Empty<RateLimitWindow>()));
                }

                snapshot = new UsageSnapshot(DateTimeOffset.UtcNow, errorServices.ToArray());
            }

            if (_disposed || _shutdown.IsCancellationRequested)
            {
                return;
            }

            _lastSnapshot = snapshot;
            var fallbackSnapshot = BuildFallbackSnapshot(snapshot.CapturedAtUtc);
            _statusForm.UpdateSnapshot(snapshot, fallbackSnapshot);
            UpdateCopilotWindow(snapshot, fallbackSnapshot);
            var iconSnapshot = fallbackSnapshot ?? snapshot;
            UpdateTrayIcons(iconSnapshot, loading: false);
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
        var changed = false;
        foreach (var service in snapshot.Services)
        {
            if (service.Status == ProviderStatus.Available && service.Windows.Count > 0)
            {
                _lastSuccessfulServices[service.ServiceName] = service;
                changed = true;
            }
        }

        if (changed)
        {
            PersistLastSuccessfulServices(snapshot.CapturedAtUtc);
        }
    }

    private void PersistLastSuccessfulServices(DateTimeOffset capturedAtUtc)
    {
        if (_lastSuccessfulServices.Count == 0)
        {
            return;
        }

        var snapshot = new UsageSnapshot(capturedAtUtc, _lastSuccessfulServices.Values.ToArray());
        _lastUsageStore.Save(snapshot);
    }

    private void SeedLastSuccessfulFromStore()
    {
        var loaded = _lastUsageStore.Load();
        if (loaded is null)
        {
            return;
        }

        foreach (var service in loaded.Services)
        {
            if (service.Status == ProviderStatus.Available && service.Windows.Count > 0)
            {
                _lastSuccessfulServices[service.ServiceName] = service;
            }
        }
    }

    private UsageSnapshot? BuildFallbackSnapshot(DateTimeOffset capturedAtUtc)
    {
        // Only surface last-success values for services whose provider is still
        // enabled. last_usage.json keeps the full history (re-enabling restores it),
        // but a disabled service's stale Available value must never reach the tray
        // ring, status window, or tooltip.
        var services = _lastSuccessfulServices.Values
            .Where(service => IsServiceEnabledForDisplay(service.ServiceName))
            .ToArray();
        return services.Length == 0 ? null : new UsageSnapshot(capturedAtUtc, services);
    }

    // A service is shown only when its provider is enabled under the current
    // settings (so a disabled Claude/Codex/Copilot is filtered out of all display
    // paths — tray icon, status window, tooltip).
    private bool IsServiceEnabledForDisplay(string serviceName)
    {
        if (string.Equals(serviceName, "Claude", StringComparison.OrdinalIgnoreCase))
        {
            return _settings.ClaudeProviderEnabled;
        }

        if (string.Equals(serviceName, "Codex", StringComparison.OrdinalIgnoreCase))
        {
            return _settings.CodexProviderEnabled;
        }

        if (string.Equals(serviceName, AppSettings.CopilotServiceName, StringComparison.OrdinalIgnoreCase))
        {
            return _settings.CopilotProviderEnabled;
        }

        return false;
    }

    private UsageSnapshot? FilterForDisplay(UsageSnapshot? snapshot)
    {
        if (snapshot is null)
        {
            return null;
        }

        var services = snapshot.Services.Where(s => IsServiceEnabledForDisplay(s.ServiceName)).ToArray();
        return new UsageSnapshot(snapshot.CapturedAtUtc, services);
    }

    // ----- Window show/hide + display methods ------------------------------

    // Esc / window-close gesture. Only the Copilot window in Always mode stays visible
    // (its "常時表示" must not be dismissed by Esc/close — turn the window off in
    // settings to remove it). The Claude/Codex status window hides as before.
    private void DismissViaUserGesture(PopupSlot slot)
    {
        if (ReferenceEquals(slot, _copilot) && slot.Mode == WindowDisplayMode.Always)
        {
            return;
        }

        slot.Pinned = false;
        HidePopup(slot);
    }

    private void OnIconClick(PopupSlot slot)
    {
        if (_disposed || !slot.Enabled)
        {
            return;
        }

        if (slot.Mode == WindowDisplayMode.Always)
        {
            // The Copilot window in Always mode must NEVER hide on click ("常時表示" =
            // always visible) — a click only shows / brings it to front. The Claude/
            // Codex status window keeps its traditional show/hide TOGGLE on click.
            if (ReferenceEquals(slot, _copilot) || !slot.Form.Visible)
            {
                ShowPopup(slot, activate: true);
            }
            else
            {
                HidePopup(slot);
            }

            return;
        }

        // HoverPreview: clicking the icon pins (keeps it shown) / un-pins (hides).
        slot.Pinned = !slot.Pinned;
        if (slot.Pinned)
        {
            ShowPopup(slot, activate: true);
        }
        else
        {
            HidePopup(slot);
        }
    }

    private void OnIconMouseMove(PopupSlot slot)
    {
        if (_disposed || !slot.Enabled || slot.Mode != WindowDisplayMode.HoverPreview || slot.Pinned)
        {
            return;
        }

        slot.LastIconHoverTick = Environment.TickCount;
        slot.LastIconHoverPos = Cursor.Position;
        if (!slot.Form.Visible)
        {
            ShowPopup(slot, activate: false);
        }

        _hoverLeaveTimer.Start();
    }

    // activate=true (explicit click/menu/pin) brings the window forward (Esc works)
    // at full opacity. activate=false (hover glance) fades it in without stealing
    // focus.
    private void ShowPopup(PopupSlot slot, bool activate)
    {
        if (_disposed || !slot.Enabled)
        {
            return;
        }

        var fadeGlance = slot.Mode == WindowDisplayMode.HoverPreview && !activate;
        if (!slot.Form.Visible)
        {
            slot.Form.StartPosition = FormStartPosition.Manual;
            slot.Form.Location = slot.ResolveLocation();
            slot.Form.Opacity = fadeGlance ? 0d : 1d;
            slot.Form.Show();
            if (fadeGlance)
            {
                StartFadeIn(slot.Form);
            }
        }

        if (activate)
        {
            StopFadeIn(slot.Form);
            slot.Form.Opacity = 1d;
            slot.Activate();
            // Re-base the outside-click suppression to the actual activation moment:
            // the click -> show -> Activate path can take longer than the MouseDown
            // window, so a stray Deactivate right after showing must not dismiss it.
            _suppressPopupDeactivateUntil = Environment.TickCount + 400;
        }

        UpdateCopilotPinnedAppearance();
    }

    private void HidePopup(PopupSlot slot)
    {
        StopFadeIn(slot.Form);
        if (slot.Form.Visible)
        {
            slot.SaveLocation();
            slot.Form.Hide();
        }

        slot.Form.Opacity = 1d;
        UpdateCopilotPinnedAppearance();
    }

    // The faint 1px "pinned" outline is shown only while the Copilot window is in a
    // persistent state: Always-shown, or HoverPreview pinned via a tray click. A
    // plain hover glance shows no outline.
    private void UpdateCopilotPinnedAppearance()
    {
        var bordered = _copilot.Enabled
            && _copilot.Form.Visible
            && (_copilot.Mode == WindowDisplayMode.Always || _copilot.Pinned);
        _copilotWindow.SetPinned(bordered);
    }

    // Click-outside-to-dismiss for either popup. The Deactivate event fires once the
    // window is activated, but dismissal is gated below to HoverPreview + pinned only
    // — an Always ("常時表示") window is never dismissed here. Suppressed around any
    // tray icon's own click and while the shared context menu is open so tray/menu
    // actions don't conflict. Both the Claude/Codex and Copilot windows share this.
    private void OnPopupDeactivated(PopupSlot slot)
    {
        if (_disposed || _contextMenuOpen || Environment.TickCount < _suppressPopupDeactivateUntil)
        {
            return;
        }

        if (!slot.Form.Visible)
        {
            return;
        }

        // Outside-click dismissal applies ONLY to a HoverPreview window pinned via a
        // click. An Always ("常時表示") window must NOT be dismissed by an outside
        // click — that would defeat the always-visible setting.
        var dismissable = slot.Mode == WindowDisplayMode.HoverPreview && slot.Pinned;
        if (!dismissable)
        {
            return;
        }

        slot.Pinned = false;
        HidePopup(slot);
    }

    // Reconcile each window's visibility with its enabled + display method.
    private void ApplyWindowModes()
    {
        _status.Mode = _settings.ClaudeCodexDisplayMode;
        _status.Enabled = _settings.ClaudeCodexWindowEnabled;
        _copilot.Mode = _settings.CopilotDisplayMode;
        _copilot.Enabled = _settings.CopilotWindowEnabled;

        foreach (var slot in _slots)
        {
            if (!slot.Enabled)
            {
                slot.Pinned = false;
                HidePopup(slot);
            }
            else if (slot.Mode == WindowDisplayMode.Always)
            {
                ShowPopup(slot, activate: false);
            }
            else if (!slot.Pinned)
            {
                // HoverPreview appears on hover only.
                HidePopup(slot);
            }
        }
    }

    private void StartFadeIn(Form form)
    {
        // Two HoverPreview windows can overlap and be fading independently. The
        // fade is single-targeted, so finalize any other in-flight target to full
        // opacity before retargeting — otherwise it could be stranded transparent.
        if (_fadeTarget is not null && !ReferenceEquals(_fadeTarget, form))
        {
            _fadeTarget.Opacity = 1d;
        }

        _fadeTarget = form;
        form.Opacity = 0d;
        _fadeInTimer.Start();
    }

    private void StopFadeIn(Form form)
    {
        if (ReferenceEquals(_fadeTarget, form))
        {
            _fadeInTimer.Stop();
            _fadeTarget = null;
        }
    }

    private void FadeInStep()
    {
        if (_disposed || _fadeTarget is null || !_fadeTarget.Visible)
        {
            _fadeInTimer.Stop();
            _fadeTarget = null;
            return;
        }

        var next = _fadeTarget.Opacity + 0.18d;
        if (next >= 1d)
        {
            _fadeTarget.Opacity = 1d;
            _fadeInTimer.Stop();
            _fadeTarget = null;
        }
        else
        {
            _fadeTarget.Opacity = next;
        }
    }

    private void HoverLeaveCheck()
    {
        if (_disposed)
        {
            _hoverLeaveTimer.Stop();
            return;
        }

        var anyActive = false;
        foreach (var slot in _slots)
        {
            if (!slot.Enabled || slot.Mode != WindowDisplayMode.HoverPreview || slot.Pinned || !slot.Form.Visible)
            {
                continue;
            }

            anyActive = true;

            // Defensive: a visible hover window that is not the current fade target
            // must be fully opaque (recover any window stranded by a fade retarget).
            if (!ReferenceEquals(_fadeTarget, slot.Form) && slot.Form.Opacity < 1d)
            {
                slot.Form.Opacity = 1d;
            }

            var cursor = Cursor.Position;
            // This tests the WHOLE window (Form.Bounds) for show/hide. It is separate
            // from the Copilot card's RefreshHover, which tests the main-value rect
            // only (MainUsageScreenRect) to swap %/detail — the two must not be conflated.
            var overWindow = slot.Form.Bounds.Contains(cursor);
            // A motionless cursor still resting on the tray icon produces no further
            // MouseMove events: an unchanged position since the last icon hover means
            // it is still on the icon. The recent-hover grace bridges the natural
            // icon->window move so it does not flicker mid-transit.
            var stillOnIcon = cursor == slot.LastIconHoverPos;
            var recentlyOverIcon = Environment.TickCount - slot.LastIconHoverTick < 700;
            if (!overWindow && !stillOnIcon && !recentlyOverIcon)
            {
                HidePopup(slot);
            }
        }

        if (!anyActive)
        {
            _hoverLeaveTimer.Stop();
        }
    }

    // Menu / CLI entry points.
    public void ShowStatusForm()
    {
        if (_disposed || !_status.Enabled)
        {
            return;
        }

        ShowPopup(_status, activate: true);
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

    private Point GetCopilotWindowLocation()
    {
        if (_settings.CopilotWindowLocation is not null)
        {
            var saved = _settings.CopilotWindowLocation.Value.ToPoint();
            if (TryClampToVisibleScreen(saved, _copilotWindow.Size, out var visibleLocation))
            {
                return visibleLocation;
            }
        }

        var area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1024, 768);
        return new Point(
            Math.Max(area.Left, area.Right - _copilotWindow.Width - 16),
            Math.Max(area.Top, area.Bottom - _copilotWindow.Height - 16));
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

    public void ShowSettingsForm() => ShowSettings();

    private void ShowSettings()
    {
        if (_disposed)
        {
            return;
        }

        if (_settingsForm is not null)
        {
            _settingsForm.Activate();
            return;
        }

        using var form = new SettingsForm(_settings, this);
        _settingsForm = form;
        try
        {
            IWin32Window? owner = _statusForm.Visible ? (IWin32Window)_statusForm
                : _copilotWindow.Visible ? _copilotWindow
                : null;
            if (form.ShowDialog(owner) != DialogResult.OK)
            {
                return;
            }

            _settings = form.ToSettings(_settings);

            // Rebuild the aggregator (cheap, stateless) so only enabled windows/
            // services have providers, then reconcile window visibility and icons.
            _aggregator = BuildAggregator(_settings);
            _statusForm.ApplySettings(_settings);
            _copilotWindow.ApplyAccent(_settings.CopilotAccentColor());
            ApplyWindowModes();
            // Filter the cached snapshot so a just-disabled service's stale value
            // does not flash in the tray icon before the next refresh.
            UpdateTrayIcons(FilterForDisplay(_lastSnapshot), loading: _lastSnapshot is null);
            SyncContextMenuState();
            SyncDisplayModeMenuChecks();
            _settingsStore.Save(_settings);
            AutoStartManager.Apply(_settings.AutoStartEnabled);
            ApplyRefreshInterval();
            _ = RefreshAsync();
        }
        finally
        {
            _settingsForm = null;
        }
    }

    private void SetDisplayMode(DisplayMode mode)
    {
        if (_disposed)
        {
            return;
        }

        if (_settings.DisplayMode == mode)
        {
            SyncDisplayModeMenuChecks();
            return;
        }

        _settings.DisplayMode = mode;
        _settings.Normalize();
        _statusForm.ApplySettings(_settings);
        SyncDisplayModeMenuChecks();
        _settingsStore.Save(_settings);
        // Re-render the status window layout (above) and the tray from the cached
        // snapshot — no provider re-fetch needed for a content-mode change.
        UpdateTrayIcons(FilterForDisplay(_lastSnapshot), loading: _lastSnapshot is null);
    }

    private void SyncDisplayModeMenuChecks()
    {
        _modeNormalItem.Checked = _settings.DisplayMode == DisplayMode.Normal;
        _modeCompactItem.Checked = _settings.DisplayMode == DisplayMode.Compact;
        _modeMinimumItem.Checked = _settings.DisplayMode == DisplayMode.Minimum;
    }

    // GitHub Copilot window display method (常時表示 / ホバー表示). Saves and applies
    // immediately: Always shows the window (and unifies away any pin); HoverPreview
    // hides it unless pinned (then it appears on tray-icon hover). The tray icons
    // are refreshed from the cached snapshot.
    private void SetCopilotDisplayMode(WindowDisplayMode mode)
    {
        if (_disposed)
        {
            return;
        }

        if (_settings.CopilotDisplayMode != mode)
        {
            _settings.CopilotDisplayMode = mode;
            _settings.Normalize();
            if (mode == WindowDisplayMode.Always)
            {
                // Always == always-visible; drop any pin so the state is unified.
                _copilot.Pinned = false;
            }

            ApplyWindowModes();
            UpdateTrayIcons(FilterForDisplay(_lastSnapshot), loading: _lastSnapshot is null);
            _settingsStore.Save(_settings);
        }

        SyncCopilotDisplayMenuChecks();
    }

    private void SyncCopilotDisplayMenuChecks()
    {
        _copilotAlwaysItem.Checked = _settings.CopilotDisplayMode == WindowDisplayMode.Always;
        _copilotHoverItem.Checked = _settings.CopilotDisplayMode == WindowDisplayMode.HoverPreview;
    }

    private void SyncContextMenuState()
    {
        // The display-mode submenus are only meaningful when their window is on.
        _displayModeMenuItem.Enabled = _settings.ClaudeCodexWindowEnabled;
        _copilotDisplayMenuItem.Enabled = _settings.CopilotWindowEnabled;
        SyncDisplayModeMenuChecks();
        SyncCopilotDisplayMenuChecks();
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

    private void SaveCopilotWindowLocation()
    {
        if (!_copilotWindow.Visible || _copilotWindow.WindowState != FormWindowState.Normal)
        {
            return;
        }

        _settings.CopilotWindowLocation = FormLocation.FromPoint(_copilotWindow.Location);
        _settingsStore.Save(_settings);
    }

    // ----- Tray icon rendering ---------------------------------------------

    private void UpdateTrayIcons(UsageSnapshot? snapshot, bool loading)
    {
        if (_disposed)
        {
            return;
        }

        var ccOn = _settings.ClaudeCodexWindowEnabled;
        var cpOn = _settings.CopilotWindowEnabled;

        if (ccOn)
        {
            // Only the enabled (visible) services get a bar — one shows a single bar.
            var showClaude = _settings.IsServiceVisible("Claude");
            var showCodex = _settings.IsServiceVisible("Codex");
            Icon icon = loading || snapshot is null
                ? TrayIconRenderer.CreateStatusBarsIcon(null, null, showClaude, showCodex, loading: true)
                : MakeStatusIcon(snapshot, showClaude, showCodex);
            SetIcon(_status, icon);
            _statusIcon.Text = loading ? Strings.T("TokenCheckerWin 更新中") : TrimTooltip(BuildStatusTooltip(snapshot));
        }

        _statusIcon.Visible = ccOn;

        if (cpOn)
        {
            var percent = loading || snapshot is null ? null : CopilotPercent(snapshot);
            // today's-burn mark: amber at 4-5% / red at >=5% (no mark below 4% or
            // while loading). Reuses the cached percent from UpdateCopilotWindow and
            // the shared severity/color logic in CopilotWindow.
            var burnSeverity = CopilotWindow.GetTodayDeltaSeverity(loading ? null : _copilotTodayPercent);
            var burnMark = TrayIconRenderer.BurnMarkColor(burnSeverity);
            SetIcon(_copilot, TrayIconRenderer.CreateCopilotIcon(
                percent, loading, _settings.CopilotAccentColor(), burnMark));
            _copilotIcon.Text = loading ? Strings.T("GitHub Copilot 更新中") : TrimTooltip(BuildCopilotTooltip(snapshot));
        }

        _copilotIcon.Visible = cpOn;

        // The control icon is the always-reachable menu host when both windows are
        // off; it never shows a window (left-click opens settings).
        _controlIcon.Visible = !ccOn && !cpOn;
    }

    private static Icon MakeStatusIcon(UsageSnapshot snapshot, bool showClaude, bool showCodex)
    {
        // DetermineState computes each service's percent (max of its windows); the
        // overall state is no longer needed because each bar self-escalates at 80/95.
        _ = TrayIconRenderer.DetermineState(snapshot, out var claudePercent, out var codexPercent);
        return TrayIconRenderer.CreateStatusBarsIcon(claudePercent, codexPercent, showClaude, showCodex, loading: false);
    }

    private static void SetIcon(PopupSlot slot, Icon icon)
    {
        slot.Icon.Icon = icon;
        slot.CurrentImage?.Dispose();
        slot.CurrentImage = icon;
    }

    private double? CopilotPercent(UsageSnapshot snapshot)
    {
        if (_settings.CopilotCreditAllowance() is not int cap || cap <= 0)
        {
            return null;
        }

        var service = snapshot.Services.FirstOrDefault(s =>
            string.Equals(s.ServiceName, AppSettings.CopilotServiceName, StringComparison.OrdinalIgnoreCase)
            && s.Status == ProviderStatus.Available);
        var window = service?.Windows.FirstOrDefault(w => w.WindowDurationMins == 43200);
        return window?.Used is long used ? Math.Min(100d, used / (double)cap * 100d) : null;
    }

    private void UpdateCopilotWindow(UsageSnapshot snapshot, UsageSnapshot? fallbackSnapshot)
    {
        var copilot = snapshot.Services.FirstOrDefault(s =>
            string.Equals(s.ServiceName, AppSettings.CopilotServiceName, StringComparison.OrdinalIgnoreCase));
        var fallbackCopilot = copilot?.Status == ProviderStatus.Available
            ? copilot
            : fallbackSnapshot?.Services.FirstOrDefault(s =>
                string.Equals(s.ServiceName, AppSettings.CopilotServiceName, StringComparison.OrdinalIgnoreCase)
                && s.Status == ProviderStatus.Available);

        var allowance = _settings.CopilotCreditAllowance();

        // Track + project ONLY on a genuinely fresh Available sample (current
        // snapshot); fallback values do not update the tracker (insights stay null).
        CopilotInsights? insights = null;
        if (copilot?.Status == ProviderStatus.Available
            && copilot.Windows.FirstOrDefault(w => w.WindowDurationMins == 43200)?.Used is long usedNow)
        {
            insights = _copilotTracker.Observe(usedNow, allowance, snapshot.CapturedAtUtc);
        }

        // Cache today's burn for the tray icon's warning mark. Only a fresh sample
        // produces insights; a fallback turn leaves the previous value in place so
        // the mark matches the bar (which also holds its last value on fallback).
        if (insights is not null)
        {
            _copilotTodayPercent = insights.TodayDeltaPercent;
        }

        _copilotWindow.Update(_settings.CopilotPlanTitle(), copilot, fallbackCopilot, allowance, insights);
    }

    // ----- Tooltips ---------------------------------------------------------

    private string BuildStatusTooltip(UsageSnapshot? snapshot)
    {
        // Only list the services whose provider is enabled, so a disabled service
        // never lingers in the tooltip.
        var lines = new List<string> { "TokenCheckerWin" };
        if (_settings.ClaudeProviderEnabled)
        {
            lines.Add($"Claude Code: {FormatTooltipService(FindService(snapshot, "Claude"))}");
        }

        if (_settings.CodexProviderEnabled)
        {
            lines.Add($"Codex: {FormatTooltipService(FindService(snapshot, "Codex"))}");
        }

        return string.Join("\n", lines);
    }

    private static ServiceUsage? FindService(UsageSnapshot? snapshot, string serviceName)
        => snapshot?.Services.FirstOrDefault(s => string.Equals(s.ServiceName, serviceName, StringComparison.OrdinalIgnoreCase));

    private string BuildCopilotTooltip(UsageSnapshot? snapshot)
    {
        var copilot = snapshot?.Services.FirstOrDefault(service =>
            string.Equals(service.ServiceName, AppSettings.CopilotServiceName, StringComparison.OrdinalIgnoreCase));
        return $"GitHub Copilot: {FormatCopilotTooltip(copilot)}";
    }

    private string FormatCopilotTooltip(ServiceUsage? service)
    {
        if (service is null)
        {
            return Strings.T("未取得");
        }

        if (service.Status != ProviderStatus.Available)
        {
            return ProviderStatusPresenter.BadgeText(service.Status);
        }

        var window = service.Windows.FirstOrDefault(w => w.WindowDurationMins == 43200);
        var used = window?.Used;
        var allowance = _settings.CopilotCreditAllowance();
        if (used is long u && allowance is int cap && cap > 0)
        {
            var percent = Math.Min(100d, u / (double)cap * 100d);
            return $"{Math.Round(percent):0}% ({u:N0}/{cap:N0})";
        }

        return used is long u2 ? $"{u2:N0} credits" : "n/a";
    }

    private static string FormatTooltipService(ServiceUsage? service)
    {
        if (service is null)
        {
            return Strings.T("未取得");
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

    // Build the aggregator from the per-window/per-service gates so a disabled
    // window never triggers its provider's fetch.
    private static UsageAggregator BuildAggregator(AppSettings settings)
    {
        var providers = new List<IUsageProvider>();
        if (settings.ClaudeProviderEnabled)
        {
            providers.Add(new ClaudeUsageProvider());
        }

        if (settings.CodexProviderEnabled)
        {
            providers.Add(new CodexUsageProvider());
        }

        if (settings.CopilotProviderEnabled)
        {
            providers.Add(new GitHubCopilotUsageProvider());
        }

        return new UsageAggregator(providers);
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
        SaveCopilotWindowLocation();
        _refreshTimer.Stop();
        _fadeInTimer.Stop();
        _hoverLeaveTimer.Stop();
        Application.Idle -= OnApplicationIdle;

        foreach (var slot in _slots)
        {
            slot.Icon.Visible = false;
            slot.Icon.ContextMenuStrip = null;
            slot.Icon.Icon = null;
            slot.Icon.Dispose();
            slot.CurrentImage?.Dispose();
            slot.CurrentImage = null;
        }

        _controlIcon.Visible = false;
        _controlIcon.ContextMenuStrip = null;
        _controlIcon.Icon = null;
        _controlIcon.Dispose();
        _controlIconImage.Dispose();

        _contextMenu.Dispose();
        _statusForm.Dispose();
        _copilotWindow.Dispose();
        _refreshTimer.Dispose();
        _fadeInTimer.Dispose();
        _hoverLeaveTimer.Dispose();
        _shutdown.Dispose();
        _refreshLock.Dispose();
        base.ExitThreadCore();
    }
}
