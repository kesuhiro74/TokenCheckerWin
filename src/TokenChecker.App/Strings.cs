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
        ["詳細を表示"] = "Show details",
        ["詳細を隠す"] = "Hide details",
        ["更新中"] = "Updating",
        ["最終更新: 更新中"] = "Last updated: updating",
        ["最終更新: {0}"] = "Last updated: {0}",

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
    };
}
