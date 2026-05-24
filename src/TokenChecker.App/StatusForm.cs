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

    private const int CardHeight = 200;
    private const int CardSpacing = 10;
    private const int DetailExtraHeight = 96;

    private readonly ServiceCard _claudeCard;
    private readonly ServiceCard _codexCard;
    private readonly Label _updatedAt = CreateMutedLabel();
    private readonly TableLayoutPanel _root;

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

        _claudeCard = new ServiceCard("Claude");
        _codexCard = new ServiceCard("Codex");
        _claudeCard.DetailToggled += (_, _) => RecalculateLayout();
        _codexCard.DetailToggled += (_, _) => RecalculateLayout();

        _root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            RowCount = 3,
            ColumnCount = 1,
            BackColor = Surface
        };
        _root.RowStyles.Add(new RowStyle(SizeType.Absolute, CardHeight));
        _root.RowStyles.Add(new RowStyle(SizeType.Absolute, CardHeight));
        _root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _root.Controls.Add(_claudeCard, 0, 0);
        _root.Controls.Add(_codexCard, 0, 1);
        _root.Controls.Add(_updatedAt, 0, 2);

        Controls.Add(_root);
        Size = new Size(372, ComputeHeight(true, true));
        SetLoading();
    }

    public void ApplySettings(AppSettings settings)
    {
        var showClaude = settings.IsServiceVisible("Claude");
        var showCodex = settings.IsServiceVisible("Codex");
        _claudeCard.Visible = showClaude;
        _codexCard.Visible = showCodex;
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
        var showClaude = _claudeCard.Visible;
        var showCodex = _codexCard.Visible;
        _root.RowStyles[0].Height = showClaude ? _claudeCard.PreferredHeight : 0;
        _root.RowStyles[1].Height = showCodex ? _codexCard.PreferredHeight : 0;
        Height = ComputeHeight(showClaude, showCodex);
    }

    private int ComputeHeight(bool showClaude, bool showCodex)
    {
        const int Chrome = 84;
        var height = Chrome;
        if (showClaude)
        {
            height += _claudeCard.PreferredHeight + CardSpacing;
        }
        if (showCodex)
        {
            height += _codexCard.PreferredHeight + CardSpacing;
        }
        return height;
    }

    private static Label CreateMutedLabel()
        => new()
        {
            AutoEllipsis = true,
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
        public event EventHandler? DetailToggled;

        private readonly string _serviceName;
        private readonly Label _title;
        private readonly Label _badge;
        private readonly Label _message;
        private readonly UsageWindowPanel _primary;
        private readonly UsageWindowPanel _secondary;
        private readonly Label _resetSummary;
        private readonly LinkLabel _detailToggle;
        private readonly TextBox _detailBox;
        private string _diagnostics = string.Empty;
        private bool _detailExpanded;

        public ServiceCard(string serviceName)
        {
            _serviceName = serviceName;
            Dock = DockStyle.Fill;
            Margin = new Padding(0, 0, 0, CardSpacing);
            Padding = new Padding(12);
            BackColor = Card;

            _title = new Label
            {
                Text = serviceName,
                AutoSize = true,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = PrimaryText,
                BackColor = Color.Transparent,
                Location = new Point(12, 10)
            };

            _badge = new Label
            {
                AutoSize = true,
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                BackColor = Color.Transparent,
                Location = new Point(12, 34)
            };

            _message = CreateMutedLabel();
            _message.Location = new Point(112, 35);
            _message.Size = new Size(214, 32);

            _primary = new UsageWindowPanel { Location = new Point(12, 76) };
            _secondary = new UsageWindowPanel { Location = new Point(174, 76) };

            _resetSummary = CreateMutedLabel();
            _resetSummary.Location = new Point(12, 154);
            _resetSummary.Size = new Size(240, 18);

            _detailToggle = new LinkLabel
            {
                Text = "詳細を表示",
                AutoSize = true,
                BackColor = Color.Transparent,
                LinkColor = DetailToggle,
                ActiveLinkColor = PrimaryText,
                VisitedLinkColor = DetailToggle,
                LinkBehavior = LinkBehavior.HoverUnderline,
                Location = new Point(258, 154),
                Font = new Font("Segoe UI", 8.5F)
            };
            _detailToggle.LinkClicked += (_, _) => ToggleDetail();

            _detailBox = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.FromArgb(28, 30, 35),
                ForeColor = MutedText,
                Location = new Point(12, 178),
                Size = new Size(316, 80),
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

        public int PreferredHeight => _detailExpanded ? CardHeight + DetailExtraHeight : CardHeight;

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
            UpdateDiagnostics(ProviderStatusPresenter.SafeDiagnostics(current?.Message));
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
            _diagnostics = diagnostics;
            var available = !string.IsNullOrWhiteSpace(diagnostics);
            _detailToggle.Visible = available;
            if (!available)
            {
                if (_detailExpanded)
                {
                    _detailExpanded = false;
                    _detailBox.Visible = false;
                    DetailToggled?.Invoke(this, EventArgs.Empty);
                }
                _detailBox.Text = string.Empty;
                _detailToggle.Text = "詳細を表示";
                return;
            }

            _detailBox.Text = diagnostics;
            _detailToggle.Text = _detailExpanded ? "詳細を隠す" : "詳細を表示";
        }

        private void ToggleDetail()
        {
            _detailExpanded = !_detailExpanded;
            _detailBox.Visible = _detailExpanded;
            _detailToggle.Text = _detailExpanded ? "詳細を隠す" : "詳細を表示";
            DetailToggled?.Invoke(this, EventArgs.Empty);
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
        private readonly Label _name = CreateMutedLabel();
        private readonly UsageRingControl _ring = new();
        private readonly Label _reset = CreateMutedLabel();

        public UsageWindowPanel()
        {
            Size = new Size(150, 68);
            BackColor = Color.FromArgb(37, 40, 47);
            _ring.BackColor = BackColor;
            _name.Location = new Point(8, 6);
            _name.Size = new Size(68, 18);
            _ring.Location = new Point(82, 6);
            _reset.Location = new Point(8, 36);
            _reset.Size = new Size(68, 18);

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
            Size = new Size(58, 58);
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
