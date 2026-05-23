using System.Drawing;
using System.Text.RegularExpressions;
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

    private readonly Label _claudeBadge = CreateBadgeLabel();
    private readonly Label _claudeMessage = CreateMutedLabel();
    private readonly Label _codexBadge = CreateBadgeLabel();
    private readonly Label _codexMessage = CreateMutedLabel();
    private readonly UsageWindowPanel _primaryWindow = new();
    private readonly UsageWindowPanel _secondaryWindow = new();
    private readonly Label _resetSummary = CreateMutedLabel();
    private readonly Label _updatedAt = CreateMutedLabel();

    public StatusForm()
    {
        Text = "TokenCheckerWin";
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        TopMost = true;
        Size = new Size(372, 332);
        BackColor = Surface;
        Font = new Font("Segoe UI", 9F);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            RowCount = 3,
            ColumnCount = 1,
            BackColor = Surface
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 84));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 190));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        root.Controls.Add(CreateClaudeCard(), 0, 0);
        root.Controls.Add(CreateCodexCard(), 0, 1);
        root.Controls.Add(_updatedAt, 0, 2);

        Controls.Add(root);
        SetLoading();
    }

    public void SetLoading()
    {
        SetBadge(_claudeBadge, "更新中", Warning);
        _claudeMessage.Text = "";
        SetBadge(_codexBadge, "更新中", Warning);
        _codexMessage.Text = "";
        _primaryWindow.SetEmpty("5h");
        _secondaryWindow.SetEmpty("Weekly");
        _resetSummary.Text = "Reset: n/a";
        _updatedAt.Text = "最終更新: 更新中";
    }

    public void UpdateSnapshot(UsageSnapshot snapshot, UsageSnapshot? lastSuccessfulSnapshot)
    {
        var claude = snapshot.Services.FirstOrDefault(service => service.ServiceName == "Claude");
        var codex = snapshot.Services.FirstOrDefault(service => service.ServiceName == "Codex");
        var fallbackCodex = codex?.Status == ProviderStatus.Available
            ? codex
            : lastSuccessfulSnapshot?.Services.FirstOrDefault(service => service.ServiceName == "Codex" && service.Status == ProviderStatus.Available);

        SetService(_claudeBadge, _claudeMessage, claude);
        SetService(_codexBadge, _codexMessage, codex);
        UpdateCodexWindows(fallbackCodex);
        _updatedAt.Text = $"最終更新: {snapshot.CapturedAtUtc.ToLocalTime():HH:mm:ss}";
    }

    private Control CreateClaudeCard()
    {
        var card = new CardPanel();
        var title = CreateTitleLabel("Claude");
        title.Location = new Point(12, 10);
        _claudeBadge.Location = new Point(12, 34);
        _claudeMessage.Location = new Point(112, 35);
        _claudeMessage.Size = new Size(214, 32);

        card.Controls.Add(title);
        card.Controls.Add(_claudeBadge);
        card.Controls.Add(_claudeMessage);
        return card;
    }

    private Control CreateCodexCard()
    {
        var card = new CardPanel();
        var title = CreateTitleLabel("Codex");
        title.Location = new Point(12, 10);
        _codexBadge.Location = new Point(12, 34);
        _codexMessage.Location = new Point(112, 35);
        _codexMessage.Size = new Size(214, 32);

        _primaryWindow.Location = new Point(12, 76);
        _secondaryWindow.Location = new Point(174, 76);
        _resetSummary.Location = new Point(12, 154);
        _resetSummary.Size = new Size(316, 24);

        card.Controls.Add(title);
        card.Controls.Add(_codexBadge);
        card.Controls.Add(_codexMessage);
        card.Controls.Add(_primaryWindow);
        card.Controls.Add(_secondaryWindow);
        card.Controls.Add(_resetSummary);
        return card;
    }

    private void UpdateCodexWindows(ServiceUsage? codex)
    {
        var windows = codex?.Windows
            .Where(window => window.WindowDurationMins is not null)
            .OrderBy(window => window.WindowDurationMins)
            .ToArray() ?? Array.Empty<RateLimitWindow>();

        var first = windows.ElementAtOrDefault(0);
        var second = windows.ElementAtOrDefault(1);

        _primaryWindow.SetWindow(first, "5h");
        _secondaryWindow.SetWindow(second, "Weekly");
        _resetSummary.Text = $"Reset: {FormatReset(first)} / {FormatReset(second)}";
    }

    private static void SetService(Label badge, Label message, ServiceUsage? service)
    {
        var status = service?.Status ?? ProviderStatus.Unknown;
        SetBadge(badge, status.ToString(), StatusColor(status));
        message.Text = SafeMessage(service?.Message);
    }

    private static void SetBadge(Label label, string text, Color color)
    {
        label.Text = text;
        label.ForeColor = color;
    }

    private static Color StatusColor(ProviderStatus status)
        => status switch
        {
            ProviderStatus.Available => Good,
            ProviderStatus.NotInstalled or ProviderStatus.NotLoggedIn => Warning,
            ProviderStatus.Error => Bad,
            _ => MutedText
        };

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

    private static string FormatPercent(RateLimitWindow? window)
        => window?.UsedPercent is null
            ? "n/a"
            : $"{window.UsedPercent.Value:0.#}%";

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

    private static Label CreateTitleLabel(string text)
        => new()
        {
            Text = text,
            AutoSize = true,
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
            ForeColor = PrimaryText,
            BackColor = Color.Transparent
        };

    private static Label CreateBadgeLabel()
        => new()
        {
            AutoSize = true,
            Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
            BackColor = Color.Transparent
        };

    private static Label CreateMutedLabel()
        => new()
        {
            AutoEllipsis = true,
            ForeColor = MutedText,
            BackColor = Color.Transparent
        };

    private static string SafeMessage(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return "";
        }

        var masked = Regex.Replace(message, @"[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}", "<email>", RegexOptions.IgnoreCase);
        masked = Regex.Replace(masked, @"[A-Za-z]:\\(?:[^\\\s]+\\)*[^\\\s]*", "<path>");
        masked = Regex.Replace(masked, @"/(?:[^/\s]+/)+[^/\s]*", "<path>");
        masked = Regex.Replace(masked, @"(?i)(token|secret|key|authorization|bearer)\s*[:=]\s*\S+", "$1=<redacted>");
        masked = Regex.Replace(masked, @"\b[A-Za-z0-9_-]{32,}\b", "<redacted>");

        return masked.Length <= 120 ? masked : masked[..120];
    }

    private sealed class CardPanel : Panel
    {
        public CardPanel()
        {
            Dock = DockStyle.Fill;
            Margin = new Padding(0, 0, 0, 10);
            Padding = new Padding(12);
            BackColor = Card;
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
        private readonly Label _percent = new()
        {
            AutoSize = true,
            Font = new Font("Segoe UI", 18F, FontStyle.Bold),
            ForeColor = PrimaryText,
            BackColor = Color.Transparent
        };
        private readonly Label _reset = CreateMutedLabel();

        public UsageWindowPanel()
        {
            Size = new Size(150, 68);
            BackColor = Color.FromArgb(37, 40, 47);
            _name.Location = new Point(8, 6);
            _name.Size = new Size(130, 18);
            _percent.Location = new Point(8, 22);
            _reset.Location = new Point(92, 42);
            _reset.Size = new Size(50, 18);

            Controls.Add(_name);
            Controls.Add(_percent);
            Controls.Add(_reset);
        }

        public void SetEmpty(string name)
        {
            _name.Text = name;
            _percent.Text = "n/a";
            _reset.Text = "";
        }

        public void SetWindow(RateLimitWindow? window, string fallbackName)
        {
            _name.Text = FormatWindowName(window, fallbackName);
            _percent.Text = FormatPercent(window);
            _reset.Text = FormatReset(window);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using var pen = new Pen(CardBorder);
            e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        }
    }
}
