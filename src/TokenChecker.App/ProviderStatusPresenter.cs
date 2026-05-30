using TokenChecker.Core;

namespace TokenChecker.App;

internal static class ProviderStatusPresenter
{
    public static string BadgeText(ProviderStatus status)
        => status switch
        {
            ProviderStatus.Available => "正常取得",
            ProviderStatus.NotInstalled => "未インストール",
            ProviderStatus.NotLoggedIn => "未ログイン",
            ProviderStatus.Unauthorized => "認証エラー",
            // "レート制限中" was easily misread as "the user's LLM quota is
            // throttled", which is not what this state actually means — it
            // only fires when the usage endpoint itself returns HTTP 429.
            ProviderStatus.RateLimited => "取得を一時制限中",
            ProviderStatus.Error => "取得失敗",
            _ => "状態不明"
        };

    public static string BuildDebugSummary(string serviceName, ServiceUsage? current, ServiceUsage? fallback)
    {
        var currentStatus = current?.Status.ToString() ?? "null";
        var currentCount = current?.Windows.Count ?? 0;
        var fallbackStatus = fallback?.Status.ToString() ?? "null";
        var fallbackCount = fallback?.Windows.Count ?? 0;
        return $"[debug] serviceName={serviceName}; "
            + $"currentStatus={currentStatus}; currentWindowCount={currentCount}; "
            + $"fallbackStatus={fallbackStatus}; fallbackWindowCount={fallbackCount};";
    }

    public static string FriendlyMessage(string serviceName, ProviderStatus status, bool hasFallbackWindows)
        => status switch
        {
            ProviderStatus.Available => $"{serviceName} の使用率を取得できています",
            ProviderStatus.NotInstalled => $"{serviceName} CLI が見つかりません",
            ProviderStatus.NotLoggedIn => $"{serviceName}にログインしてください",
            ProviderStatus.Unauthorized => $"{serviceName} の認証に失敗しました。再ログインしてください",
            ProviderStatus.RateLimited => hasFallbackWindows
                ? $"{serviceName} の取得が一時的に制限されています。前回成功値を表示しています"
                : $"{serviceName} の取得が一時的に制限されています",
            ProviderStatus.Error => hasFallbackWindows
                ? "一時的に取得できません。前回成功値を表示しています"
                : "一時的に取得できません",
            _ => $"{serviceName} の状態を確認できません"
        };

    // Delegates to the shared masker (TokenChecker.Core.DiagnosticMasker) so the
    // privacy rules live in exactly one place; the UI allows a longer tail than
    // the providers' own summaries.
    public static string SafeDiagnostics(string? rawMessage)
        => DiagnosticMasker.Mask(rawMessage, maxLength: 400);
}
