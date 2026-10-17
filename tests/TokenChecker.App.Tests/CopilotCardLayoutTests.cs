using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows.Forms;
using TokenChecker.Core;
using Xunit;

namespace TokenChecker.App.Tests;

// Regression guard for the Copilot card's variable height.
//
// The card used to be a fixed 214px (+88 when the diagnostics box is expanded).
// It now grows by the optional extras block (daily bar chart + per-model credit
// breakdown + billed-overage line), and CopilotCard.ApplyLayout is the single
// place that decides those positions and the resulting height.
//
// The window is never shown, so every child's Control.Visible getter reads
// false — exactly the state a timer refresh lands in while the popup is hidden.
// A layout that gated on those getters would collapse here.
public class CopilotCardLayoutTests
{
    private const int BaseHeight = 214;

    [Fact]
    public void NoExtras_KeepsTheBaseHeight()
        => RunSta(() =>
        {
            using var window = new CopilotWindow();
            window.Update("Copilot Pro+", Usage(extras: null), Usage(extras: null), 7000, null);

            Assert.Equal(BaseHeight, Card(window).Height);
        });

    [Fact]
    public void ThreeModelRows_GrowTheCardByCaptionPlusRows()
        => RunSta(() =>
        {
            using var window = new CopilotWindow();
            var usage = Usage(Extras(models: 3, spend: null));
            window.Update("Copilot Pro+", usage, usage, 7000, null);

            // caption + 3 rows = 4 * 16 = 64, plus the 4px gap above the toggle.
            Assert.Equal(BaseHeight + 68, Card(window).Height);
        });

    [Fact]
    public void SixModelRowsPlusSpend_GrowTheCardAndStayInside()
        => RunSta(() =>
        {
            using var window = new CopilotWindow();
            // 7 models fold into 5 + "other" = 6 rows.
            var usage = Usage(Extras(models: 7, spend: 1.23m));
            window.Update("Copilot Pro+", usage, usage, 7000, null);

            var card = Card(window);
            // caption + 6 rows = 112, + 16 spend row, + the 4px gap = 132.
            Assert.Equal(BaseHeight + 132, card.Height);

            var breakdown = Child<Control>(card, "_breakdown");
            var spend = Child<Control>(card, "_extraSpend");
            var toggle = Child<Control>(card, "_detailToggle");

            Assert.True(breakdown.Bottom <= spend.Top, "The spend line overlaps the breakdown block.");
            Assert.True(spend.Bottom <= toggle.Top, "The detail toggle overlaps the spend line.");
            Assert.True(toggle.Top < card.Height, "The detail toggle falls outside the card.");
            Assert.Equal(card.Height, window.ClientSize.Height);
        });

    [Fact]
    public void DailyChartOnly_GrowsTheCardByCaptionPlusPlot()
        => RunSta(() =>
        {
            using var window = new CopilotWindow();
            var usage = Usage(Extras(models: 0, spend: null, dailyDays: 3));
            window.Update("Copilot Pro+", usage, usage, 7000, null);

            // caption 16 + plot 36 = 52, plus the 4px gap above the toggle.
            Assert.Equal(BaseHeight + 56, Card(window).Height);
        });

    [Fact]
    public void DailyChartAndThreeModelRows_StackInOrder()
        => RunSta(() =>
        {
            using var window = new CopilotWindow();
            var usage = Usage(Extras(models: 3, spend: null, dailyDays: 5));
            window.Update("Copilot Pro+", usage, usage, 7000, null);

            var card = Card(window);
            // 52 chart + (caption + 3 rows) 64 + the 4px gap = 120.
            Assert.Equal(BaseHeight + 120, card.Height);

            var chart = Child<Control>(card, "_dailyChart");
            var breakdown = Child<Control>(card, "_breakdown");
            var toggle = Child<Control>(card, "_detailToggle");

            // The chart sits directly under the projection row, above the
            // per-model block.
            Assert.Equal(172, chart.Top);
            Assert.True(chart.Bottom <= breakdown.Top, "The per-model block overlaps the daily chart.");
            Assert.True(breakdown.Bottom <= toggle.Top, "The detail toggle overlaps the per-model block.");
        });

    [Fact]
    public void DailyChartSixModelRowsAndSpend_GrowTheCardAndStayInside()
        => RunSta(() =>
        {
            using var window = new CopilotWindow();
            var usage = Usage(Extras(models: 7, spend: 1.23m, dailyDays: 10));
            window.Update("Copilot Pro+", usage, usage, 7000, null);

            var card = Card(window);
            // 52 chart + (caption + 6 rows) 112 + 16 spend + the 4px gap = 184.
            Assert.Equal(BaseHeight + 184, card.Height);

            var chart = Child<Control>(card, "_dailyChart");
            var breakdown = Child<Control>(card, "_breakdown");
            var spend = Child<Control>(card, "_extraSpend");
            var toggle = Child<Control>(card, "_detailToggle");

            Assert.True(chart.Bottom <= breakdown.Top);
            Assert.True(breakdown.Bottom <= spend.Top);
            Assert.True(spend.Bottom <= toggle.Top);
            Assert.True(toggle.Top < card.Height, "The detail toggle falls outside the card.");
            Assert.Equal(card.Height, window.ClientSize.Height);
        });

    [Fact]
    public void DailyChartDisappears_WhenTheNextFetchHasNoDailyRows()
        => RunSta(() =>
        {
            using var window = new CopilotWindow();
            var withChart = Usage(Extras(models: 0, spend: null, dailyDays: 4));
            window.Update("Copilot Pro+", withChart, withChart, 7000, null);
            Assert.Equal(BaseHeight + 56, Card(window).Height);

            // The extra billing read failed on this refresh: the chart goes, the
            // per-model block stays.
            var modelsOnly = Usage(Extras(models: 3, spend: null));
            window.Update("Copilot Pro+", modelsOnly, modelsOnly, 7000, null);

            Assert.Equal(BaseHeight + 68, Card(window).Height);
        });

    [Fact]
    public void DailyChart_SetLoading_ReturnsToTheBaseHeight()
        => RunSta(() =>
        {
            using var window = new CopilotWindow();
            var usage = Usage(Extras(models: 0, spend: null, dailyDays: 4));
            window.Update("Copilot Pro+", usage, usage, 7000, null);

            window.SetLoading();

            Assert.Equal(BaseHeight, Card(window).Height);
        });

    [Fact]
    public void DailyChart_WithoutAnAllowance_StillShows()
        => RunSta(() =>
        {
            // No allowance -> no prorated budget line, but the bars are still
            // worth showing.
            using var window = new CopilotWindow();
            var usage = Usage(Extras(models: 0, spend: null, dailyDays: 4));
            window.Update("Copilot", usage, usage, null, null);

            Assert.Equal(BaseHeight + 56, Card(window).Height);
        });

    [Fact]
    public void ExtrasAndDetailBox_StackWithoutClipping()
        => RunSta(() =>
        {
            using var window = new CopilotWindow();
            var usage = Usage(Extras(models: 3, spend: 0m));
            window.Update("Copilot Pro+", usage, usage, 7000, null);

            var card = Card(window);
            var collapsed = card.Height;

            card.GetType()
                .GetMethod("ToggleDetail", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(card, null);

            var toggle = Child<Control>(card, "_detailToggle");
            var box = Child<Control>(card, "_detailBox");

            Assert.True(card.Height > collapsed);
            // The toggle is an AutoSize LinkLabel whose box includes a couple of
            // padding pixels, so compare the row origins (the pre-existing
            // 176 / 194 spacing), not the label's measured bottom.
            Assert.Equal(18, box.Top - toggle.Top);
            Assert.True(box.Bottom <= card.Height, "The detail box falls outside the card.");
        });

    [Fact]
    public void ExtrasDisappear_WhenTheNextFetchHasNone()
        => RunSta(() =>
        {
            using var window = new CopilotWindow();
            var withExtras = Usage(Extras(models: 3, spend: 1m));
            window.Update("Copilot Pro+", withExtras, withExtras, 7000, null);
            Assert.True(Card(window).Height > BaseHeight);

            var bare = Usage(extras: null);
            window.Update("Copilot Pro+", bare, bare, 7000, null);

            Assert.Equal(BaseHeight, Card(window).Height);
        });

    [Fact]
    public void SetLoading_ReturnsToTheBaseHeight()
        => RunSta(() =>
        {
            using var window = new CopilotWindow();
            var usage = Usage(Extras(models: 3, spend: 1m));
            window.Update("Copilot Pro+", usage, usage, 7000, null);

            window.SetLoading();

            Assert.Equal(BaseHeight, Card(window).Height);
        });

    [Fact]
    public void NonAvailableStatus_HidesTheExtras()
        => RunSta(() =>
        {
            using var window = new CopilotWindow();
            var fallback = Usage(Extras(models: 3, spend: 1m));
            var failed = new ServiceUsage(
                "GitHub Copilot", ProviderStatus.Error, "boom", Array.Empty<RateLimitWindow>());

            window.Update("Copilot Pro+", failed, fallback, 7000, null);

            // The whole usage block is hidden when the status is not Available,
            // so the extras go with it and the card keeps its base height.
            Assert.Equal(BaseHeight, Card(window).Height);
        });

    // ----- fixtures ---------------------------------------------------------

    private static ServiceUsage Usage(ServiceExtras? extras)
        => new(
            "GitHub Copilot",
            ProviderStatus.Available,
            null,
            new[]
            {
                new RateLimitWindow(
                    "GitHub Copilot AI Credits",
                    new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero),
                    4627, null, null, null, 43200)
            },
            extras);

    private static ServiceExtras Extras(int models, decimal? spend, int dailyDays = 0)
        => new(
            ExtraSpend: spend is { } amount ? new ExtraSpend(amount, null, "USD") : null,
            ModelCredits: Enumerable
                .Range(1, models)
                .Select(i => new ModelCredits($"model-{i}", 100m - i))
                .ToArray(),
            DailyCredits: dailyDays <= 0
                ? null
                : Enumerable
                    .Range(1, dailyDays)
                    .Select(i => new DailyCredits(
                        DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-(i - 1)), 100m * i))
                    .ToArray());

    // ----- plumbing ---------------------------------------------------------

    // CopilotCard is a private nested type, so the test reaches it (and its
    // rows) by reflection rather than widening production accessibility.
    private static Control Card(CopilotWindow window) => Child<Control>(window, "_card");

    private static T Child<T>(object owner, string field)
        where T : class
    {
        var info = owner.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"{owner.GetType().Name} has no field '{field}'.");
        return info.GetValue(owner) as T
            ?? throw new InvalidOperationException($"Field '{field}' is not a {typeof(T).Name}.");
    }

    // WinForms needs a single-threaded apartment; xunit worker threads are MTA.
    private static void RunSta(Action action)
    {
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                failure = ExceptionDispatchInfo.Capture(ex);
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        thread.Join();
        failure?.Throw();
    }
}
