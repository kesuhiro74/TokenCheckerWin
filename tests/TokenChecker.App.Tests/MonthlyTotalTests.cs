using Xunit;

namespace TokenChecker.App.Tests;

// The status window's footer total: only the services the window currently
// shows contribute, and "nothing to show" (null) is what hides the footer.
public class MonthlyTotalTests
{
    private static ServiceCostView View(decimal? monthUsd, decimal? monthJpy)
        => new(
            Today: new DailyCost(1m, 150m),
            Month: monthUsd is null || monthJpy is null ? null : new DailyCost(monthUsd.Value, monthJpy.Value),
            MonthByModel: Array.Empty<ModelCostLine>());

    [Fact]
    public void Sum_BothShown_AddsBothCurrencies()
    {
        var total = MonthlyTotal.Sum(View(20m, 3000m), true, View(13m, 2000m), true);

        Assert.NotNull(total);
        Assert.Equal(33m, total!.Usd);
        Assert.Equal(5000m, total.Jpy);
    }

    [Fact]
    public void Sum_HiddenService_DoesNotContribute()
    {
        var total = MonthlyTotal.Sum(View(20m, 3000m), true, View(13m, 2000m), showCodex: false);

        Assert.NotNull(total);
        Assert.Equal(20m, total!.Usd);
        Assert.Equal(3000m, total.Jpy);
    }

    [Fact]
    public void Sum_ShownServiceWithoutMonthFigure_IsSkipped()
    {
        var total = MonthlyTotal.Sum(View(null, null), true, View(13m, 2000m), true);

        Assert.NotNull(total);
        Assert.Equal(13m, total!.Usd);
    }

    [Fact]
    public void Sum_NothingToShow_IsNull()
    {
        Assert.Null(MonthlyTotal.Sum(null, true, null, true));
        Assert.Null(MonthlyTotal.Sum(View(null, null), true, View(null, null), true));
        // Both services have a figure, but neither is shown.
        Assert.Null(MonthlyTotal.Sum(View(20m, 3000m), false, View(13m, 2000m), false));
    }
}
