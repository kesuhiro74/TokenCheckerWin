using System.Text.Json;
using System.Text.Json.Serialization;
using TokenChecker.Core;
using TokenChecker.Core.Providers;
using TokenChecker.Poc.GitHubCopilot;

// Opt-in GitHub Copilot AI Credits probe. Without this flag the default
// Claude + Codex output below is unchanged.
if (args.Any(a => string.Equals(a, "--github-copilot", StringComparison.OrdinalIgnoreCase)))
{
    return await GitHubCopilotPocRunner.RunAsync(args);
}

var providers = new IUsageProvider[]
{
    new ClaudeUsageProvider(),
    new CodexUsageProvider()
};

var aggregator = new UsageAggregator(providers);
var snapshot = await aggregator.CaptureAsync();

var options = new JsonSerializerOptions
{
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
};
options.Converters.Add(new JsonStringEnumConverter());

Console.WriteLine(JsonSerializer.Serialize(snapshot, options));
return 0;
