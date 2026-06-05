using TokenChecker.Core;

namespace TokenChecker.App;

internal sealed class SettingsForm : Form
{
    // Common
    private readonly ComboBox _refreshInterval = new();
    private readonly CheckBox _autoStart = new();

    // Claude / Codex
    private readonly CheckBox _ccWindowEnabled = new();
    private readonly ComboBox _ccDisplayMode = new();
    private readonly ComboBox _displayMode = new();
    private readonly CheckBox _showClaude = new();
    private readonly CheckBox _showCodex = new();

    // GitHub Copilot
    private readonly CheckBox _copilotWindowEnabled = new();
    private readonly ComboBox _copilotPlan = new();
    private readonly NumericUpDown _copilotCustomCredits = new();
    private readonly ComboBox _copilotDisplayMode = new();

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
        Size = new Size(404, 632);
        Font = new Font("Segoe UI", 9F);

        // ----- 共通設定 -----------------------------------------------------
        var gbCommon = new GroupBox
        {
            Text = "共通設定",
            Location = new Point(12, 12),
            Size = new Size(368, 92)
        };

        var intervalLabel = new Label { Text = "更新間隔", AutoSize = true, Location = new Point(14, 28) };
        _refreshInterval.DropDownStyle = ComboBoxStyle.DropDownList;
        _refreshInterval.Location = new Point(110, 24);
        _refreshInterval.Size = new Size(170, 24);
        foreach (var option in AppSettings.AllowedRefreshIntervalSeconds)
        {
            _refreshInterval.Items.Add(new RefreshIntervalOption(option));
        }

        _autoStart.Text = "Windowsログイン時に自動起動";
        _autoStart.AutoSize = true;
        _autoStart.Location = new Point(14, 58);

        gbCommon.Controls.Add(intervalLabel);
        gbCommon.Controls.Add(_refreshInterval);
        gbCommon.Controls.Add(_autoStart);

        // ----- Claude / Codex 設定 -----------------------------------------
        var gbClaudeCodex = new GroupBox
        {
            Text = "Claude / Codex 設定",
            Location = new Point(12, 112),
            Size = new Size(368, 272)
        };

        _ccWindowEnabled.Text = "Claude / Codex ウィンドウを表示";
        _ccWindowEnabled.AutoSize = true;
        _ccWindowEnabled.Location = new Point(14, 24);
        _ccWindowEnabled.CheckedChanged += (_, _) => UpdateEnabledStates();

        var ccMethodLabel = new Label { Text = "表示方法", AutoSize = true, Location = new Point(14, 56) };
        _ccDisplayMode.DropDownStyle = ComboBoxStyle.DropDownList;
        _ccDisplayMode.Location = new Point(110, 52);
        _ccDisplayMode.Size = new Size(170, 24);
        _ccDisplayMode.Items.Add(new WindowDisplayModeOption(WindowDisplayMode.Always));
        _ccDisplayMode.Items.Add(new WindowDisplayModeOption(WindowDisplayMode.HoverPreview));

        var displayModeLabel = new Label { Text = "表示モード", AutoSize = true, Location = new Point(14, 88) };
        _displayMode.DropDownStyle = ComboBoxStyle.DropDownList;
        _displayMode.Location = new Point(110, 84);
        _displayMode.Size = new Size(170, 24);
        _displayMode.Items.Add(new DisplayModeOption(DisplayMode.Normal));
        _displayMode.Items.Add(new DisplayModeOption(DisplayMode.Compact));
        _displayMode.Items.Add(new DisplayModeOption(DisplayMode.Minimum));

        var servicesLabel = new Label { Text = "表示対象", AutoSize = true, Location = new Point(14, 120) };
        _showClaude.Text = "Claude Code";
        _showClaude.AutoSize = true;
        _showClaude.Location = new Point(110, 118);
        _showCodex.Text = "Codex";
        _showCodex.AutoSize = true;
        _showCodex.Location = new Point(235, 118);

        var authLabel = new Label
        {
            Text = "ログイン状態",
            AutoSize = true,
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
            Location = new Point(14, 150)
        };

        var claudeName = new Label
        {
            Text = "Claude Code",
            AutoSize = true,
            Location = new Point(14, 178),
            Font = new Font("Segoe UI", 9F, FontStyle.Bold)
        };
        _claudeStatusLabel = new Label
        {
            AutoSize = false,
            Size = new Size(80, 22),
            Location = new Point(104, 176),
            TextAlign = ContentAlignment.MiddleLeft,
            Text = "確認中"
        };
        _claudeLoginBtn = new Button { Text = "ログイン", Location = new Point(188, 172), Size = new Size(70, 28) };
        _claudeLoginBtn.Click += (_, _) => RunAuth(host is null ? null : host.AuthService.LaunchClaudeLogin);
        _claudeLogoutBtn = new Button { Text = "ログアウト", Location = new Point(262, 172), Size = new Size(84, 28) };
        _claudeLogoutBtn.Click += (_, _) => RunAuth(host is null ? null : host.AuthService.LaunchClaudeLogout);

        var codexName = new Label
        {
            Text = "Codex",
            AutoSize = true,
            Location = new Point(14, 210),
            Font = new Font("Segoe UI", 9F, FontStyle.Bold)
        };
        _codexStatusLabel = new Label
        {
            AutoSize = false,
            Size = new Size(80, 22),
            Location = new Point(104, 208),
            TextAlign = ContentAlignment.MiddleLeft,
            Text = "確認中"
        };
        _codexLoginBtn = new Button { Text = "ログイン", Location = new Point(188, 204), Size = new Size(70, 28) };
        _codexLoginBtn.Click += (_, _) => RunAuth(host is null ? null : host.AuthService.LaunchCodexLogin);
        _codexLogoutBtn = new Button { Text = "ログアウト", Location = new Point(262, 204), Size = new Size(84, 28) };
        _codexLogoutBtn.Click += (_, _) => RunAuth(host is null ? null : host.AuthService.LaunchCodexLogout);

        _refreshAuthBtn = new Button { Text = "認証状態を再確認", Location = new Point(188, 236), Size = new Size(158, 28) };
        _refreshAuthBtn.Click += async (_, _) => await RefreshAuthAsync().ConfigureAwait(true);

        gbClaudeCodex.Controls.Add(_ccWindowEnabled);
        gbClaudeCodex.Controls.Add(ccMethodLabel);
        gbClaudeCodex.Controls.Add(_ccDisplayMode);
        gbClaudeCodex.Controls.Add(displayModeLabel);
        gbClaudeCodex.Controls.Add(_displayMode);
        gbClaudeCodex.Controls.Add(servicesLabel);
        gbClaudeCodex.Controls.Add(_showClaude);
        gbClaudeCodex.Controls.Add(_showCodex);
        gbClaudeCodex.Controls.Add(authLabel);
        gbClaudeCodex.Controls.Add(claudeName);
        gbClaudeCodex.Controls.Add(_claudeStatusLabel);
        gbClaudeCodex.Controls.Add(_claudeLoginBtn);
        gbClaudeCodex.Controls.Add(_claudeLogoutBtn);
        gbClaudeCodex.Controls.Add(codexName);
        gbClaudeCodex.Controls.Add(_codexStatusLabel);
        gbClaudeCodex.Controls.Add(_codexLoginBtn);
        gbClaudeCodex.Controls.Add(_codexLogoutBtn);
        gbClaudeCodex.Controls.Add(_refreshAuthBtn);

        // ----- GitHub Copilot 設定 -----------------------------------------
        var gbCopilot = new GroupBox
        {
            Text = "GitHub Copilot 設定",
            Location = new Point(12, 392),
            Size = new Size(368, 150)
        };

        _copilotWindowEnabled.Text = "GitHub Copilot ウィンドウを表示";
        _copilotWindowEnabled.AutoSize = true;
        _copilotWindowEnabled.Location = new Point(14, 24);
        _copilotWindowEnabled.CheckedChanged += (_, _) => UpdateEnabledStates();

        var planLabel = new Label { Text = "プラン", AutoSize = true, Location = new Point(14, 56) };
        _copilotPlan.DropDownStyle = ComboBoxStyle.DropDownList;
        _copilotPlan.Location = new Point(110, 52);
        _copilotPlan.Size = new Size(180, 24);
        _copilotPlan.Items.Add(new CopilotPlanOption(CopilotPlan.None));
        _copilotPlan.Items.Add(new CopilotPlanOption(CopilotPlan.Pro));
        _copilotPlan.Items.Add(new CopilotPlanOption(CopilotPlan.ProPlus));
        _copilotPlan.Items.Add(new CopilotPlanOption(CopilotPlan.Max));
        _copilotPlan.Items.Add(new CopilotPlanOption(CopilotPlan.Custom));
        _copilotPlan.SelectedIndexChanged += (_, _) => UpdateEnabledStates();

        var customLabel = new Label { Text = "Custom 上限", AutoSize = true, Location = new Point(14, 88) };
        _copilotCustomCredits.Location = new Point(110, 86);
        _copilotCustomCredits.Size = new Size(120, 24);
        _copilotCustomCredits.Minimum = 0;
        _copilotCustomCredits.Maximum = 1_000_000;
        _copilotCustomCredits.Increment = 100;
        _copilotCustomCredits.ThousandsSeparator = true;

        var copilotMethodLabel = new Label { Text = "表示方法", AutoSize = true, Location = new Point(14, 120) };
        _copilotDisplayMode.DropDownStyle = ComboBoxStyle.DropDownList;
        _copilotDisplayMode.Location = new Point(110, 116);
        _copilotDisplayMode.Size = new Size(180, 24);
        _copilotDisplayMode.Items.Add(new WindowDisplayModeOption(WindowDisplayMode.Always));
        _copilotDisplayMode.Items.Add(new WindowDisplayModeOption(WindowDisplayMode.HoverPreview));

        gbCopilot.Controls.Add(_copilotWindowEnabled);
        gbCopilot.Controls.Add(planLabel);
        gbCopilot.Controls.Add(_copilotPlan);
        gbCopilot.Controls.Add(customLabel);
        gbCopilot.Controls.Add(_copilotCustomCredits);
        gbCopilot.Controls.Add(copilotMethodLabel);
        gbCopilot.Controls.Add(_copilotDisplayMode);

        // ----- OK / Cancel --------------------------------------------------
        var okButton = new Button
        {
            Text = "OK",
            DialogResult = DialogResult.OK,
            Location = new Point(224, 552),
            Size = new Size(76, 28)
        };
        var cancelButton = new Button
        {
            Text = "キャンセル",
            DialogResult = DialogResult.Cancel,
            Location = new Point(306, 552),
            Size = new Size(76, 28)
        };

        Controls.Add(gbCommon);
        Controls.Add(gbClaudeCodex);
        Controls.Add(gbCopilot);
        Controls.Add(okButton);
        Controls.Add(cancelButton);

        AcceptButton = okButton;
        CancelButton = cancelButton;

        LoadSettings(settings);
        RefreshAuthStatusLabels();
    }

    public AppSettings ToSettings(AppSettings current)
    {
        var settings = current.Clone();

        settings.RefreshIntervalSeconds = (_refreshInterval.SelectedItem as RefreshIntervalOption)?.Seconds ?? current.RefreshIntervalSeconds;
        settings.AutoStartEnabled = _autoStart.Checked;

        settings.ClaudeCodexWindowEnabled = _ccWindowEnabled.Checked;
        settings.ClaudeCodexDisplayMode = (_ccDisplayMode.SelectedItem as WindowDisplayModeOption)?.Mode ?? current.ClaudeCodexDisplayMode;
        settings.DisplayMode = (_displayMode.SelectedItem as DisplayModeOption)?.Mode ?? current.DisplayMode;

        var visible = new List<string>();
        if (_showClaude.Checked)
        {
            visible.Add("Claude");
        }

        if (_showCodex.Checked)
        {
            visible.Add("Codex");
        }

        settings.VisibleServices = visible.ToArray();

        settings.CopilotWindowEnabled = _copilotWindowEnabled.Checked;
        settings.CopilotDisplayMode = (_copilotDisplayMode.SelectedItem as WindowDisplayModeOption)?.Mode ?? current.CopilotDisplayMode;
        settings.CopilotPlan = (_copilotPlan.SelectedItem as CopilotPlanOption)?.Plan ?? current.CopilotPlan;
        settings.CopilotCustomCredits = (int)_copilotCustomCredits.Value;

        settings.Normalize();
        return settings;
    }

    private void LoadSettings(AppSettings settings)
    {
        _refreshInterval.SelectedIndex = IndexOf(_refreshInterval, o => (o as RefreshIntervalOption)?.Seconds == settings.RefreshIntervalSeconds);
        _autoStart.Checked = settings.AutoStartEnabled;

        _ccWindowEnabled.Checked = settings.ClaudeCodexWindowEnabled;
        _ccDisplayMode.SelectedIndex = IndexOf(_ccDisplayMode, o => (o as WindowDisplayModeOption)?.Mode == settings.ClaudeCodexDisplayMode);
        _displayMode.SelectedIndex = IndexOf(_displayMode, o => (o as DisplayModeOption)?.Mode == settings.DisplayMode);
        _showClaude.Checked = settings.IsServiceVisible("Claude");
        _showCodex.Checked = settings.IsServiceVisible("Codex");

        _copilotWindowEnabled.Checked = settings.CopilotWindowEnabled;
        _copilotDisplayMode.SelectedIndex = IndexOf(_copilotDisplayMode, o => (o as WindowDisplayModeOption)?.Mode == settings.CopilotDisplayMode);
        _copilotPlan.SelectedIndex = IndexOf(_copilotPlan, o => (o as CopilotPlanOption)?.Plan == settings.CopilotPlan);
        _copilotCustomCredits.Value = Math.Clamp(settings.CopilotCustomCredits, 0, 1_000_000);

        UpdateEnabledStates();
    }

    private static int IndexOf(ComboBox combo, Func<object?, bool> match)
    {
        for (var i = 0; i < combo.Items.Count; i++)
        {
            if (match(combo.Items[i]))
            {
                return i;
            }
        }

        return combo.Items.Count > 0 ? 0 : -1;
    }

    // Disable dependent controls so the on/off and plan dependencies read clearly.
    private void UpdateEnabledStates()
    {
        var cc = _ccWindowEnabled.Checked;
        _ccDisplayMode.Enabled = cc;
        _displayMode.Enabled = cc;
        _showClaude.Enabled = cc;
        _showCodex.Enabled = cc;

        var cp = _copilotWindowEnabled.Checked;
        _copilotPlan.Enabled = cp;
        _copilotDisplayMode.Enabled = cp;
        _copilotCustomCredits.Enabled = cp
            && (_copilotPlan.SelectedItem as CopilotPlanOption)?.Plan == CopilotPlan.Custom;
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

    private sealed record WindowDisplayModeOption(WindowDisplayMode Mode)
    {
        public override string ToString()
            => Mode switch
            {
                WindowDisplayMode.Always => "常時表示",
                WindowDisplayMode.HoverPreview => "ホバー表示（トレイ）",
                _ => Mode.ToString()
            };
    }

    private sealed record CopilotPlanOption(CopilotPlan Plan)
    {
        public override string ToString()
            => Plan switch
            {
                CopilotPlan.None => "なし（使用量のみ）",
                CopilotPlan.Pro => $"Pro（{AppSettings.ProCredits:N0}）",
                CopilotPlan.ProPlus => $"Pro+（{AppSettings.ProPlusCredits:N0}）",
                CopilotPlan.Max => $"Max（{AppSettings.MaxCredits:N0}）",
                CopilotPlan.Custom => "Custom（手入力）",
                _ => Plan.ToString()
            };
    }
}
