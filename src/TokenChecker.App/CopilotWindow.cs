using System.Runtime.InteropServices;
using TokenChecker.Core;

namespace TokenChecker.App;

// Dedicated, borderless popup for the GitHub Copilot AI Credits card. Separate
// from the Claude/Codex status window (the user asked for it as its own screen).
//
// Resting state shows the usage percentage only ("66% 使用済み"); hovering
// anywhere on the window — or giving it keyboard focus — reveals the detailed
// values ("4,627 / 7,000 使用済み"). The swap is a pure Label.Text change on a
// FIXED-size label, so the bar ratio, reset line, and window size never move:
// no layout jitter. The diagnostics expander is a separate mechanism (it grows
// the window deliberately, like the status cards).
internal sealed class CopilotWindow : Form
{
    private const int FormPadding = 12;
    public const int CardWidth = 300;
    // Taller than the original 152 to fit the two sub-info lines (projection +
    // today's delta). This is a one-time base-size change, NOT hover jitter — the
    // sub-lines are static and never move when the main line swaps on hover.
    public const int CardBaseHeight = 186;
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
    }

    // Raised when the user dismisses the popup with Esc.
    public event EventHandler? HideRequested;

    // Raised whenever the hover/focus (interaction) state changes; the tray
    // context uses it to pause the click-then-fade countdown while the user is
    // reading the window.
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

    // Applies the configured accent color to the card's numbers + bar (the
    // <80% "good" color; severity still escalates to amber/red at 80/95).
    public void ApplyAccent(Color accent) => _card.SetAccent(accent);

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

    // Attach hover tracking to every descendant (so moving across child controls
    // never reads as "left the window"), and the drag handler to everything
    // except the genuinely interactive controls (the detail link and box).
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

    // Trust the cursor position against the whole window, not the per-child enter/
    // leave events: moving from one child to another fires leave+enter but the
    // cursor is still inside, so _hovered stays put and the text never flickers.
    private void RefreshHover()
    {
        if (IsDisposed || !IsHandleCreated)
        {
            return;
        }

        var inside = ClientRectangle.Contains(PointToClient(Cursor.Position));
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

        private readonly Label _title;
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
        // The main line is rendered as a big value + a smaller, lighter suffix.
        private string _compactValue = "—";
        private string _detailValue = "—";
        private string _suffix = string.Empty;
        private Color _valueColor = UsageTheme.MutedText;
        // Configurable base color for the numbers + bar in the <80% "good" state.
        // Severity still overrides it at 80/95 (amber/red) via UsageTheme.
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

            const int contentWidth = CardWidth - 28;

            // Title shows the selected plan (e.g. "Copilot Pro+"). Fixed width with
            // ellipsis so a long custom title never collides with the badge.
            _title = new Label
            {
                Text = "GitHub Copilot",
                AutoSize = false,
                Size = new Size(174, 22),
                Font = UsageTheme.CreateTitleFont("Segoe UI", 11.5F, FontStyle.Bold),
                ForeColor = UsageTheme.PrimaryText,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
                Location = new Point(14, 12)
            };

            // Smaller and lighter than before so it reads as a quiet status chip.
            // Sized to its text by ApplyBadge (so a long status never collides with
            // the title); AutoEllipsis is a final safety net.
            _badge = new Label
            {
                AutoSize = false,
                Size = new Size(90, 16),
                Location = new Point(CardWidth - 14 - 90, 14),
                Font = new Font("Segoe UI", 7.5F),
                BackColor = Color.Transparent,
                ForeColor = UsageTheme.MutedText,
                TextAlign = ContentAlignment.MiddleRight,
                AutoEllipsis = true,
                Text = "状態不明"
            };

            _statusMessage = new Label
            {
                AutoSize = false,
                Size = new Size(contentWidth, 16),
                Location = new Point(14, 32),
                ForeColor = UsageTheme.MutedText,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
                Font = new Font("Segoe UI", 8.5F),
                Visible = false
            };

            // The hover/focus swap target: a fixed-size custom control that draws a
            // big value plus a smaller, lighter "使用済み" suffix on a shared
            // baseline. Only the value text swaps on hover; the box never changes,
            // so the layout cannot jitter.
            _mainLine = new MainUsageControl(15F)
            {
                Location = new Point(14, 50),
                Size = new Size(contentWidth, 30)
            };

            _bar = new UsageBarControl
            {
                Location = new Point(14, 88),
                Size = new Size(contentWidth, 8),
                BackColor = Color.Transparent,
                UseGradient = true
            };

            _resetSub = new Label
            {
                AutoSize = false,
                Size = new Size(contentWidth, 16),
                Location = new Point(14, 100),
                ForeColor = UsageTheme.MutedText,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
                Font = new Font("Segoe UI", 8.5F),
                Text = "—"
            };

            // Sub-info lines (smaller, muted): a 100%-reach projection and the
            // since-today's-09:00 delta. Static — they do not change on hover.
            _predictionSub = new Label
            {
                AutoSize = false,
                Size = new Size(contentWidth, 16),
                Location = new Point(14, 118),
                ForeColor = UsageTheme.MutedText,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
                Font = new Font("Segoe UI", 8.5F),
                Visible = false
            };

            _todaySub = new Label
            {
                AutoSize = false,
                Size = new Size(contentWidth, 16),
                Location = new Point(14, 136),
                ForeColor = UsageTheme.MutedText,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
                Font = new Font("Segoe UI", 8.5F),
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
                Location = new Point(14, 160),
                Font = new Font("Segoe UI", 8.5F),
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
                Location = new Point(14, 178),
                Size = new Size(contentWidth, CardDetailExtra - 16),
                Visible = false,
                TabStop = false,
                WordWrap = true,
                Font = new Font("Consolas", 8.5F)
            };

            Controls.Add(_title);
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
            _title.Text = planTitle;

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
                _valueColor = UsageTheme.AccentColor(percent, _accent);
                _bar.AccentColor = _valueColor;
                _bar.SetValue(percent);
                _resetSub.Text = $"残 {remaining:N0} · {FormatReset(window)}";
            }
            else
            {
                _hasPercent = false;
                // No allowance to compare against: show the raw used credits only.
                // The "当月集計" context and the plan hint move to the sub-line.
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

            // The projection needs an allowance; with no plan the reset sub-line
            // already prompts to pick one, so hide the projection rather than
            // repeat "予測にはデータ不足".
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
            // matches what the Label actually renders — a NoPadding measure undersizes
            // it and the text clips. Size both axes and center in the title row so a
            // tall (DPI-scaled) glyph never gets cut off vertically either.
            var measured = TextRenderer.MeasureText(text, _badge.Font);
            var width = Math.Clamp(measured.Width + 2, 40, CardWidth - 28 - 70);
            // Height tracks the text so nothing clips vertically. Center it in the
            // title row, but never let it float ABOVE the row: at very high DPI the
            // measured text can exceed the 22px row, so floor y at the row top and
            // let the (right-aligned) badge extend down into the empty right margin.
            var height = Math.Max(18, measured.Height + 2);
            var x = CardWidth - 14 - width;
            var y = Math.Max(12, 12 + (22 - height) / 2);
            _badge.Bounds = new Rectangle(x, y, width, height);
            _title.Width = Math.Max(40, x - 14 - 8);
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
        // numbers + bar instead (see _accent / SetAccent), not this backdrop.
        private readonly Color _brand = UsageTheme.CopilotBrand;

        // Applies the configured accent to the numbers + bar. Recomputes the live
        // color immediately so a settings change shows without waiting for the next
        // refresh; severity (80/95) still overrides it.
        public void SetAccent(Color accent)
        {
            _accent = accent;
            if (_hasPercent)
            {
                _valueColor = UsageTheme.AccentColor(_lastPercent, _accent);
                _bar.AccentColor = _valueColor;
                _bar.Invalidate();
                ApplyMainLineText();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
            => UsageTheme.PaintGlassCard(e.Graphics, Width, Height, _brand);
    }

    // Renders a large value plus a smaller, lighter suffix on a shared baseline,
    // inside a fixed box — so the hover value-swap repaints text only and never
    // changes layout (no jitter). e.g. "66%" (15pt bold) + "使用済み" (~55%, lighter).
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
            _suffixSize = valuePointSize * 0.46f;
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
            using var valueFont = new Font("Segoe UI", _valueSize, FontStyle.Bold);
            using var suffixFont = new Font("Segoe UI", _suffixSize, FontStyle.Regular);

            const TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;

            // Vertically center the big value; the small suffix shares its baseline.
            var valueTop = (Height - valueFont.GetHeight(g)) / 2f;
            var baseline = valueTop + AscentPx(g, valueFont);

            TextRenderer.DrawText(g, _value, valueFont, new Point(0, (int)Math.Round(valueTop)), _valueColor, flags);

            if (!string.IsNullOrEmpty(_suffix))
            {
                var valueWidth = TextRenderer.MeasureText(g, _value, valueFont, new Size(int.MaxValue, int.MaxValue), flags).Width;
                var suffixTop = baseline - AscentPx(g, suffixFont);
                var suffixColor = UsageTheme.Lighten(_valueColor, 0.40f);
                TextRenderer.DrawText(g, _suffix, suffixFont, new Point(valueWidth + Gap, (int)Math.Round(suffixTop)), suffixColor, flags);
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
}
