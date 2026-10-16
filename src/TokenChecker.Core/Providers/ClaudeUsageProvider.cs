using System.Diagnostics;
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

    // In-memory only (never persisted): after a 429 the endpoint is left alone
    // until this instant, per the Retry-After header. Written and read from
    // the single refresh path, so no extra synchronization is needed.
    private DateTimeOffset? _rateLimitedUntilUtc;

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
                usageResult.Windows,
                usageResult.Extras);
        }

        return new ServiceUsage(
            ServiceName,
            usageResult.Status,
            $"{message} usageApi={usageResult.SafeSummary};",
            Array.Empty<RateLimitWindow>(),
            // The plan label comes from the credentials file, so it is known
            // even when the usage call itself failed.
            usageResult.Extras);
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

    private async Task<ClaudeUsageReadResult> TryReadUsageWindowsAsync(
        string configDirectory,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            // One read of the credentials file per refresh: it yields the
            // access token plus the only two non-secret values we use (the
            // plan tier fields behind the plan label).
            var credentials = await ReadCredentialShapeAsync(configDirectory, cancellationToken).ConfigureAwait(false);
            var planExtras = BuildPlanExtras(credentials);
            var accessToken = credentials.AccessToken;

            if (string.IsNullOrWhiteSpace(accessToken))
            {
                // .credentials.json exists but contains no usable access token.
                // This typically means the user ran /logout (which can leave an
                // empty file behind), or the JSON structure does not match what
                // we expect. Treat it as "not logged in" so the UI can prompt
                // the user to log in instead of mislabeling it as a transient
                // error or rate-limit event.
                return ClaudeUsageReadResult.Failed(ProviderStatus.NotLoggedIn, "accessTokenPresent=false", planExtras);
            }

            // Honor a Retry-After cooldown from an earlier 429 before issuing
            // any HTTP at all, so a rate-limited account is not hammered by
            // the refresh timer.
            var now = DateTimeOffset.UtcNow;
            if (_rateLimitedUntilUtc is { } until && until > now)
            {
                return ClaudeUsageReadResult.Failed(ProviderStatus.RateLimited, "rateLimited;cooldown", planExtras);
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(UsageTimeout);

            using var request = new HttpRequestMessage(HttpMethod.Get, UsageEndpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            request.Headers.TryAddWithoutValidation("anthropic-beta", OAuthBetaHeader);

            using var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                .ConfigureAwait(false);

            if (ClaudeUsageStatusMapper.Classify(response.StatusCode) is { } httpFailure)
            {
                if (httpFailure.Status == ProviderStatus.RateLimited)
                {
                    StartRateLimitCooldown(response);
                }

                return ClaudeUsageReadResult.Failed(httpFailure.Status, httpFailure.SafeSummary, planExtras);
            }

            // A success clears any stale cooldown.
            _rateLimitedUntilUtc = null;

            var body = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
            var root = JsonNode.Parse(body);
            if (root is null)
            {
                return ClaudeUsageReadResult.Failed(ProviderStatus.Error, "invalidJson", planExtras);
            }

            var parsed = ClaudeUsageResponseParser.Parse(root, credentials.SubscriptionType, credentials.RateLimitTier);
            // An all-empty Extras carries nothing, so hand back null instead
            // and keep it out of the serialized snapshot entirely.
            var extras = parsed.Extras.IsEmpty ? null : parsed.Extras;
            return parsed.Windows.Count > 0
                ? ClaudeUsageReadResult.Available(parsed.Windows, extras)
                : ClaudeUsageReadResult.Failed(ProviderStatus.Error, "unexpectedJson", extras);
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

    private void StartRateLimitCooldown(HttpResponseMessage response)
    {
        var headerValue = response.Headers.TryGetValues("Retry-After", out var values)
            ? values.FirstOrDefault()
            : null;

        var cooldown = ClaudeRetryAfter.Parse(headerValue, DateTimeOffset.UtcNow);
        _rateLimitedUntilUtc = cooldown > TimeSpan.Zero
            ? DateTimeOffset.UtcNow + cooldown
            : null;
    }

    // Plan label only: it comes from the credentials file, so it is available
    // regardless of whether the usage call succeeds.
    private static ServiceExtras? BuildPlanExtras(CredentialShape credentials)
    {
        var planLabel = ClaudePlanLabel.From(credentials.SubscriptionType, credentials.RateLimitTier);
        return planLabel is null ? null : new ServiceExtras(PlanLabel: planLabel);
    }

    // Diagnostic-only probe used by TokenChecker.Poc's --usage-shape mode to
    // inspect the *shape* of the Claude usage API response (key paths and
    // value kinds, never raw secret values) ahead of implementing per-model
    // weekly limits / extra_usage / limits[] parsing. It reuses this
    // provider's own credential directory resolution and HTTP call (same
    // endpoint, same headers, same bearer token source) instead of the
    // caller duplicating any of that. It does not change, and is not called
    // by, GetUsageAsync/TryReadUsageWindowsAsync above.
    public async Task<ClaudeUsageShapeProbe> ProbeUsageShapeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var config = GetConfigDirectory();
        if (!CredentialsFileExists(config.Directory))
        {
            return ClaudeUsageShapeProbe.NoCredentials();
        }

        var credentialShape = await ReadCredentialShapeAsync(config.Directory, cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(credentialShape.AccessToken))
        {
            return new ClaudeUsageShapeProbe(
                CredentialsPresent: true,
                OauthKeys: credentialShape.OauthKeys,
                SubscriptionType: credentialShape.SubscriptionType,
                RateLimitTier: credentialShape.RateLimitTier,
                AccessTokenPresent: false,
                HttpStatusCode: null,
                ResponseHeaders: new Dictionary<string, string>(),
                Body: null,
                ErrorSummary: "accessTokenMissing");
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(UsageTimeout);

            using var request = new HttpRequestMessage(HttpMethod.Get, UsageEndpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credentialShape.AccessToken);
            request.Headers.TryAddWithoutValidation("anthropic-beta", OAuthBetaHeader);

            using var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                .ConfigureAwait(false);

            var headers = new Dictionary<string, string>();
            foreach (var header in response.Headers)
            {
                if (header.Key.StartsWith("anthropic-ratelimit-", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(header.Key, "retry-after", StringComparison.OrdinalIgnoreCase))
                {
                    headers[header.Key] = string.Join(",", header.Value);
                }
            }

            JsonNode? body = null;
            var statusCode = (int)response.StatusCode;
            if (statusCode is >= 200 and <= 299)
            {
                var text = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
                body = JsonNode.Parse(text);
            }

            return new ClaudeUsageShapeProbe(
                CredentialsPresent: true,
                OauthKeys: credentialShape.OauthKeys,
                SubscriptionType: credentialShape.SubscriptionType,
                RateLimitTier: credentialShape.RateLimitTier,
                AccessTokenPresent: true,
                HttpStatusCode: statusCode,
                ResponseHeaders: headers,
                Body: body,
                ErrorSummary: null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return new ClaudeUsageShapeProbe(
                true, credentialShape.OauthKeys, credentialShape.SubscriptionType, credentialShape.RateLimitTier,
                true, null, new Dictionary<string, string>(), null, "timeout");
        }
        catch (JsonException)
        {
            return new ClaudeUsageShapeProbe(
                true, credentialShape.OauthKeys, credentialShape.SubscriptionType, credentialShape.RateLimitTier,
                true, null, new Dictionary<string, string>(), null, "invalidJson");
        }
        catch
        {
            return new ClaudeUsageShapeProbe(
                true, credentialShape.OauthKeys, credentialShape.SubscriptionType, credentialShape.RateLimitTier,
                true, null, new Dictionary<string, string>(), null, "usageReadFailed");
        }
    }

    private sealed record CredentialShape(
        IReadOnlyList<string> OauthKeys,
        string? SubscriptionType,
        string? RateLimitTier,
        string? AccessToken);

    private static async Task<CredentialShape> ReadCredentialShapeAsync(string configDirectory, CancellationToken cancellationToken)
    {
        try
        {
            var credentialsPath = Path.Combine(configDirectory, CredentialsFileName);
            await using var stream = File.OpenRead(credentialsPath);
            var root = await JsonNode.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            var oauth = root?["claudeAiOauth"] as JsonObject;

            var keys = oauth is null
                ? Array.Empty<string>()
                : oauth.Select(pair => pair.Key).ToArray();

            var subscriptionType = oauth is null ? null : GetString(oauth["subscriptionType"]);
            var rateLimitTier = oauth is null ? null : GetString(oauth["rateLimitTier"]);
            var accessToken = GetString(oauth?["accessToken"])
                ?? GetString(oauth?["access_token"])
                ?? GetString(root?["accessToken"])
                ?? GetString(root?["access_token"]);

            return new CredentialShape(keys, subscriptionType, rateLimitTier, accessToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return new CredentialShape(Array.Empty<string>(), null, null, null);
        }
    }

    private static string? GetString(JsonNode? node)
    {
        if (node is null || node.GetValueKind() != JsonValueKind.String)
        {
            return null;
        }

        return node.GetValue<string>();
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
        IReadOnlyList<RateLimitWindow> Windows,
        ServiceExtras? Extras)
    {
        public static ClaudeUsageReadResult Available(IReadOnlyList<RateLimitWindow> windows, ServiceExtras? extras)
            => new(ProviderStatus.Available, "available", windows, extras);

        public static ClaudeUsageReadResult Failed(ProviderStatus status, string safeSummary, ServiceExtras? extras = null)
            => new(status, safeSummary, Array.Empty<RateLimitWindow>(), extras);
    }
}
