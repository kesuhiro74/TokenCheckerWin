using System.Text.RegularExpressions;
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
            ProviderStatus.RateLimited => "レート制限中",
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
            ProviderStatus.NotLoggedIn => $"{serviceName} にログインが必要です",
            ProviderStatus.Unauthorized => $"{serviceName} の認証に失敗しました。再ログインしてください",
            ProviderStatus.RateLimited => $"{serviceName} のレート制限に達しています",
            ProviderStatus.Error => hasFallbackWindows
                ? "一時的に取得できません。前回成功値を表示しています"
                : "一時的に取得できません",
            _ => $"{serviceName} の状態を確認できません"
        };

    public static string SafeDiagnostics(string? rawMessage)
    {
        if (string.IsNullOrWhiteSpace(rawMessage))
        {
            return "";
        }

        var masked = Regex.Replace(rawMessage, @"[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}", "<email>", RegexOptions.IgnoreCase);
        masked = Regex.Replace(masked, @"[A-Za-z]:\\(?:[^\\\s]+\\)*[^\\\s]*", "<path>");
        masked = Regex.Replace(masked, @"/(?:[^/\s]+/)+[^/\s]*", "<path>");
        masked = Regex.Replace(masked, @"(?i)(token|secret|key|authorization|bearer)\s*[:=]\s*\S+", "$1=<redacted>");
        masked = Regex.Replace(masked, @"\b[A-Za-z0-9_-]{32,}\b", "<redacted>");

        return masked.Length <= 400 ? masked : masked[..400];
    }
}
