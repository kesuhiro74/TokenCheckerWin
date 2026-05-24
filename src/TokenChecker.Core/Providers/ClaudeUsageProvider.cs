using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TokenChecker.Core.Providers;

public sealed class ClaudeUsageProvider : IUsageProvider
{
    private const string CredentialsFileName = ".credentials.json";
    private const string UsageEndpoint = "https://api.anthropic.com/api/oauth/usage";
    private const string OAuthBetaHeader = "oauth-2025-04-20";

    private static readonly TimeSpan VersionTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan ProcessKillTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan UsageTimeout = TimeSpan.FromSeconds(10);
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = Timeout.InfiniteTimeSpan
    };

    public string ServiceName => "Claude";

    public async Task<ServiceUsage> GetUsageAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var claudeFound = CommandLineProbe.TryFindOnPath("claude", out var claudeCommand);
        if (!claudeFound)
        {
            return new ServiceUsage(
                ServiceName,
                ProviderStatus.NotInstalled,
                BuildMessage(claudeFound: false, versionPresent: false, credentialsPresent: false, configDirSource: "default"),
                Array.Empty<RateLimitWindow>());
        }

        var versionPresent = await HasVersionAsync(claudeCommand, cancellationToken).ConfigureAwait(false);
        var config = GetConfigDirectory();
        var credentialsPresent = CredentialsFileExists(config.Directory);
        var message = BuildMessage(claudeFound, versionPresent, credentialsPresent, config.Source);

        if (!credentialsPresent)
        {
            return new ServiceUsage(
                ServiceName,
                ProviderStatus.NotLoggedIn,
                message,
                Array.Empty<RateLimitWindow>());
        }

        var usageResult = await TryReadUsageWindowsAsync(config.Directory, cancellationToken).ConfigureAwait(false);
        if (usageResult.Status == ProviderStatus.Available)
        {
            return new ServiceUsage(
                ServiceName,
                ProviderStatus.Available,
                $"{message} usageApi=available;",
                usageResult.Windows);
        }

        return new ServiceUsage(
            ServiceName,
            usageResult.Status,
            $"{message} usageApi={usageResult.SafeSummary};",
            Array.Empty<RateLimitWindow>());
    }

    private static async Task<bool> HasVersionAsync(string claudeCommand, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(VersionTimeout);

        using var process = Process.Start(CreateVersionStartInfo(claudeCommand));

        if (process is null)
        {
            return false;
        }

        try
        {
            process.ErrorDataReceived += static (_, _) => { };
            process.OutputDataReceived += static (_, _) => { };
            process.BeginErrorReadLine();
            process.BeginOutputReadLine();

            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            return process.ExitCode == 0;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await StopProcessAsync(process).ConfigureAwait(false);
            throw;
        }
        catch (OperationCanceledException)
        {
            await StopProcessAsync(process).ConfigureAwait(false);
            return false;
        }
        catch
        {
            return false;
        }
    }

    private static async Task StopProcessAsync(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            return;
        }

        try
        {
            using var timeout = new CancellationTokenSource(ProcessKillTimeout);
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch
        {
            // Best-effort cleanup only.
        }
    }

    private static ProcessStartInfo CreateVersionStartInfo(string claudeCommand)
    {
        if (OperatingSystem.IsWindows() && string.Equals(Path.GetExtension(claudeCommand), ".cmd", StringComparison.OrdinalIgnoreCase))
        {
            return new ProcessStartInfo("cmd.exe", $"/d /c \"{claudeCommand}\" --version")
            {
                RedirectStandardInput = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
        }

        return new ProcessStartInfo(claudeCommand, "--version")
        {
            RedirectStandardInput = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
    }

    private static (string Directory, string Source) GetConfigDirectory()
    {
        var configured = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return (configured, "env");
        }

        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return (Path.Combine(profile, ".claude"), "default");
    }

    private static bool CredentialsFileExists(string configDirectory)
    {
        try
        {
            return File.Exists(Path.Combine(configDirectory, CredentialsFileName));
        }
        catch
        {
            return false;
        }
    }

    private static async Task<ClaudeUsageReadResult> TryReadUsageWindowsAsync(
        string configDirectory,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var accessToken = await TryReadAccessTokenAsync(configDirectory, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                return ClaudeUsageReadResult.Failed(ProviderStatus.Error, "accessTokenPresent=false");
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(UsageTimeout);

            using var request = new HttpRequestMessage(HttpMethod.Get, UsageEndpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            request.Headers.TryAddWithoutValidation("anthropic-beta", OAuthBetaHeader);

            using var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                .ConfigureAwait(false);

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                return ClaudeUsageReadResult.Failed(ProviderStatus.Unauthorized, "unauthorized");
            }

            if ((int)response.StatusCode == 429)
            {
                return ClaudeUsageReadResult.Failed(ProviderStatus.RateLimited, "rateLimited");
            }

            if ((int)response.StatusCode >= 500)
            {
                return ClaudeUsageReadResult.Failed(ProviderStatus.Error, "serverError");
            }

            if (!response.IsSuccessStatusCode)
            {
                return ClaudeUsageReadResult.Failed(ProviderStatus.Error, "httpError");
            }

            var body = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
            var root = JsonNode.Parse(body);
            if (root is null)
            {
                return ClaudeUsageReadResult.Failed(ProviderStatus.Error, "invalidJson");
            }

            var windows = ParseUsageWindows(root).ToArray();
            return windows.Length > 0
                ? ClaudeUsageReadResult.Available(windows)
                : ClaudeUsageReadResult.Failed(ProviderStatus.Error, "unexpectedJson");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return ClaudeUsageReadResult.Failed(ProviderStatus.Error, "timeout");
        }
        catch (JsonException)
        {
            return ClaudeUsageReadResult.Failed(ProviderStatus.Error, "invalidJson");
        }
        catch
        {
            return ClaudeUsageReadResult.Failed(ProviderStatus.Error, "usageReadFailed");
        }
    }

    private static async Task<string?> TryReadAccessTokenAsync(string configDirectory, CancellationToken cancellationToken)
    {
        try
        {
            var credentialsPath = Path.Combine(configDirectory, CredentialsFileName);
            await using var stream = File.OpenRead(credentialsPath);
            var root = await JsonNode.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (root is null)
            {
                return null;
            }

            return GetString(root["claudeAiOauth"]?["accessToken"])
                ?? GetString(root["claudeAiOauth"]?["access_token"])
                ?? GetString(root["accessToken"])
                ?? GetString(root["access_token"]);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    private static IEnumerable<RateLimitWindow> ParseUsageWindows(JsonNode root)
    {
        var fiveHour = ParseUsageWindow(root["five_hour"], "Claude 5h", 300);
        if (fiveHour is not null)
        {
            yield return fiveHour;
        }

        var sevenDay = ParseUsageWindow(root["seven_day"], "Claude Weekly", 10080);
        if (sevenDay is not null)
        {
            yield return sevenDay;
        }
    }

    private static RateLimitWindow? ParseUsageWindow(JsonNode? node, string name, long durationMins)
    {
        if (node is null || node.GetValueKind() == JsonValueKind.Null)
        {
            return null;
        }

        var usedPercent = GetDouble(node["utilization"]);
        if (usedPercent is null)
        {
            return null;
        }

        usedPercent = Math.Clamp(usedPercent.Value, 0, 100);
        var resetAt = GetDateTimeOffset(node["resets_at"]);
        var used = (long)Math.Round(usedPercent.Value, MidpointRounding.AwayFromZero);
        var remaining = Math.Max(0, 100 - (long)Math.Ceiling(usedPercent.Value));

        return new RateLimitWindow(
            name,
            resetAt,
            used,
            100,
            remaining,
            usedPercent,
            durationMins);
    }

    private static string? GetString(JsonNode? node)
    {
        if (node is null || node.GetValueKind() == JsonValueKind.Null)
        {
            return null;
        }

        return node.GetValue<string>();
    }

    private static double? GetDouble(JsonNode? node)
    {
        if (node is null || node.GetValueKind() == JsonValueKind.Null)
        {
            return null;
        }

        if (node.GetValueKind() == JsonValueKind.Number)
        {
            return node.GetValue<double>();
        }

        var text = node.GetValue<string>();
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static DateTimeOffset? GetDateTimeOffset(JsonNode? node)
    {
        var text = GetString(node);
        return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed.ToUniversalTime()
            : null;
    }

    private static string BuildMessage(bool claudeFound, bool versionPresent, bool credentialsPresent, string configDirSource)
    {
        var safeSource = string.Equals(configDirSource, "env", StringComparison.OrdinalIgnoreCase)
            ? "env"
            : "default";

        return $"claudeFound={FormatBool(claudeFound)}; "
            + $"versionPresent={FormatBool(versionPresent)}; "
            + $"credentialsPresent={FormatBool(credentialsPresent)}; "
            + $"configDirSource={safeSource};";
    }

    private static string FormatBool(bool value)
        => value ? "true" : "false";

    private sealed record ClaudeUsageReadResult(
        ProviderStatus Status,
        string SafeSummary,
        IReadOnlyList<RateLimitWindow> Windows)
    {
        public static ClaudeUsageReadResult Available(IReadOnlyList<RateLimitWindow> windows)
            => new(ProviderStatus.Available, "available", windows);

        public static ClaudeUsageReadResult Failed(ProviderStatus status, string safeSummary)
            => new(status, safeSummary, Array.Empty<RateLimitWindow>());
    }
}
