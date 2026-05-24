using System.Drawing;
using System.Drawing.Drawing2D;
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
    private static readonly Color RingEmpty = Color.FromArgb(73, 78, 90);

    private readonly Label _claudeBadge = CreateBadgeLabel();
    private readonly Label _claudeMessage = CreateMutedLabel();
    private readonly Label _claudeUsage = CreateMutedLabel();
    private readonly Label _codexBadge = CreateBadgeLabel();
    private readonly Label _codexMessage = CreateMutedLabel();
    private readonly UsageWindowPanel _primaryWindow = new();
    private readonly UsageWindowPanel _secondaryWindow = new();
    private readonly Label _resetSummary = CreateMutedLabel();
    private readonly Label _updatedAt = CreateMutedLabel();
    private readonly TableLayoutPanel _root;
    private readonly Control _claudeCard;
    private readonly Control _codexCard;

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

        _root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            RowCount = 3,
            ColumnCount = 1,
            BackColor = Surface
        };
        _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 84));
        _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 190));
        _root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _claudeCard = CreateClaudeCard();
        _codexCard = CreateCodexCard();
        _root.Controls.Add(_claudeCard, 0, 0);
        _root.Controls.Add(_codexCard, 0, 1);
        _root.Controls.Add(_updatedAt, 0, 2);

        Controls.Add(_root);
        SetLoading();
    }

    public void ApplySettings(AppSettings settings)
    {
        var showClaude = settings.IsServiceVisible("Claude");
        var showCodex = settings.IsServiceVisible("Codex");

        _claudeCard.Visible = showClaude;
        _codexCard.Visible = showCodex;
        _root.RowStyles[0].Height = showClaude ? 84 : 0;
        _root.RowStyles[1].Height = showCodex ? 190 : 0;

        Height = (showClaude, showCodex) switch
        {
            (true, true) => 332,
            (true, false) => 142,
            (false, true) => 238,
            _ => 84
        };
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
        var fallbackClaude = claude?.Status == ProviderStatus.Available
            ? claude
            : lastSuccessfulSnapshot?.Services.FirstOrDefault(service => service.ServiceName == "Claude" && service.Status == ProviderStatus.Available);
        var fallbackCodex = codex?.Status == ProviderStatus.Available
            ? codex
            : lastSuccessfulSnapshot?.Services.FirstOrDefault(service => service.ServiceName == "Codex" && service.Status == ProviderStatus.Available);

        SetService(_claudeBadge, _claudeMessage, claude);
        SetClaudeUsage(fallbackClaude);
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
        _claudeMessage.Size = new Size(214, 20);
        _claudeUsage.Location = new Point(112, 54);
        _claudeUsage.Size = new Size(214, 18);

        card.Controls.Add(title);
        card.Controls.Add(_claudeBadge);
        card.Controls.Add(_claudeMessage);
        card.Controls.Add(_claudeUsage);
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

    private void SetClaudeUsage(ServiceUsage? claude)
    {
        if (claude?.Status != ProviderStatus.Available || claude.Windows.Count == 0)
        {
            _claudeUsage.Text = "";
            return;
        }

        var windows = claude.Windows
            .Where(window => window.UsedPercent is not null)
            .OrderBy(window => window.WindowDurationMins)
            .Take(2)
            .Select(window => $"{FormatWindowName(window, "Usage")} {FormatPercent(window)}");

        _claudeUsage.Text = string.Join(" / ", windows);
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
            ProviderStatus.NotInstalled or ProviderStatus.NotLoggedIn or ProviderStatus.Unauthorized or ProviderStatus.RateLimited => Warning,
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
        => TryClampPercent(window?.UsedPercent, out var percent)
            ? $"{percent:0.#}%"
            : "n/a";

    private static bool TryClampPercent(double? value, out double percent)
    {
        if (value is null || double.IsNaN(value.Value) || double.IsInfinity(value.Value))
        {
            percent = 0;
            return false;
        }

        percent = Math.Clamp(value.Value, 0, 100);
        return true;
    }

    private static Color UsageColor(double? value)
    {
        if (!TryClampPercent(value, out var percent))
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
                ForeColor: PrimaryText,
                EmptyColor: RingEmpty,
                AccentColor: UsageColor(_usedPercent),
                BackColor);
        }
    }

    private static class UsageRingRenderer
    {
        public static void Draw(
            Graphics graphics,
            Rectangle bounds,
            double? usedPercent,
            Color ForeColor,
            Color EmptyColor,
            Color AccentColor,
            Color BackColor)
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(BackColor);

            var size = Math.Min(bounds.Width, bounds.Height);
            var stroke = Math.Max(4f, size * 0.11f);
            var inset = stroke / 2f + 2f;
            var rect = new RectangleF(
                bounds.Left + inset,
                bounds.Top + inset,
                size - inset * 2f,
                size - inset * 2f);

            using var backgroundPen = new Pen(EmptyColor, stroke)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };
            graphics.DrawArc(backgroundPen, rect, 0, 360);

            if (TryClampPercent(usedPercent, out var percent))
            {
                using var accentPen = new Pen(AccentColor, stroke)
                {
                    StartCap = LineCap.Round,
                    EndCap = LineCap.Round
                };
                graphics.DrawArc(accentPen, rect, -90, (float)(percent / 100d * 360d));
            }

            var text = TryClampPercent(usedPercent, out var labelPercent)
                ? $"{Math.Round(labelPercent):0}%"
                : "n/a";

            using var font = new Font("Segoe UI", text == "n/a" ? 8F : 9F, FontStyle.Bold);
            using var brush = new SolidBrush(TryClampPercent(usedPercent, out _) ? ForeColor : EmptyColor);
            using var format = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            };

            graphics.DrawString(text, font, brush, bounds, format);
        }
    }
}
