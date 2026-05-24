using System.Drawing;
using System.Windows.Forms;
using TokenChecker.Core;

namespace TokenChecker.App;

internal sealed class StatusForm : Form
{
    private static readonly Color Surface = Color.FromArgb(32, 34, 39);
    private static readonly Color Card = Color.FromArgb(43, 46, 53);
    private static readonly Color CardBorder = Color.FromArgb(62, 66, 76);
    private static readonly Color PrimaryText = Color.FromArgb(238, 240, 244);
    private static readonly Color MutedText = Color.FromArgb(166, 172, 184);
    private static readonly Color Good = Color.FromArgb(111, 207, 151);
    private static readonly Color Warning = Color.FromArgb(242, 201, 118);
    private static readonly Color Bad = Color.FromArgb(235, 113, 113);
    private static readonly Color RingEmpty = Color.FromArgb(73, 78, 90);
    private static readonly Color DetailToggle = Color.FromArgb(140, 170, 220);
    private static readonly Color DetailBackground = Color.FromArgb(28, 30, 35);

    private const int FormPadding = 12;
    private const int FormWidth = 372;
    private const int CardHeight = 196;
    private const int CardSpacing = 10;
    private const int DetailExtraHeight = 96;
    private const int FooterHeight = 22;

    private readonly ServiceCard _claudeCard;
    private readonly ServiceCard _codexCard;
    private readonly Label _updatedAt;
    private bool _showClaude = true;
    private bool _showCodex = true;

    public StatusForm()
    {
        Text = "TokenCheckerWin";
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        TopMost = true;
        BackColor = Surface;
        Font = new Font("Segoe UI", 9F);
        StartPosition = FormStartPosition.Manual;

        _claudeCard = new ServiceCard("Claude")
        {
            Location = new Point(FormPadding, FormPadding),
            Size = new Size(FormWidth - FormPadding * 2, CardHeight)
        };
        _codexCard = new ServiceCard("Codex")
        {
            Size = new Size(FormWidth - FormPadding * 2, CardHeight)
        };
        _updatedAt = CreateMutedLabel();
        _updatedAt.AutoSize = false;
        _updatedAt.Size = new Size(FormWidth - FormPadding * 2, FooterHeight);
        _updatedAt.TextAlign = ContentAlignment.MiddleLeft;

        _claudeCard.HeightChanged += (_, _) => RecalculateLayout();
        _codexCard.HeightChanged += (_, _) => RecalculateLayout();

        Controls.Add(_claudeCard);
        Controls.Add(_codexCard);
        Controls.Add(_updatedAt);

        RecalculateLayout();
        SetLoading();
    }

    public void ApplySettings(AppSettings settings)
    {
        _showClaude = settings.IsServiceVisible("Claude");
        _showCodex = settings.IsServiceVisible("Codex");
        _claudeCard.Visible = _showClaude;
        _codexCard.Visible = _showCodex;
        RecalculateLayout();
    }

    public void SetLoading()
    {
        _claudeCard.SetLoading();
        _codexCard.SetLoading();
        _updatedAt.Text = "最終更新: 更新中";
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
        _updatedAt.Text = $"最終更新: {snapshot.CapturedAtUtc.ToLocalTime():HH:mm:ss}";
    }

    private void RecalculateLayout()
    {
        SuspendLayout();
        try
        {
            var y = FormPadding;
            if (_showClaude)
            {
                _claudeCard.Location = new Point(FormPadding, y);
                y += _claudeCard.Height + CardSpacing;
            }

            if (_showCodex)
            {
                _codexCard.Location = new Point(FormPadding, y);
                y += _codexCard.Height + CardSpacing;
            }

            _updatedAt.Location = new Point(FormPadding, y);
            var contentHeight = y + FooterHeight + FormPadding;
            ClientSize = new Size(FormWidth, contentHeight);
        }
        finally
        {
            ResumeLayout(true);
        }
    }

    private static Label CreateMutedLabel()
        => new()
        {
            AutoEllipsis = true,
            AutoSize = true,
            ForeColor = MutedText,
            BackColor = Color.Transparent
        };

    private static Color StatusColor(ProviderStatus status)
        => status switch
        {
            ProviderStatus.Available => Good,
            ProviderStatus.NotInstalled or ProviderStatus.NotLoggedIn or ProviderStatus.Unauthorized or ProviderStatus.RateLimited => Warning,
            ProviderStatus.Error => Bad,
            _ => MutedText
        };

    private static Color UsageAccentColor(double? value)
    {
        if (!UsageRingRenderer.TryClampPercent(value, out var percent))
        {
            return MutedText;
        }

        return percent switch
        {
            >= 95 => Bad,
            >= 80 => Warning,
            _ => Good
        };
    }

    private static string FormatWindowName(RateLimitWindow? window, string fallback)
    {
        var minutes = window?.WindowDurationMins;
        if (minutes is null)
        {
            return fallback;
        }

        return minutes.Value switch
        {
            300 => "5h",
            10080 => "Weekly",
            >= 60 when minutes.Value % 60 == 0 => $"{minutes.Value / 60}h",
            _ => $"{minutes.Value}m"
        };
    }

    private static string FormatReset(RateLimitWindow? window)
    {
        if (window?.ResetAtUtc is null)
        {
            return "n/a";
        }

        var remaining = window.ResetAtUtc.Value - DateTimeOffset.UtcNow;
        if (remaining <= TimeSpan.Zero)
        {
            return "soon";
        }

        if (remaining.TotalDays >= 1)
        {
            return $"{Math.Ceiling(remaining.TotalDays)}d";
        }

        if (remaining.TotalHours >= 1)
        {
            return $"{Math.Ceiling(remaining.TotalHours)}h";
        }

        return $"{Math.Max(1, Math.Ceiling(remaining.TotalMinutes))}m";
    }

    private sealed class ServiceCard : Panel
    {
        public event EventHandler? HeightChanged;

        private readonly string _serviceName;
        private readonly Label _title;
        private readonly Label _badge;
        private readonly Label _message;
        private readonly UsageWindowPanel _primary;
        private readonly UsageWindowPanel _secondary;
        private readonly Label _resetSummary;
        private readonly LinkLabel _detailToggle;
        private readonly TextBox _detailBox;
        private bool _detailExpanded;

        public ServiceCard(string serviceName)
        {
            _serviceName = serviceName;
            BackColor = Card;

            _title = new Label
            {
                Text = serviceName,
                AutoSize = true,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                ForeColor = PrimaryText,
                BackColor = Color.Transparent,
                Location = new Point(12, 10)
            };

            _badge = new Label
            {
                AutoSize = true,
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                BackColor = Color.Transparent,
                ForeColor = MutedText,
                Location = new Point(12, 36),
                Text = "状態不明"
            };

            _message = new Label
            {
                AutoSize = false,
                Size = new Size(220, 32),
                Location = new Point(108, 36),
                ForeColor = MutedText,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true
            };

            _primary = new UsageWindowPanel { Location = new Point(12, 76) };
            _secondary = new UsageWindowPanel { Location = new Point(178, 76) };

            _resetSummary = new Label
            {
                AutoSize = false,
                Size = new Size(220, 18),
                Location = new Point(12, 152),
                ForeColor = MutedText,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft
            };

            _detailToggle = new LinkLabel
            {
                Text = "詳細を表示",
                AutoSize = true,
                BackColor = Color.Transparent,
                LinkColor = DetailToggle,
                ActiveLinkColor = PrimaryText,
                VisitedLinkColor = DetailToggle,
                LinkBehavior = LinkBehavior.HoverUnderline,
                Location = new Point(260, 152),
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
                ForeColor = MutedText,
                Location = new Point(12, 178),
                Size = new Size(324, DetailExtraHeight - 16),
                Visible = false,
                TabStop = false,
                WordWrap = true,
                Font = new Font("Consolas", 8.5F)
            };

            Controls.Add(_title);
            Controls.Add(_badge);
            Controls.Add(_message);
            Controls.Add(_primary);
            Controls.Add(_secondary);
            Controls.Add(_resetSummary);
            Controls.Add(_detailToggle);
            Controls.Add(_detailBox);
        }

        public void SetLoading()
        {
            _badge.Text = "更新中";
            _badge.ForeColor = Warning;
            _message.Text = "";
            _primary.SetEmpty("5h");
            _secondary.SetEmpty("Weekly");
            _resetSummary.Text = "Reset: n/a";
            UpdateDiagnostics(string.Empty);
        }

        public void Update(ServiceUsage? current, ServiceUsage? fallback)
        {
            var status = current?.Status ?? ProviderStatus.Unknown;
            _badge.Text = ProviderStatusPresenter.BadgeText(status);
            _badge.ForeColor = StatusColor(status);

            var hasFallbackWindows = fallback is { Windows.Count: > 0 };
            _message.Text = ProviderStatusPresenter.FriendlyMessage(_serviceName, status, hasFallbackWindows);

            UpdateWindows(fallback);

            var debug = ProviderStatusPresenter.BuildDebugSummary(_serviceName, current, fallback);
            var masked = ProviderStatusPresenter.SafeDiagnostics(current?.Message);
            var combined = string.IsNullOrEmpty(masked) ? debug : $"{debug}{Environment.NewLine}{masked}";
            UpdateDiagnostics(combined);
        }

        private void UpdateWindows(ServiceUsage? service)
        {
            var windows = service?.Windows
                .Where(window => window.WindowDurationMins is not null)
                .OrderBy(window => window.WindowDurationMins)
                .ToArray() ?? Array.Empty<RateLimitWindow>();

            var first = windows.ElementAtOrDefault(0);
            var second = windows.ElementAtOrDefault(1);

            _primary.SetWindow(first, "5h");
            _secondary.SetWindow(second, "Weekly");
            _resetSummary.Text = $"Reset: {FormatReset(first)} / {FormatReset(second)}";
        }

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
            var newHeight = expanded ? CardHeight + DetailExtraHeight : CardHeight;
            if (Height != newHeight)
            {
                Height = newHeight;
                HeightChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using var pen = new Pen(CardBorder);
            e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        }
    }

    private sealed class UsageWindowPanel : Panel
    {
        private readonly Label _name;
        private readonly UsageRingControl _ring;
        private readonly Label _reset;

        public UsageWindowPanel()
        {
            Size = new Size(150, 68);
            BackColor = Color.FromArgb(37, 40, 47);

            _name = new Label
            {
                AutoSize = false,
                Size = new Size(70, 18),
                Location = new Point(8, 8),
                ForeColor = MutedText,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold)
            };

            _ring = new UsageRingControl
            {
                Size = new Size(56, 56),
                Location = new Point(86, 6)
            };
            _ring.BackColor = BackColor;

            _reset = new Label
            {
                AutoSize = false,
                Size = new Size(70, 18),
                Location = new Point(8, 38),
                ForeColor = MutedText,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft
            };

            Controls.Add(_name);
            Controls.Add(_ring);
            Controls.Add(_reset);
        }

        public void SetEmpty(string name)
        {
            _name.Text = name;
            _ring.SetValue(null);
            _reset.Text = "";
        }

        public void SetWindow(RateLimitWindow? window, string fallbackName)
        {
            _name.Text = FormatWindowName(window, fallbackName);
            _ring.SetValue(window?.UsedPercent);
            _reset.Text = FormatReset(window);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using var pen = new Pen(CardBorder);
            e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        }
    }

    private sealed class UsageRingControl : Control
    {
        private double? _usedPercent;

        public UsageRingControl()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw
                | ControlStyles.UserPaint,
                true);
        }

        public void SetValue(double? usedPercent)
        {
            _usedPercent = usedPercent;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            UsageRingRenderer.Draw(
                e.Graphics,
                ClientRectangle,
                _usedPercent,
                foreColor: PrimaryText,
                emptyColor: RingEmpty,
                accentColor: UsageAccentColor(_usedPercent),
                backColor: BackColor);
        }
    }
}
