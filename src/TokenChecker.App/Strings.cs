namespace TokenChecker.App;

// UI localization table (English / Japanese), resolved once at startup like the
// theme (see Program / Strings.Apply). The Japanese text is the source key; in
// English mode it is looked up in the English map (falling back to the Japanese
// text if a translation is missing, so the UI is never blank).
//
// T(ja)            -> a plain string.
// Tf(jaFormat,...) -> a composite-format string (Japanese template is the key;
//                     the English template is filled with the same args).
//
// This is a custom static table (no .resx / satellite assemblies) so it ships in
// the single-file self-contained build and is unit-testable (English-map parity).
internal static class Strings
{
    private static bool _ja = true;

    // Called ONCE at startup (Program), before any window is built. There is no
    // live re-language; a settings change takes effect on the next launch.
    public static void Apply(bool japanese) => _ja = japanese;

    public static bool IsJapanese => _ja;

    public static string T(string ja)
        => _ja ? ja : (English.TryGetValue(ja, out var en) ? en : ja);

    public static string Tf(string jaFormat, params object?[] args)
        => string.Format(_ja ? jaFormat : (English.TryGetValue(jaFormat, out var en) ? en : jaFormat), args);

    // Japanese source text -> English. Keep entries grouped by UI area. Every
    // user-facing Japanese literal passed to T/Tf should have a key here.
    internal static readonly Dictionary<string, string> English = new(StringComparer.Ordinal)
    {
        // ----- Reset-time text (ResetTimeFormatter) -----
        ["リセット時刻不明"] = "Reset time unknown",
        ["まもなくリセット"] = "Resetting soon",
        ["あと{0}日{1}時間"] = "in {0}d {1}h",
        ["あと{0}時間{1:00}分"] = "in {0}h {1:00}m",
        ["あと{0}分"] = "in {0}m",
        ["まもなく（{0}リセット）"] = "soon (resets {0})",
        ["あと{0}分（{1}リセット）"] = "in {0}m (resets {1})",
        ["あと{0}時間{1:00}分（{2}リセット）"] = "in {0}h {1:00}m (resets {2})",
        ["あと{0}日{1}時間（{2}リセット）"] = "in {0}d {1}h (resets {2})",
        ["{0}時間{1:00}分（{2} リセット）"] = "{0}h {1:00}m (resets {2})",
        ["{0}分（{1} リセット）"] = "{0}m (resets {1})",
        ["（{0} リセット）"] = "(resets {0})",

        // ----- Settings dialog: common group -----
        ["更新間隔"] = "Update interval",
        ["Windowsログイン時に自動起動"] = "Start at Windows login",
        ["テーマ"] = "Theme",
        ["言語"] = "Language",
        ["（再起動で反映）"] = "(restart to apply)",
        ["システム連動"] = "System (auto)",
        ["ライト"] = "Light",
        ["ダーク"] = "Dark",
        ["30秒"] = "30 sec",
        ["1分"] = "1 min",
        ["5分"] = "5 min",
        ["10分"] = "10 min",
        ["{0}秒"] = "{0} sec",
        ["キャンセル"] = "Cancel",

        // ----- Auth console hints (AuthCommandService) -----
        ["Claude Code のコンソールを開きました。プロンプトで /login と入力してログインを完了し、その後に『認証状態を再確認』を実行してください。"]
            = "Opened the Claude Code console. Type /login at the prompt to finish logging in, then click \"Re-check auth status\".",
        ["Claude Code のコンソールを開きました。プロンプトで /logout と入力してログアウトを完了してください。"]
            = "Opened the Claude Code console. Type /logout at the prompt to finish logging out.",
        ["Codex のコンソールを開きました。ブラウザでサインインを完了してから『認証状態を再確認』を実行してください。Codex は ChatGPT ログインを推奨します（API キー認証では使用率を取得できません）。"]
            = "Opened the Codex console. Finish signing in via the browser, then click \"Re-check auth status\". Codex recommends ChatGPT login (usage cannot be read with API-key auth).",
        ["Codex のコンソールを開きました。ログアウト結果が表示されたらウィンドウを閉じてください。"]
            = "Opened the Codex console. Close the window once the logout result is shown.",
        ["{0} CLI が見つかりません。先にインストールしてください。"] = "{0} CLI not found. Please install it first.",
        ["{0} のコンソールを開けませんでした。"] = "Could not open the {0} console.",

        // ----- Provider status badges & messages (ProviderStatusPresenter) -----
        ["正常取得"] = "OK",
        ["未インストール"] = "Not installed",
        ["未ログイン"] = "Not logged in",
        ["認証エラー"] = "Auth error",
        ["取得を一時制限中"] = "Temporarily rate-limited",
        ["取得失敗"] = "Fetch failed",
        ["状態不明"] = "Unknown",
        ["{0} の使用率を取得できています"] = "{0} usage is being read",
        ["{0} CLI が見つかりません"] = "{0} CLI not found",
        ["{0}にログインしてください"] = "Please log in to {0}",
        ["{0} の認証に失敗しました。再ログインしてください"] = "{0} authentication failed — please log in again",
        ["{0} の取得が一時的に制限されています。前回成功値を表示しています"] = "{0} is temporarily rate-limited — showing the last successful values",
        ["{0} の取得が一時的に制限されています"] = "{0} is temporarily rate-limited",
        ["一時的に取得できません。前回成功値を表示しています"] = "Temporarily unavailable — showing the last successful values",
        ["一時的に取得できません"] = "Temporarily unavailable",
        ["{0} の状態を確認できません"] = "Cannot determine {0} status",

        // ----- Status window (StatusForm) -----
        ["5時間"] = "5-hour",
        ["週次"] = "Weekly",
        // Minimum-mode status line window labels (identity entries: the line is
        // deliberately language-neutral, but routing through T() keeps the
        // "every UI string goes through Strings" convention auditable).
        ["5h"] = "5h",
        ["7d"] = "7d",
        ["詳細を表示"] = "Show details",
        ["詳細を隠す"] = "Hide details",
        ["更新中"] = "Updating",
        ["最終更新: 更新中"] = "Last updated: updating",
        ["最終更新: {0}"] = "Last updated: {0}",
        ["{0}: リセット時刻不明"] = "{0}: reset time unknown",
        ["¥{0:N0} (daily)"] = "¥{0:N0} (daily)",

        // ----- Tray menu & tooltips (TrayApplicationContext) -----
        ["今すぐ更新"] = "Refresh now",
        ["通常モード"] = "Normal",
        ["コンパクトモード"] = "Compact",
        ["ミニマムモード"] = "Minimum",
        ["Claude/Codexステータス表示モード"] = "Claude/Codex status display mode",
        ["常時表示"] = "Always show",
        ["ホバー表示"] = "Hover preview",
        ["GitHubCopilot表示モード"] = "GitHub Copilot display mode",
        ["設定"] = "Settings",
        ["終了"] = "Exit",
        ["TokenCheckerWin 更新中"] = "TokenCheckerWin updating",
        ["GitHub Copilot 更新中"] = "GitHub Copilot updating",
        ["未取得"] = "No data",

        // ----- Copilot window (CopilotWindow) -----
        ["使用済み"] = "used",
        ["credits 使用"] = "credits used",
        ["設定画面の「初回設定」から手順を確認してください"] = "See the steps under \"First-time setup\" in settings",
        ["このペースだと {0} 頃に 100%"] = "At this pace, 100% around {0}",
        ["このペースなら今月は上限に到達しない見込み"] = "At this pace it won't reach the cap this month",
        ["すでに上限に到達しています"] = "Already at the cap",
        ["予測にはデータ不足"] = "Not enough data to project",
        ["クレジット"] = "Credits",
        ["プランを選ぶと上限・残量を表示"] = "Pick a plan to show the cap & remaining",
        ["リセット目安 {0}/{1}"] = "Reset ~{0}/{1}",
        ["リセット目安 月初"] = "Reset ~month start",
        ["本日"] = "Today",
        ["+{0}（+{1}%）"] = "+{0} (+{1}%)",
        ["+{0} credits"] = "+{0} credits",
        ["本日: 未計測"] = "Today: not measured",
        ["GITHUB_TOKEN が未設定です"] = "GITHUB_TOKEN is not set",
        ["GITHUB_TOKEN の Plan(Read) 権限・個人課金・Enhanced Billing 対象を確認してください"]
            = "Check GITHUB_TOKEN's Plan (Read) permission, personal billing, and Enhanced Billing eligibility",
        ["GITHUB_TOKEN が無効または期限切れです"] = "GITHUB_TOKEN is invalid or expired",
        ["取得が一時的に制限されています。前回成功値を表示しています"] = "Temporarily rate-limited — showing the last successful values",
        ["取得が一時的に制限されています"] = "Temporarily rate-limited",

        // ----- First-time setup wizard (GitHubCopilotSetupForm) -----
        ["GitHub Copilot 初回設定"] = "GitHub Copilot first-time setup",
        ["「GitHub のトークン作成ページを開く」「環境変数の設定方法を表示」「接続テスト」の結果はここに表示されます。"]
            = "Output from \"Open GitHub token page\", \"Show how to set the environment variable\", and \"Connection test\" appears here.",
        ["GitHub のトークン作成ページを開く"] = "Open GitHub token page",
        ["環境変数の設定方法を表示"] = "Show how to set the environment variable",
        ["接続テスト"] = "Connection test",
        ["閉じる"] = "Close",
        ["接続テスト中..."] = "Testing connection...",
        ["ブラウザを開けませんでした。GitHub の personal access token 作成ページを手動で開いてください。"]
            = "Could not open the browser. Please open the GitHub personal access token page manually.",
        ["GitHub Copilot AI Credits を取得できませんでした"] = "Could not read GitHub Copilot AI Credits",
        ["正常取得しました。\r\n当月使用量: {0} / {1} credits\r\n使用率: {2}%"]
            = "Fetched successfully.\r\nThis month: {0} / {1} credits\r\nUsage: {2}%",
        ["正常取得しました。\r\n当月使用量: {0} credits"] = "Fetched successfully.\r\nThis month: {0} credits",
        ["権限不足です。fine-grained PAT の User permissions: Plan = read を確認してください"]
            = "Insufficient permission. Check the fine-grained PAT's User permissions: Plan = read.",
        ["トークンが無効、期限切れ、または取り消されています"] = "The token is invalid, expired, or revoked.",
        ["GitHub API のレート制限中です。しばらくしてから再試行してください"]
            = "The GitHub API is rate-limited. Please try again later.",

        ["GitHub Copilot AI Credits を取得するには、GitHub の fine-grained personal access token が必要です。\r\n\r\n"
            + "この画面では token を入力しません。\r\n"
            + "GitHub の画面で token を作成し、Windows のユーザー環境変数 GITHUB_TOKEN に設定します。\r\n\r\n"
            + "必要な権限:\r\n"
            + "  - User permissions: Plan = read\r\n\r\n"
            + "TokenCheckerWin はトークンを保存しません。"]
            = "To read GitHub Copilot AI Credits you need a GitHub fine-grained personal access token.\r\n\r\n"
            + "You do NOT enter the token on this screen.\r\n"
            + "Create the token on GitHub and set it in the Windows user environment variable GITHUB_TOKEN.\r\n\r\n"
            + "Required permission:\r\n"
            + "  - User permissions: Plan = read\r\n\r\n"
            + "TokenCheckerWin does not store the token.",

        ["GitHub の fine-grained personal access token 作成画面で、以下を設定してください。\r\n\r\n"
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
            + "   次に「環境変数の設定方法を表示」を押して、GITHUB_TOKEN に設定してください。"]
            = "On GitHub's fine-grained personal access token page, set the following.\r\n\r\n"
            + "1. Token name\r\n"
            + "   Enter a recognizable token name.\r\n"
            + "   e.g. TokenChecker\r\n\r\n"
            + "2. Expiration\r\n"
            + "   Set an expiration.\r\n"
            + "   Recommended: 90 days\r\n"
            + "   You may also use No expiration if you prefer uninterrupted use.\r\n\r\n"
            + "3. Permissions\r\n"
            + "   Click [+ Add permissions].\r\n"
            + "   Select [Plan] from the list.\r\n"
            + "   Make sure the permission is [Read-only].\r\n\r\n"
            + "4. Generate token\r\n"
            + "   Click [Generate token] at the bottom.\r\n\r\n"
            + "5. Copy the created token\r\n"
            + "   Copy the displayed token.\r\n"
            + "   You do NOT enter the token into TokenCheckerWin.\r\n"
            + "   Next, click \"Show how to set the environment variable\" and set GITHUB_TOKEN.",

        ["Windows のユーザー環境変数 GITHUB_TOKEN を設定する手順:\r\n\r\n"
            + "1. スタートメニューで「環境変数」と検索し、「環境変数を編集」を開く\r\n"
            + "   （または「システムのプロパティ」→「環境変数」）。\r\n"
            + "2. 「ユーザー環境変数」で「新規」をクリック。\r\n"
            + "3. 変数名に GITHUB_TOKEN、変数値に作成した fine-grained PAT を入力して保存。\r\n\r\n"
            + "PowerShell で設定する場合の例:\r\n\r\n"
            + "    [Environment]::SetEnvironmentVariable(\"GITHUB_TOKEN\", \"<your-token>\", \"User\")\r\n\r\n"
            + "  - <your-token> を GitHub で生成した token に置き換えてください。\r\n"
            + "  - 設定後、TokenCheckerWin を再起動してください。\r\n"
            + "  - 既に開いている PowerShell や起動中のアプリには反映されない場合があります。\r\n"
            + "  - TokenCheckerWin は token を保存せず、環境変数から読み取るだけです。"]
            = "Steps to set the Windows user environment variable GITHUB_TOKEN:\r\n\r\n"
            + "1. In the Start menu, search \"environment variables\" and open \"Edit environment variables for your account\"\r\n"
            + "   (or System Properties -> Environment Variables).\r\n"
            + "2. Under \"User variables\", click \"New\".\r\n"
            + "3. Enter GITHUB_TOKEN as the name and your fine-grained PAT as the value, then save.\r\n\r\n"
            + "Example using PowerShell:\r\n\r\n"
            + "    [Environment]::SetEnvironmentVariable(\"GITHUB_TOKEN\", \"<your-token>\", \"User\")\r\n\r\n"
            + "  - Replace <your-token> with the token you generated on GitHub.\r\n"
            + "  - After setting it, restart TokenCheckerWin.\r\n"
            + "  - Already-open PowerShell windows or running apps may not pick it up.\r\n"
            + "  - TokenCheckerWin does not store the token; it only reads the environment variable.",

        // ----- Settings dialog: groups, login status, options -----
        ["共通設定"] = "Common",
        ["Claude / Codex 設定"] = "Claude / Codex settings",
        ["Claude / Codex ウィンドウを表示"] = "Show the Claude / Codex window",
        ["表示方法"] = "Display method",
        ["表示モード"] = "Display mode",
        ["表示対象"] = "Shown services",
        ["ログイン状態"] = "Auth status",
        ["ログイン"] = "Log in",
        ["ログアウト"] = "Log out",
        ["認証状態を再確認"] = "Re-check auth status",
        ["確認中"] = "Checking...",
        ["ホスト未接続"] = "Host not connected",
        ["GitHub Copilot 設定"] = "GitHub Copilot settings",
        ["GitHub Copilot ウィンドウを表示"] = "Show the GitHub Copilot window",
        ["プラン"] = "Plan",
        ["Custom 上限"] = "Custom cap",
        ["配色"] = "Accent color",
        ["初回設定"] = "First-time setup",
        ["コマンドの実行に失敗しました。"] = "Failed to run the command.",
        ["正常"] = "OK",
        ["CLI未検出"] = "CLI not found",
        ["ホバー表示（トレイ）"] = "Hover preview (tray)",
        ["なし（使用量のみ）"] = "None (usage only)",
        ["Free（{0}）"] = "Free ({0})",
        ["Pro（{0}）"] = "Pro ({0})",
        ["Pro+（{0}）"] = "Pro+ ({0})",
        ["Max（{0}）"] = "Max ({0})",
        ["Custom（手入力）"] = "Custom (manual)",
        ["ブルー（既定）"] = "Blue (default)",
        ["グリーン"] = "Green",
        ["スカイ"] = "Sky",
        ["パープル"] = "Purple",
        ["スレート"] = "Slate",
    };
}
