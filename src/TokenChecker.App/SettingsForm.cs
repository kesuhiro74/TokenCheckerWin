using System.Drawing;
using System.Windows.Forms;
using TokenChecker.Core;

namespace TokenChecker.App;

internal sealed class SettingsForm : Form
{
    private readonly ComboBox _refreshInterval = new();
    private readonly ComboBox _displayMode = new();
    private readonly CheckBox _autoStart = new();
    private readonly CheckBox _showOnStartup = new();
    private readonly CheckBox _showClaude = new();
    private readonly CheckBox _showCodex = new();

    private readonly TrayApplicationContext? _host;
    private readonly Label _claudeStatusLabel;
    private readonly Label _codexStatusLabel;
    private readonly Button _claudeLoginBtn;
    private readonly Button _claudeLogoutBtn;
    private readonly Button _codexLoginBtn;
    private readonly Button _codexLogoutBtn;
    private readonly Button _refreshAuthBtn;

    public SettingsForm(AppSettings settings, TrayApplicationContext? host = null)
    {
        _host = host;

        Text = "設定";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(404, 462);
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
        foreach (var option in AppSettings.AllowedRefreshIntervalSeconds)
        {
            _refreshInterval.Items.Add(new RefreshIntervalOption(option));
        }

        var displayModeLabel = new Label
        {
            Text = "表示モード",
            AutoSize = true,
            Location = new Point(16, 88)
        };

        _displayMode.DropDownStyle = ComboBoxStyle.DropDownList;
        _displayMode.Location = new Point(112, 84);
        _displayMode.Size = new Size(170, 24);
        _displayMode.Items.Add(new DisplayModeOption(DisplayMode.Normal));
        _displayMode.Items.Add(new DisplayModeOption(DisplayMode.Compact));
        _displayMode.Items.Add(new DisplayModeOption(DisplayMode.Minimum));

        _autoStart.Text = "Windowsログイン時に自動起動";
        _autoStart.AutoSize = true;
        _autoStart.Location = new Point(16, 122);

        _showOnStartup.Text = "起動時にステータスを表示";
        _showOnStartup.AutoSize = true;
        _showOnStartup.Location = new Point(16, 148);

        var servicesLabel = new Label
        {
            Text = "表示対象",
            AutoSize = true,
            Location = new Point(16, 186)
        };

        _showClaude.Text = "Claude Code";
        _showClaude.AutoSize = true;
        _showClaude.Location = new Point(112, 184);
        _showCodex.Text = "Codex";
        _showCodex.AutoSize = true;
        _showCodex.Location = new Point(240, 184);

        var authLabel = new Label
        {
            Text = "ログイン状態",
            AutoSize = true,
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
            Location = new Point(16, 222)
        };

        var claudeName = new Label
        {
            Text = "Claude Code",
            AutoSize = true,
            Location = new Point(16, 256),
            Font = new Font("Segoe UI", 9F, FontStyle.Bold)
        };

        _claudeStatusLabel = new Label
        {
            AutoSize = false,
            Size = new Size(96, 22),
            Location = new Point(106, 254),
            TextAlign = ContentAlignment.MiddleLeft,
            Text = "確認中"
        };

        _claudeLoginBtn = new Button
        {
            Text = "ログイン",
            Location = new Point(208, 250),
            Size = new Size(72, 28)
        };
        _claudeLoginBtn.Click += (_, _) => RunAuth(host is null ? null : host.AuthService.LaunchClaudeLogin);

        _claudeLogoutBtn = new Button
        {
            Text = "ログアウト",
            Location = new Point(286, 250),
            Size = new Size(84, 28)
        };
        _claudeLogoutBtn.Click += (_, _) => RunAuth(host is null ? null : host.AuthService.LaunchClaudeLogout);

        var codexName = new Label
        {
            Text = "Codex",
            AutoSize = true,
            Location = new Point(16, 294),
            Font = new Font("Segoe UI", 9F, FontStyle.Bold)
        };

        _codexStatusLabel = new Label
        {
            AutoSize = false,
            Size = new Size(96, 22),
            Location = new Point(106, 292),
            TextAlign = ContentAlignment.MiddleLeft,
            Text = "確認中"
        };

        _codexLoginBtn = new Button
        {
            Text = "ログイン",
            Location = new Point(208, 288),
            Size = new Size(72, 28)
        };
        _codexLoginBtn.Click += (_, _) => RunAuth(host is null ? null : host.AuthService.LaunchCodexLogin);

        _codexLogoutBtn = new Button
        {
            Text = "ログアウト",
            Location = new Point(286, 288),
            Size = new Size(84, 28)
        };
        _codexLogoutBtn.Click += (_, _) => RunAuth(host is null ? null : host.AuthService.LaunchCodexLogout);

        _refreshAuthBtn = new Button
        {
            Text = "認証状態を再確認",
            Location = new Point(208, 326),
            Size = new Size(162, 28)
        };
        _refreshAuthBtn.Click += async (_, _) => await RefreshAuthAsync().ConfigureAwait(true);

        var okButton = new Button
        {
            Text = "OK",
            DialogResult = DialogResult.OK,
            Location = new Point(212, 380),
            Size = new Size(76, 28)
        };

        var cancelButton = new Button
        {
            Text = "キャンセル",
            DialogResult = DialogResult.Cancel,
            Location = new Point(294, 380),
            Size = new Size(76, 28)
        };

        Controls.Add(title);
        Controls.Add(intervalLabel);
        Controls.Add(_refreshInterval);
        Controls.Add(displayModeLabel);
        Controls.Add(_displayMode);
        Controls.Add(_autoStart);
        Controls.Add(_showOnStartup);
        Controls.Add(servicesLabel);
        Controls.Add(_showClaude);
        Controls.Add(_showCodex);
        Controls.Add(authLabel);
        Controls.Add(claudeName);
        Controls.Add(_claudeStatusLabel);
        Controls.Add(_claudeLoginBtn);
        Controls.Add(_claudeLogoutBtn);
        Controls.Add(codexName);
        Controls.Add(_codexStatusLabel);
        Controls.Add(_codexLoginBtn);
        Controls.Add(_codexLogoutBtn);
        Controls.Add(_refreshAuthBtn);
        Controls.Add(okButton);
        Controls.Add(cancelButton);

        AcceptButton = okButton;
        CancelButton = cancelButton;

        LoadSettings(settings);
        RefreshAuthStatusLabels();
    }

    public AppSettings ToSettings(AppSettings current)
    {
        var selectedInterval = (_refreshInterval.SelectedItem as RefreshIntervalOption)?.Seconds ?? current.RefreshIntervalSeconds;
        var selectedMode = (_displayMode.SelectedItem as DisplayModeOption)?.Mode ?? current.DisplayMode;
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
        settings.ShowOnStartup = _showOnStartup.Checked;
        settings.DisplayMode = selectedMode;
        settings.VisibleServices = visible.ToArray();
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

        var displayIndex = 0;
        for (var i = 0; i < _displayMode.Items.Count; i++)
        {
            if ((_displayMode.Items[i] as DisplayModeOption)?.Mode == settings.DisplayMode)
            {
                displayIndex = i;
                break;
            }
        }

        _displayMode.SelectedIndex = displayIndex;
        _autoStart.Checked = settings.AutoStartEnabled;
        _showOnStartup.Checked = settings.ShowOnStartup;
        _showClaude.Checked = settings.IsServiceVisible("Claude");
        _showCodex.Checked = settings.IsServiceVisible("Codex");
    }

    private void RunAuth(Func<AuthLaunchResult>? command)
    {
        if (command is null)
        {
            return;
        }

        AuthLaunchResult result;
        try
        {
            result = command();
        }
        catch
        {
            result = new AuthLaunchResult(false, "コマンドの実行に失敗しました。");
        }

        var icon = result.Ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning;
        MessageBox.Show(this, result.Message, "TokenCheckerWin", MessageBoxButtons.OK, icon);
    }

    private async Task RefreshAuthAsync()
    {
        if (_host is null)
        {
            return;
        }

        _refreshAuthBtn.Enabled = false;
        _claudeLoginBtn.Enabled = false;
        _claudeLogoutBtn.Enabled = false;
        _codexLoginBtn.Enabled = false;
        _codexLogoutBtn.Enabled = false;
        _claudeStatusLabel.Text = "確認中";
        _codexStatusLabel.Text = "確認中";

        try
        {
            await _host.RefreshUsageAsync().ConfigureAwait(true);
        }
        finally
        {
            RefreshAuthStatusLabels();
            _refreshAuthBtn.Enabled = true;
            _claudeLoginBtn.Enabled = true;
            _claudeLogoutBtn.Enabled = true;
            _codexLoginBtn.Enabled = true;
            _codexLogoutBtn.Enabled = true;
        }
    }

    private void RefreshAuthStatusLabels()
    {
        if (_host is null)
        {
            _claudeStatusLabel.Text = "ホスト未接続";
            _codexStatusLabel.Text = "ホスト未接続";
            return;
        }

        ApplyAuthStatus(_claudeStatusLabel, _host.GetServiceStatus("Claude"));
        ApplyAuthStatus(_codexStatusLabel, _host.GetServiceStatus("Codex"));
    }

    private static void ApplyAuthStatus(Label label, ProviderStatus status)
    {
        label.Text = status switch
        {
            ProviderStatus.Available => "正常",
            ProviderStatus.NotLoggedIn => "未ログイン",
            ProviderStatus.NotInstalled => "CLI未検出",
            ProviderStatus.Unauthorized => "認証エラー",
            ProviderStatus.RateLimited => "取得を一時制限中",
            ProviderStatus.Error => "取得失敗",
            _ => "状態不明"
        };

        label.ForeColor = status switch
        {
            ProviderStatus.Available => Color.FromArgb(31, 130, 73),
            ProviderStatus.NotInstalled or ProviderStatus.NotLoggedIn => Color.FromArgb(165, 102, 21),
            ProviderStatus.Unauthorized or ProviderStatus.RateLimited => Color.FromArgb(165, 102, 21),
            ProviderStatus.Error => Color.FromArgb(175, 60, 60),
            _ => SystemColors.ControlText
        };
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

    private sealed record DisplayModeOption(DisplayMode Mode)
    {
        public override string ToString()
            => Mode switch
            {
                DisplayMode.Normal => "通常モード",
                DisplayMode.Compact => "コンパクトモード",
                DisplayMode.Minimum => "ミニマムモード",
                _ => Mode.ToString()
            };
    }
}
