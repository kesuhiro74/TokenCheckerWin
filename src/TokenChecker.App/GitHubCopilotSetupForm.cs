using System.Diagnostics;
using TokenChecker.Core;
using TokenChecker.Core.Providers.GitHubCopilot;

namespace TokenChecker.App;

// First-time setup wizard for GitHub Copilot AI Credits. We deliberately keep the
// PAT + GITHUB_TOKEN environment-variable model (no in-app login / OAuth / Device
// Flow). The token is NEVER entered, stored, displayed, or logged by this form —
// it only points the user at the right page, shows step-by-step instructions, and
// reads GITHUB_TOKEN from the current process environment for the connection test.
internal sealed class GitHubCopilotSetupForm : Form
{
    // Public GitHub page only (no token/login). Constant so it is easy to audit.
    private const string TokenCreateUrl = "https://github.com/settings/personal-access-tokens/new";

    private const string DescriptionText =
        "GitHub Copilot AI Credits を取得するには、GitHub の fine-grained personal access token が必要です。\r\n\r\n"
        + "この画面では token を入力しません。\r\n"
        + "GitHub の画面で token を作成し、Windows のユーザー環境変数 GITHUB_TOKEN に設定します。\r\n\r\n"
        + "必要な権限:\r\n"
        + "  - User permissions: Plan = read\r\n\r\n"
        + "TokenCheckerWin はトークンを保存しません。";

    // Shown in the output area when the user opens the token-creation page, so the
    // GitHub-side choices are clear. No token value appears here.
    private const string TokenCreateStepsText =
        "GitHub の fine-grained personal access token 作成画面で、以下を設定してください。\r\n\r\n"
        + "1. Token name\r\n"
        + "   Token name に分かりやすい名前を入力してください。\r\n"
        + "   例: TokenChecker\r\n\r\n"
        + "2. Expiration\r\n"
        + "   有効期限を設定してください。\r\n"
        + "   推奨: 90 days\r\n"
        + "   継続利用を優先する場合は No expiration でも利用できます。\r\n\r\n"
        + "3. Permissions\r\n"
        + "   Permissions の [+ Add permissions] を押してください。\r\n"
        + "   一覧から [Plan] を選択してください。\r\n"
        + "   権限が [Read-only] になっていることを確認してください。\r\n\r\n"
        + "4. Generate token\r\n"
        + "   画面下部の [Generate token] を押してください。\r\n\r\n"
        + "5. 作成された token をコピー\r\n"
        + "   表示された token をコピーしてください。\r\n"
        + "   TokenCheckerWin には token を入力しません。\r\n"
        + "   次に「環境変数の設定方法を表示」を押して、GITHUB_TOKEN に設定してください。";

    private const string EnvVarHelpText =
        "Windows のユーザー環境変数 GITHUB_TOKEN を設定する手順:\r\n\r\n"
        + "1. スタートメニューで「環境変数」と検索し、「環境変数を編集」を開く\r\n"
        + "   （または「システムのプロパティ」→「環境変数」）。\r\n"
        + "2. 「ユーザー環境変数」で「新規」をクリック。\r\n"
        + "3. 変数名に GITHUB_TOKEN、変数値に作成した fine-grained PAT を入力して保存。\r\n\r\n"
        + "PowerShell で設定する場合の例:\r\n\r\n"
        + "    [Environment]::SetEnvironmentVariable(\"GITHUB_TOKEN\", \"<your-token>\", \"User\")\r\n\r\n"
        + "  - <your-token> を GitHub で生成した token に置き換えてください。\r\n"
        + "  - 設定後、TokenCheckerWin を再起動してください。\r\n"
        + "  - 既に開いている PowerShell や起動中のアプリには反映されない場合があります。\r\n"
        + "  - TokenCheckerWin は token を保存せず、環境変数から読み取るだけです。";

    private readonly int? _allowance;
    private readonly TextBox _output;
    private readonly Button _openTokenButton;
    private readonly Button _showEnvButton;
    private readonly Button _testButton;

    public GitHubCopilotSetupForm(int? allowance)
    {
        _allowance = allowance;

        Text = "GitHub Copilot 初回設定";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(524, 470);
        Font = new Font("Segoe UI", 9F);

        var heading = new Label
        {
            Text = "GitHub Copilot 初回設定",
            AutoSize = true,
            Font = new Font("Segoe UI", 12F, FontStyle.Bold),
            Location = new Point(16, 14)
        };

        var description = new Label
        {
            Text = DescriptionText,
            AutoSize = false,
            Size = new Size(492, 148),
            Location = new Point(16, 46),
            TextAlign = ContentAlignment.TopLeft
        };

        _output = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.FromArgb(247, 248, 252),
            Location = new Point(16, 286),
            Size = new Size(492, 130),
            TabStop = false,
            WordWrap = true,
            Font = new Font("Consolas", 9F),
            Text = "「GitHub のトークン作成ページを開く」「環境変数の設定方法を表示」「接続テスト」の結果はここに表示されます。"
        };

        _openTokenButton = new Button
        {
            Text = "GitHub のトークン作成ページを開く",
            Location = new Point(16, 200),
            Size = new Size(244, 30)
        };
        _openTokenButton.Click += (_, _) =>
        {
            _output.Text = TokenCreateStepsText;
            OpenUrl(TokenCreateUrl);
        };

        _showEnvButton = new Button
        {
            Text = "環境変数の設定方法を表示",
            Location = new Point(16, 236),
            Size = new Size(244, 30)
        };
        _showEnvButton.Click += (_, _) => _output.Text = EnvVarHelpText;

        _testButton = new Button
        {
            Text = "接続テスト",
            Location = new Point(272, 200),
            Size = new Size(140, 30)
        };
        _testButton.Click += async (_, _) => await RunTestAsync().ConfigureAwait(true);

        var closeButton = new Button
        {
            Text = "閉じる",
            DialogResult = DialogResult.OK,
            Location = new Point(432, 424),
            Size = new Size(76, 28)
        };

        Controls.Add(heading);
        Controls.Add(description);
        Controls.Add(_openTokenButton);
        Controls.Add(_showEnvButton);
        Controls.Add(_testButton);
        Controls.Add(_output);
        Controls.Add(closeButton);

        AcceptButton = closeButton;
        CancelButton = closeButton;
    }

    private async Task RunTestAsync()
    {
        _testButton.Enabled = false;
        _output.Text = "接続テスト中...";
        try
        {
            _output.Text = await RunConnectionTestAsync(_allowance).ConfigureAwait(true);
        }
        finally
        {
            if (!IsDisposed)
            {
                _testButton.Enabled = true;
            }
        }
    }

    private void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            // Policy: do not surface URLs in the UI. Tell the user what to open
            // in words instead of printing the URL.
            MessageBox.Show(
                this,
                "ブラウザを開けませんでした。GitHub の personal access token 作成ページを手動で開いてください。",
                "TokenCheckerWin",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    // Runs the existing GitHubCopilotUsageProvider against the current process
    // GITHUB_TOKEN and returns a SAFE, fixed message (usage numbers on success,
    // canned text on failure). The provider's diagnostic Message is used ONLY to
    // tell 401 from 403 — it is never returned/displayed. No token / login / URL /
    // path / email is ever surfaced. Shared by the wizard and the settings dialog's
    // "接続テスト" button; callers disable their button while it runs.
    public static async Task<string> RunConnectionTestAsync(int? allowance)
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GITHUB_TOKEN")))
        {
            return "GITHUB_TOKEN が未設定です";
        }

        ServiceUsage usage;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            usage = await new GitHubCopilotUsageProvider().GetUsageAsync(timeout.Token).ConfigureAwait(true);
        }
        catch
        {
            return "GitHub Copilot AI Credits を取得できませんでした";
        }

        switch (usage.Status)
        {
            case ProviderStatus.Available:
                var window = usage.Windows.FirstOrDefault(w => w.WindowDurationMins == 43200);
                var used = window?.Used ?? 0;
                if (allowance is int cap && cap > 0)
                {
                    var percent = Math.Min(100d, used / (double)cap * 100d);
                    return $"正常取得しました。\r\n当月使用量: {used:N0} / {cap:N0} credits\r\n使用率: {Math.Round(percent):0}%";
                }

                return $"正常取得しました。\r\n当月使用量: {used:N0} credits";

            case ProviderStatus.NotLoggedIn:
                return "GITHUB_TOKEN が未設定です";

            case ProviderStatus.Unauthorized:
                // The masked Message carries "(401)" / "(403)" tokens (no secrets);
                // used only to choose the canned message, never displayed.
                return usage.Message?.Contains("(403)", StringComparison.Ordinal) == true
                    ? "権限不足です。fine-grained PAT の User permissions: Plan = read を確認してください"
                    : "トークンが無効、期限切れ、または取り消されています";

            case ProviderStatus.RateLimited:
                return "GitHub API のレート制限中です。しばらくしてから再試行してください";

            default:
                return "GitHub Copilot AI Credits を取得できませんでした";
        }
    }
}
