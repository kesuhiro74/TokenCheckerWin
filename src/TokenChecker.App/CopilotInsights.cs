namespace TokenChecker.App;

internal enum CopilotPrediction
{
    Insufficient,     // not enough data, or no allowance
    ReachesThisMonth, // FullDateLocal is set
    NotThisMonth,     // at this pace the monthly reset arrives before 100%
    AlreadyFull       // already at/over 100%
}

internal sealed record CopilotInsights(
    CopilotPrediction Prediction,
    DateTimeOffset? FullDateLocal,
    long? TodayDeltaCredits,   // null = no baseline yet (未計測)
    double? TodayDeltaPercent); // null = no allowance
