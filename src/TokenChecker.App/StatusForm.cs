using System.Drawing;
using System.Windows.Forms;
using TokenChecker.Core;

namespace TokenChecker.App;

internal sealed class StatusForm : Form
{
    private readonly Label _claudeStatus = CreateValueLabel();
    private readonly Label _claudeMessage = CreateMutedLabel();
    private readonly Label _codexStatus = CreateValueLabel();
    private readonly Label _codexMessage = CreateMutedLabel();
    private readonly Label _codexShortUsage = CreateValueLabel();
    private readonly Label _codexWeeklyUsage = CreateValueLabel();
    private readonly Label _codexShortReset = CreateMutedLabel();
    private readonly Label _codexWeeklyReset = CreateMutedLabel();
    private readonly Label _updatedAt = CreateMutedLabel();

    public StatusForm()
    {
        Text = "TokenCheckerWin";
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        TopMost = true;
        Size = new Size(360, 310);
        BackColor = Color.FromArgb(248, 249, 251);
        Font = new Font("Segoe UI", 9F);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            RowCount = 4,
            ColumnCount = 1
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 78));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 136));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        root.Controls.Add(CreateCard("Claude", _claudeStatus, _claudeMessage), 0, 0);
        root.Controls.Add(CreateCodexCard(), 0, 1);
        root.Controls.Add(_updatedAt, 0, 2);

        Controls.Add(root);
        SetLoading();
    }

    public void SetLoading()
    {
        _claudeStatus.Text = "更新中";
        _claudeMessage.Text = "";
        _codexStatus.Text = "更新中";
        _codexMessage.Text = "";
        _codexShortUsage.Text = "300分: n/a";
        _codexWeeklyUsage.Text = "10080分: n/a";
        _codexShortReset.Text = "Reset: n/a";
        _codexWeeklyReset.Text = "Reset: n/a";
        _updatedAt.Text = "最終更新: 更新中";
    }

    public void UpdateSnapshot(UsageSnapshot snapshot)
    {
        var claude = snapshot.Services.FirstOrDefault(service => service.ServiceName == "Claude");
        var codex = snapshot.Services.FirstOrDefault(service => service.ServiceName == "Codex");

        _claudeStatus.Text = claude?.Status.ToString() ?? "Unknown";
        _claudeMessage.Text = SafeMessage(claude?.Message);

        _codexStatus.Text = codex?.Status.ToString() ?? "Unknown";
        _codexMessage.Text = SafeMessage(codex?.Message);

        var shortWindow = codex is null ? null : FindWindow(codex, 300);
        var weeklyWindow = codex is null ? null : FindWindow(codex, 10080);

        _codexShortUsage.Text = $"300分: {FormatPercent(shortWindow)}";
        _codexWeeklyUsage.Text = $"10080分: {FormatPercent(weeklyWindow)}";
        _codexShortReset.Text = $"Reset: {FormatReset(shortWindow)}";
        _codexWeeklyReset.Text = $"Reset: {FormatReset(weeklyWindow)}";
        _updatedAt.Text = $"最終更新: {snapshot.CapturedAtUtc.ToLocalTime():yyyy/MM/dd HH:mm:ss}";
    }

    private static Panel CreateCard(string title, Label status, Label message)
    {
        var card = CreateCardPanel();
        var titleLabel = CreateTitleLabel(title);
        titleLabel.Location = new Point(10, 8);
        status.Location = new Point(10, 30);
        message.Location = new Point(116, 32);
        message.Size = new Size(196, 36);

        card.Controls.Add(titleLabel);
        card.Controls.Add(status);
        card.Controls.Add(message);
        return card;
    }

    private Panel CreateCodexCard()
    {
        var card = CreateCardPanel();
        var titleLabel = CreateTitleLabel("Codex");
        titleLabel.Location = new Point(10, 8);
        _codexStatus.Location = new Point(10, 30);
        _codexMessage.Location = new Point(116, 32);
        _codexMessage.Size = new Size(196, 36);
        _codexShortUsage.Location = new Point(10, 72);
        _codexWeeklyUsage.Location = new Point(166, 72);
        _codexShortReset.Location = new Point(10, 100);
        _codexShortReset.Size = new Size(150, 24);
        _codexWeeklyReset.Location = new Point(166, 100);
        _codexWeeklyReset.Size = new Size(150, 24);

        card.Controls.Add(titleLabel);
        card.Controls.Add(_codexStatus);
        card.Controls.Add(_codexMessage);
        card.Controls.Add(_codexShortUsage);
        card.Controls.Add(_codexWeeklyUsage);
        card.Controls.Add(_codexShortReset);
        card.Controls.Add(_codexWeeklyReset);
        return card;
    }

    private static Panel CreateCardPanel()
        => new()
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, 10),
            BackColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle
        };

    private static Label CreateTitleLabel(string text)
        => new()
        {
            Text = text,
            AutoSize = true,
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
            ForeColor = Color.FromArgb(35, 38, 45)
        };

    private static Label CreateValueLabel()
        => new()
        {
            AutoSize = true,
            Font = new Font("Segoe UI", 10F, FontStyle.Bold),
            ForeColor = Color.FromArgb(30, 35, 45)
        };

    private static Label CreateMutedLabel()
        => new()
        {
            AutoEllipsis = true,
            ForeColor = Color.FromArgb(90, 96, 106)
        };

    private static RateLimitWindow? FindWindow(ServiceUsage service, long durationMins)
        => service.Windows.FirstOrDefault(window => window.WindowDurationMins == durationMins);

    private static string FormatPercent(RateLimitWindow? window)
        => window?.UsedPercent is null
            ? "n/a"
            : $"{window.UsedPercent.Value:0.#}%";

    private static string FormatReset(RateLimitWindow? window)
        => window?.ResetAtUtc is null
            ? "n/a"
            : window.ResetAtUtc.Value.ToLocalTime().ToString("MM/dd HH:mm");

    private static string SafeMessage(string? message)
        => string.IsNullOrWhiteSpace(message) ? "" : message;
}
