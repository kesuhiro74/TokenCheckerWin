using System.Text.Json;
using System.Text.Json.Serialization;
using TokenChecker.Core;
using TokenChecker.Core.Providers;

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
