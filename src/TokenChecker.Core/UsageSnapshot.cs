namespace TokenChecker.Core;

public sealed record UsageSnapshot(
    DateTimeOffset CapturedAtUtc,
    IReadOnlyList<ServiceUsage> Services);
