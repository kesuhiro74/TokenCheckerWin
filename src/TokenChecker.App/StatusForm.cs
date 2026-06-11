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
    // Wide enough that a serif service name ("Claude") isn't clipped.
    private const int MinimumFormWidth = 232;
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
    }

    public void UpdateSnapshot(UsageSnapshot snapshot, UsageSnapshot? lastSuccessfulSnapshot)
    {
        var claude = snapshot.Services.FirstOrDefault(service => service.ServiceName == "Claude");
        var codex = snapshot.Services.FirstOrDefault(service => service.ServiceName == "Codex");
        var fallbackClaude = claude?.Status == ProviderStatus.Available
            ? claude
            : lastSuccessfulSnapshot?.Services.FirstOrDefault(service => service.ServiceName == "Claude" && service.Status == ProviderStatus.Available);
        var fallbackCodex = codex?.Status == ProviderStatus.Available
            ? codex
            : lastSuccessfulSnapshot?.Services.FirstOrDefault(service => service.ServiceName == "Codex" && service.Status == ProviderStatus.Available);

        _claudeCard.Update(claude, fallbackClaude);
        _codexCard.Update(codex, fallbackCodex);
        _compactClaude.Update(claude, fallbackClaude);
        _compactCodex.Update(codex, fallbackCodex);
        _minimumPanel.Update(fallbackClaude, fallbackCodex, _showClaude, _showCodex);
        _updatedAt.Text = Strings.Tf("最終更新: {0}", snapshot.CapturedAtUtc.ToLocalTime().ToString("HH:mm:ss"));
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
        _minimumPanel.Location = new Point(0, 0);
        _minimumPanel.Size = new Size(MinimumFormWidth, MinimumPanelHeight);
        ClientSize = new Size(MinimumFormWidth, MinimumPanelHeight);
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

    // Like UsageAccentColor, but keeps the service brand color in the normal
    // range so the minimum strip carries each service's identity (blue/purple)
    // while still escalating to amber/red as usage climbs.
    private static Color BrandUsageColor(Color brand, double? value) => UsageTheme.BrandUsageColor(brand, value);

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
        private readonly Label _shortPercent;
        private readonly UsageBarControl _shortBar;
        private readonly Label _shortReset;
        private readonly Label _weeklyLabel;
        private readonly Label _weeklyPercent;
        private readonly UsageBarControl _weeklyBar;
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

            _shortWindowLabel = new Label
            {
                Text = Strings.T("5時間"),
                AutoSize = false,
                Size = new Size(140, 22),
                Location = new Point(14, 46),
                ForeColor = SecondaryText,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI", 9.5F)
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

            _shortReset = new Label
            {
                AutoSize = false,
                Size = new Size(340, 18),
                Location = new Point(14, 92),
                ForeColor = MutedText,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI", 8.5F),
                Text = "—"
            };

            _weeklyLabel = new Label
            {
                Text = Strings.T("週次"),
                AutoSize = false,
                Size = new Size(120, 22),
                Location = new Point(14, 122),
                ForeColor = SecondaryText,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI", 9.5F)
            };

            _weeklyPercent = new Label
            {
                AutoSize = false,
                Size = new Size(100, 22),
                Location = new Point(254, 122),
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
                Location = new Point(14, 148),
                Size = new Size(340, 5),
                BackColor = Color.Transparent,
                UseGradient = true
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
            Controls.Add(_shortPercent);
            Controls.Add(_shortBar);
            Controls.Add(_shortReset);
            Controls.Add(_weeklyLabel);
            Controls.Add(_weeklyPercent);
            Controls.Add(_weeklyBar);
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
            _shortReset.Text = Strings.T("更新中");
            _weeklyPercent.Text = "—";
            _weeklyPercent.ForeColor = MutedText;
            _weeklyBar.SetValue(null);
            UpdateDiagnostics(string.Empty);
        }

        public void Update(ServiceUsage? current, ServiceUsage? fallback)
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
            _shortReset.Text = ResetTimeFormatter.Format(shortWindow);

            _weeklyPercent.Text = FormatPercent(weekly);
            _weeklyPercent.ForeColor = UsageAccentColor(weekly?.UsedPercent);
            _weeklyBar.AccentColor = UsageAccentColor(weekly?.UsedPercent);
            _weeklyBar.SetValue(weekly?.UsedPercent);

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
    // Two stacked rows ("● Claude ▰▰▰▱▱ 45%") inside a single rounded card.
    private sealed class MinimumStripPanel : Panel
    {
        private const int RowHeight = 21;
        private const int RowGap = 2;
        // Keep rows clear of the rounded corners so the card border stays crisp.
        private const int SideInset = 8;

        private readonly MinimumServiceRow _claude;
        private readonly MinimumServiceRow _codex;

        public MinimumStripPanel()
        {
            BackColor = Card;
            DoubleBuffered = true;

            _claude = new MinimumServiceRow("Claude", ClaudeBrand,
                CreateTitleFont(ClaudeTitleFontFamily, 9.5F, ClaudeTitleFontStyle));
            _codex = new MinimumServiceRow("Codex", CodexBrand,
                CreateTitleFont(CodexTitleFontFamily, 9.5F, CodexTitleFontStyle));

            Controls.Add(_claude);
            Controls.Add(_codex);
        }

        public void SetLoading()
        {
            _claude.SetLoading();
            _codex.SetLoading();
        }

        public void Update(ServiceUsage? claude, ServiceUsage? codex, bool showClaude, bool showCodex)
        {
            _claude.Visible = showClaude;
            _codex.Visible = showCodex;

            if (showClaude)
            {
                _claude.SetWindow(FindFiveHourWindow(claude));
            }

            if (showCodex)
            {
                _codex.SetWindow(FindFiveHourWindow(codex));
            }

            LayoutRows();
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

            if (_claude.Visible && _codex.Visible)
            {
                var stackHeight = RowHeight * 2 + RowGap;
                var top = Math.Max(0, (Height - stackHeight) / 2);
                _claude.Location = new Point(SideInset, top);
                _claude.Size = new Size(rowWidth, RowHeight);
                _codex.Location = new Point(SideInset, top + RowHeight + RowGap);
                _codex.Size = new Size(rowWidth, RowHeight);
            }
            else if (_claude.Visible)
            {
                _claude.Location = new Point(SideInset, (Height - RowHeight) / 2);
                _claude.Size = new Size(rowWidth, RowHeight);
            }
            else if (_codex.Visible)
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

    // A single linear row: brand dot + service name + usage bar + percent.
    private sealed class MinimumServiceRow : Panel
    {
        private const int DotSize = 8;
        private const int Gap = 7;
        // Wide enough for a serif name (Georgia "Claude") at this size.
        private const int LabelWidth = 66;
        private const int PercentWidth = 42;
        private const int BarHeight = 6;

        private readonly Color _brand;
        private readonly Label _name;
        private readonly UsageBarControl _bar;
        private readonly Label _percent;
        private bool _hasValue;

        public MinimumServiceRow(string displayName, Color brand, Font nameFont)
        {
            _brand = brand;
            BackColor = Card;
            DoubleBuffered = true;

            _name = new Label
            {
                Text = displayName,
                AutoSize = false,
                ForeColor = PrimaryText,
                BackColor = Color.Transparent,
                Font = nameFont,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true
            };

            _bar = new UsageBarControl
            {
                BackColor = Card,
                AccentColor = brand
            };

            _percent = new Label
            {
                Text = "—",
                AutoSize = false,
                ForeColor = MutedText,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleRight
            };

            Controls.Add(_name);
            Controls.Add(_bar);
            Controls.Add(_percent);
        }

        public void SetLoading()
        {
            _hasValue = false;
            _bar.SetValue(null);
            _percent.Text = "—";
            _percent.ForeColor = MutedText;
            Invalidate();
            LayoutChildren();
        }

        public void SetWindow(RateLimitWindow? window)
        {
            var value = window?.UsedPercent;
            if (UsageRingRenderer.TryClampPercent(value, out var pct))
            {
                _hasValue = true;
                _bar.AccentColor = BrandUsageColor(_brand, value);
                _bar.SetValue(value);
                _percent.Text = $"{Math.Round(pct):0}%";
                _percent.ForeColor = PrimaryText;
            }
            else
            {
                _hasValue = false;
                _bar.SetValue(null);
                _percent.Text = "n/a";
                _percent.ForeColor = MutedText;
            }

            Invalidate();
            LayoutChildren();
        }

        protected override void OnResize(EventArgs eventargs)
        {
            base.OnResize(eventargs);
            LayoutChildren();
        }

        private void LayoutChildren()
        {
            var nameX = DotSize + Gap;
            _name.Location = new Point(nameX, 0);
            _name.Size = new Size(LabelWidth, Height);

            var barX = nameX + LabelWidth + Gap;
            var percentX = Width - PercentWidth;
            var barWidth = Math.Max(16, percentX - Gap - barX);
            _bar.Location = new Point(barX, (Height - BarHeight) / 2);
            _bar.Size = new Size(barWidth, BarHeight);

            _percent.Location = new Point(percentX, 0);
            _percent.Size = new Size(PercentWidth, Height);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var dotColor = _hasValue ? _brand : MutedText;
            var dotY = (Height - DotSize) / 2f;
            using var brush = new SolidBrush(dotColor);
            e.Graphics.FillEllipse(brush, 0, dotY, DotSize, DotSize);
        }
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
