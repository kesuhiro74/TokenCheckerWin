using System.Reflection;
using System.Runtime.InteropServices;
using TokenChecker.Core;

namespace TokenChecker.App;

internal sealed class SettingsForm : Form
{
    // Common
    private readonly ComboBox _refreshInterval = new();
    private readonly CheckBox _autoStart = new();
    private readonly ComboBox _theme = new();
    private readonly ComboBox _language = new();

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
    private readonly ComboBox _copilotAccent = new();
    private readonly Button _copilotSetupBtn = new();
    private readonly Button _copilotTestBtn = new();

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

        Text = Strings.T("設定");
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(404, 786);
        Font = new Font("Segoe UI", 9F);

        // ----- 共通設定 -----------------------------------------------------
        var gbCommon = new GroupBox
        {
            Text = Strings.T("共通設定"),
            Location = new Point(12, 12),
            Size = new Size(368, 168)
        };

        var intervalLabel = new Label { Text = Strings.T("更新間隔"), AutoSize = true, Location = new Point(14, 28) };
        _refreshInterval.DropDownStyle = ComboBoxStyle.DropDownList;
        _refreshInterval.Location = new Point(110, 24);
        _refreshInterval.Size = new Size(170, 24);
        foreach (var option in AppSettings.AllowedRefreshIntervalSeconds)
        {
            _refreshInterval.Items.Add(new RefreshIntervalOption(option));
        }

        _autoStart.Text = Strings.T("Windowsログイン時に自動起動");
        _autoStart.AutoSize = true;
        _autoStart.Location = new Point(14, 58);

        var themeLabel = new Label { Text = Strings.T("テーマ"), AutoSize = true, Location = new Point(14, 90) };
        _theme.DropDownStyle = ComboBoxStyle.DropDownList;
        _theme.Location = new Point(110, 86);
        _theme.Size = new Size(170, 24);
        _theme.Items.Add(new ThemeOption(ThemeMode.System));
        _theme.Items.Add(new ThemeOption(ThemeMode.Light));
        _theme.Items.Add(new ThemeOption(ThemeMode.Dark));

        var languageLabel = new Label { Text = Strings.T("言語"), AutoSize = true, Location = new Point(14, 120) };
        _language.DropDownStyle = ComboBoxStyle.DropDownList;
        _language.Location = new Point(110, 116);
        _language.Size = new Size(170, 24);
        _language.Items.Add(new LanguageOption(AppLanguage.System));
        _language.Items.Add(new LanguageOption(AppLanguage.English));
        _language.Items.Add(new LanguageOption(AppLanguage.Japanese));

        var restartNote = new Label
        {
            Text = Strings.T("（再起動で反映）"),
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Location = new Point(110, 146)
        };

        gbCommon.Controls.Add(intervalLabel);
        gbCommon.Controls.Add(_refreshInterval);
        gbCommon.Controls.Add(_autoStart);
        gbCommon.Controls.Add(themeLabel);
        gbCommon.Controls.Add(_theme);
        gbCommon.Controls.Add(languageLabel);
        gbCommon.Controls.Add(_language);
        gbCommon.Controls.Add(restartNote);

        // ----- Claude / Codex 設定 -----------------------------------------
        var gbClaudeCodex = new GroupBox
        {
            Text = Strings.T("Claude / Codex 設定"),
            Location = new Point(12, 188),
            Size = new Size(368, 272)
        };

        _ccWindowEnabled.Text = Strings.T("Claude / Codex ウィンドウを表示");
        _ccWindowEnabled.AutoSize = true;
        _ccWindowEnabled.Location = new Point(14, 24);
        _ccWindowEnabled.CheckedChanged += (_, _) => UpdateEnabledStates();

        var ccMethodLabel = new Label { Text = Strings.T("表示方法"), AutoSize = true, Location = new Point(14, 56) };
        _ccDisplayMode.DropDownStyle = ComboBoxStyle.DropDownList;
        _ccDisplayMode.Location = new Point(110, 52);
        _ccDisplayMode.Size = new Size(170, 24);
        _ccDisplayMode.Items.Add(new WindowDisplayModeOption(WindowDisplayMode.Always));
        _ccDisplayMode.Items.Add(new WindowDisplayModeOption(WindowDisplayMode.HoverPreview));

        var displayModeLabel = new Label { Text = Strings.T("表示モード"), AutoSize = true, Location = new Point(14, 88) };
        _displayMode.DropDownStyle = ComboBoxStyle.DropDownList;
        _displayMode.Location = new Point(110, 84);
        _displayMode.Size = new Size(170, 24);
        _displayMode.Items.Add(new DisplayModeOption(DisplayMode.Normal));
        _displayMode.Items.Add(new DisplayModeOption(DisplayMode.Compact));
        _displayMode.Items.Add(new DisplayModeOption(DisplayMode.Minimum));

        var servicesLabel = new Label { Text = Strings.T("表示対象"), AutoSize = true, Location = new Point(14, 120) };
        _showClaude.Text = "Claude Code";
        _showClaude.AutoSize = true;
        _showClaude.Location = new Point(110, 118);
        _showCodex.Text = "Codex";
        _showCodex.AutoSize = true;
        _showCodex.Location = new Point(235, 118);

        var authLabel = new Label
        {
            Text = Strings.T("ログイン状態"),
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
            Text = Strings.T("確認中")
        };
        _claudeLoginBtn = new Button { Text = Strings.T("ログイン"), Location = new Point(188, 172), Size = new Size(70, 28) };
        _claudeLoginBtn.Click += (_, _) => RunAuth(host is null ? null : host.AuthService.LaunchClaudeLogin);
        _claudeLogoutBtn = new Button { Text = Strings.T("ログアウト"), Location = new Point(262, 172), Size = new Size(84, 28) };
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
            Text = Strings.T("確認中")
        };
        _codexLoginBtn = new Button { Text = Strings.T("ログイン"), Location = new Point(188, 204), Size = new Size(70, 28) };
        _codexLoginBtn.Click += (_, _) => RunAuth(host is null ? null : host.AuthService.LaunchCodexLogin);
        _codexLogoutBtn = new Button { Text = Strings.T("ログアウト"), Location = new Point(262, 204), Size = new Size(84, 28) };
        _codexLogoutBtn.Click += (_, _) => RunAuth(host is null ? null : host.AuthService.LaunchCodexLogout);

        _refreshAuthBtn = new Button { Text = Strings.T("認証状態を再確認"), Location = new Point(188, 236), Size = new Size(158, 28) };
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
            Text = Strings.T("GitHub Copilot 設定"),
            Location = new Point(12, 468),
            Size = new Size(368, 224)
        };

        _copilotWindowEnabled.Text = Strings.T("GitHub Copilot ウィンドウを表示");
        _copilotWindowEnabled.AutoSize = true;
        _copilotWindowEnabled.Location = new Point(14, 24);
        _copilotWindowEnabled.CheckedChanged += (_, _) => UpdateEnabledStates();

        var planLabel = new Label { Text = Strings.T("プラン"), AutoSize = true, Location = new Point(14, 56) };
        _copilotPlan.DropDownStyle = ComboBoxStyle.DropDownList;
        _copilotPlan.Location = new Point(110, 52);
        _copilotPlan.Size = new Size(180, 24);
        _copilotPlan.Items.Add(new CopilotPlanOption(CopilotPlan.None));
        _copilotPlan.Items.Add(new CopilotPlanOption(CopilotPlan.Free));
        _copilotPlan.Items.Add(new CopilotPlanOption(CopilotPlan.Pro));
        _copilotPlan.Items.Add(new CopilotPlanOption(CopilotPlan.ProPlus));
        _copilotPlan.Items.Add(new CopilotPlanOption(CopilotPlan.Max));
        _copilotPlan.Items.Add(new CopilotPlanOption(CopilotPlan.Custom));
        _copilotPlan.SelectedIndexChanged += (_, _) => UpdateEnabledStates();

        var customLabel = new Label { Text = Strings.T("Custom 上限"), AutoSize = true, Location = new Point(14, 88) };
        _copilotCustomCredits.Location = new Point(110, 86);
        _copilotCustomCredits.Size = new Size(120, 24);
        _copilotCustomCredits.Minimum = 0;
        _copilotCustomCredits.Maximum = 1_000_000;
        _copilotCustomCredits.Increment = 100;
        _copilotCustomCredits.ThousandsSeparator = true;

        var copilotMethodLabel = new Label { Text = Strings.T("表示方法"), AutoSize = true, Location = new Point(14, 120) };
        _copilotDisplayMode.DropDownStyle = ComboBoxStyle.DropDownList;
        _copilotDisplayMode.Location = new Point(110, 116);
        _copilotDisplayMode.Size = new Size(180, 24);
        _copilotDisplayMode.Items.Add(new WindowDisplayModeOption(WindowDisplayMode.Always));
        _copilotDisplayMode.Items.Add(new WindowDisplayModeOption(WindowDisplayMode.HoverPreview));

        var accentLabel = new Label { Text = Strings.T("配色"), AutoSize = true, Location = new Point(14, 152) };
        _copilotAccent.DropDownStyle = ComboBoxStyle.DropDownList;
        _copilotAccent.Location = new Point(110, 148);
        _copilotAccent.Size = new Size(180, 24);
        _copilotAccent.Items.Add(new CopilotAccentOption(CopilotAccent.Blue));
        _copilotAccent.Items.Add(new CopilotAccentOption(CopilotAccent.Green));
        _copilotAccent.Items.Add(new CopilotAccentOption(CopilotAccent.Sky));
        _copilotAccent.Items.Add(new CopilotAccentOption(CopilotAccent.Purple));
        _copilotAccent.Items.Add(new CopilotAccentOption(CopilotAccent.Slate));

        // First-time setup wizard + connection test. Always enabled (the user can
        // set up the token before enabling the window).
        _copilotSetupBtn.Text = Strings.T("初回設定");
        _copilotSetupBtn.Location = new Point(14, 184);
        _copilotSetupBtn.Size = new Size(110, 28);
        _copilotSetupBtn.Click += (_, _) => OpenCopilotSetup();

        _copilotTestBtn.Text = Strings.T("接続テスト");
        _copilotTestBtn.Location = new Point(132, 184);
        _copilotTestBtn.Size = new Size(110, 28);
        _copilotTestBtn.Click += async (_, _) => await RunCopilotTestAsync().ConfigureAwait(true);

        gbCopilot.Controls.Add(_copilotWindowEnabled);
        gbCopilot.Controls.Add(planLabel);
        gbCopilot.Controls.Add(_copilotPlan);
        gbCopilot.Controls.Add(customLabel);
        gbCopilot.Controls.Add(_copilotCustomCredits);
        gbCopilot.Controls.Add(copilotMethodLabel);
        gbCopilot.Controls.Add(_copilotDisplayMode);
        gbCopilot.Controls.Add(accentLabel);
        gbCopilot.Controls.Add(_copilotAccent);
        gbCopilot.Controls.Add(_copilotSetupBtn);
        gbCopilot.Controls.Add(_copilotTestBtn);

        // ----- OK / Cancel --------------------------------------------------
        var okButton = new Button
        {
            Text = "OK",
            DialogResult = DialogResult.OK,
            Location = new Point(224, 702),
            Size = new Size(76, 28)
        };
        var cancelButton = new Button
        {
            Text = Strings.T("キャンセル"),
            DialogResult = DialogResult.Cancel,
            Location = new Point(306, 702),
            Size = new Size(76, 28)
        };

        // App version, shown quietly in the footer (left of the buttons).
        var versionLabel = new Label
        {
            Text = $"TokenCheckerWin v{AppVersion()}",
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Location = new Point(16, 708)
        };

        Controls.Add(gbCommon);
        Controls.Add(gbClaudeCodex);
        Controls.Add(gbCopilot);
        Controls.Add(versionLabel);
        Controls.Add(okButton);
        Controls.Add(cancelButton);

        AcceptButton = okButton;
        CancelButton = cancelButton;

        LoadSettings(settings);
        RefreshAuthStatusLabels();

        // Standard-style buttons render poorly under SetColorMode dark; the System
        // (OS-drawn) style follows the dark theme cleanly.
        ApplyButtonStyle(this);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        // SetColorMode does not theme the (non-client) title bar; ask DWM for the
        // dark frame so the dialog chrome matches the dark content. Win11+/no-op else.
        if (UsageTheme.IsDark)
        {
            try
            {
                var dark = 1;
                _ = DwmSetWindowAttribute(Handle, DwmwaUseImmersiveDarkMode, ref dark, sizeof(int));
            }
            catch
            {
                // Older Windows / unsupported — leave the default frame.
            }
        }
    }

    private const int DwmwaUseImmersiveDarkMode = 20;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    // The app version for the footer label. Prefers the informational version
    // (matches the csproj <Version>, e.g. "0.7.0"), stripping any "+build" suffix;
    // falls back to the assembly version's Major.Minor.Build.
    private static string AppVersion()
    {
        var assembly = typeof(SettingsForm).Assembly;
        var info = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(info))
        {
            var plus = info.IndexOf('+');
            return plus >= 0 ? info[..plus] : info;
        }

        var version = assembly.GetName().Version;
        return version is null ? "?" : $"{version.Major}.{version.Minor}.{version.Build}";
    }

    private static void ApplyButtonStyle(Control root)
    {
        foreach (Control child in root.Controls)
        {
            if (child is Button button)
            {
                button.FlatStyle = FlatStyle.System;
            }

            ApplyButtonStyle(child);
        }
    }

    public AppSettings ToSettings(AppSettings current)
    {
        var settings = current.Clone();

        settings.RefreshIntervalSeconds = (_refreshInterval.SelectedItem as RefreshIntervalOption)?.Seconds ?? current.RefreshIntervalSeconds;
        settings.AutoStartEnabled = _autoStart.Checked;
        settings.Theme = (_theme.SelectedItem as ThemeOption)?.Mode ?? current.Theme;
        settings.Language = (_language.SelectedItem as LanguageOption)?.Lang ?? current.Language;

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
        settings.CopilotAccent = (_copilotAccent.SelectedItem as CopilotAccentOption)?.Accent ?? current.CopilotAccent;

        settings.Normalize();
        return settings;
    }

    private void LoadSettings(AppSettings settings)
    {
        _refreshInterval.SelectedIndex = IndexOf(_refreshInterval, o => (o as RefreshIntervalOption)?.Seconds == settings.RefreshIntervalSeconds);
        _autoStart.Checked = settings.AutoStartEnabled;
        _theme.SelectedIndex = IndexOf(_theme, o => (o as ThemeOption)?.Mode == settings.Theme);
        _language.SelectedIndex = IndexOf(_language, o => (o as LanguageOption)?.Lang == settings.Language);

        _ccWindowEnabled.Checked = settings.ClaudeCodexWindowEnabled;
        _ccDisplayMode.SelectedIndex = IndexOf(_ccDisplayMode, o => (o as WindowDisplayModeOption)?.Mode == settings.ClaudeCodexDisplayMode);
        _displayMode.SelectedIndex = IndexOf(_displayMode, o => (o as DisplayModeOption)?.Mode == settings.DisplayMode);
        _showClaude.Checked = settings.IsServiceVisible("Claude");
        _showCodex.Checked = settings.IsServiceVisible("Codex");

        _copilotWindowEnabled.Checked = settings.CopilotWindowEnabled;
        _copilotDisplayMode.SelectedIndex = IndexOf(_copilotDisplayMode, o => (o as WindowDisplayModeOption)?.Mode == settings.CopilotDisplayMode);
        _copilotPlan.SelectedIndex = IndexOf(_copilotPlan, o => (o as CopilotPlanOption)?.Plan == settings.CopilotPlan);
        _copilotCustomCredits.Value = Math.Clamp(settings.CopilotCustomCredits, 0, 1_000_000);
        _copilotAccent.SelectedIndex = IndexOf(_copilotAccent, o => (o as CopilotAccentOption)?.Accent == settings.CopilotAccent);

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
        _copilotAccent.Enabled = cp;
        _copilotCustomCredits.Enabled = cp
            && (_copilotPlan.SelectedItem as CopilotPlanOption)?.Plan == CopilotPlan.Custom;
    }

    // The allowance reflects the CURRENT (possibly unsaved) plan/custom selection so
    // the setup wizard's connection test shows the right usage ratio.
    private int? CurrentCopilotAllowance()
    {
        var plan = (_copilotPlan.SelectedItem as CopilotPlanOption)?.Plan ?? CopilotPlan.None;
        return plan switch
        {
            CopilotPlan.Pro => AppSettings.ProCredits,
            CopilotPlan.ProPlus => AppSettings.ProPlusCredits,
            CopilotPlan.Max => AppSettings.MaxCredits,
            CopilotPlan.Custom => (int)_copilotCustomCredits.Value > 0 ? (int)_copilotCustomCredits.Value : null,
            _ => null
        };
    }

    private void OpenCopilotSetup()
    {
        using var wizard = new GitHubCopilotSetupForm(CurrentCopilotAllowance());
        wizard.ShowDialog(this);
    }

    private async Task RunCopilotTestAsync()
    {
        // Disable while running to prevent double execution; the message is a safe
        // canned string + usage numbers only (no token/login/URL/path/email).
        _copilotTestBtn.Enabled = false;
        try
        {
            var message = await GitHubCopilotSetupForm.RunConnectionTestAsync(CurrentCopilotAllowance()).ConfigureAwait(true);
            MessageBox.Show(this, message, Strings.T("接続テスト"), MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        finally
        {
            if (!IsDisposed)
            {
                _copilotTestBtn.Enabled = true;
            }
        }
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
            result = new AuthLaunchResult(false, Strings.T("コマンドの実行に失敗しました。"));
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
        _claudeStatusLabel.Text = Strings.T("確認中");
        _codexStatusLabel.Text = Strings.T("確認中");

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
            _claudeStatusLabel.Text = Strings.T("ホスト未接続");
            _codexStatusLabel.Text = Strings.T("ホスト未接続");
            return;
        }

        ApplyAuthStatus(_claudeStatusLabel, _host.GetServiceStatus("Claude"));
        ApplyAuthStatus(_codexStatusLabel, _host.GetServiceStatus("Codex"));
    }

    private static void ApplyAuthStatus(Label label, ProviderStatus status)
    {
        label.Text = status switch
        {
            ProviderStatus.Available => Strings.T("正常"),
            ProviderStatus.NotLoggedIn => Strings.T("未ログイン"),
            ProviderStatus.NotInstalled => Strings.T("CLI未検出"),
            ProviderStatus.Unauthorized => Strings.T("認証エラー"),
            ProviderStatus.RateLimited => Strings.T("取得を一時制限中"),
            ProviderStatus.Error => Strings.T("取得失敗"),
            _ => Strings.T("状態不明")
        };

        // Theme-aware severity color (green/amber/red), matching the windows and
        // adapting to light/dark.
        label.ForeColor = UsageTheme.StatusColor(status);
    }

    private sealed record ThemeOption(ThemeMode Mode)
    {
        public override string ToString()
            => Mode switch
            {
                ThemeMode.Light => Strings.T("ライト"),
                ThemeMode.Dark => Strings.T("ダーク"),
                _ => Strings.T("システム連動")
            };
    }

    private sealed record LanguageOption(AppLanguage Lang)
    {
        public override string ToString()
            => Lang switch
            {
                AppLanguage.English => "English",
                AppLanguage.Japanese => "日本語",
                _ => Strings.T("システム連動")
            };
    }

    private sealed record RefreshIntervalOption(int Seconds)
    {
        public override string ToString()
            => Seconds switch
            {
                30 => Strings.T("30秒"),
                60 => Strings.T("1分"),
                300 => Strings.T("5分"),
                600 => Strings.T("10分"),
                _ => Strings.Tf("{0}秒", Seconds)
            };
    }

    private sealed record DisplayModeOption(DisplayMode Mode)
    {
        public override string ToString()
            => Mode switch
            {
                DisplayMode.Normal => Strings.T("通常モード"),
                DisplayMode.Compact => Strings.T("コンパクトモード"),
                DisplayMode.Minimum => Strings.T("ミニマムモード"),
                _ => Mode.ToString()
            };
    }

    private sealed record WindowDisplayModeOption(WindowDisplayMode Mode)
    {
        public override string ToString()
            => Mode switch
            {
                WindowDisplayMode.Always => Strings.T("常時表示"),
                WindowDisplayMode.HoverPreview => Strings.T("ホバー表示（トレイ）"),
                _ => Mode.ToString()
            };
    }

    private sealed record CopilotPlanOption(CopilotPlan Plan)
    {
        public override string ToString()
            => Plan switch
            {
                CopilotPlan.None => Strings.T("なし（使用量のみ）"),
                CopilotPlan.Free => Strings.Tf("Free（{0}）", AppSettings.FreeCredits.ToString("N0")),
                CopilotPlan.Pro => Strings.Tf("Pro（{0}）", AppSettings.ProCredits.ToString("N0")),
                CopilotPlan.ProPlus => Strings.Tf("Pro+（{0}）", AppSettings.ProPlusCredits.ToString("N0")),
                CopilotPlan.Max => Strings.Tf("Max（{0}）", AppSettings.MaxCredits.ToString("N0")),
                CopilotPlan.Custom => Strings.T("Custom（手入力）"),
                _ => Plan.ToString()
            };
    }

    private sealed record CopilotAccentOption(CopilotAccent Accent)
    {
        public override string ToString()
            => Accent switch
            {
                CopilotAccent.Blue => Strings.T("ブルー（既定）"),
                CopilotAccent.Green => Strings.T("グリーン"),
                CopilotAccent.Sky => Strings.T("スカイ"),
                CopilotAccent.Purple => Strings.T("パープル"),
                CopilotAccent.Slate => Strings.T("スレート"),
                _ => Accent.ToString()
            };
    }
}
