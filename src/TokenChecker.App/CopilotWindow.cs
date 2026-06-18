using System.Runtime.InteropServices;
using TokenChecker.Core;

namespace TokenChecker.App;

// Dedicated, borderless popup for the GitHub Copilot AI Credits card. Separate
// from the Claude/Codex status window (the user asked for it as its own screen).
//
// Resting state shows the usage percentage only ("66% 使用済み"); hovering the
// MAIN value area (only) reveals the detailed values ("4,627 / 7,000 使用済み").
// The swap is hover-driven, NOT focus-driven (see IsInteracting), and is a pure
// text change on a FIXED-size control, so the bar ratio, reset line, and window
// size never move: no layout jitter. The diagnostics expander is a separate
// mechanism (it grows the window deliberately, like the status cards).
internal sealed class CopilotWindow : Form
{
    // No outer gutter: the card fills the window edge-to-edge. The rounded corners
    // are applied by the DWM compositor (see OnHandleCreated), so there is no GDI
    // Region and the card itself paints square.
    private const int FormPadding = 0;
    public const int CardWidth = 300;
    // Taller than 186 to fit the two-line header (icon + "GitHub Copilot" / plan
    // name) on top of the sub-info lines, plus the promoted "today's delta" line
    // (now a second hero, between the reset line and the projection). This is a
    // one-time base-size change, NOT hover jitter — the sub-lines and header are
    // static when the main line swaps.
    public const int CardBaseHeight = 214;
    public const int CardDetailExtra = 88;
    private const int FormWidth = CardWidth + FormPadding * 2;

    // Amber band width below the prorated daily budget, in percentage points: a
    // today's-burn within this margin under budget warns (amber) before going red.
    // This ONE-DAY pace concept is a DIFFERENT thing from the monthly 80% / 95%
    // usage escalation (UsageTheme.WarningPercent / CriticalPercent) — don't conflate.
    private const double DailyAmberMarginPercent = 1d;

    // Maps today's delta (% of the monthly allowance) to a severity band for the
    // spark icon, comparing it against the PRORATED daily budget — the remaining
    // allowance spread over the remaining weekdays until reset (DailyBudgetPercent).
    // Over budget -> red; within 1 point below budget -> amber; otherwise green. A
    // null / non-finite / non-positive delta, or a null budget (no allowance or
    // unknown reset), -> green. internal so the bands can be unit-tested.
    internal static DeltaSeverity GetTodayDeltaSeverity(double? percent, double? dailyBudgetPercent)
    {
        if (percent is not double p || double.IsNaN(p) || double.IsInfinity(p) || p <= 0d)
        {
            return DeltaSeverity.Normal;
        }

        if (dailyBudgetPercent is not double budget || double.IsNaN(budget) || double.IsInfinity(budget))
        {
            return DeltaSeverity.Normal;
        }

        if (p > budget)
        {
            return DeltaSeverity.Red;
        }

        return p >= budget - DailyAmberMarginPercent ? DeltaSeverity.Alert : DeltaSeverity.Normal;
    }

    // Weekdays (Mon-Fri) remaining in the current allowance period: from todayLocal
    // (inclusive) up to, but excluding, resetLocalDate. Clamped to >= 1 so the last
    // day's budget is all that remains and there is never a divide-by-zero. Public
    // holidays are not modeled (no calendar available) — Mon-Fri only. Pure (operates
    // on wall-clock dates), so unit-testable independent of the machine timezone.
    internal static int RemainingWeekdays(DateTime todayLocal, DateTime resetLocalDate)
    {
        var count = 0;
        for (var day = todayLocal.Date; day < resetLocalDate.Date; day = day.AddDays(1))
        {
            if (day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            {
                count++;
            }
        }

        return Math.Max(1, count);
    }

    // The prorated per-weekday budget as a percent of the monthly allowance: the
    // remaining allowance (100 - usedPercent, never negative) spread over the
    // remaining weekdays. null when there is no usage percentage to prorate (no
    // allowance). The divisor is clamped to >= 1.
    internal static double? DailyBudgetPercent(double? usedPercent, int remainingWeekdays)
    {
        if (usedPercent is not double used || double.IsNaN(used) || double.IsInfinity(used))
        {
            return null;
        }

        var remaining = Math.Max(0d, 100d - used);
        return remaining / Math.Max(1, remainingWeekdays);
    }

    // The spark-icon color for a severity band (full-strength green / amber / red).
    internal static Color SeverityIconColor(DeltaSeverity severity)
        => severity switch
        {
            DeltaSeverity.Red => UsageTheme.Bad,
            DeltaSeverity.Alert => UsageTheme.Warning,
            _ => UsageTheme.Good
        };

    private readonly CopilotCard _card;
    // Window-level mirror of the accent + pinned state, used to drive the DWM border
    // (the pinned outline). The card keeps its own copy for the bar/divider.
    private Color _accent = UsageTheme.Good;
    private bool _pinned;
    // Polls the cursor while the window is visible so the hover detail-swap works
    // even when the window appears under a stationary cursor or stays inactive
    // (HoverPreview), where MouseEnter/MouseLeave do not fire reliably. This is the
    // PRIMARY driver; MouseMove/Enter/Leave just make it snappier.
    private readonly System.Windows.Forms.Timer _hoverPoll = new() { Interval = 90 };
    private bool _hovered;

    public CopilotWindow()
    {
        Text = "GitHub Copilot";
        FormBorderStyle = FormBorderStyle.None;
        ControlBox = false;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        TopMost = true;
        BackColor = UsageTheme.Surface;
        ForeColor = UsageTheme.PrimaryText;
        Font = new Font("Segoe UI", 9F);
        StartPosition = FormStartPosition.Manual;

        _card = new CopilotCard
        {
            Location = new Point(FormPadding, FormPadding),
            Size = new Size(CardWidth, CardBaseHeight)
        };
        _card.HeightChanged += (_, _) => RelayoutForCardHeight();
        Controls.Add(_card);

        RelayoutForCardHeight();
        SetLoading();

        MouseDown += OnDragMouseDown;
        MouseEnter += OnPointerChanged;
        MouseLeave += OnPointerChanged;
        MouseMove += OnPointerChanged;
        AttachHandlers(this);
        _hoverPoll.Tick += (_, _) => RefreshHover();
        // When the window loses activation, re-evaluate hover from the CURRENT cursor
        // position so the detail-swap never sticks on after the cursor has left.
        Deactivate += (_, _) =>
        {
            if (IsDisposed || !IsHandleCreated)
            {
                return;
            }

            _hovered = IsCursorOverMainUsageArea();
            RaiseInteractionChanged();
        };
    }

    // Raised when the user dismisses the popup with Esc.
    public event EventHandler? HideRequested;

    // Raised whenever the hover (interaction) state changes.
    public event EventHandler? InteractionChanged;

    // The detail-swap is driven purely by the mouse hovering the main value area —
    // NOT by keyboard focus. (Focus used to reveal detail too, but the window can be
    // activated/focused on first launch, which then wrongly opened in detail mode.)
    public bool IsInteracting => _hovered;

    // Glance-friendly: showing the window must not steal focus from whatever the
    // user is doing, and must leave it in the compact resting state.
    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            const int CS_DROPSHADOW = 0x00020000;
            var cp = base.CreateParams;
            cp.ClassStyle |= CS_DROPSHADOW;
            return cp;
        }
    }

    public void Update(string planTitle, ServiceUsage? current, ServiceUsage? fallback, int? allowance, CopilotInsights? insights)
        => _card.Update(planTitle, current, fallback, allowance, insights);

    public void SetLoading() => _card.SetLoading();

    // Applies the configured accent color to the card (bar + left divider pill) and,
    // when pinned, the window's DWM border. The numbers stay a fixed near-black; the
    // bar still escalates to amber/red at 80/95.
    public void ApplyAccent(Color accent)
    {
        _accent = accent;
        _card.SetAccent(accent);
        UpdatePinnedBorder();
    }

    // Shows/hides the pinned outline. The outline is the window's DWM border (in the
    // accent color), not a GDI line: a self-painted line on the outermost pixels is
    // hidden by the DWM-rounded edge, whereas the DWM border tracks the rounded edge
    // and corners exactly.
    public void SetPinned(bool pinned)
    {
        _pinned = pinned;
        UpdatePinnedBorder();
    }

    private void UpdatePinnedBorder()
    {
        if (IsHandleCreated)
        {
            WindowEffects.SetBorderColor(Handle, _pinned ? _accent : (Color?)null);
        }
    }

    // Drop any inner focus so an explicitly-activated window (the click trigger)
    // still opens in the compact resting state. Esc/keyboard still work at the
    // form level even with no active control.
    public void ClearFocus() => ActiveControl = null;

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    private const int WM_NCLBUTTONDOWN = 0x00A1;
    private const int HTCAPTION = 2;

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape)
        {
            HideRequested?.Invoke(this, EventArgs.Empty);
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        RefreshHover();
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (Disposing || IsDisposed)
        {
            // During teardown the form's handle destruction flips Visible; don't
            // touch the (already-disposed) timer or card here — Dispose handles it.
            return;
        }

        if (Visible)
        {
            // Drive hover detection by polling while shown (see _hoverPoll): in
            // HoverPreview the window is shown inactive and often appears under the
            // cursor, so MouseEnter never fires and the event-only path misses it.
            _hoverPoll.Start();
            // Re-check once layout has settled, in case the window appeared with the
            // cursor already over the main area (no MouseEnter fires in that case).
            // This runs on EVERY show — unlike OnShown, which only fires the first
            // time, whereas a HoverPreview window is shown/hidden repeatedly.
            if (IsHandleCreated)
            {
                BeginInvoke((Action)RefreshHover);
            }
        }
        else
        {
            _hoverPoll.Stop();
            // The window is hidden: nothing can be hovered, so reset to the compact
            // resting state for a consistent next open.
            _hovered = false;
            _card.SetDetailed(false);
        }
    }

    private void RelayoutForCardHeight()
    {
        SuspendLayout();
        try
        {
            _card.Location = new Point(FormPadding, FormPadding);
            _card.Width = CardWidth;
            ClientSize = new Size(FormWidth, _card.Height + FormPadding * 2);
        }
        finally
        {
            ResumeLayout(true);
        }
    }

    // The card fills the window and paints square; the rounded corners come from the
    // Win11 DWM compositor (anti-aliased, DPI-correct) rather than a hard GDI Region
    // that stair-steps a small radius into a square-looking corner.
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        WindowEffects.UseRoundedCorners(Handle, small: true);
        // Re-apply the pinned border now the handle exists (it may have been set
        // while the window was hidden, before the handle was created).
        UpdatePinnedBorder();
    }

    private void BeginWindowDrag()
    {
        ReleaseCapture();
        SendMessage(Handle, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero);
    }

    private void OnDragMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            BeginWindowDrag();
        }
    }

    // Attach hover tracking to every descendant (so events keep firing as the
    // cursor moves), and the drag handler to everything except the genuinely
    // interactive controls (the detail link and box).
    private void AttachHandlers(Control root)
    {
        foreach (Control child in root.Controls)
        {
            child.MouseEnter += OnPointerChanged;
            child.MouseLeave += OnPointerChanged;
            child.MouseMove += OnPointerChanged;
            if (child is not (LinkLabel or TextBoxBase))
            {
                child.MouseDown += OnDragMouseDown;
            }

            AttachHandlers(child);
        }
    }

    private void OnPointerChanged(object? sender, EventArgs e) => RefreshHover();

    // The hover detail-swap is scoped to the MAIN value area only: the detailed
    // values appear only while the cursor is over "n% 使用済み", not anywhere else on
    // the card. Driven primarily by the poll (so it is robust in HoverPreview where
    // MouseEnter is unreliable) plus MouseMove/Enter/Leave for immediacy.
    private void RefreshHover()
    {
        if (IsDisposed || !IsHandleCreated)
        {
            return;
        }

        var inside = IsCursorOverMainUsageArea();
        if (inside != _hovered)
        {
            _hovered = inside;
            RaiseInteractionChanged();
        }
    }

    // Single source of truth for the detail-swap hit-test: is the cursor over the
    // main value area? Uses the fixed-layout control rect (NOT the text width),
    // slightly inflated so the edges aren't fiddly. This is intentionally separate
    // from the whole-window Form.Bounds test the tray context uses for show/hide.
    private bool IsCursorOverMainUsageArea()
    {
        if (IsDisposed || !IsHandleCreated)
        {
            return false;
        }

        var rect = _card.MainUsageScreenRect;
        if (rect.IsEmpty)
        {
            return false;
        }

        rect.Inflate(6, 4);
        return rect.Contains(Cursor.Position);
    }

    private void RaiseInteractionChanged()
    {
        _card.SetDetailed(IsInteracting);
        InteractionChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _hoverPoll.Stop();
            _hoverPoll.Dispose();
        }

        base.Dispose(disposing);
    }

    // ----- CopilotCard -----------------------------------------------------
    private sealed class CopilotCard : Panel
    {
        public event EventHandler? HeightChanged;

        // Left edge of the content column. Wider than the original 14 so there is a
        // clear gap between the glass card's left accent pill and the content.
        private const int ContentLeft = 18;
        private const int ContentRightPad = 14;
        private const int ContentWidth = CardWidth - ContentLeft - ContentRightPad;
        private const int IconSize = 16;
        private const int TitleLeft = ContentLeft + IconSize + 8;

        private readonly Label _title;
        private readonly Label _planSub;
        private readonly Label _badge;
        private readonly Label _statusMessage;
        // Small header row directly above the 67% hero: a fixed "クレジット" caption on
        // the left and the (right-aligned) monthly reset estimate on the right.
        private readonly Label _creditLabel;
        private readonly Label _resetTop;
        private readonly MainUsageControl _mainLine;
        private readonly UsageBarControl _bar;
        // Hint line used ONLY for non-Available / no-plan states (e.g. "set a token",
        // "pick a plan"); the normal reset estimate now lives in _resetTop.
        private readonly Label _resetSub;
        private readonly Label _predictionSub;
        // Promoted to a second hero line (icon + bold ~14pt, color-escalated). No
        // longer a quiet muted Label — see TodayDeltaControl.
        private readonly TodayDeltaControl _todayLine;
        private readonly LinkLabel _detailToggle;
        private readonly TextBox _detailBox;

        private bool _detailExpanded;
        private bool _detailed;
        private bool _hasPercent;
        // The main line is rendered as a big value + a smaller suffix.
        private string _compactValue = "—";
        private string _detailValue = "—";
        private string _suffix = string.Empty;
        private Color _valueColor = UsageTheme.MutedText;
        // Configurable accent for the BAR only (numbers stay near-black). Severity
        // still overrides it at 80/95 (amber/red) via UsageTheme.
        private Color _accent = UsageTheme.Good;
        private double _lastPercent;

        public CopilotCard()
        {
            BackColor = UsageTheme.Card;
            // Glass background is painted in OnPaint; double buffer to avoid
            // flicker. Selectable + a Tab stop so the card can take keyboard focus
            // (for Esc/keyboard handling); the detail swap itself is driven only by
            // hovering the main value area, NOT by focus (see IsInteracting).
            SetStyle(
                ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.UserPaint
                | ControlStyles.ResizeRedraw
                | ControlStyles.Selectable,
                true);
            TabStop = true;

            // Header line 1: a fixed "GitHub Copilot" (the Octicon icon is painted to
            // its left in OnPaint). Fixed width with ellipsis so it never collides
            // with the badge.
            _title = new Label
            {
                Text = "GitHub Copilot",
                AutoSize = false,
                Size = new Size(174, 20),
                Font = UsageTheme.CreateCopilotFont(11.5F, FontStyle.Bold),
                ForeColor = UsageTheme.PrimaryText,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
                Location = new Point(TitleLeft, 12)
            };

            // Header line 2: the selected plan (e.g. "Copilot Pro+"), smaller/lighter.
            _planSub = new Label
            {
                Text = string.Empty,
                AutoSize = false,
                Size = new Size(ContentWidth, 16),
                Font = UsageTheme.CreateCopilotFont(9F),
                ForeColor = UsageTheme.MutedText,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
                Location = new Point(ContentLeft, 34)
            };

            // Quiet status chip, sized to its text by ApplyBadge so a long status
            // never collides with the title; AutoEllipsis is a final safety net.
            _badge = new Label
            {
                AutoSize = false,
                Size = new Size(90, 18),
                Location = new Point(CardWidth - 14 - 90, 12),
                Font = UsageTheme.CreateCopilotFont(8.5F),
                BackColor = Color.Transparent,
                ForeColor = UsageTheme.MutedText,
                TextAlign = ContentAlignment.MiddleRight,
                AutoEllipsis = true,
                Text = Strings.T("状態不明")
            };

            _statusMessage = new Label
            {
                AutoSize = false,
                Size = new Size(ContentWidth, 16),
                Location = new Point(ContentLeft, 52),
                ForeColor = UsageTheme.MutedText,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
                Font = UsageTheme.CreateCopilotFont(8.5F),
                Visible = false
            };

            // Header row above the 67% hero: "クレジット" caption (left). Kept to the
            // left ~100px so it never sits under the right-aligned reset label.
            _creditLabel = new Label
            {
                Text = Strings.T("クレジット"),
                AutoSize = false,
                Size = new Size(100, 16),
                Location = new Point(ContentLeft, 54),
                ForeColor = UsageTheme.MutedText,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
                Font = UsageTheme.CreateCopilotFont(8.5F),
                Visible = false
            };

            // Same row, right-aligned: a short monthly reset estimate. Occupies only
            // the right portion so it cannot overlap the caption on its left.
            _resetTop = new Label
            {
                AutoSize = false,
                Size = new Size(ContentWidth - 106, 16),
                Location = new Point(ContentLeft + 106, 54),
                ForeColor = UsageTheme.MutedText,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleRight,
                AutoEllipsis = true,
                Font = UsageTheme.CreateCopilotFont(8.5F),
                Visible = false
            };

            // The hover swap target: a fixed-size custom control that draws a
            // big value plus a smaller suffix on a shared baseline. Only the value
            // text swaps on hover; the box never changes, so layout cannot jitter.
            _mainLine = new MainUsageControl(15F)
            {
                Location = new Point(ContentLeft, 72),
                Size = new Size(ContentWidth, 30)
            };

            _bar = new UsageBarControl
            {
                Location = new Point(ContentLeft, 108),
                Size = new Size(ContentWidth, 8),
                BackColor = Color.Transparent,
                UseGradient = true
            };

            // Hint line (non-Available / no-plan only). Sits under the status message
            // when the usage block is hidden, so it never collides with the number.
            _resetSub = new Label
            {
                AutoSize = false,
                Size = new Size(ContentWidth, 16),
                Location = new Point(ContentLeft, 72),
                ForeColor = UsageTheme.MutedText,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
                Font = UsageTheme.CreateCopilotFont(8.5F),
                Visible = false,
                Text = "—"
            };

            // Second hero: the since-today's-09:00 delta. The "本日" label matches the
            // "使用済み" suffix (small, muted) and the value matches the 67% number
            // (15pt bold, primary text); only the spark icon carries the daily-pace
            // severity color. Sits right under the bar.
            _todayLine = new TodayDeltaControl(15F)
            {
                Location = new Point(ContentLeft, 120),
                Size = new Size(ContentWidth, 30),
                Visible = false
            };

            // Sub-info: a 100%-reach projection (smaller, muted). Static — it does
            // not change on hover. Below the promoted today line.
            _predictionSub = new Label
            {
                AutoSize = false,
                Size = new Size(ContentWidth, 16),
                Location = new Point(ContentLeft, 152),
                ForeColor = UsageTheme.MutedText,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
                Font = UsageTheme.CreateCopilotFont(8.5F),
                Visible = false
            };

            _detailToggle = new LinkLabel
            {
                Text = Strings.T("詳細を表示"),
                AutoSize = true,
                BackColor = Color.Transparent,
                LinkColor = UsageTheme.DetailToggle,
                ActiveLinkColor = UsageTheme.PrimaryText,
                VisitedLinkColor = UsageTheme.DetailToggle,
                LinkBehavior = LinkBehavior.HoverUnderline,
                Location = new Point(ContentLeft, 176),
                Font = UsageTheme.CreateCopilotFont(8.5F),
                Visible = false
            };
            _detailToggle.LinkClicked += (_, _) => ToggleDetail();

            _detailBox = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = UsageTheme.DetailBackground,
                ForeColor = UsageTheme.SecondaryText,
                Location = new Point(ContentLeft, 194),
                Size = new Size(ContentWidth, CardDetailExtra - 16),
                Visible = false,
                TabStop = false,
                WordWrap = true,
                Font = new Font("Consolas", 8.5F)
            };

            Controls.Add(_title);
            Controls.Add(_planSub);
            Controls.Add(_badge);
            Controls.Add(_statusMessage);
            Controls.Add(_creditLabel);
            Controls.Add(_resetTop);
            Controls.Add(_mainLine);
            Controls.Add(_bar);
            Controls.Add(_resetSub);
            Controls.Add(_todayLine);
            Controls.Add(_predictionSub);
            Controls.Add(_detailToggle);
            Controls.Add(_detailBox);
        }

        public bool HasPercent => _hasPercent;

        // Screen-space rectangle of the main value area (for the detail-swap hit-test).
        public Rectangle MainUsageScreenRect => _mainLine.RectangleToScreen(_mainLine.ClientRectangle);

        public void SetDetailed(bool detailed)
        {
            if (detailed == _detailed)
            {
                return;
            }

            _detailed = detailed;
            ApplyMainLineText();
        }

        public void SetLoading()
        {
            _badge.ForeColor = BadgeColor(ProviderStatus.RateLimited);
            ApplyBadge(Strings.T("更新中"));
            _statusMessage.Visible = false;
            _statusMessage.Text = string.Empty;
            _hasPercent = false;
            _compactValue = "—";
            _detailValue = "—";
            _suffix = string.Empty;
            _valueColor = UsageTheme.MutedText;
            _bar.SetValue(null);
            _creditLabel.Visible = false;
            _resetTop.Visible = false;
            _resetSub.Visible = false;
            _predictionSub.Visible = false;
            _todayLine.Visible = false;
            ApplyMainLineText();
            UpdateDiagnostics(string.Empty);
        }

        public void Update(string planTitle, ServiceUsage? current, ServiceUsage? fallback, int? allowance, CopilotInsights? insights)
        {
            _planSub.Text = planTitle;

            var status = current?.Status ?? ProviderStatus.Unknown;
            _badge.ForeColor = BadgeColor(status);
            ApplyBadge(ProviderStatusPresenter.BadgeText(status));

            var window = FindMonthlyWindow(fallback);
            var used = window?.Used;
            var available = status == ProviderStatus.Available;

            // The status message takes over the credit-row space when not Available.
            if (available)
            {
                _statusMessage.Visible = false;
                _statusMessage.Text = string.Empty;
            }
            else
            {
                _statusMessage.Text = CopilotStatusMessage(status, current, fallback);
                _statusMessage.ForeColor = UsageTheme.StatusColor(status);
                _statusMessage.Visible = true;
            }

            var hasPlan = false;
            double? dailyBudgetPercent = null;
            if (allowance is int cap && cap > 0 && used is long u)
            {
                hasPlan = true;
                var percent = Math.Min(100d, u / (double)cap * 100d);
                _hasPercent = true;
                _lastPercent = percent;
                _compactValue = $"{Math.Round(percent):0}%";
                _detailValue = $"{u:N0} / {cap:N0}";
                _suffix = Strings.T("使用済み");
                // The number is a fixed near-black; only the BAR carries the accent
                // (and still escalates to amber/red at 80/95).
                _valueColor = UsageTheme.PrimaryText;
                _bar.AccentColor = UsageTheme.AccentColor(percent, _accent);
                _bar.SetValue(percent);
                // Prorate the remaining allowance over the remaining weekdays so the
                // today's-burn icon escalates against a daily budget (not a fixed %).
                dailyBudgetPercent = window?.ResetAtUtc is { } resetUtc
                    ? DailyBudgetPercent(percent, RemainingWeekdays(DateTime.Now, resetUtc.ToLocalTime().DateTime))
                    : null;
            }
            else
            {
                _hasPercent = false;
                // No allowance to compare against: show the raw used credits only.
                _compactValue = _detailValue = used is long u2 ? $"{u2:N0}" : "—";
                _suffix = used is null ? string.Empty : Strings.T("credits 使用");
                _valueColor = used is null ? UsageTheme.MutedText : UsageTheme.PrimaryText;
                _bar.SetValue(null);
            }

            // The credit-row reset estimate (right side) — monthly reset is an
            // estimate (the API gives none), so it stays a "目安".
            _resetTop.Text = FormatResetShort(window);

            ApplyMainLineText();
            ApplyInsights(insights, dailyBudgetPercent);

            // Visibility — the usage hero block (credit row, number, bar, today's
            // delta, projection) only makes sense once usage was actually fetched.
            // For every non-Available state we hide it so it can never overlap the
            // status message that takes its place.
            _creditLabel.Visible = available;
            _resetTop.Visible = available;
            _mainLine.Visible = available;
            _bar.Visible = available;
            if (!available)
            {
                _todayLine.Visible = false;
                _predictionSub.Visible = false;
            }

            // Action hints:
            //  - NotLoggedIn: a first-time-setup pointer right under the status line.
            //  - Available but no plan: reuse the (otherwise-empty) projection row to
            //    point the user at plan selection.
            if (!available)
            {
                // NOTE: gate the text on a LOCAL bool, never on _resetSub.Visible —
                // the Control.Visible getter returns false while the window is hidden
                // (updates run on a timer with the popup hidden), so reading it back
                // here would skip the assignment and leave the stale default text.
                var showHint = status == ProviderStatus.NotLoggedIn;
                _resetSub.Visible = showHint;
                if (showHint)
                {
                    _resetSub.Text = Strings.T("設定画面の「初回設定」から手順を確認してください");
                }
            }
            else
            {
                _resetSub.Visible = false;
                if (!hasPlan)
                {
                    _predictionSub.Visible = true;
                    _predictionSub.Text = Strings.T("プランを選ぶと上限・残量を表示");
                }
            }

            var debug = ProviderStatusPresenter.BuildDebugSummary("GitHub Copilot", current, fallback);
            var masked = ProviderStatusPresenter.SafeDiagnostics(current?.Message);
            var combined = string.IsNullOrEmpty(masked) ? debug : $"{debug}{Environment.NewLine}{masked}";
            UpdateDiagnostics(combined);
        }

        // Sub-info: a 100%-reach projection (only meaningful with an allowance) and
        // the since-today's-09:00 delta. Null insights (non-Available / loading)
        // hide both. These lines are static — they never change on hover, so they
        // do not affect the no-jitter main-line swap.
        private void ApplyInsights(CopilotInsights? insights, double? dailyBudgetPercent)
        {
            if (insights is null)
            {
                _predictionSub.Visible = false;
                _todayLine.Visible = false;
                return;
            }

            if (_hasPercent)
            {
                _predictionSub.Visible = true;
                _predictionSub.Text = insights.Prediction switch
                {
                    CopilotPrediction.ReachesThisMonth when insights.FullDateLocal is DateTimeOffset due
                        => Strings.Tf("このペースだと {0} 頃に 100%", due.ToString("M/d")),
                    CopilotPrediction.NotThisMonth => Strings.T("このペースなら今月は上限に到達しない見込み"),
                    CopilotPrediction.AlreadyFull => Strings.T("すでに上限に到達しています"),
                    _ => Strings.T("予測にはデータ不足")
                };
            }
            else
            {
                _predictionSub.Visible = false;
            }

            // The since-09:00 delta — a small "本日" label + a larger value, drawn in
            // the user-selected accent color (no daily-pace escalation). Three cases:
            // Label matches "使用済み" (muted) and value matches the 67% number
            // (primary) — both fixed inside TodayDeltaControl. Only the SPARK ICON
            // escalates green/amber/red with the daily pace.
            _todayLine.Visible = true;
            var label = Strings.T("本日");
            if (insights.TodayDeltaCredits is not long delta)
            {
                // Not measured yet (no 09:00 baseline this day): muted note, muted icon.
                _todayLine.SetContent(Strings.T("本日: 未計測"), string.Empty, UsageTheme.MutedText);
            }
            else if (insights.TodayDeltaPercent is not double percentToday)
            {
                // No allowance to express the burn as a % of: show credits only, and a
                // calm (Normal) icon — there is no pace to escalate on.
                _todayLine.SetContent(
                    label,
                    Strings.Tf("+{0} credits", delta.ToString("N0")),
                    SeverityIconColor(DeltaSeverity.Normal));
            }
            else
            {
                // The "クレジット" header already labels the unit, so the value stays
                // terse: "+96（+1.4%）". The icon escalates by today's pace.
                _todayLine.SetContent(
                    label,
                    Strings.Tf("+{0}（+{1}%）", delta.ToString("N0"), percentToday.ToString("0.0")),
                    SeverityIconColor(GetTodayDeltaSeverity(percentToday, dailyBudgetPercent)));
            }
        }

        // The detailed values only appear on hover, and only when there is a
        // percentage to reveal; otherwise compact == detailed so nothing changes.
        private void ApplyMainLineText()
        {
            var value = _detailed && _hasPercent ? _detailValue : _compactValue;
            _mainLine.SetContent(value, _suffix, _valueColor);
        }

        // The status chip is intentionally quiet: a lightened version of the
        // severity status color so it does not compete with the main number.
        private static Color BadgeColor(ProviderStatus status)
            => UsageTheme.Lighten(UsageTheme.StatusColor(status), 0.30f);

        // Right-anchor the badge sized to its text and hand the title the rest, so
        // a long status (e.g. "取得を一時制限中") never overflows its box or collides
        // with the title. The title's AutoEllipsis truncates if space gets tight.
        private void ApplyBadge(string text)
        {
            _badge.Text = text;
            // Measure WITH the Label's default padding (no NoPadding flag) so the box
            // matches what the Label actually renders. Size both axes and align in the
            // title row, flooring y at the row top so a tall (DPI-scaled) glyph is
            // never cut off vertically nor floats above the row.
            var measured = TextRenderer.MeasureText(text, _badge.Font);
            var width = Math.Clamp(measured.Width + 2, 40, CardWidth - ContentLeft - 70);
            var height = Math.Max(18, measured.Height + 2);
            var x = CardWidth - 14 - width;
            var y = Math.Max(12, 12 + (20 - height) / 2);
            _badge.Bounds = new Rectangle(x, y, width, height);
            _title.Width = Math.Max(40, x - TitleLeft - 8);
        }

        // Compact reset estimate for the credit-row right slot (the "クレジット" caption
        // already gives the monthly context, so this stays short). Still an estimate.
        private static string FormatResetShort(RateLimitWindow? window)
        {
            if (window?.ResetAtUtc is null)
            {
                return Strings.T("リセット目安 月初");
            }

            var reset = window.ResetAtUtc.Value.ToLocalTime();
            return Strings.Tf("リセット目安 {0}/{1}", reset.Month, reset.Day);
        }

        // Copilot-specific copy: the generic "再ログインしてください" hints assume a
        // CLI login, which does not fit the GITHUB_TOKEN (env PAT) model.
        private static string CopilotStatusMessage(ProviderStatus status, ServiceUsage? current, ServiceUsage? fallback)
        {
            var hasFallback = fallback is { Windows.Count: > 0 };
            switch (status)
            {
                case ProviderStatus.NotLoggedIn:
                    return Strings.T("GITHUB_TOKEN が未設定です");
                case ProviderStatus.Unauthorized:
                    return current?.Message?.Contains("(403)", StringComparison.Ordinal) == true
                        ? Strings.T("GITHUB_TOKEN の Plan(Read) 権限・個人課金・Enhanced Billing 対象を確認してください")
                        : Strings.T("GITHUB_TOKEN が無効または期限切れです");
                case ProviderStatus.RateLimited:
                    return hasFallback
                        ? Strings.T("取得が一時的に制限されています。前回成功値を表示しています")
                        : Strings.T("取得が一時的に制限されています");
                default:
                    return hasFallback
                        ? Strings.T("一時的に取得できません。前回成功値を表示しています")
                        : Strings.T("一時的に取得できません");
            }
        }

        private static RateLimitWindow? FindMonthlyWindow(ServiceUsage? service)
            => service?.Windows.FirstOrDefault(window => window.WindowDurationMins == 43200);

        private void UpdateDiagnostics(string diagnostics)
        {
            var available = !string.IsNullOrWhiteSpace(diagnostics);
            _detailToggle.Visible = available;

            if (!available)
            {
                if (_detailExpanded)
                {
                    SetExpanded(false);
                }

                _detailBox.Text = string.Empty;
                _detailToggle.Text = Strings.T("詳細を表示");
                return;
            }

            _detailBox.Text = diagnostics;
            _detailToggle.Text = _detailExpanded ? Strings.T("詳細を隠す") : Strings.T("詳細を表示");
        }

        private void ToggleDetail() => SetExpanded(!_detailExpanded);

        private void SetExpanded(bool expanded)
        {
            _detailExpanded = expanded;
            _detailBox.Visible = expanded;
            _detailToggle.Text = expanded ? Strings.T("詳細を隠す") : Strings.T("詳細を表示");
            var newHeight = expanded ? CardBaseHeight + CardDetailExtra : CardBaseHeight;
            if (Height != newHeight)
            {
                Height = newHeight;
                HeightChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        // Decorative glass tint is fixed (slate); the configurable accent rides the
        // bar, the left divider pill, and the pinned outline (see _accent / SetAccent),
        // not this neutral backdrop tint.
        private readonly Color _brand = UsageTheme.CopilotBrand;

        // Applies the configured accent to the BAR, the left divider pill, and the
        // pinned outline (numbers stay near-black). Recomputes the live bar color and
        // repaints the glass immediately so a settings change shows without waiting
        // for the next refresh; severity (80/95) still overrides the bar color.
        public void SetAccent(Color accent)
        {
            _accent = accent;
            if (_hasPercent)
            {
                _bar.AccentColor = UsageTheme.AccentColor(_lastPercent, _accent);
                _bar.Invalidate();
            }

            // The divider pill and (when shown) the pinned outline are painted from
            // _accent in OnPaint, so repaint the card to pick up the new color.
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            // The card fills the window and paints SQUARE — the rounded corners come
            // from the DWM compositor (see CopilotWindow.OnHandleCreated), which
            // anti-aliases them. The left accent pill (the vertical divider line)
            // follows the user-selected accent; the glass tint stays neutral slate.
            // The pinned outline is the window's DWM border (see CopilotWindow.
            // SetPinned), not painted here — a GDI line on the edge is hidden by the
            // rounded edge.
            UsageTheme.PaintGlassCard(e.Graphics, Width, Height, _brand, 0f, _accent);
            CopilotGlyph.Draw(e.Graphics, new RectangleF(ContentLeft, 13f, IconSize, IconSize), UsageTheme.CopilotBrand);
        }
    }
}
