using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TokenChecker.Core.Providers;

public sealed class CodexUsageProvider : IUsageProvider
{
    private static readonly TimeSpan OperationTimeout = TimeSpan.FromSeconds(10);

    public string ServiceName => "Codex";

    public async Task<ServiceUsage> GetUsageAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!CommandLineProbe.TryFindOnPath("codex", out var codexCommand))
        {
            return new ServiceUsage(
                ServiceName,
                ProviderStatus.NotInstalled,
                "Codex CLI was not found.",
                Array.Empty<RateLimitWindow>());
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(OperationTimeout);

            await using var client = await CodexAppServerClient.StartAsync(codexCommand, timeout.Token)
                .ConfigureAwait(false);

            await client.SendRequestAsync(
                "initialize",
                new
                {
                    clientInfo = new
                    {
                        name = "token-checker-poc",
                        title = (string?)null,
                        version = "0.1.0"
                    },
                    capabilities = new
                    {
                        experimentalApi = false,
                        requestAttestation = false,
                        optOutNotificationMethods = Array.Empty<string>()
                    }
                },
                timeout.Token).ConfigureAwait(false);

            await client.SendNotificationAsync("initialized", timeout.Token).ConfigureAwait(false);

            JsonNode account;
            try
            {
                account = await client.SendRequestAsync(
                    "account/read",
                    new { refreshToken = false },
                    timeout.Token).ConfigureAwait(false);
            }
            catch (CodexAppServerException ex)
            {
                // A JSON-RPC *error* on account/read (as opposed to a result whose
                // account is null) has no documented login/auth mapping, so surface
                // a specific Error with the masked summary rather than letting it
                // fall through to the generic outer catch.
                return new ServiceUsage(
                    ServiceName,
                    ProviderStatus.Error,
                    $"Codex account could not be read. error={ex.SafeSummary}",
                    Array.Empty<RateLimitWindow>());
            }

            var accountState = ReadAccountState(account);
            var decision = CodexAccountClassifier.Classify(
                accountState.AccountIsNull, accountState.AccountType, accountState.RequiresOpenAiAuth);

            if (!decision.CanProceed)
            {
                return new ServiceUsage(
                    ServiceName,
                    decision.Status,
                    $"{decision.Message} {accountState.ToDebugMessage()}",
                    Array.Empty<RateLimitWindow>());
            }

            JsonNode rateLimits;
            try
            {
                rateLimits = await client.SendRequestAsync(
                    "account/rateLimits/read",
                    null,
                    timeout.Token).ConfigureAwait(false);
            }
            catch (CodexAppServerException ex)
            {
                return new ServiceUsage(
                    ServiceName,
                    ProviderStatus.Error,
                    $"Codex rate limits could not be read. {accountState.ToDebugMessage()} error={ex.SafeSummary}",
                    Array.Empty<RateLimitWindow>());
            }

            var windows = ParseRateLimitWindows(rateLimits).ToArray();

            return new ServiceUsage(
                ServiceName,
                windows.Length > 0 ? ProviderStatus.Available : ProviderStatus.Error,
                windows.Length > 0
                    ? $"Codex usage data was read. {accountState.ToDebugMessage()}"
                    : $"Codex rate limits were not present in the app-server response. {accountState.ToDebugMessage()}",
                windows);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return new ServiceUsage(
                ServiceName,
                ProviderStatus.Error,
                "Codex app-server timed out.",
                Array.Empty<RateLimitWindow>());
        }
        catch
        {
            return new ServiceUsage(
                ServiceName,
                ProviderStatus.Error,
                "Codex app-server usage data could not be read.",
                Array.Empty<RateLimitWindow>());
        }
    }

    private static AccountState ReadAccountState(JsonNode response)
    {
        var account = response["account"];
        var requiresOpenAiAuth = response["requiresOpenaiAuth"]?.GetValue<bool>() ?? false;
        var accountIsNull = IsJsonNull(account);
        var accountType = accountIsNull ? null : GetString(account?["type"]);
        var planTypePresent = !accountIsNull && !IsJsonNull(account?["planType"]);

        return new AccountState(accountIsNull, accountType, requiresOpenAiAuth, planTypePresent);
    }

    private static IEnumerable<RateLimitWindow> ParseRateLimitWindows(JsonNode response)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var byLimitId = response["rateLimitsByLimitId"] as JsonObject;

        if (byLimitId is not null)
        {
            foreach (var item in byLimitId)
            {
                if (IsJsonNull(item.Value))
                {
                    continue;
                }

                foreach (var window in ParseSnapshot(item.Value!, item.Key))
                {
                    if (seen.Add(window.Name))
                    {
                        yield return window;
                    }
                }
            }
        }

        var rateLimits = response["rateLimits"];
        if (!IsJsonNull(rateLimits))
        {
            foreach (var window in ParseSnapshot(rateLimits!, "codex"))
            {
                if (seen.Add(window.Name))
                {
                    yield return window;
                }
            }
        }
    }

    private static IEnumerable<RateLimitWindow> ParseSnapshot(JsonNode snapshot, string fallbackLimitId)
    {
        var limitName = GetString(snapshot["limitName"])
            ?? GetString(snapshot["limitId"])
            ?? fallbackLimitId;

        var primary = ParseWindow(snapshot["primary"], $"{limitName} primary");
        if (primary is not null)
        {
            yield return primary;
        }

        var secondary = ParseWindow(snapshot["secondary"], $"{limitName} secondary");
        if (secondary is not null)
        {
            yield return secondary;
        }
    }

    private static RateLimitWindow? ParseWindow(JsonNode? window, string name)
    {
        if (IsJsonNull(window))
        {
            return null;
        }

        var windowNode = window!;
        var usedPercent = GetDouble(windowNode["usedPercent"]);
        var windowDurationMins = GetLong(windowNode["windowDurationMins"]);
        var resetAtUtc = GetResetAtUtc(windowNode["resetsAt"]);

        var used = usedPercent is null ? null : (long?)Math.Round(usedPercent.Value, MidpointRounding.AwayFromZero);
        var remaining = usedPercent is null ? null : (long?)Math.Max(0, 100 - (long)Math.Ceiling(usedPercent.Value));

        return new RateLimitWindow(
            name,
            resetAtUtc,
            used,
            usedPercent is null ? null : 100,
            remaining,
            usedPercent,
            windowDurationMins);
    }

    private static DateTimeOffset? GetResetAtUtc(JsonNode? node)
    {
        var unixSeconds = GetLong(node);
        return unixSeconds is null ? null : DateTimeOffset.FromUnixTimeSeconds(unixSeconds.Value);
    }

    private static string? GetString(JsonNode? node)
        => IsJsonNull(node) ? null : node?.GetValue<string>();

    private static long? GetLong(JsonNode? node)
    {
        if (IsJsonNull(node))
        {
            return null;
        }

        var valueNode = node!;
        if (valueNode.GetValueKind() == JsonValueKind.Number && valueNode.GetValue<long>() is var value)
        {
            return value;
        }

        var text = valueNode.GetValue<string>();
        return long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static double? GetDouble(JsonNode? node)
    {
        if (IsJsonNull(node))
        {
            return null;
        }

        var valueNode = node!;
        if (valueNode.GetValueKind() == JsonValueKind.Number && valueNode.GetValue<double>() is var value)
        {
            return value;
        }

        var text = valueNode.GetValue<string>();
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static bool IsJsonNull(JsonNode? node)
        => node is null || node.GetValueKind() == JsonValueKind.Null;

    private sealed record AccountState(
        bool AccountIsNull,
        string? AccountType,
        bool RequiresOpenAiAuth,
        bool PlanTypePresent)
    {
        public string ToDebugMessage()
            => $"accountNull={AccountIsNull.ToString(CultureInfo.InvariantCulture).ToLowerInvariant()}; "
                + $"accountType={AccountType ?? "null"}; "
                + $"requiresOpenaiAuth={RequiresOpenAiAuth.ToString(CultureInfo.InvariantCulture).ToLowerInvariant()}; "
                + $"planTypePresent={PlanTypePresent.ToString(CultureInfo.InvariantCulture).ToLowerInvariant()};";
    }

    private sealed class CodexAppServerException : Exception
    {
        public CodexAppServerException(string safeSummary)
            : base("Codex app-server returned an error.")
        {
            SafeSummary = safeSummary;
        }

        public string SafeSummary { get; }
    }

    private sealed class CodexAppServerClient : IAsyncDisposable
    {
        private static readonly TimeSpan GracefulExitTimeout = TimeSpan.FromSeconds(2);

        private readonly Process _process;
        private readonly StreamWriter _stdin;
        private readonly StreamReader _stdout;
        private long _nextId;

        private CodexAppServerClient(Process process)
        {
            _process = process;
            _stdin = process.StandardInput;
            _stdout = process.StandardOutput;
        }

        public static Task<CodexAppServerClient> StartAsync(string codexCommand, CancellationToken cancellationToken)
        {
            var startInfo = CreateStartInfo(codexCommand);
            var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Codex app-server did not start.");

            try
            {
                process.ErrorDataReceived += static (_, _) => { };
                process.BeginErrorReadLine();

                cancellationToken.ThrowIfCancellationRequested();
                return Task.FromResult(new CodexAppServerClient(process));
            }
            catch
            {
                StopProcess(process, GracefulExitTimeout);
                process.Dispose();
                throw;
            }
        }

        public async Task<JsonNode> SendRequestAsync(string method, object? parameters, CancellationToken cancellationToken)
        {
            var id = Interlocked.Increment(ref _nextId);
            var request = new JsonObject
            {
                ["id"] = id,
                ["method"] = method
            };

            if (parameters is not null)
            {
                request["params"] = JsonSerializer.SerializeToNode(parameters);
            }

            await WriteLineAsync(request, cancellationToken).ConfigureAwait(false);

            while (true)
            {
                var response = await ReadLineAsync(cancellationToken).ConfigureAwait(false);
                // Skip anything that is not OUR response: notifications / server-side
                // requests (no id or non-numeric id), or a numeric id other than the
                // one we just sent. GetValue<long>() on a non-numeric id would throw
                // and abort the whole read, so guard the value kind first.
                if (!TryGetLongId(response["id"], out var responseId) || responseId != id)
                {
                    continue;
                }

                if (response["error"] is not null)
                {
                    throw new CodexAppServerException(SummarizeJsonRpcError(response["error"]!));
                }

                return response["result"]
                    ?? throw new JsonException("Codex app-server response did not contain a result.");
            }
        }

        public Task SendNotificationAsync(string method, CancellationToken cancellationToken)
        {
            var notification = new JsonObject
            {
                ["method"] = method
            };

            return WriteLineAsync(notification, cancellationToken);
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                CloseStandardInput();
                await WaitForExitOrKillAsync(_process, GracefulExitTimeout).ConfigureAwait(false);
            }
            catch
            {
                // Best-effort cleanup only.
            }
            finally
            {
                _process.Dispose();
            }
        }

        private void CloseStandardInput()
        {
            try
            {
                _stdin.Close();
            }
            catch
            {
                // Best-effort shutdown signal only.
            }
        }

        private static async Task WaitForExitOrKillAsync(Process process, TimeSpan timeout)
        {
            if (process.HasExited)
            {
                return;
            }

            using var waitTimeout = new CancellationTokenSource(timeout);
            try
            {
                await process.WaitForExitAsync(waitTimeout.Token).ConfigureAwait(false);
                return;
            }
            catch (OperationCanceledException)
            {
                // Fall through to forced termination.
            }

            StopProcess(process, timeout);
        }

        private static void StopProcess(Process process, TimeSpan timeout)
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
                // Process may already be exiting or inaccessible.
            }

            try
            {
                process.WaitForExit(timeout);
            }
            catch
            {
                // Best-effort cleanup only.
            }
        }

        private static string SummarizeJsonRpcError(JsonNode error)
            => CodexErrorSummarizer.Summarize(error);

        private static ProcessStartInfo CreateStartInfo(string codexCommand)
        {
            if (OperatingSystem.IsWindows() && string.Equals(Path.GetExtension(codexCommand), ".cmd", StringComparison.OrdinalIgnoreCase))
            {
                return new ProcessStartInfo("cmd.exe", $"/d /c \"{codexCommand}\" app-server --listen stdio://")
                {
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
            }

            return new ProcessStartInfo(codexCommand, "app-server --listen stdio://")
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
        }

        private async Task WriteLineAsync(JsonNode message, CancellationToken cancellationToken)
        {
            await _stdin.WriteLineAsync(message.ToJsonString().AsMemory(), cancellationToken).ConfigureAwait(false);
            await _stdin.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        // Safely reads a JSON-RPC id as our long, or false when the node is absent,
        // not a number, or a number we cannot represent as Int64 (a fractional or
        // out-of-range id is never one we issued, so it is simply skipped).
        private static bool TryGetLongId(JsonNode? node, out long value)
        {
            value = 0;
            if (node is null || node.GetValueKind() != JsonValueKind.Number)
            {
                return false;
            }

            try
            {
                value = node.GetValue<long>();
                return true;
            }
            catch (Exception ex) when (ex is FormatException or InvalidOperationException or OverflowException)
            {
                return false;
            }
        }

        private async Task<JsonNode> ReadLineAsync(CancellationToken cancellationToken)
        {
            var line = await _stdout.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
            {
                throw new InvalidOperationException("Codex app-server exited before returning a response.");
            }

            return JsonNode.Parse(line)
                ?? throw new JsonException("Codex app-server returned invalid JSON.");
        }
    }
}
