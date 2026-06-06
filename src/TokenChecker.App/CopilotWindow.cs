using System.Drawing.Drawing2D;
using System.Globalization;
using System.Runtime.InteropServices;
using TokenChecker.Core;

namespace TokenChecker.App;

// Dedicated, borderless popup for the GitHub Copilot AI Credits card. Separate
// from the Claude/Codex status window (the user asked for it as its own screen).
//
// Resting state shows the usage percentage only ("66% 使用済み"); hovering the
// MAIN value area (only) — or giving the card keyboard focus — reveals the
// detailed values ("4,627 / 7,000 使用済み"). The swap is a pure text change on a
// FIXED-size control, so the bar ratio, reset line, and window size never move:
// no layout jitter. The diagnostics expander is a separate mechanism (it grows
// the window deliberately, like the status cards).
internal sealed class CopilotWindow : Form
{
    // Outer margin between the card and the window edge (the drop-shadow gutter).
    private const int FormPadding = 9;
    public const int CardWidth = 300;
    // Taller than 186 to fit the two-line header (icon + "GitHub Copilot" / plan
    // name) on top of the sub-info lines. This is a one-time base-size change, NOT
    // hover jitter — the sub-lines and header are static when the main line swaps.
    public const int CardBaseHeight = 204;
    public const int CardDetailExtra = 88;
    private const int FormWidth = CardWidth + FormPadding * 2;

    private readonly CopilotCard _card;
    // Polls the cursor while the window is visible so the hover detail-swap works
    // even when the window appears under a stationary cursor or stays inactive
    // (HoverPreview), where MouseEnter/MouseLeave do not fire reliably.
    private readonly System.Windows.Forms.Timer _hoverPoll = new() { Interval = 120 };
    private bool _hovered;
    private bool _focused;

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
        // The card panel is the single Tab stop; focusing it (e.g. by tabbing
        // after the window is activated) reveals the detailed values too.
        _card.GotFocus += (_, _) => SetFocused(true);
        _card.LostFocus += (_, _) => SetFocused(false);
        Controls.Add(_card);

        RelayoutForCardHeight();
        SetLoading();

        MouseDown += OnDragMouseDown;
        MouseEnter += OnPointerChanged;
        MouseLeave += OnPointerChanged;
        AttachHandlers(this);
        _hoverPoll.Tick += (_, _) => RefreshHover();
        // When the window loses activation, drop the keyboard-focus detail state and
        // re-evaluate hover immediately so the detail-swap can never stick on (the
        // 120ms poll also covers this, just less promptly).
        Deactivate += (_, _) =>
        {
            if (IsDisposed || !IsHandleCreated)
            {
                return;
            }

            _focused = false;
            _hovered = _card.MainLineScreenBounds.Contains(Cursor.Position);
            RaiseInteractionChanged();
        };
    }

    // Raised when the user dismisses the popup with Esc.
    public event EventHandler? HideRequested;

    // Raised whenever the hover/focus (interaction) state changes.
    public event EventHandler? InteractionChanged;

    public bool IsInteracting => _hovered || _focused;

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

    // Applies the configured accent color to the card's bar (and the tray bar is
    // updated separately). The numbers stay a fixed near-black; only the bar follows
    // the accent, still escalating to amber/red at 80/95.
    public void ApplyAccent(Color accent) => _card.SetAccent(accent);

    // Shows/hides the faint 1px "pinned" outline (the window will stay open).
    public void SetPinned(bool pinned) => _card.SetPinned(pinned);

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
        }
        else
        {
            _hoverPoll.Stop();
            // Reset to the compact resting state so the next open is consistent.
            _hovered = false;
            _focused = false;
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
            if (child is not (LinkLabel or TextBoxBase))
            {
                child.MouseDown += OnDragMouseDown;
            }

            AttachHandlers(child);
        }
    }

    private void OnPointerChanged(object? sender, EventArgs e) => RefreshHover();

    // The hover detail-swap is scoped to the MAIN value area only: the detailed
    // values appear only while the cursor is over "n% 使用済み", not anywhere else
    // on the card. The cursor is tested against the main control's screen rect
    // (driven by the poll + enter/leave), so it is robust in HoverPreview too and
    // stays independent of the tray context's window show/hide leave-poll.
    private void RefreshHover()
    {
        if (IsDisposed || !IsHandleCreated)
        {
            return;
        }

        var inside = _card.MainLineScreenBounds.Contains(Cursor.Position);
        if (inside != _hovered)
        {
            _hovered = inside;
            RaiseInteractionChanged();
        }
    }

    private void SetFocused(bool value)
    {
        if (value == _focused)
        {
            return;
        }

        _focused = value;
        RaiseInteractionChanged();
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
        private readonly MainUsageControl _mainLine;
        private readonly UsageBarControl _bar;
        private readonly Label _resetSub;
        private readonly Label _predictionSub;
        private readonly Label _todaySub;
        private readonly LinkLabel _detailToggle;
        private readonly TextBox _detailBox;

        private bool _detailExpanded;
        private bool _detailed;
        private bool _hasPercent;
        private bool _pinned;
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
            // flicker. Selectable + a Tab stop so keyboard focus can reveal detail.
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
                Text = "状態不明"
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

            // The hover/focus swap target: a fixed-size custom control that draws a
            // big value plus a smaller suffix on a shared baseline. Only the value
            // text swaps on hover; the box never changes, so layout cannot jitter.
            _mainLine = new MainUsageControl(15F)
            {
                Location = new Point(ContentLeft, 68),
                Size = new Size(ContentWidth, 30)
            };

            _bar = new UsageBarControl
            {
                Location = new Point(ContentLeft, 106),
                Size = new Size(ContentWidth, 8),
                BackColor = Color.Transparent,
                UseGradient = true
            };

            _resetSub = new Label
            {
                AutoSize = false,
                Size = new Size(ContentWidth, 16),
                Location = new Point(ContentLeft, 118),
                ForeColor = UsageTheme.MutedText,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
                Font = UsageTheme.CreateCopilotFont(8.5F),
                Text = "—"
            };

            // Sub-info lines (smaller, muted): a 100%-reach projection and the
            // since-today's-09:00 delta. Static — they do not change on hover.
            _predictionSub = new Label
            {
                AutoSize = false,
                Size = new Size(ContentWidth, 16),
                Location = new Point(ContentLeft, 136),
                ForeColor = UsageTheme.MutedText,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
                Font = UsageTheme.CreateCopilotFont(8.5F),
                Visible = false
            };

            _todaySub = new Label
            {
                AutoSize = false,
                Size = new Size(ContentWidth, 16),
                Location = new Point(ContentLeft, 154),
                ForeColor = UsageTheme.MutedText,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
                Font = UsageTheme.CreateCopilotFont(8.5F),
                Visible = false
            };

            _detailToggle = new LinkLabel
            {
                Text = "詳細を表示",
                AutoSize = true,
                BackColor = Color.Transparent,
                LinkColor = UsageTheme.DetailToggle,
                ActiveLinkColor = UsageTheme.PrimaryText,
                VisitedLinkColor = UsageTheme.DetailToggle,
                LinkBehavior = LinkBehavior.HoverUnderline,
                Location = new Point(ContentLeft, 178),
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
                Location = new Point(ContentLeft, 196),
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
            Controls.Add(_mainLine);
            Controls.Add(_bar);
            Controls.Add(_resetSub);
            Controls.Add(_predictionSub);
            Controls.Add(_todaySub);
            Controls.Add(_detailToggle);
            Controls.Add(_detailBox);
        }

        public bool HasPercent => _hasPercent;

        // Screen-space rectangle of the main value area (for the hover detail-swap).
        public Rectangle MainLineScreenBounds => _mainLine.RectangleToScreen(_mainLine.ClientRectangle);

        public void SetDetailed(bool detailed)
        {
            if (detailed == _detailed)
            {
                return;
            }

            _detailed = detailed;
            ApplyMainLineText();
        }

        public void SetPinned(bool pinned)
        {
            if (pinned == _pinned)
            {
                return;
            }

            _pinned = pinned;
            Invalidate();
        }

        public void SetLoading()
        {
            _badge.ForeColor = BadgeColor(ProviderStatus.RateLimited);
            ApplyBadge("更新中");
            _statusMessage.Visible = false;
            _statusMessage.Text = string.Empty;
            _hasPercent = false;
            _compactValue = "—";
            _detailValue = "—";
            _suffix = string.Empty;
            _valueColor = UsageTheme.MutedText;
            _bar.SetValue(null);
            _resetSub.Text = "更新中";
            _predictionSub.Visible = false;
            _todaySub.Visible = false;
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

            if (status == ProviderStatus.Available)
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

            if (allowance is int cap && cap > 0 && used is long u)
            {
                var percent = Math.Min(100d, u / (double)cap * 100d);
                var remaining = Math.Max(0L, cap - u);
                _hasPercent = true;
                _lastPercent = percent;
                _compactValue = $"{Math.Round(percent):0}%";
                _detailValue = $"{u:N0} / {cap:N0}";
                _suffix = "使用済み";
                // The number is a fixed near-black; only the BAR carries the accent
                // (and still escalates to amber/red at 80/95).
                _valueColor = UsageTheme.PrimaryText;
                _bar.AccentColor = UsageTheme.AccentColor(percent, _accent);
                _bar.SetValue(percent);
                _resetSub.Text = $"残 {remaining:N0} · {FormatReset(window)}";
            }
            else
            {
                _hasPercent = false;
                // No allowance to compare against: show the raw used credits only.
                _compactValue = _detailValue = used is long u2 ? $"{u2:N0}" : "—";
                _suffix = used is null ? string.Empty : "credits 使用";
                _valueColor = used is null ? UsageTheme.MutedText : UsageTheme.PrimaryText;
                _bar.SetValue(null);
                _resetSub.Text = "当月集計 · 設定でプランを選ぶと上限・残量を表示";
            }

            // When the token is unset, point the user at the first-time setup (no
            // token input here — only env GITHUB_TOKEN is read).
            if (status == ProviderStatus.NotLoggedIn)
            {
                _resetSub.Text = "設定画面の「初回設定」から手順を確認してください";
            }

            ApplyMainLineText();
            ApplyInsights(insights);

            var debug = ProviderStatusPresenter.BuildDebugSummary("GitHub Copilot", current, fallback);
            var masked = ProviderStatusPresenter.SafeDiagnostics(current?.Message);
            var combined = string.IsNullOrEmpty(masked) ? debug : $"{debug}{Environment.NewLine}{masked}";
            UpdateDiagnostics(combined);
        }

        // Sub-info: a 100%-reach projection (only meaningful with an allowance) and
        // the since-today's-09:00 delta. Null insights (non-Available / loading)
        // hide both. These lines are static — they never change on hover, so they
        // do not affect the no-jitter main-line swap.
        private void ApplyInsights(CopilotInsights? insights)
        {
            if (insights is null)
            {
                _predictionSub.Visible = false;
                _todaySub.Visible = false;
                return;
            }

            if (_hasPercent)
            {
                _predictionSub.Visible = true;
                _predictionSub.Text = insights.Prediction switch
                {
                    CopilotPrediction.ReachesThisMonth when insights.FullDateLocal is DateTimeOffset due
                        => $"このペースだと {due:M/d} 頃に 100%",
                    CopilotPrediction.NotThisMonth => "このペースなら今月は上限に到達しない見込み",
                    CopilotPrediction.AlreadyFull => "すでに上限に到達しています",
                    _ => "予測にはデータ不足"
                };
            }
            else
            {
                _predictionSub.Visible = false;
            }

            _todaySub.Visible = true;
            _todaySub.Text = insights.TodayDeltaCredits is long delta
                ? insights.TodayDeltaPercent is double percent
                    ? $"本日9:00以降 +{delta:N0} credits（+{percent:0.0}%）"
                    : $"本日9:00以降 +{delta:N0} credits"
                : "本日9:00以降: 未計測";
        }

        // The detailed values only appear on hover/focus, and only when there is a
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

        // Calendar-month reset is an estimate (the API gives no reset), so it is
        // shown as a "目安/推定", never a definitive countdown.
        private static string FormatReset(RateLimitWindow? window)
        {
            if (window?.ResetAtUtc is null)
            {
                return "当月集計（暦月近似）";
            }

            var reset = window.ResetAtUtc.Value.ToLocalTime();
            return $"当月集計 · リセット目安 {reset.Month}/{reset.Day}（推定）";
        }

        // Copilot-specific copy: the generic "再ログインしてください" hints assume a
        // CLI login, which does not fit the GITHUB_TOKEN (env PAT) model.
        private static string CopilotStatusMessage(ProviderStatus status, ServiceUsage? current, ServiceUsage? fallback)
        {
            var hasFallback = fallback is { Windows.Count: > 0 };
            switch (status)
            {
                case ProviderStatus.NotLoggedIn:
                    return "GITHUB_TOKEN が未設定です";
                case ProviderStatus.Unauthorized:
                    return current?.Message?.Contains("(403)", StringComparison.Ordinal) == true
                        ? "GITHUB_TOKEN の Plan(Read) 権限・個人課金・Enhanced Billing 対象を確認してください"
                        : "GITHUB_TOKEN が無効または期限切れです";
                case ProviderStatus.RateLimited:
                    return hasFallback
                        ? "取得が一時的に制限されています。前回成功値を表示しています"
                        : "取得が一時的に制限されています";
                default:
                    return hasFallback
                        ? "一時的に取得できません。前回成功値を表示しています"
                        : "一時的に取得できません";
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
                _detailToggle.Text = "詳細を表示";
                return;
            }

            _detailBox.Text = diagnostics;
            _detailToggle.Text = _detailExpanded ? "詳細を隠す" : "詳細を表示";
        }

        private void ToggleDetail() => SetExpanded(!_detailExpanded);

        private void SetExpanded(bool expanded)
        {
            _detailExpanded = expanded;
            _detailBox.Visible = expanded;
            _detailToggle.Text = expanded ? "詳細を隠す" : "詳細を表示";
            var newHeight = expanded ? CardBaseHeight + CardDetailExtra : CardBaseHeight;
            if (Height != newHeight)
            {
                Height = newHeight;
                HeightChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        // Decorative glass tint is fixed (slate); the configurable accent rides the
        // bar instead (see _accent / SetAccent), not this backdrop.
        private readonly Color _brand = UsageTheme.CopilotBrand;

        // Applies the configured accent to the BAR (numbers stay near-black).
        // Recomputes the live bar color immediately so a settings change shows
        // without waiting for the next refresh; severity (80/95) still overrides it.
        public void SetAccent(Color accent)
        {
            _accent = accent;
            if (_hasPercent)
            {
                _bar.AccentColor = UsageTheme.AccentColor(_lastPercent, _accent);
                _bar.Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            UsageTheme.PaintGlassCard(e.Graphics, Width, Height, _brand);
            CopilotGlyph.Draw(e.Graphics, new RectangleF(ContentLeft, 13f, IconSize, IconSize), UsageTheme.CopilotBrand);

            if (_pinned)
            {
                // A quiet, slightly more-defined edge (semi-transparent slate) on the
                // same rounded rect as the card border — no extra inset so it doesn't
                // read as a double border, and it stays clear of the drop shadow.
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using var path = UsageTheme.CreateRoundedRectPath(new RectangleF(0, 0, Width - 1, Height - 1), 13f);
                using var pen = new Pen(UsageTheme.PinnedBorder, 1f);
                e.Graphics.DrawPath(pen, path);
            }
        }
    }

    // Renders a large value plus a smaller, fixed-grey suffix on a shared baseline,
    // inside a fixed box — so the hover value-swap repaints text only and never
    // changes layout (no jitter). e.g. "66%" (15pt bold, near-black) + "使用済み"
    // (~7.9pt, muted grey). The number color is fixed (not the accent); only the
    // card bar carries the configurable accent.
    private sealed class MainUsageControl : Control
    {
        private const int Gap = 6;
        private readonly float _valueSize;
        private readonly float _suffixSize;
        private string _value = "—";
        private string _suffix = string.Empty;
        private Color _valueColor = UsageTheme.MutedText;

        public MainUsageControl(float valuePointSize)
        {
            _valueSize = valuePointSize;
            // One point larger than before, but still clearly smaller than the value.
            _suffixSize = valuePointSize * 0.46f + 1f;
            SetStyle(
                ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.UserPaint
                | ControlStyles.SupportsTransparentBackColor,
                true);
            BackColor = Color.Transparent;
        }

        public void SetContent(string value, string suffix, Color valueColor)
        {
            _value = value ?? string.Empty;
            _suffix = suffix ?? string.Empty;
            _valueColor = valueColor;
            Invalidate();
        }

        // Transparent: let the glass card paint its gradient behind us.
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            if (BackColor == Color.Transparent && Parent is not null)
            {
                var g = e.Graphics;
                var state = g.Save();
                g.TranslateTransform(-Left, -Top);
                using var pe = new PaintEventArgs(g, new Rectangle(Left, Top, Width, Height));
                InvokePaintBackground(Parent, pe);
                InvokePaint(Parent, pe);
                g.Restore(state);
                return;
            }

            base.OnPaintBackground(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            using var valueFont = UsageTheme.CreateCopilotFont(_valueSize, FontStyle.Bold);
            using var suffixFont = UsageTheme.CreateCopilotFont(_suffixSize, FontStyle.Regular);

            const TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;

            // Vertically center the big value; the small suffix shares its baseline.
            var valueTop = (Height - valueFont.GetHeight(g)) / 2f;
            var baseline = valueTop + AscentPx(g, valueFont);

            TextRenderer.DrawText(g, _value, valueFont, new Point(0, (int)Math.Round(valueTop)), _valueColor, flags);

            if (!string.IsNullOrEmpty(_suffix))
            {
                var valueWidth = TextRenderer.MeasureText(g, _value, valueFont, new Size(int.MaxValue, int.MaxValue), flags).Width;
                var suffixTop = baseline - AscentPx(g, suffixFont);
                // Fixed muted grey — a few steps lighter than the near-black number,
                // and not affected by the accent/color setting.
                TextRenderer.DrawText(g, _suffix, suffixFont, new Point(valueWidth + Gap, (int)Math.Round(suffixTop)), UsageTheme.MutedText, flags);
            }
        }

        // Ascent (baseline offset from the top of the text cell), in pixels.
        private static float AscentPx(Graphics g, Font font)
        {
            var family = font.FontFamily;
            var lineSpacing = family.GetLineSpacing(font.Style);
            return lineSpacing <= 0
                ? font.GetHeight(g)
                : font.GetHeight(g) * family.GetCellAscent(font.Style) / lineSpacing;
        }
    }

    // Octicons "copilot-16" glyph (MIT, https://github.com/primer/octicons), built
    // once from its embedded SVG path data and filled scaled to a target rect. No
    // external network access and no font file — just vector paths in code.
    private static class CopilotGlyph
    {
        private const string Body = "M7.998 15.035c-4.562 0-7.873-2.914-7.998-3.749V9.338c.085-.628.677-1.686 1.588-2.065.013-.07.024-.143.036-.218.029-.183.06-.384.126-.612-.201-.508-.254-1.084-.254-1.656 0-.87.128-1.769.693-2.484.579-.733 1.494-1.124 2.724-1.261 1.206-.134 2.262.034 2.944.765.05.053.096.108.139.165.044-.057.094-.112.143-.165.682-.731 1.738-.899 2.944-.765 1.23.137 2.145.528 2.724 1.261.566.715.693 1.614.693 2.484 0 .572-.053 1.148-.254 1.656.066.228.098.429.126.612.012.076.024.148.037.218.924.385 1.522 1.471 1.591 2.095v1.872c0 .766-3.351 3.795-8.002 3.795Zm0-1.485c2.28 0 4.584-1.11 5.002-1.433V7.862l-.023-.116c-.49.21-1.075.291-1.727.291-1.146 0-2.059-.327-2.71-.991A3.222 3.222 0 0 1 8 6.303a3.24 3.24 0 0 1-.544.743c-.65.664-1.563.991-2.71.991-.652 0-1.236-.081-1.727-.291l-.023.116v4.255c.419.323 2.722 1.433 5.002 1.433ZM6.762 2.83c-.193-.206-.637-.413-1.682-.297-1.019.113-1.479.404-1.713.7-.247.312-.369.789-.369 1.554 0 .793.129 1.171.308 1.371.162.181.519.379 1.442.379.853 0 1.339-.235 1.638-.54.315-.322.527-.827.617-1.553.117-.935-.037-1.395-.241-1.614Zm4.155-.297c-1.044-.116-1.488.091-1.681.297-.204.219-.359.679-.242 1.614.091.726.303 1.231.618 1.553.299.305.784.54 1.638.54.922 0 1.28-.198 1.442-.379.179-.2.308-.578.308-1.371 0-.765-.123-1.242-.37-1.554-.233-.296-.693-.587-1.713-.7Z";
        private const string Eyes = "M6.25 9.037a.75.75 0 0 1 .75.75v1.501a.75.75 0 0 1-1.5 0V9.787a.75.75 0 0 1 .75-.75Zm4.25.75v1.501a.75.75 0 0 1-1.5 0V9.787a.75.75 0 0 1 1.5 0Z";
        private const float ViewBox = 16f;

        private static GraphicsPath? _path;

        private static GraphicsPath BuildPath()
        {
            if (_path is not null)
            {
                return _path;
            }

            // Winding (nonzero) so the counter-wound lens openings render as holes.
            var p = new GraphicsPath(FillMode.Winding);
            try
            {
                SvgPath.AddToPath(p, Body);
                SvgPath.AddToPath(p, Eyes);
            }
            catch
            {
                // The path data is a hardcoded constant, but never let a parsing
                // problem reach OnPaint and take the window down — just skip the icon.
            }

            _path = p;
            return p;
        }

        public static void Draw(Graphics g, RectangleF dest, Color color)
        {
            var path = BuildPath();
            var prevSmoothing = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var state = g.Save();
            try
            {
                g.TranslateTransform(dest.X, dest.Y);
                g.ScaleTransform(dest.Width / ViewBox, dest.Height / ViewBox);
                using var brush = new SolidBrush(color);
                g.FillPath(brush, path);
            }
            finally
            {
                g.Restore(state);
                g.SmoothingMode = prevSmoothing;
            }
        }
    }

    // Minimal SVG path-data -> GraphicsPath converter, scoped to the commands the
    // Octicons copilot glyph uses (M/m L/l H/h V/v C/c A/a Z/z). Arcs are converted
    // via the SVG endpoint->center parameterization (rotation assumed 0, which holds
    // for Octicons) and added with GraphicsPath.AddArc.
    private static class SvgPath
    {
        public static void AddToPath(GraphicsPath path, string d)
        {
            var t = new Tokenizer(d);
            char cmd = '\0';
            PointF cur = default, start = default;
            var open = false;

            while (!t.AtEnd)
            {
                if (t.NextIsCommand)
                {
                    cmd = t.ReadCommand();
                }

                switch (cmd)
                {
                    case 'M':
                        cur = new PointF(t.ReadNumber(), t.ReadNumber());
                        if (open) { path.CloseFigure(); }
                        path.StartFigure();
                        open = true;
                        start = cur;
                        cmd = 'L';
                        break;
                    case 'm':
                        cur = new PointF(cur.X + t.ReadNumber(), cur.Y + t.ReadNumber());
                        if (open) { path.CloseFigure(); }
                        path.StartFigure();
                        open = true;
                        start = cur;
                        cmd = 'l';
                        break;
                    case 'L':
                        cur = AddLine(path, cur, new PointF(t.ReadNumber(), t.ReadNumber()));
                        break;
                    case 'l':
                        cur = AddLine(path, cur, new PointF(cur.X + t.ReadNumber(), cur.Y + t.ReadNumber()));
                        break;
                    case 'H':
                        cur = AddLine(path, cur, new PointF(t.ReadNumber(), cur.Y));
                        break;
                    case 'h':
                        cur = AddLine(path, cur, new PointF(cur.X + t.ReadNumber(), cur.Y));
                        break;
                    case 'V':
                        cur = AddLine(path, cur, new PointF(cur.X, t.ReadNumber()));
                        break;
                    case 'v':
                        cur = AddLine(path, cur, new PointF(cur.X, cur.Y + t.ReadNumber()));
                        break;
                    case 'C':
                    {
                        var c1 = new PointF(t.ReadNumber(), t.ReadNumber());
                        var c2 = new PointF(t.ReadNumber(), t.ReadNumber());
                        var e = new PointF(t.ReadNumber(), t.ReadNumber());
                        path.AddBezier(cur, c1, c2, e);
                        cur = e;
                        break;
                    }
                    case 'c':
                    {
                        var c1 = new PointF(cur.X + t.ReadNumber(), cur.Y + t.ReadNumber());
                        var c2 = new PointF(cur.X + t.ReadNumber(), cur.Y + t.ReadNumber());
                        var e = new PointF(cur.X + t.ReadNumber(), cur.Y + t.ReadNumber());
                        path.AddBezier(cur, c1, c2, e);
                        cur = e;
                        break;
                    }
                    case 'A':
                    {
                        var rx = t.ReadNumber();
                        var ry = t.ReadNumber();
                        var rot = t.ReadNumber();
                        var laf = t.ReadFlag();
                        var sf = t.ReadFlag();
                        var e = new PointF(t.ReadNumber(), t.ReadNumber());
                        cur = AddArc(path, cur, rx, ry, rot, laf, sf, e);
                        break;
                    }
                    case 'a':
                    {
                        var rx = t.ReadNumber();
                        var ry = t.ReadNumber();
                        var rot = t.ReadNumber();
                        var laf = t.ReadFlag();
                        var sf = t.ReadFlag();
                        var e = new PointF(cur.X + t.ReadNumber(), cur.Y + t.ReadNumber());
                        cur = AddArc(path, cur, rx, ry, rot, laf, sf, e);
                        break;
                    }
                    case 'Z':
                    case 'z':
                        path.CloseFigure();
                        open = false;
                        cur = start;
                        cmd = '\0';
                        break;
                    default:
                        return; // Unsupported command — stop safely.
                }
            }
        }

        private static PointF AddLine(GraphicsPath path, PointF from, PointF to)
        {
            if (from != to)
            {
                path.AddLine(from, to);
            }

            return to;
        }

        private static PointF AddArc(GraphicsPath path, PointF p0, float rx, float ry, float rotDeg, bool largeArc, bool sweep, PointF p1)
        {
            rx = Math.Abs(rx);
            ry = Math.Abs(ry);
            if (rx < 1e-4f || ry < 1e-4f || p0 == p1)
            {
                return AddLine(path, p0, p1);
            }

            // Octicons arcs have no x-axis rotation; treat phi = 0.
            double x1p = (p0.X - p1.X) / 2.0;
            double y1p = (p0.Y - p1.Y) / 2.0;

            // Scale up radii if they are too small to span the endpoints.
            double lambda = (x1p * x1p) / (rx * rx) + (y1p * y1p) / (ry * ry);
            if (lambda > 1.0)
            {
                var s = Math.Sqrt(lambda);
                rx = (float)(rx * s);
                ry = (float)(ry * s);
            }

            double rx2 = (double)rx * rx;
            double ry2 = (double)ry * ry;
            double num = rx2 * ry2 - rx2 * y1p * y1p - ry2 * x1p * x1p;
            double den = rx2 * y1p * y1p + ry2 * x1p * x1p;
            double co = den <= 0 ? 0 : Math.Sqrt(Math.Max(0.0, num / den));
            if (largeArc == sweep)
            {
                co = -co;
            }

            double cxp = co * (rx * y1p) / ry;
            double cyp = co * -(ry * x1p) / rx;
            double cx = cxp + (p0.X + p1.X) / 2.0;
            double cy = cyp + (p0.Y + p1.Y) / 2.0;

            double startAngle = Angle(1, 0, (x1p - cxp) / rx, (y1p - cyp) / ry);
            double delta = Angle((x1p - cxp) / rx, (y1p - cyp) / ry, (-x1p - cxp) / rx, (-y1p - cyp) / ry);
            if (!sweep && delta > 0)
            {
                delta -= 2 * Math.PI;
            }
            else if (sweep && delta < 0)
            {
                delta += 2 * Math.PI;
            }

            path.AddArc(
                (float)(cx - rx), (float)(cy - ry), (float)(2 * rx), (float)(2 * ry),
                (float)(startAngle * 180.0 / Math.PI), (float)(delta * 180.0 / Math.PI));
            return p1;
        }

        private static double Angle(double ux, double uy, double vx, double vy)
        {
            double dot = ux * vx + uy * vy;
            double len = Math.Sqrt((ux * ux + uy * uy) * (vx * vx + vy * vy));
            double a = len <= 0 ? 0 : Math.Acos(Math.Clamp(dot / len, -1.0, 1.0));
            return ux * vy - uy * vx < 0 ? -a : a;
        }

        private sealed class Tokenizer
        {
            private readonly string _s;
            private int _i;

            public Tokenizer(string s) => _s = s;

            private void SkipSep()
            {
                while (_i < _s.Length)
                {
                    var c = _s[_i];
                    if (c is ' ' or ',' or '\t' or '\n' or '\r')
                    {
                        _i++;
                    }
                    else
                    {
                        break;
                    }
                }
            }

            public bool AtEnd
            {
                get
                {
                    SkipSep();
                    return _i >= _s.Length;
                }
            }

            public bool NextIsCommand
            {
                get
                {
                    SkipSep();
                    return _i < _s.Length && char.IsLetter(_s[_i]);
                }
            }

            public char ReadCommand()
            {
                SkipSep();
                return _i < _s.Length ? _s[_i++] : '\0';
            }

            public float ReadNumber()
            {
                SkipSep();
                var startIndex = _i;
                if (_i < _s.Length && (_s[_i] == '+' || _s[_i] == '-'))
                {
                    _i++;
                }

                while (_i < _s.Length && char.IsDigit(_s[_i]))
                {
                    _i++;
                }

                if (_i < _s.Length && _s[_i] == '.')
                {
                    _i++;
                    while (_i < _s.Length && char.IsDigit(_s[_i]))
                    {
                        _i++;
                    }
                }

                if (_i < _s.Length && (_s[_i] == 'e' || _s[_i] == 'E'))
                {
                    _i++;
                    if (_i < _s.Length && (_s[_i] == '+' || _s[_i] == '-'))
                    {
                        _i++;
                    }

                    while (_i < _s.Length && char.IsDigit(_s[_i]))
                    {
                        _i++;
                    }
                }

                // Total parse: a malformed/empty span yields 0 rather than throwing,
                // so the glyph builder can never crash the paint pipeline.
                return float.TryParse(_s.AsSpan(startIndex, _i - startIndex), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                    ? value
                    : 0f;
            }

            // Arc flags are a single '0' or '1', which may be packed with no separator.
            public bool ReadFlag()
            {
                SkipSep();
                return _i < _s.Length && _s[_i++] == '1';
            }
        }
    }
}
