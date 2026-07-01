using Xunit;

namespace TokenChecker.App.Tests;

// Pins the saved-window-position clamp (TrayApplicationContext.TryClampToScreens),
// the pure core behind Get{Status,Copilot}WindowLocation's off-screen recovery.
// The production wrapper (TryClampToVisibleScreen) feeds it each monitor's FULL
// Bounds (not WorkingArea) so a window the user dragged over the taskbar keeps that
// position on re-show; these tests lock that "clamp to the physical monitor, allow
// taskbar overlap, but never lose it off-screen" contract.
public class TryClampToScreensTests
{
    // A 1920x1080 primary monitor with a 40px taskbar pinned to the bottom:
    // the physical screen is the full 1080; the working area stops at 1040.
    private static readonly Rectangle Bounds = new(0, 0, 1920, 1080);
    private static readonly Rectangle WorkingArea = new(0, 0, 1920, 1040);
    private static readonly Size WindowSize = new(300, 220);

    [Fact]
    public void OverTaskbar_ClampToBounds_KeepsPosition_ButWorkingAreaWouldPushUp()
    {
        // Dragged so the bottom (850+220=1070) sits inside the taskbar band (1040..1080)
        // yet fully within the physical screen.
        var dragged = new Point(1600, 850);

        Assert.True(TrayApplicationContext.TryClampToScreens(new[] { Bounds }, dragged, WindowSize, out var onBounds));
        Assert.Equal(dragged, onBounds); // preserved over the taskbar

        // The old WorkingArea-based clamp would have shoved it up to bottom==1040.
        Assert.True(TrayApplicationContext.TryClampToScreens(new[] { WorkingArea }, dragged, WindowSize, out var onWorkArea));
        Assert.Equal(new Point(1600, 1040 - WindowSize.Height), onWorkArea);
        Assert.NotEqual(onBounds, onWorkArea);
    }

    [Fact]
    public void FullyOnScreen_IsReturnedUnchanged()
    {
        var inside = new Point(200, 200);
        Assert.True(TrayApplicationContext.TryClampToScreens(new[] { Bounds }, inside, WindowSize, out var clamped));
        Assert.Equal(inside, clamped);
    }

    [Fact]
    public void HangingOffRightAndBottom_IsPulledFullyOntoMonitor()
    {
        // Overlaps the screen but spills past the right/bottom edges.
        var spilling = new Point(1800, 1000);
        Assert.True(TrayApplicationContext.TryClampToScreens(new[] { Bounds }, spilling, WindowSize, out var clamped));
        // Pulled in so the far edges land exactly on the monitor's edges.
        Assert.Equal(new Point(1920 - WindowSize.Width, 1080 - WindowSize.Height), clamped);
    }

    [Fact]
    public void FullyOffEveryMonitor_ReturnsFalse()
    {
        var lost = new Point(5000, 5000);
        Assert.False(TrayApplicationContext.TryClampToScreens(new[] { Bounds }, lost, WindowSize, out var clamped));
        Assert.Equal(Point.Empty, clamped);
    }

    [Fact]
    public void PicksTheMonitorItIntersects_OnMultiMonitor()
    {
        // Second monitor to the right of the primary.
        var secondary = new Rectangle(1920, 0, 1920, 1080);
        var onSecondary = new Point(2000, 300);
        Assert.True(TrayApplicationContext.TryClampToScreens(new[] { Bounds, secondary }, onSecondary, WindowSize, out var clamped));
        Assert.Equal(onSecondary, clamped);
    }
}
