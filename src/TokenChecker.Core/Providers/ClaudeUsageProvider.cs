using System.Diagnostics;

namespace TokenChecker.Core.Providers;

public sealed class ClaudeUsageProvider : IUsageProvider
{
    private static readonly TimeSpan VersionTimeout = TimeSpan.FromSeconds(3);

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

        return new ServiceUsage(
            ServiceName,
            ProviderStatus.NotLoggedIn,
            $"Claude usage API is not implemented yet. {message}",
            Array.Empty<RateLimitWindow>());
    }

    private static async Task<bool> HasVersionAsync(string claudeCommand, CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(VersionTimeout);

            using var process = Process.Start(CreateVersionStartInfo(claudeCommand));

            if (process is null)
            {
                return false;
            }

            process.ErrorDataReceived += static (_, _) => { };
            process.OutputDataReceived += static (_, _) => { };
            process.BeginErrorReadLine();
            process.BeginOutputReadLine();

            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            return process.ExitCode == 0;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return false;
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
            return File.Exists(Path.Combine(configDirectory, ".credentials.json"));
        }
        catch
        {
            return false;
        }
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
}
