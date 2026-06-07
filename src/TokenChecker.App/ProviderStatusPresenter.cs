using TokenChecker.Core;

namespace TokenChecker.App;

internal static class ProviderStatusPresenter
{
    public static string BadgeText(ProviderStatus status)
        => status switch
        {
            ProviderStatus.Available => Strings.T("正常取得"),
            ProviderStatus.NotInstalled => Strings.T("未インストール"),
            ProviderStatus.NotLoggedIn => Strings.T("未ログイン"),
            ProviderStatus.Unauthorized => Strings.T("認証エラー"),
            // "レート制限中" was easily misread as "the user's LLM quota is
            // throttled", which is not what this state actually means — it
            // only fires when the usage endpoint itself returns HTTP 429.
            ProviderStatus.RateLimited => Strings.T("取得を一時制限中"),
            ProviderStatus.Error => Strings.T("取得失敗"),
            _ => Strings.T("状態不明")
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
            ProviderStatus.Available => Strings.Tf("{0} の使用率を取得できています", serviceName),
            ProviderStatus.NotInstalled => Strings.Tf("{0} CLI が見つかりません", serviceName),
            ProviderStatus.NotLoggedIn => Strings.Tf("{0}にログインしてください", serviceName),
            ProviderStatus.Unauthorized => Strings.Tf("{0} の認証に失敗しました。再ログインしてください", serviceName),
            ProviderStatus.RateLimited => hasFallbackWindows
                ? Strings.Tf("{0} の取得が一時的に制限されています。前回成功値を表示しています", serviceName)
                : Strings.Tf("{0} の取得が一時的に制限されています", serviceName),
            ProviderStatus.Error => hasFallbackWindows
                ? Strings.T("一時的に取得できません。前回成功値を表示しています")
                : Strings.T("一時的に取得できません"),
            _ => Strings.Tf("{0} の状態を確認できません", serviceName)
        };

    // Delegates to the shared masker (TokenChecker.Core.DiagnosticMasker) so the
    // privacy rules live in exactly one place; the UI allows a longer tail than
    // the providers' own summaries.
    public static string SafeDiagnostics(string? rawMessage)
        => DiagnosticMasker.Mask(rawMessage, maxLength: 400);
}
