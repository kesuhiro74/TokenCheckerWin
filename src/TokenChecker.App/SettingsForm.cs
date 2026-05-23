using System.Drawing;
using System.Windows.Forms;

namespace TokenChecker.App;

internal sealed class SettingsForm : Form
{
    public static readonly int[] RefreshIntervalOptions = [30, 60, 300, 600];

    private readonly ComboBox _refreshInterval = new();
    private readonly CheckBox _autoStart = new();
    private readonly CheckBox _showClaude = new();
    private readonly CheckBox _showCodex = new();

    public SettingsForm(AppSettings settings)
    {
        Text = "設定";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(330, 250);
        Font = new Font("Segoe UI", 9F);

        var title = new Label
        {
            Text = "TokenCheckerWin",
            AutoSize = true,
            Font = new Font("Segoe UI", 10F, FontStyle.Bold),
            Location = new Point(16, 14)
        };

        var intervalLabel = new Label
        {
            Text = "更新間隔",
            AutoSize = true,
            Location = new Point(16, 52)
        };

        _refreshInterval.DropDownStyle = ComboBoxStyle.DropDownList;
        _refreshInterval.Location = new Point(112, 48);
        _refreshInterval.Size = new Size(170, 24);
        foreach (var option in RefreshIntervalOptions)
        {
            _refreshInterval.Items.Add(new RefreshIntervalOption(option));
        }

        _autoStart.Text = "Windowsログイン時に自動起動";
        _autoStart.AutoSize = true;
        _autoStart.Location = new Point(16, 88);

        var servicesLabel = new Label
        {
            Text = "表示対象",
            AutoSize = true,
            Location = new Point(16, 124)
        };

        _showClaude.Text = "Claude";
        _showClaude.AutoSize = true;
        _showClaude.Location = new Point(112, 122);
        _showCodex.Text = "Codex";
        _showCodex.AutoSize = true;
        _showCodex.Location = new Point(190, 122);

        var okButton = new Button
        {
            Text = "OK",
            DialogResult = DialogResult.OK,
            Location = new Point(126, 170),
            Size = new Size(76, 28)
        };

        var cancelButton = new Button
        {
            Text = "キャンセル",
            DialogResult = DialogResult.Cancel,
            Location = new Point(208, 170),
            Size = new Size(76, 28)
        };

        Controls.Add(title);
        Controls.Add(intervalLabel);
        Controls.Add(_refreshInterval);
        Controls.Add(_autoStart);
        Controls.Add(servicesLabel);
        Controls.Add(_showClaude);
        Controls.Add(_showCodex);
        Controls.Add(okButton);
        Controls.Add(cancelButton);

        AcceptButton = okButton;
        CancelButton = cancelButton;

        LoadSettings(settings);
    }

    public AppSettings ToSettings(AppSettings current)
    {
        var selectedInterval = (_refreshInterval.SelectedItem as RefreshIntervalOption)?.Seconds ?? current.RefreshIntervalSeconds;
        var visible = new List<string>();
        if (_showClaude.Checked)
        {
            visible.Add("Claude");
        }

        if (_showCodex.Checked)
        {
            visible.Add("Codex");
        }

        var settings = current.Clone();
        settings.RefreshIntervalSeconds = selectedInterval;
        settings.AutoStartEnabled = _autoStart.Checked;
        settings.VisibleServices = visible.Count == 0 ? ["Claude", "Codex"] : visible.ToArray();
        settings.Normalize();
        return settings;
    }

    private void LoadSettings(AppSettings settings)
    {
        var selectedIndex = 0;
        for (var i = 0; i < _refreshInterval.Items.Count; i++)
        {
            if ((_refreshInterval.Items[i] as RefreshIntervalOption)?.Seconds == settings.RefreshIntervalSeconds)
            {
                selectedIndex = i;
                break;
            }
        }

        _refreshInterval.SelectedIndex = selectedIndex;
        _autoStart.Checked = settings.AutoStartEnabled;
        _showClaude.Checked = settings.IsServiceVisible("Claude");
        _showCodex.Checked = settings.IsServiceVisible("Codex");
    }

    private sealed record RefreshIntervalOption(int Seconds)
    {
        public override string ToString()
            => Seconds switch
            {
                30 => "30秒",
                60 => "1分",
                300 => "5分",
                600 => "10分",
                _ => $"{Seconds}秒"
            };
    }
}
