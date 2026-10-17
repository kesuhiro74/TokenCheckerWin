using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows.Forms;
using TokenChecker.Core;
using Xunit;

namespace TokenChecker.App.Tests;

// Regression guard for the normal-mode ServiceCard layout.
//
// ServiceCard.LayoutRows is the single place that knows the card's vertical
// order (CLAUDE.md). It used to decide which rows to skip by reading each
// child's Control.Visible, but that getter also reflects ancestor visibility:
// while the card is hidden (minimum/compact mode) or the popup itself is
// hidden, it reads false for children that were explicitly set visible. A
// refresh landing in that state left the optional rows at their default (0, 0)
// — stacked on top of the title — and made the card too short, which only
// became visible after switching back to normal mode.
//
// These tests drive the real StatusForm without ever showing it (so every
// child's Visible getter is false, exactly the broken condition) and assert the
// rows are still laid out in order inside the card.
public class ServiceCardLayoutTests
{
    [Fact]
    public void NormalCardKeepsRowsOrderedWhenUpdatedWhileHidden()
    {
        RunSta(() =>
        {
            using var form = new StatusForm();
            var settings = new AppSettings { DisplayMode = DisplayMode.Minimum };
            form.ApplySettings(settings);

            // The refresh lands while the normal-mode cards are hidden.
            form.UpdateSnapshot(CurrentSnapshot(), LastSuccessfulSnapshot(), Costs());

            settings.DisplayMode = DisplayMode.Normal;
            form.ApplySettings(settings);

            var card = Card(form, "_claudeCard");
            var rows = VisibleRows(card);

            // Every optional row must sit below the title, in the documented
            // order, without overlapping its neighbour.
            for (var i = 1; i < rows.Count; i++)
            {
                var previous = rows[i - 1];
                var current = rows[i];
                Assert.True(
                    previous.Bottom <= current.Top,
                    $"'{previous.Name}' (top {previous.Top}, bottom {previous.Bottom}) overlaps '{current.Name}' (top {current.Top}).");
            }

            // ...and the card is tall enough for all of them.
            foreach (var row in rows)
            {
                Assert.True(
                    row.Bottom <= card.Height,
                    $"'{row.Name}' (bottom {row.Bottom}) falls outside the card (height {card.Height}).");
            }
        });
    }

    [Fact]
    public void NormalCardHeightGrowsWithTheOptionalRowsWhileHidden()
    {
        RunSta(() =>
        {
            using var form = new StatusForm();
            var settings = new AppSettings { DisplayMode = DisplayMode.Minimum };
            form.ApplySettings(settings);

            var card = Card(form, "_claudeCard");

            // Bare: no status message, no scoped limits, no extras, no money.
            form.UpdateSnapshot(BareSnapshot(), BareSnapshot(), DailyCostsView.Empty);
            var bareHeight = card.Height;

            // Full: status message + scoped row + money row + extras row.
            form.UpdateSnapshot(CurrentSnapshot(), LastSuccessfulSnapshot(), Costs());
            var fullHeight = card.Height;

            settings.DisplayMode = DisplayMode.Normal;
            form.ApplySettings(settings);

            // Four extra rows plus the divider; the exact pixel budget lives in
            // LayoutRows, so assert only that the card actually grew for them.
            Assert.True(
                fullHeight > bareHeight + 60,
                $"Expected the optional rows to grow the card (bare {bareHeight}, full {fullHeight}).");
            Assert.Equal(fullHeight, card.Height);
        });
    }

    [Fact]
    public void DetailBoxIsPlacedBelowTheToggleWhileHidden()
    {
        RunSta(() =>
        {
            using var form = new StatusForm();
            form.ApplySettings(new AppSettings { DisplayMode = DisplayMode.Minimum });
            form.UpdateSnapshot(CurrentSnapshot(), LastSuccessfulSnapshot(), Costs());

            var card = Card(form, "_claudeCard");
            var collapsedHeight = card.Height;

            card.GetType()
                .GetMethod("ToggleDetail", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(card, null);

            var toggle = Child<Control>(card, "_detailToggle");
            var box = Child<Control>(card, "_detailBox");

            Assert.True(toggle.Bottom <= box.Top, $"Detail box (top {box.Top}) overlaps the toggle (bottom {toggle.Bottom}).");
            Assert.True(box.Bottom <= card.Height, $"Detail box (bottom {box.Bottom}) falls outside the card (height {card.Height}).");
            Assert.True(card.Height > collapsedHeight);
        });
    }

    // ----- rows -------------------------------------------------------------

    private sealed record Row(string Name, int Top, int Bottom);

    // The rows the test scenario makes visible, in the order LayoutRows lays
    // them out. The money row is a group of four labels, so it contributes its
    // bounding band.
    private static IReadOnlyList<Row> VisibleRows(Control card)
    {
        var money = new[] { "_todayLabel", "_todayValue", "_monthLabel", "_monthValue" }
            .Select(name => Child<Control>(card, name))
            .ToArray();

        return new List<Row>
        {
            Band(card, "title", "_title"),
            Band(card, "status message", "_statusMessage"),
            Band(card, "5-hour block", "_shortWindowLabel", "_shortPercent", "_shortBar"),
            Band(card, "weekly block", "_weeklyLabel", "_weeklyPercent", "_weeklyBar"),
            Band(card, "scoped limits", "_scopedRow"),
            new Row("money", money.Min(control => control.Top), money.Max(control => control.Bottom)),
            Band(card, "extra info", "_extrasRow"),
            Band(card, "detail toggle", "_detailToggle")
        };
    }

    private static Row Band(Control card, string name, params string[] fields)
    {
        var controls = fields.Select(field => Child<Control>(card, field)).ToArray();
        return new Row(name, controls.Min(control => control.Top), controls.Max(control => control.Bottom));
    }

    // ----- fixtures ---------------------------------------------------------

    // Current fetch failed -> the card shows the status message and falls back
    // to the last successful snapshot for the windows and extras.
    private static UsageSnapshot CurrentSnapshot()
        => new(DateTimeOffset.UtcNow, new[]
        {
            new ServiceUsage("Claude", ProviderStatus.Error, "boom", Array.Empty<RateLimitWindow>()),
            new ServiceUsage("Codex", ProviderStatus.Error, "boom", Array.Empty<RateLimitWindow>())
        });

    private static UsageSnapshot LastSuccessfulSnapshot()
    {
        var reset = DateTimeOffset.UtcNow.AddHours(3);
        var extras = new ServiceExtras(
            PlanLabel: "Max 5x",
            ScopedLimits: new[] { new ScopedLimit("Fable", 4, reset) },
            ResetCreditsAvailable: 2,
            ResetCreditsNextExpiry: reset.AddDays(13));

        var windows = new[]
        {
            new RateLimitWindow("5h", reset, 10, 100, 90, 10, 300),
            new RateLimitWindow("weekly", reset.AddDays(4), 19, 100, 81, 19, 10080)
        };

        return new UsageSnapshot(DateTimeOffset.UtcNow, new[]
        {
            new ServiceUsage("Claude", ProviderStatus.Available, null, windows, extras),
            new ServiceUsage("Codex", ProviderStatus.Available, null, windows)
        });
    }

    // No status message, no scoped limits, no extras.
    private static UsageSnapshot BareSnapshot()
    {
        var windows = new[] { new RateLimitWindow("5h", DateTimeOffset.UtcNow.AddHours(2), 1, 100, 99, 1, 300) };
        return new UsageSnapshot(DateTimeOffset.UtcNow, new[]
        {
            new ServiceUsage("Claude", ProviderStatus.Available, null, windows),
            new ServiceUsage("Codex", ProviderStatus.Available, null, windows)
        });
    }

    private static DailyCostsView Costs()
    {
        var today = new DailyCost(12.3m, 1_845m);
        var month = new DailyCost(45.6m, 6_840m);
        var view = new ServiceCostView(today, month, new[] { new ModelCostLine("claude-opus-4", month) });
        return new DailyCostsView(view, view);
    }

    // ----- plumbing ---------------------------------------------------------

    // ServiceCard is a private nested type, so the test reaches it (and its
    // rows) by reflection rather than widening production accessibility.
    private static Control Card(StatusForm form, string field) => Child<Control>(form, field);

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
