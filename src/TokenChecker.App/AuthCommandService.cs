using System.Diagnostics;

namespace TokenChecker.App;

internal sealed class AuthCommandService
{
    public bool IsClaudeAvailable() => TryLocate("claude", out _);
    public bool IsCodexAvailable() => TryLocate("codex", out _);

    public AuthLaunchResult LaunchClaudeLogin()
    {
        if (!TryLocate("claude", out var command))
        {
            return AuthLaunchResult.NotInstalled("Claude Code");
        }

        return OpenInteractiveConsole(
            "Claude Code",
            command,
            arguments: null,
            successHint: Strings.T("Claude Code のコンソールを開きました。プロンプトで /login と入力してログインを完了し、その後に『認証状態を再確認』を実行してください。"));
    }

    public AuthLaunchResult LaunchClaudeLogout()
    {
        if (!TryLocate("claude", out var command))
        {
            return AuthLaunchResult.NotInstalled("Claude Code");
        }

        return OpenInteractiveConsole(
            "Claude Code",
            command,
            arguments: null,
            successHint: Strings.T("Claude Code のコンソールを開きました。プロンプトで /logout と入力してログアウトを完了してください。"));
    }

    public AuthLaunchResult LaunchCodexLogin()
    {
        if (!TryLocate("codex", out var command))
        {
            return AuthLaunchResult.NotInstalled("Codex");
        }

        return OpenInteractiveConsole(
            "Codex",
            command,
            arguments: "login",
            successHint: Strings.T("Codex のコンソールを開きました。ブラウザでサインインを完了してから『認証状態を再確認』を実行してください。Codex は ChatGPT ログインを推奨します（API キー認証では使用率を取得できません）。"));
    }

    public AuthLaunchResult LaunchCodexLogout()
    {
        if (!TryLocate("codex", out var command))
        {
            return AuthLaunchResult.NotInstalled("Codex");
        }

        return OpenInteractiveConsole(
            "Codex",
            command,
            arguments: "logout",
            successHint: Strings.T("Codex のコンソールを開きました。ログアウト結果が表示されたらウィンドウを閉じてください。"));
    }

    private static AuthLaunchResult OpenInteractiveConsole(
        string serviceName,
        string command,
        string? arguments,
        string successHint)
    {
        try
        {
            var quotedCommand = "\"" + command + "\"";
            var fullArgs = string.IsNullOrEmpty(arguments)
                ? quotedCommand
                : $"{quotedCommand} {arguments}";

            // Use cmd.exe /k so the user can see and interact with the CLI.
            // No path or username from this app is shown to stdout/log;
            // the only path that appears is the cmd window's working
            // directory, which is OS-controlled and not produced by us.
            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/k {fullArgs}",
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Normal,
                CreateNoWindow = false
            };

            using var process = Process.Start(psi);
            if (process is null)
            {
                return AuthLaunchResult.Failed(serviceName);
            }

            return AuthLaunchResult.Success(successHint);
        }
        catch
        {
            return AuthLaunchResult.Failed(serviceName);
        }
    }

    private static bool TryLocate(string commandName, out string commandPath)
    {
        commandPath = string.Empty;
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var extensions = OperatingSystem.IsWindows()
            ? GetPathExtensions()
            : new[] { string.Empty };

        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            foreach (var extension in extensions)
            {
                var candidate = Path.Combine(directory, commandName + extension);
                if (File.Exists(candidate))
                {
                    commandPath = candidate;
                    return true;
                }
            }
        }

        return false;
    }

    private static string[] GetPathExtensions()
    {
        var pathExt = Environment.GetEnvironmentVariable("PATHEXT");
        if (string.IsNullOrWhiteSpace(pathExt))
        {
            return new[] { ".exe", ".cmd", ".bat", string.Empty };
        }

        return pathExt
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Append(string.Empty)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}

internal sealed record AuthLaunchResult(bool Ok, string Message)
{
    public static AuthLaunchResult Success(string message) => new(true, message);

    public static AuthLaunchResult NotInstalled(string serviceName)
        => new(false, Strings.Tf("{0} CLI が見つかりません。先にインストールしてください。", serviceName));

    public static AuthLaunchResult Failed(string serviceName)
        => new(false, Strings.Tf("{0} のコンソールを開けませんでした。", serviceName));
}
