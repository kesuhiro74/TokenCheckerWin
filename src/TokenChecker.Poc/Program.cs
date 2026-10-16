using System.Text.Json;
using System.Text.Json.Serialization;
using TokenChecker.Core;
using TokenChecker.Core.Providers;
using TokenChecker.Poc.ClaudeUsage;
using TokenChecker.Poc.GitHubCopilot;
using TokenChecker.Poc.LocalCost;

// Opt-in GitHub Copilot AI Credits probe. Without this flag the default
// Claude + Codex output below is unchanged.
if (args.Any(a => string.Equals(a, "--github-copilot", StringComparison.OrdinalIgnoreCase)))
{
    return await GitHubCopilotPocRunner.RunAsync(args);
}

// Opt-in local daily-cost aggregation probe. Without this flag the default
// Claude + Codex output below is unchanged.
if (args.Any(a => string.Equals(a, "--daily-cost", StringComparison.OrdinalIgnoreCase)))
{
    return await DailyCostPocRunner.RunAsync();
}

// Opt-in investigation probe for the Claude usage API response shape. Calls
// the live endpoint at most once per run. Without this flag the default
// Claude + Codex output below is unchanged.
if (args.Any(a => string.Equals(a, "--usage-shape", StringComparison.OrdinalIgnoreCase)))
{
    return await ClaudeUsageShapePocRunner.RunAsync();
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
