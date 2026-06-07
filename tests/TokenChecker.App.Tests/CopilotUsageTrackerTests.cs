using Xunit;

namespace TokenChecker.App.Tests;

// Locks down the Copilot 100%-reach projection and the "since today's 09:00"
// delta. Each tracker is backed by a unique temp file (store seam) so the real
// copilot_usage.json is never touched; "now" is built from LOCAL wall-clock so
// the local-time logic (09:00 window, local month start) is runner-TZ-agnostic.
public class CopilotUsageTrackerTests : IDisposable
{
    private readonly List<string> _tempFiles = new();

    private CopilotUsageTracker NewTracker()
    {
        var path = Path.Combine(Path.GetTempPath(), $"copilot_usage_test_{Guid.NewGuid():N}.json");
        _tempFiles.Add(path);
        return new CopilotUsageTracker(new CopilotUsageStore(path));
    }

    // A DateTimeOffset whose instant is the given LOCAL wall-clock time, so
    // nowUtc.ToLocalTime() reproduces exactly this wall-clock on any machine.
    private static DateTimeOffset Local(int year, int month, int day, int hour, int minute)
        => new(new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Local));

    // ---- prediction ----

    [Fact]
    public void Predict_NoAllowance_Insufficient()
    {
        var i = NewTracker().Observe(100, null, Local(2025, 6, 15, 12, 0));
        Assert.Equal(CopilotPrediction.Insufficient, i.Prediction);
        Assert.Null(i.FullDateLocal);
    }

    [Fact]
    public void Predict_ZeroUsed_Insufficient()
        => Assert.Equal(CopilotPrediction.Insufficient, NewTracker().Observe(0, 1500, Local(2025, 6, 15, 12, 0)).Prediction);

    [Fact]
    public void Predict_ElapsedUnderHalfDay_Insufficient()
        => Assert.Equal(CopilotPrediction.Insufficient, NewTracker().Observe(50, 1500, Local(2025, 6, 1, 6, 0)).Prediction);

    [Fact]
    public void Predict_AtOrOverCap_AlreadyFull()
        => Assert.Equal(CopilotPrediction.AlreadyFull, NewTracker().Observe(1500, 1500, Local(2025, 6, 15, 12, 0)).Prediction);

    [Fact]
    public void Predict_PaceCrossesReset_NotThisMonth()
    {
        var i = NewTracker().Observe(700, 1500, Local(2025, 6, 28, 12, 0));
        Assert.Equal(CopilotPrediction.NotThisMonth, i.Prediction);
        Assert.Null(i.FullDateLocal);
    }

    [Fact]
    public void Predict_ReachesThisMonth_HasDateBeforeNextReset()
    {
        var i = NewTracker().Observe(1000, 1500, Local(2025, 6, 20, 0, 0));
        Assert.Equal(CopilotPrediction.ReachesThisMonth, i.Prediction);
        Assert.NotNull(i.FullDateLocal);
        Assert.True(i.FullDateLocal!.Value >= Local(2025, 6, 1, 0, 0));
        Assert.True(i.FullDateLocal!.Value < Local(2025, 7, 1, 0, 0));
    }

    // ---- today's 09:00 delta ----

    [Fact]
    public void Today_FirstObserve_NoBaseline_Null()
    {
        var i = NewTracker().Observe(500, 1500, Local(2025, 6, 15, 12, 0));
        Assert.Null(i.TodayDeltaCredits);
        Assert.Null(i.TodayDeltaPercent);
    }

    [Fact]
    public void Today_PreThenPostNine_SameWindow_DeltaAndPercent()
    {
        var t = NewTracker();
        t.Observe(100, 1500, Local(2025, 6, 15, 8, 0));           // pre-09:00 baseline source
        var i = t.Observe(130, 1500, Local(2025, 6, 15, 10, 0));  // first sample of today's window
        Assert.Equal(30L, i.TodayDeltaCredits);
        Assert.Equal(2.0d, i.TodayDeltaPercent!.Value, 5);        // 30 / 1500 * 100
    }

    [Fact]
    public void Today_UsageDrop_DeltaClampedToZero()
    {
        var t = NewTracker();
        t.Observe(200, 1500, Local(2025, 6, 15, 8, 0));
        var i = t.Observe(150, 1500, Local(2025, 6, 15, 10, 0));
        Assert.Equal(0L, i.TodayDeltaCredits);
    }

    [Fact]
    public void Today_NoAllowance_DeltaButNullPercent()
    {
        var t = NewTracker();
        t.Observe(100, null, Local(2025, 6, 15, 8, 0));
        var i = t.Observe(130, null, Local(2025, 6, 15, 10, 0));
        Assert.Equal(30L, i.TodayDeltaCredits);
        Assert.Null(i.TodayDeltaPercent);
    }

    [Fact]
    public void Today_StalePriorSample_NoBaseline_Null()
    {
        var t = NewTracker();
        t.Observe(100, 1500, Local(2025, 6, 13, 8, 0));           // more than one 09:00 window ago
        var i = t.Observe(130, 1500, Local(2025, 6, 15, 10, 0));
        Assert.Null(i.TodayDeltaCredits);
    }

    [Fact]
    public void Today_MonthRollover_DiscardsBaseline_Null()
    {
        var t = NewTracker();
        t.Observe(1000, 1500, Local(2025, 6, 30, 10, 0));
        var i = t.Observe(50, 1500, Local(2025, 7, 1, 10, 0));    // new month — prior baseline not reused
        Assert.Null(i.TodayDeltaCredits);
    }

    public void Dispose()
    {
        foreach (var path in _tempFiles)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // best-effort temp cleanup
            }
        }
    }
}
