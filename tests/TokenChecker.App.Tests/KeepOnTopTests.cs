using Xunit;

namespace TokenChecker.App.Tests;

// Pins the gate for the keep-above-taskbar re-assertion
// (TrayApplicationContext.ShouldKeepOnTop). A popup is kept re-raised only when it
// is enabled, in Always ("常時表示") mode, currently visible, AND opting into
// topmost — so turning "常に最前面に表示" off (topMost=false) or using HoverPreview
// leaves the window's z-order alone.
public class KeepOnTopTests
{
    // `isAlways` maps to WindowDisplayMode (Always vs HoverPreview) inside the body:
    // the enum is internal, so a public xUnit signature cannot take it directly.
    [Theory]
    [InlineData(true, true, true, true, true)]    // canonical keep-on-top case
    [InlineData(false, true, true, true, false)]  // window disabled
    [InlineData(true, false, true, true, false)]  // HoverPreview never pinned above
    [InlineData(true, true, false, true, false)]  // hidden
    [InlineData(true, true, true, false, false)]  // always-on-top off -> respect user's choice
    public void KeptOnTop_OnlyWhen_Enabled_Always_Visible_AndTopMost(
        bool enabled, bool isAlways, bool visible, bool topMost, bool expected)
    {
        var mode = isAlways ? WindowDisplayMode.Always : WindowDisplayMode.HoverPreview;
        Assert.Equal(expected, TrayApplicationContext.ShouldKeepOnTop(enabled, mode, visible, topMost));
    }
}
