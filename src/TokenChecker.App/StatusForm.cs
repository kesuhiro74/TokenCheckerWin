using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using TokenChecker.Core;

namespace TokenChecker.App;

internal sealed class StatusForm : Form
{
    // Surface/chrome and brand colors come from the shared, theme-aware palette
    // (UsageTheme), so the status window follows the light/dark theme. These thin
    // forwarders keep the rest of this file's color references unchanged.
    private static Color Surface => UsageTheme.Surface;
    private static Color Card => UsageTheme.Card;
    private static Color CardBorder => UsageTheme.CardBorder;
    private static Color PrimaryText => UsageTheme.PrimaryText;
    private static Color SecondaryText => UsageTheme.SecondaryText;
    private static Color MutedText => UsageTheme.MutedText;
    private static Color SubtleText => UsageTheme.SubtleText;
    private static Color Good => UsageTheme.Good;
    private static Color Warning => UsageTheme.Warning;
    private static Color Bad => UsageTheme.Bad;
    private static Color TrackEmpty => UsageTheme.TrackEmpty;
    private static Color DetailToggle => UsageTheme.DetailToggle;
    private static Color DetailBackground => UsageTheme.DetailBackground;

    // Service brand colors (Claude=blue / Codex=purple), theme-tuned in UsageTheme.
    private static Color ClaudeBrand => UsageTheme.ClaudeBrand;
    private static Color CodexBrand => UsageTheme.CodexBrand;

    // Card title typefaces, each echoing the service's own wordmark:
    // Claude Code uses an elegant serif (Georgia ~ the Claude serif wordmark),
    // Codex/ChatGPT uses a clean grotesque sans (Arial ~ the ChatGPT wordmark).
    // Both ship with Windows; CreateTitleFont falls back to Segoe UI Bold if a
    // family is somehow missing.
    private const string ClaudeTitleFontFamily = "Georgia";
    private const float ClaudeTitleFontSize = 11.5F;
    private const FontStyle ClaudeTitleFontStyle = FontStyle.Bold;

    private const string CodexTitleFontFamily = "Arial";
    private const float CodexTitleFontSize = 11F;
    private const FontStyle CodexTitleFontStyle = FontStyle.Bold;

    private const int FormPadding = 14;

    // Normal mode dimensions
    private const int NormalFormWidth = 392;
    private const int NormalCardHeight = 184;
    private const int NormalCardGap = 10;
    private const int NormalDetailExtra = 92;

    // Compact mode dimensions — the window hugs the cards with a thin margin and
    // its width follows how many services are shown (no wasted space on the right
    // when only one service is visible). Halved from 6 for a tighter compact gutter.
    private const int CompactFormPadding = 3;
    private const int CompactPanelWidth = 200;
    private const int CompactPanelHeight = 96;
    private const int CompactPanelGap = 8;

    // Minimum mode dimensions — the popup hugs the card with no outer padding.
    // The width follows the composed status-line content (LayoutMinimum), with
    // this floor so a sparse line still reads as a card.
    private const int MinimumFormMinWidth = 180;
    private const int MinimumPanelHeight = 46;

    private const int FooterHeight = 22;

    private readonly ServiceCard _claudeCard;
    private readonly ServiceCard _codexCard;
    private readonly CompactServicePanel _compactClaude;
    private readonly CompactServicePanel _compactCodex;
    private readonly MinimumStripPanel _minimumPanel;
    private readonly Label _updatedAt;

    private bool _showClaude = true;
    private bool _showCodex = true;
    private DisplayMode _displayMode = DisplayMode.Normal;

    public StatusForm()
    {
        Text = "TokenCheckerWin";
        FormBorderStyle = FormBorderStyle.None;
        ControlBox = false;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        TopMost = true;
        BackColor = Surface;
        ForeColor = PrimaryText;
        Font = new Font("Segoe UI", 9F);
        StartPosition = FormStartPosition.Manual;

        _claudeCard = new ServiceCard("Claude Code", ClaudeBrand,
            CreateTitleFont(ClaudeTitleFontFamily, ClaudeTitleFontSize, ClaudeTitleFontStyle),
            "✳", new Font("Segoe UI Symbol", 11F))
        {
            Size = new Size(NormalFormWidth - FormPadding * 2, NormalCardHeight)
        };
        _codexCard = new ServiceCard("Codex", CodexBrand,
            CreateTitleFont(CodexTitleFontFamily, CodexTitleFontSize, CodexTitleFontStyle),
            "</>")
        {
            Size = new Size(NormalFormWidth - FormPadding * 2, NormalCardHeight)
        };
        _compactClaude = new CompactServicePanel("Claude Code", ClaudeBrand,
            CreateTitleFont(ClaudeTitleFontFamily, 9.5F, ClaudeTitleFontStyle),
            "✳", new Font("Segoe UI Symbol", 9F));
        _compactCodex = new CompactServicePanel("Codex", CodexBrand,
            CreateTitleFont(CodexTitleFontFamily, 9.5F, CodexTitleFontStyle),
            "</>");
        _minimumPanel = new MinimumStripPanel();

        _claudeCard.HeightChanged += (_, _) => RecalculateLayout();
        _codexCard.HeightChanged += (_, _) => RecalculateLayout();

        _updatedAt = new Label
        {
            AutoSize = false,
            ForeColor = MutedText,
            BackColor = Color.Transparent,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", 8.5F)
        };

        Controls.Add(_claudeCard);
        Controls.Add(_codexCard);
        Controls.Add(_compactClaude);
        Controls.Add(_compactCodex);
        Controls.Add(_minimumPanel);
        Controls.Add(_updatedAt);

        ApplyVisibilityForMode();
        RecalculateLayout();
        SetLoading();

        MouseDown += OnDragMouseDown;
        AttachDragHandlers(this);
    }

    // Raised when the user dismisses the borderless popup with Esc. The tray
    // context saves the location and hides the form in response.
    public event EventHandler? HideRequested;

    // The popup is borderless (no title bar); give it a native drop shadow so
    // it reads as a floating card instead of a flat rectangle.
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

    // Shown without stealing focus (the tray context manages activation per the
    // display method); the context calls Activate() explicitly when the window is
    // opened by a click/menu so Esc still works.
    protected override bool ShowWithoutActivation => true;

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    private const int WM_NCLBUTTONDOWN = 0x00A1;
    private const int HTCAPTION = 2;

    // With no title bar (and no surrounding padding in minimum mode), let the
    // user move the popup by dragging anywhere on it. Genuinely interactive
    // controls (the "詳細" link and diagnostics box) are skipped in
    // AttachDragHandlers so they still receive their own clicks.
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

    private void AttachDragHandlers(Control root)
    {
        foreach (Control child in root.Controls)
        {
            if (child is LinkLabel || child is TextBoxBase)
            {
                continue;
            }

            child.MouseDown += OnDragMouseDown;
            AttachDragHandlers(child);
        }
    }

    // Esc closes the popup (no close button anymore).
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape)
        {
            HideRequested?.Invoke(this, EventArgs.Empty);
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    public void ApplySettings(AppSettings settings)
    {
        _showClaude = settings.IsServiceVisible("Claude");
        _showCodex = settings.IsServiceVisible("Codex");
        _displayMode = settings.DisplayMode;
        ApplyVisibilityForMode();
        RecalculateLayout();
    }

    public void SetLoading()
    {
        _claudeCard.SetLoading();
        _codexCard.SetLoading();
        _compactClaude.SetLoading();
        _compactCodex.SetLoading();
        _minimumPanel.SetLoading();
        _updatedAt.Text = Strings.T("最終更新: 更新中");

        // The minimum strip width follows its (possibly placeholder) content.
        if (_displayMode == DisplayMode.Minimum)
        {
            RecalculateLayout();
        }
    }

    public void UpdateSnapshot(UsageSnapshot snapshot, UsageSnapshot? lastSuccessfulSnapshot, DailyCostsView? dailyCosts)
    {
        var claude = snapshot.Services.FirstOrDefault(service => service.ServiceName == "Claude");
        var codex = snapshot.Services.FirstOrDefault(service => service.ServiceName == "Codex");
        var fallbackClaude = claude?.Status == ProviderStatus.Available
            ? claude
            : lastSuccessfulSnapshot?.Services.FirstOrDefault(service => service.ServiceName == "Claude" && service.Status == ProviderStatus.Available);
        var fallbackCodex = codex?.Status == ProviderStatus.Available
            ? codex
            : lastSuccessfulSnapshot?.Services.FirstOrDefault(service => service.ServiceName == "Codex" && service.Status == ProviderStatus.Available);

        _claudeCard.Update(claude, fallbackClaude, dailyCosts?.Claude);
        _codexCard.Update(codex, fallbackCodex, dailyCosts?.Codex);
        _compactClaude.Update(claude, fallbackClaude);
        _compactCodex.Update(codex, fallbackCodex);
        _minimumPanel.Update(fallbackClaude, fallbackCodex, _showClaude, _showCodex, dailyCosts);
        _updatedAt.Text = Strings.Tf("最終更新: {0}", snapshot.CapturedAtUtc.ToLocalTime().ToString("HH:mm:ss"));

        // The minimum strip is sized to its content, which may have just changed
        // (reset stamps, cost); re-fit the window in that mode only.
        if (_displayMode == DisplayMode.Minimum)
        {
            RecalculateLayout();
        }
    }

    private void ApplyVisibilityForMode()
    {
        _claudeCard.Visible = _displayMode == DisplayMode.Normal && _showClaude;
        _codexCard.Visible = _displayMode == DisplayMode.Normal && _showCodex;
        _compactClaude.Visible = _displayMode == DisplayMode.Compact && _showClaude;
        _compactCodex.Visible = _displayMode == DisplayMode.Compact && _showCodex;
        _minimumPanel.Visible = _displayMode == DisplayMode.Minimum && (_showClaude || _showCodex);
        _updatedAt.Visible = _displayMode == DisplayMode.Normal;
    }

    private void RecalculateLayout()
    {
        SuspendLayout();
        try
        {
            switch (_displayMode)
            {
                case DisplayMode.Minimum:
                    LayoutMinimum();
                    break;
                case DisplayMode.Compact:
                    LayoutCompact();
                    break;
                default:
                    LayoutNormal();
                    break;
            }
        }
        finally
        {
            ResumeLayout(true);
        }
    }

    private void LayoutNormal()
    {
        var contentWidth = NormalFormWidth - FormPadding * 2;
        _claudeCard.Size = new Size(contentWidth, _claudeCard.Height);
        _codexCard.Size = new Size(contentWidth, _codexCard.Height);
        _updatedAt.Size = new Size(contentWidth, FooterHeight);

        var y = FormPadding;
        if (_showClaude)
        {
            _claudeCard.Location = new Point(FormPadding, y);
            y += _claudeCard.Height + NormalCardGap;
        }

        if (_showCodex)
        {
            _codexCard.Location = new Point(FormPadding, y);
            y += _codexCard.Height + NormalCardGap;
        }

        _updatedAt.Location = new Point(FormPadding, y);
        var contentHeight = y + FooterHeight + FormPadding;
        ClientSize = new Size(NormalFormWidth, contentHeight);
    }

    private void LayoutCompact()
    {
        // Tight uniform margin, no footer; width tracks the visible service count
        // so a single card doesn't leave a large empty gap on the right.
        var pad = CompactFormPadding;
        var visibleCount = (_showClaude ? 1 : 0) + (_showCodex ? 1 : 0);

        var x = pad;
        if (_showClaude)
        {
            _compactClaude.Location = new Point(x, pad);
            _compactClaude.Size = new Size(CompactPanelWidth, CompactPanelHeight);
            x += CompactPanelWidth + CompactPanelGap;
        }

        if (_showCodex)
        {
            _compactCodex.Location = new Point(x, pad);
            _compactCodex.Size = new Size(CompactPanelWidth, CompactPanelHeight);
        }

        var contentWidth = visibleCount > 0
            ? CompactPanelWidth * visibleCount + CompactPanelGap * (visibleCount - 1)
            : CompactPanelWidth;
        var contentHeight = (visibleCount > 0 ? CompactPanelHeight : 0) + pad * 2;
        ClientSize = new Size(contentWidth + pad * 2, contentHeight);
    }

    private void LayoutMinimum()
    {
        // No outer padding: the card fills the whole popup window (it paints square;
        // the window's rounded corners come from DWM — see OnHandleCreated).
        // The width follows the composed status-line content, clamped between a
        // floor and the working area so the popup never overflows the screen.
        var area = GetWorkingArea();
        var maxWidth = Math.Max(MinimumFormMinWidth, area.Width - 32);
        var width = Math.Clamp(_minimumPanel.PreferredContentWidth, MinimumFormMinWidth, maxWidth);

        _minimumPanel.Location = new Point(0, 0);
        _minimumPanel.Size = new Size(width, MinimumPanelHeight);
        ClientSize = new Size(width, MinimumPanelHeight);
        KeepMinimumOnScreen(area);
    }

    private Rectangle GetWorkingArea()
    {
        try
        {
            return Screen.FromControl(this).WorkingArea;
        }
        catch
        {
            // Screen resolution can fail before the handle exists / during
            // display changes; fall back to a conservative desktop size.
            return new Rectangle(0, 0, 1280, 720);
        }
    }

    // When a refresh widens the minimum strip while the popup is visible, the
    // window can grow past the right edge of the screen; nudge it back left.
    private void KeepMinimumOnScreen(Rectangle area)
    {
        if (_displayMode != DisplayMode.Minimum || !Visible)
        {
            return;
        }

        if (Right > area.Right)
        {
            Left = Math.Max(area.Left, area.Right - Width);
        }
    }

    // Rounded corners for the borderless popup come from the Win11 DWM compositor
    // (anti-aliased, DPI-correct), not a hard GDI Region — a Region stair-steps a
    // small radius into a square-looking corner. Inset cards keep their own rounded
    // glass; a window-filling card (minimum mode) paints square and DWM rounds it.
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        WindowEffects.UseRoundedCorners(Handle, small: true);
    }

    // Provider-status coloring is centralized in UsageTheme (same severity palette
    // as the Copilot card, which calls UsageTheme.StatusColor directly); this thin
    // forwarder keeps the rest of StatusForm's call sites unchanged.
    private static Color StatusColor(ProviderStatus status) => UsageTheme.StatusColor(status);

    // Severity coloring is centralized in UsageTheme so the 80%/95% escalation
    // lives in exactly one place (CLAUDE.md). These thin wrappers keep the rest
    // of StatusForm unchanged.
    private static Color UsageAccentColor(double? value) => UsageTheme.AccentColor(value);

    private static RateLimitWindow? FindFiveHourWindow(ServiceUsage? service)
    {
        if (service is null || service.Windows.Count == 0)
        {
            return null;
        }

        return service.Windows.FirstOrDefault(window => window.WindowDurationMins == 300)
            ?? service.Windows
                .Where(window => window.WindowDurationMins is not null)
                .OrderBy(window => window.WindowDurationMins)
                .FirstOrDefault();
    }

    private static RateLimitWindow? FindWeeklyWindow(ServiceUsage? service)
        => service?.Windows.FirstOrDefault(window => window.WindowDurationMins == 10080);

    // Build a title font from the requested family, falling back to Segoe UI
    // Bold if the family is not installed (so a missing display face never
    // leaves the title in an unexpected substitute face).
    private static Font CreateTitleFont(string family, float size, FontStyle style)
    {
        try
        {
            var font = new Font(family, size, style);
            if (string.Equals(font.Name, family, StringComparison.OrdinalIgnoreCase))
            {
                return font;
            }

            font.Dispose();
        }
        catch
        {
            // Fall through to the default below.
        }

        return new Font("Segoe UI", 10.5F, FontStyle.Bold);
    }

    // Frosted-glass card backdrop shared by the normal and compact service
    // panels: a faint brand-tinted vertical gradient, a soft top highlight, a
    // brand accent pill down the left edge, and a delicate double border. Brand
    // color is decorative only — usage severity stays on the numbers/bars/ring.
    // Delegates to the shared, theme-aware glass painter so the status cards follow
    // the light/dark theme (and there is a single implementation, gloss included).
    private static void PaintGlassCard(Graphics g, int width, int height, Color brand)
        => UsageTheme.PaintGlassCard(g, width, height, brand);

    // ----- ServiceCard (normal mode) ---------------------------------------
    private sealed class ServiceCard : Panel
    {
        public event EventHandler? HeightChanged;

        private readonly string _displayName;
        private readonly Color _brand;
        private readonly Label? _titleMark;
        private readonly Label _title;
        private readonly Label _badge;
        private readonly Label _statusMessage;
        private readonly Label _shortWindowLabel;
        private readonly Label _shortResetInline;
        private readonly Label _shortPercent;
        private readonly UsageBarControl _shortBar;
        private readonly Label _weeklyLabel;
        private readonly Label _weeklyResetInline;
        private readonly Label _weeklyPercent;
        private readonly UsageBarControl _weeklyBar;
        private readonly Label _dailyCost;
        private readonly LinkLabel _detailToggle;
        private readonly TextBox _detailBox;
        private bool _detailExpanded;

        public ServiceCard(string displayName, Color brand, Font titleFont, string? mark = null, Font? markFont = null)
        {
            _displayName = displayName;
            _brand = brand;
            BackColor = Card;
            // Custom gradient/glass background is painted in OnPaint; double
            // buffer so the layered fills don't flicker on refresh.
            SetStyle(
                ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.UserPaint
                | ControlStyles.ResizeRedraw,
                true);

            // An optional brand mark (e.g. "✳") is drawn in its own glyph-capable
            // font so it never falls back to a tofu box when the title face (a
            // serif such as Georgia) lacks the symbol. The name keeps the brand
            // title font; _displayName stays plain for status messages.
            var nameX = 14;
            if (!string.IsNullOrEmpty(mark))
            {
                var glyphFont = markFont ?? titleFont;
                _titleMark = new Label
                {
                    Text = mark,
                    AutoSize = true,
                    Font = glyphFont,
                    ForeColor = _brand,
                    BackColor = Color.Transparent,
                    Location = new Point(14, 13)
                };
                var markWidth = TextRenderer.MeasureText(mark, glyphFont, Size.Empty, TextFormatFlags.NoPadding).Width;
                nameX = 14 + markWidth + 7;
            }

            _title = new Label
            {
                Text = displayName,
                AutoSize = true,
                Font = titleFont,
                ForeColor = PrimaryText,
                BackColor = Color.Transparent,
                Location = new Point(nameX, 12)
            };

            _badge = new Label
            {
                AutoSize = true,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                BackColor = Color.Transparent,
                ForeColor = MutedText,
                Location = new Point(264, 14),
                Text = Strings.T("状態不明")
            };

            // Sits right under the title and gives the user an action hint when
            // status is non-Available (e.g. "Claude Code にログインしてください").
            _statusMessage = new Label
            {
                AutoSize = false,
                Size = new Size(340, 16),
                Location = new Point(14, 32),
                ForeColor = MutedText,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
                Font = new Font("Segoe UI", 8.5F),
                Visible = false
            };

            // The window labels keep AutoSize=false but shrink to the measured
            // text width so the inline reset label can start right after them
            // without overlap in either language ("5時間" vs "5-hour").
            var windowLabelFont = new Font("Segoe UI", 9.5F);
            var inlineFont = new Font("Segoe UI", 8.5F);
            var shortLabelText = Strings.T("5時間");
            var shortLabelWidth = TextRenderer.MeasureText(shortLabelText, windowLabelFont).Width;
            _shortWindowLabel = new Label
            {
                Text = shortLabelText,
                AutoSize = false,
                Size = new Size(shortLabelWidth, 22),
                Location = new Point(14, 46),
                ForeColor = SecondaryText,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = windowLabelFont
            };

            // Remaining time + reset clock, inline next to the 5h label. Spans up
            // to the percent number (x=254) and ellipsizes if it ever gets long.
            var shortInlineX = 14 + shortLabelWidth + 6;
            _shortResetInline = new Label
            {
                AutoSize = false,
                Size = new Size(254 - shortInlineX - 4, 18),
                Location = new Point(shortInlineX, 49),
                ForeColor = MutedText,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
                Font = inlineFont
            };

            _shortPercent = new Label
            {
                AutoSize = false,
                Size = new Size(100, 26),
                Location = new Point(254, 42),
                ForeColor = MutedText,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleRight,
                Font = new Font("Segoe UI", 15F, FontStyle.Bold),
                Text = "—"
            };

            _shortBar = new UsageBarControl
            {
                Location = new Point(14, 78),
                Size = new Size(340, 8),
                BackColor = Color.Transparent,
                UseGradient = true
            };

            var weeklyLabelText = Strings.T("週次");
            var weeklyLabelWidth = TextRenderer.MeasureText(weeklyLabelText, windowLabelFont).Width;
            _weeklyLabel = new Label
            {
                Text = weeklyLabelText,
                AutoSize = false,
                Size = new Size(weeklyLabelWidth, 22),
                Location = new Point(14, 96),
                ForeColor = SecondaryText,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = windowLabelFont
            };

            // Weekly inline shows the reset datetime only (no remaining time).
            var weeklyInlineX = 14 + weeklyLabelWidth + 6;
            _weeklyResetInline = new Label
            {
                AutoSize = false,
                Size = new Size(254 - weeklyInlineX - 4, 18),
                Location = new Point(weeklyInlineX, 99),
                ForeColor = MutedText,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
                Font = inlineFont
            };

            _weeklyPercent = new Label
            {
                AutoSize = false,
                Size = new Size(100, 22),
                Location = new Point(254, 96),
                ForeColor = MutedText,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleRight,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                Text = "—"
            };

            // Weekly mirrors the 5h bar but is deliberately understated: thinner
            // (5px vs 8px) and paired with the smaller weekly percent above, so
            // the 5h window stays the visual focus of the card.
            _weeklyBar = new UsageBarControl
            {
                Location = new Point(14, 122),
                Size = new Size(340, 5),
                BackColor = Color.Transparent,
                UseGradient = true
            };

            // Today's local-session spend (e.g. "¥1,234 (daily)"), per service.
            // Hidden while the cost is unknown; deliberately NOT reset on
            // SetLoading so it doesn't blink on every refresh.
            _dailyCost = new Label
            {
                AutoSize = false,
                Size = new Size(200, 18),
                Location = new Point(14, 134),
                ForeColor = SecondaryText,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = inlineFont,
                Visible = false
            };

            _detailToggle = new LinkLabel
            {
                Text = Strings.T("詳細を表示"),
                AutoSize = true,
                BackColor = Color.Transparent,
                LinkColor = DetailToggle,
                ActiveLinkColor = PrimaryText,
                VisitedLinkColor = DetailToggle,
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
                BackColor = DetailBackground,
                ForeColor = SecondaryText,
                Location = new Point(14, 178),
                Size = new Size(340, NormalDetailExtra - 16),
                Visible = false,
                TabStop = false,
                WordWrap = true,
                Font = new Font("Consolas", 8.5F)
            };

            if (_titleMark is not null)
            {
                Controls.Add(_titleMark);
            }

            Controls.Add(_title);
            Controls.Add(_badge);
            Controls.Add(_statusMessage);
            Controls.Add(_shortWindowLabel);
            Controls.Add(_shortResetInline);
            Controls.Add(_shortPercent);
            Controls.Add(_shortBar);
            Controls.Add(_weeklyLabel);
            Controls.Add(_weeklyResetInline);
            Controls.Add(_weeklyPercent);
            Controls.Add(_weeklyBar);
            Controls.Add(_dailyCost);
            Controls.Add(_detailToggle);
            Controls.Add(_detailBox);
        }

        public void SetLoading()
        {
            _badge.Text = Strings.T("更新中");
            _badge.ForeColor = Warning;
            _statusMessage.Visible = false;
            _statusMessage.Text = string.Empty;
            _shortPercent.Text = "—";
            _shortPercent.ForeColor = MutedText;
            _shortBar.SetValue(null);
            _shortResetInline.Text = string.Empty;
            _weeklyPercent.Text = "—";
            _weeklyPercent.ForeColor = MutedText;
            _weeklyBar.SetValue(null);
            _weeklyResetInline.Text = string.Empty;
            // _dailyCost is intentionally left as-is: the cost is recomputed on
            // its own cadence and clearing it here would blink on every refresh.
            UpdateDiagnostics(string.Empty);
        }

        public void Update(ServiceUsage? current, ServiceUsage? fallback, DailyCost? dailyCost)
        {
            var status = current?.Status ?? ProviderStatus.Unknown;
            _badge.Text = ProviderStatusPresenter.BadgeText(status);
            _badge.ForeColor = StatusColor(status);

            var shortWindow = FindFiveHourWindow(fallback);
            var weekly = FindWeeklyWindow(fallback);

            // Surface the friendly action hint right under the title whenever
            // status is non-Available so the user knows what to do. Available
            // hides the line entirely so the layout stays compact.
            var hasFallbackWindows = fallback is { Windows.Count: > 0 };
            if (status == ProviderStatus.Available)
            {
                _statusMessage.Visible = false;
                _statusMessage.Text = string.Empty;
            }
            else
            {
                _statusMessage.Text = ProviderStatusPresenter.FriendlyMessage(_displayName, status, hasFallbackWindows);
                _statusMessage.ForeColor = StatusColor(status);
                _statusMessage.Visible = true;
            }

            _shortPercent.Text = FormatPercent(shortWindow);
            _shortPercent.ForeColor = UsageAccentColor(shortWindow?.UsedPercent);
            _shortBar.AccentColor = UsageAccentColor(shortWindow?.UsedPercent);
            _shortBar.SetValue(shortWindow?.UsedPercent);
            _shortResetInline.Text = ResetTimeFormatter.FormatShortInline(shortWindow);

            _weeklyPercent.Text = FormatPercent(weekly);
            _weeklyPercent.ForeColor = UsageAccentColor(weekly?.UsedPercent);
            _weeklyBar.AccentColor = UsageAccentColor(weekly?.UsedPercent);
            _weeklyBar.SetValue(weekly?.UsedPercent);
            _weeklyResetInline.Text = ResetTimeFormatter.FormatWeeklyResetOnly(weekly);

            // Today's spend: hidden when unknown (cost reading failed or no
            // session data); shown otherwise as ¥ or $ per the UI language.
            if (dailyCost is null)
            {
                _dailyCost.Visible = false;
            }
            else
            {
                _dailyCost.Text = DailyCostText.Format(dailyCost);
                _dailyCost.Visible = true;
            }

            var debug = ProviderStatusPresenter.BuildDebugSummary(_displayName, current, fallback);
            var masked = ProviderStatusPresenter.SafeDiagnostics(current?.Message);
            var combined = string.IsNullOrEmpty(masked) ? debug : $"{debug}{Environment.NewLine}{masked}";
            UpdateDiagnostics(combined);
        }

        private static string FormatPercent(RateLimitWindow? window)
            => window?.UsedPercent is null
                ? "n/a"
                : $"{Math.Round(window.UsedPercent.Value)}%";

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
            var newHeight = expanded ? NormalCardHeight + NormalDetailExtra : NormalCardHeight;
            if (Height != newHeight)
            {
                Height = newHeight;
                HeightChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
            => PaintGlassCard(e.Graphics, Width, Height, _brand);
    }

    // ----- CompactServicePanel (compact mode) ------------------------------
    private sealed class CompactServicePanel : Panel
    {
        private readonly string _displayName;
        private readonly Color _brand;
        private readonly Label? _titleMark;
        private readonly Label _title;
        private readonly Label _status;
        private readonly UsageRingControl _ring;
        private readonly Label _reset;

        public CompactServicePanel(string displayName, Color brand, Font titleFont, string? mark = null, Font? markFont = null)
        {
            _displayName = displayName;
            _brand = brand;
            BackColor = Card;
            // Glass background is painted in OnPaint; double buffer to avoid
            // flicker, matching the normal-mode ServiceCard.
            SetStyle(
                ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.UserPaint
                | ControlStyles.ResizeRedraw,
                true);

            // Optional brand mark (e.g. "✳") drawn in its own glyph-capable font
            // so a serif title face never falls back to a tofu box. _displayName
            // stays plain for status messages.
            var nameX = 14;
            if (!string.IsNullOrEmpty(mark))
            {
                var glyphFont = markFont ?? titleFont;
                _titleMark = new Label
                {
                    Text = mark,
                    AutoSize = true,
                    Font = glyphFont,
                    ForeColor = _brand,
                    BackColor = Color.Transparent,
                    Location = new Point(14, 13)
                };
                var markWidth = TextRenderer.MeasureText(mark, glyphFont, Size.Empty, TextFormatFlags.NoPadding).Width;
                nameX = 14 + markWidth + 6;
            }

            _title = new Label
            {
                Text = displayName,
                AutoSize = true,
                Location = new Point(nameX, 12),
                Font = titleFont,
                ForeColor = PrimaryText,
                BackColor = Color.Transparent
            };

            _status = new Label
            {
                AutoSize = false,
                Size = new Size(118, 18),
                Location = new Point(14, 34),
                ForeColor = MutedText,
                BackColor = Color.Transparent,
                AutoEllipsis = true
            };

            _ring = new UsageRingControl
            {
                Size = new Size(54, 54),
                Location = new Point(140, 10),
                BackColor = Color.Transparent
            };

            _reset = new Label
            {
                AutoSize = false,
                Size = new Size(180, 22),
                Location = new Point(16, 66),
                ForeColor = MutedText,
                BackColor = Color.Transparent,
                AutoEllipsis = true,
                Font = new Font("Segoe UI", 8F)
            };

            if (_titleMark is not null)
            {
                Controls.Add(_titleMark);
            }

            Controls.Add(_title);
            Controls.Add(_status);
            Controls.Add(_ring);
            Controls.Add(_reset);
        }

        public void SetLoading()
        {
            _status.Text = Strings.T("更新中");
            _status.ForeColor = Warning;
            _ring.SetValue(null);
            _reset.Text = Strings.T("リセット時刻不明");
        }

        public void Update(ServiceUsage? current, ServiceUsage? fallback)
        {
            var status = current?.Status ?? ProviderStatus.Unknown;
            _status.Text = ProviderStatusPresenter.BadgeText(status);
            _status.ForeColor = StatusColor(status);

            var window = FindFiveHourWindow(fallback);
            _ring.SetValue(window?.UsedPercent);
            _reset.Text = window is null
                ? Strings.Tf("{0}: リセット時刻不明", _displayName)
                : ResetTimeFormatter.Format(window);
        }

        protected override void OnPaint(PaintEventArgs e)
            => PaintGlassCard(e.Graphics, Width, Height, _brand);
    }

    // ----- MinimumStripPanel (minimum mode) --------------------------------
    // Two stacked one-line status rows ("<icon> Claude | <clock> 5h 38% ...")
    // inside a single rounded card.
    private sealed class MinimumStripPanel : Panel
    {
        private const int RowHeight = 21;
        private const int RowGap = 2;
        // Keep rows clear of the rounded corners so the card border stays crisp.
        private const int SideInset = 8;

        private readonly MinimumServiceRow _claude;
        private readonly MinimumServiceRow _codex;

        // Service visibility is tracked explicitly (not read back via
        // Control.Visible): Visible reflects ancestor visibility too, so it
        // reads false whenever the popup itself is hidden — which is exactly
        // when the refresh timer often runs Update/layout.
        private bool _showClaude = true;
        private bool _showCodex = true;

        public MinimumStripPanel()
        {
            BackColor = Card;
            DoubleBuffered = true;

            _claude = new MinimumServiceRow("Claude", StatusLineGlyphs.Claude, ClaudeBrand);
            _codex = new MinimumServiceRow("Codex", StatusLineGlyphs.Codex, CodexBrand);

            Controls.Add(_claude);
            Controls.Add(_codex);
        }

        // Width the strip wants for its widest visible status line, including
        // the side insets. LayoutMinimum clamps this into the screen.
        public int PreferredContentWidth
        {
            get
            {
                var width = 0;
                if (_showClaude)
                {
                    width = Math.Max(width, _claude.PreferredWidth);
                }

                if (_showCodex)
                {
                    width = Math.Max(width, _codex.PreferredWidth);
                }

                return width + SideInset * 2;
            }
        }

        public void SetLoading()
        {
            _claude.SetLoading();
            _codex.SetLoading();
            AlignSeparators();
        }

        public void Update(ServiceUsage? claude, ServiceUsage? codex, bool showClaude, bool showCodex, DailyCostsView? dailyCosts)
        {
            _showClaude = showClaude;
            _showCodex = showCodex;
            _claude.Visible = showClaude;
            _codex.Visible = showCodex;

            if (showClaude)
            {
                _claude.SetData(
                    FindFiveHourWindow(claude),
                    FindWeeklyWindow(claude),
                    dailyCosts?.Claude);
            }

            if (showCodex)
            {
                _codex.SetData(
                    FindFiveHourWindow(codex),
                    FindWeeklyWindow(codex),
                    dailyCosts?.Codex);
            }

            AlignSeparators();
            LayoutRows();
        }

        // Pins each separator column at the element-wise max of the two rows'
        // natural separator positions so the "|" columns line up vertically.
        // With a single visible row there is nothing to align: its SetData has
        // already reset the row to its natural positions. Claude's third
        // (cost) separator has no Codex counterpart, so its target equals its
        // own natural X and the pin is a no-op there.
        private void AlignSeparators()
        {
            if (!_showClaude || !_showCodex)
            {
                return;
            }

            var claudeXs = _claude.NaturalSeparatorXs;
            var codexXs = _codex.NaturalSeparatorXs;
            var targets = new int[Math.Max(claudeXs.Count, codexXs.Count)];
            for (var i = 0; i < targets.Length; i++)
            {
                var claudeX = i < claudeXs.Count ? claudeXs[i] : 0;
                var codexX = i < codexXs.Count ? codexXs[i] : 0;
                targets[i] = Math.Max(claudeX, codexX);
            }

            _claude.ApplySeparatorTargets(targets);
            _codex.ApplySeparatorTargets(targets);
        }

        protected override void OnResize(EventArgs eventargs)
        {
            base.OnResize(eventargs);
            LayoutRows();
        }

        // Child panel bounds can be reset to their default size when the handle
        // is created (i.e. when the form is first shown), and the strip is not
        // necessarily resized afterwards to re-trigger OnResize. Re-assert the
        // row layout on handle creation and whenever the strip becomes visible
        // so the two rows never collapse onto the same position.
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            LayoutRows();
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (Visible)
            {
                LayoutRows();
            }
        }

        private void LayoutRows()
        {
            var rowWidth = Width - SideInset * 2;

            if (_showClaude && _showCodex)
            {
                var stackHeight = RowHeight * 2 + RowGap;
                var top = Math.Max(0, (Height - stackHeight) / 2);
                _claude.Location = new Point(SideInset, top);
                _claude.Size = new Size(rowWidth, RowHeight);
                _codex.Location = new Point(SideInset, top + RowHeight + RowGap);
                _codex.Size = new Size(rowWidth, RowHeight);
            }
            else if (_showClaude)
            {
                _claude.Location = new Point(SideInset, (Height - RowHeight) / 2);
                _claude.Size = new Size(rowWidth, RowHeight);
            }
            else if (_showCodex)
            {
                _codex.Location = new Point(SideInset, (Height - RowHeight) / 2);
                _codex.Size = new Size(rowWidth, RowHeight);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            // The strip fills the window edge-to-edge and paints SQUARE; the window's
            // rounded corners come from DWM (see StatusForm.OnHandleCreated), so a
            // rounded path here would just float a second border inside the DWM curve.
            base.OnPaint(e);
            using var bg = new SolidBrush(Card);
            e.Graphics.FillRectangle(bg, 0, 0, Width, Height);
            using var pen = new Pen(CardBorder);
            e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        }
    }

    // A single owner-drawn status line, e.g.
    //   <svcIcon> Claude | <clock> 5h 38% <reset> 4h39m 10:50 | <cal> 7d 39% <reset> 2d 6/17 18:00 | <money> ¥46 (daily)
    // Text runs render in the monospaced status-line face, icon runs in the
    // Nerd Font face; when the Nerd Font is not installed the icon runs are
    // skipped entirely (measured nor drawn), leaving a clean text-only line.
    private sealed class MinimumServiceRow : Panel
    {
        // Gap between adjacent runs; separators breathe a little wider.
        private const int RunGap = 4;
        private const int SeparatorGap = 6;

        private const TextFormatFlags RunFlags =
            TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;

        private readonly string _serviceName;
        private readonly string _iconGlyph;
        private readonly Color _brand;

        private IReadOnlyList<MinimumRun> _runs = Array.Empty<MinimumRun>();
        private IReadOnlyList<int>? _separatorTargets;
        private bool _hasData;

        public MinimumServiceRow(string serviceName, string iconGlyph, Color brand)
        {
            _serviceName = serviceName;
            _iconGlyph = iconGlyph;
            _brand = brand;
            BackColor = Card;
            SetStyle(
                ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.UserPaint
                | ControlStyles.ResizeRedraw,
                true);
        }

        // Width this row's full line wants (run widths + gaps + separator
        // pinning), kept in sync with the current runs and targets so
        // LayoutMinimum can size the window.
        public int PreferredWidth { get; private set; }

        // X of each separator with no pinning applied, in run order. The strip
        // takes the element-wise max across rows to build the pin targets.
        public IReadOnlyList<int> NaturalSeparatorXs { get; private set; } = Array.Empty<int>();

        public void SetData(RateLimitWindow? fiveHour, RateLimitWindow? weekly, DailyCost? dailyCost)
        {
            _hasData = true;
            SetRuns(MinimumLineComposer.Compose(_serviceName, _iconGlyph, fiveHour, weekly, dailyCost));
        }

        // No-op once real data has been shown: re-showing the placeholder on
        // every refresh would make the line blink. The "5h n/a | 7d n/a"
        // placeholder appears only before the first snapshot arrives.
        public void SetLoading()
        {
            if (_hasData)
            {
                return;
            }

            SetRuns(MinimumLineComposer.Compose(_serviceName, _iconGlyph, null, null, null));
        }

        // Pins separator k at max(natural X, targets[k]) — the gap before the
        // separator widens — and refreshes PreferredWidth accordingly. Targets
        // beyond this row's separator count are ignored; missing targets leave
        // the remaining separators at their natural positions.
        public void ApplySeparatorTargets(IReadOnlyList<int>? targets)
        {
            _separatorTargets = targets;
            PreferredWidth = LayoutRuns(_runs, targets, naturalSeparatorXs: null, visit: null);
            Invalidate();
        }

        private void SetRuns(IReadOnlyList<MinimumRun> runs)
        {
            _runs = runs;
            // New runs invalidate previously pinned columns; the strip
            // re-applies fresh targets via ApplySeparatorTargets right after.
            _separatorTargets = null;
            var naturals = new List<int>();
            PreferredWidth = LayoutRuns(runs, separatorTargets: null, naturals, visit: null);
            NaturalSeparatorXs = naturals;
            Invalidate();
        }

        // The single layout walk shared by measurement and painting, so the
        // two can never disagree: gap accumulation, separator pinning and run
        // widths are all decided here. Returns the total line width, reports
        // each visible run's resolved X and measured width to the optional
        // visitor, and optionally collects the unpinned separator positions.
        private static int LayoutRuns(
            IReadOnlyList<MinimumRun> runs,
            IReadOnlyList<int>? separatorTargets,
            List<int>? naturalSeparatorXs,
            Action<MinimumRun, int, int>? visit)
        {
            var x = 0;
            var separatorIndex = 0;
            var first = true;
            var previousKind = MinimumRunKind.ServiceIcon;
            foreach (var run in VisibleRuns(runs))
            {
                if (!first)
                {
                    x += GapBefore(previousKind, run.Kind);
                }

                if (run.Kind == MinimumRunKind.Separator)
                {
                    naturalSeparatorXs?.Add(x);
                    if (separatorTargets is not null && separatorIndex < separatorTargets.Count)
                    {
                        x = Math.Max(x, separatorTargets[separatorIndex]);
                    }

                    separatorIndex++;
                }

                var width = TextRenderer.MeasureText(run.Text, FontFor(run), Size.Empty, RunFlags).Width;
                visit?.Invoke(run, x, width);
                x += width;
                previousKind = run.Kind;
                first = false;
            }

            return x;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;

            // All runs share the text font's baseline (icons included), so the
            // line never wobbles when the two faces have different cell heights.
            var textFont = StatusLineFonts.Text;
            var textTop = (Height - textFont.GetHeight(g)) / 2f;
            var baseline = textTop + TextMetrics.AscentPx(g, textFont);

            var clipped = false;
            LayoutRuns(_runs, _separatorTargets, naturalSeparatorXs: null, (run, x, width) =>
            {
                if (clipped || x >= Width)
                {
                    clipped = true;
                    return;
                }

                var font = FontFor(run);
                var runTop = (int)Math.Round(baseline - TextMetrics.AscentPx(g, font));
                var color = ColorFor(run);

                if (x + width <= Width)
                {
                    TextRenderer.DrawText(g, run.Text, font, new Point(x, runTop), color, RunFlags);
                    return;
                }

                // Out of horizontal room: ellipsize a text run into what is
                // left (an icon run is just dropped) and stop drawing.
                if (!IsIconRun(run.Kind))
                {
                    var rect = new Rectangle(x, runTop, Width - x, Height - runTop);
                    TextRenderer.DrawText(g, run.Text, font, rect, color, RunFlags | TextFormatFlags.EndEllipsis);
                }

                clipped = true;
            });
        }

        // Single place that maps run kinds to colors. Percent rides the shared
        // 80/95 severity escalation via UsageTheme.AccentColor (no literals).
        private Color ColorFor(MinimumRun run) => run.Kind switch
        {
            MinimumRunKind.ServiceIcon or MinimumRunKind.ServiceName => _brand,
            MinimumRunKind.Separator => SubtleText,
            MinimumRunKind.SegmentIcon or MinimumRunKind.ResetIcon or MinimumRunKind.ResetText => MutedText,
            MinimumRunKind.WindowLabel => SecondaryText,
            MinimumRunKind.Percent => UsageTheme.AccentColor(run.PercentValue),
            _ => SecondaryText // Cost
        };

        // Icon runs exist only when the Nerd Font face is installed; without it
        // they are excluded from both measurement and drawing.
        private static IEnumerable<MinimumRun> VisibleRuns(IReadOnlyList<MinimumRun> runs)
        {
            foreach (var run in runs)
            {
                if (!StatusLineFonts.IconAvailable && IsIconRun(run.Kind))
                {
                    continue;
                }

                yield return run;
            }
        }

        private static bool IsIconRun(MinimumRunKind kind)
            => kind is MinimumRunKind.ServiceIcon or MinimumRunKind.SegmentIcon or MinimumRunKind.ResetIcon;

        private static Font FontFor(MinimumRun run)
            => IsIconRun(run.Kind) ? StatusLineFonts.Icon! : StatusLineFonts.Text;

        private static int GapBefore(MinimumRunKind previous, MinimumRunKind next)
            => previous == MinimumRunKind.Separator || next == MinimumRunKind.Separator
                ? SeparatorGap
                : RunGap;
    }

    // ----- Drawing primitives ----------------------------------------------
    // UsageBarControl now lives in its own file (UsageBarControl.cs) so the
    // dedicated Copilot window can share it; UsageRingControl stays nested.
    private sealed class UsageRingControl : Control
    {
        private double? _usedPercent;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string? CenterText { get; set; }

        public UsageRingControl()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw
                | ControlStyles.UserPaint
                | ControlStyles.SupportsTransparentBackColor,
                true);
        }

        public void SetValue(double? usedPercent)
        {
            _usedPercent = usedPercent;
            Invalidate();
        }

        // When transparent (the glass compact card), let the parent paint its
        // gradient behind the ring so it doesn't sit on an opaque square.
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
            UsageRingRenderer.Draw(
                e.Graphics,
                ClientRectangle,
                _usedPercent,
                foreColor: PrimaryText,
                emptyColor: TrackEmpty,
                accentColor: UsageAccentColor(_usedPercent),
                backColor: BackColor == Color.Transparent ? Color.Transparent : BackColor,
                centerLabel: CenterText);
        }
    }
}
