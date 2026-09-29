using UltrawideStash.Probe;
using Xunit;

namespace UltrawideStash.Server.Tests;

/// <summary>
/// The character screen's widening, planned by the probe's <see cref="StashPlan"/>.
///
/// The screen is laid out as <c>12 | LeftSide | 10 | Stash Panel 680 | 12</c> across
/// a canvas of width C, so LeftSide is <c>C - 714</c>. Every expected number here was
/// seen in a live BepInEx log, not just worked out: 3440x1440 (canvas 2580) since
/// 2026-09-21, and on 2026-09-28 a 3424x1361 window (canvas 2717) and a faked 32:9
/// canvas of 3840 -- the width a 5120x1440 monitor gets.
/// </summary>
public class StashPlanTests
{
    private const float Panel = 680f;

    private const float Chrome = 48f;

    private const float Reserve = 620f;

    private static float LeftSide(float canvas) => canvas - 714f;

    private static StashPlan For(float canvas, int grid) =>
        StashPlan.For(LeftSide(canvas), Panel, Chrome, Reserve, grid);

    /// <summary>
    /// 3440x1440 with the server's 19-wide grid: LeftSide 1866 -> 1272, panel
    /// 680 -> 1250, exactly as logged since 1.0.0.
    /// </summary>
    [Fact]
    public void TheLive3440ScreenWidensTo19Columns()
    {
        var plan = For(2580f, 19);

        Assert.Equal(10, plan.Shown);
        Assert.Equal(19, plan.Potential);
        Assert.Equal(19, plan.Columns);
        Assert.Equal(570f, plan.Grow);
        Assert.Equal(594f, plan.Shrink);
        Assert.Null(plan.WhyNot);
    }

    /// <summary>
    /// 32:9, grid of unknown width: the whole potential. Logged on 2026-09-28 as
    /// LeftSide 3126 -> 1272 and panel 680 -> 2510, "can now show 39 columns".
    /// </summary>
    [Fact]
    public void ThirtyTwoByNineHasRoomFor39Columns()
    {
        var plan = For(3840f, 0);

        Assert.Equal(39, plan.Potential);
        Assert.Equal(39, plan.Columns);
        Assert.Equal(1830f, plan.Grow);
        Assert.Equal(1272f, LeftSide(3840f) - plan.Shrink);
    }

    /// <summary>
    /// The dead space seen on 2026-09-28: a 3424x1361 window has room for 21 columns,
    /// the grid was 19, and the panel took all 21 -- two columns of empty panel. Now
    /// it stops at the grid, and the potential still says 21 for the server.
    /// </summary>
    [Fact]
    public void APanelStopsAtAGridNarrowerThanTheRoom()
    {
        var plan = StashPlan.For(2003.1f, Panel, Chrome, Reserve, 19);

        Assert.Equal(21, plan.Potential);
        Assert.Equal(19, plan.Columns);
        Assert.Equal(StashPlan.WidthOfColumns(19) + Chrome + StashPlan.ScrollSlackPixels, Panel + plan.Grow);
        Assert.True(plan.CanWiden);
    }

    [Fact]
    public void AVanillaGridLeavesThePanelAloneButReportsTheRoom()
    {
        var plan = For(2580f, 10);

        Assert.False(plan.Widens);
        Assert.Equal(10, plan.Columns);
        Assert.Equal(19, plan.Potential);
        Assert.True(plan.CanWiden);
        Assert.Null(plan.WhyNot);
    }

    /// <summary>
    /// A grid wider than the room (columns forced with ignoreMeasurement) gets all the
    /// room there is and scrolls sideways; the panel never grows past what fits.
    /// </summary>
    [Fact]
    public void AGridWiderThanTheRoomGetsAllTheRoom()
    {
        var plan = For(2580f, 25);

        Assert.Equal(19, plan.Columns);
        Assert.Equal(For(2580f, 0).Grow, plan.Grow);
    }

    /// <summary>
    /// 16:9 at any resolution: canvas 1920, LeftSide 1206, less than the 1240 the gear
    /// side keeps. Nothing to widen, and a reason a player can act on.
    /// </summary>
    [Fact]
    public void SixteenByNineCannotWidenAndSaysWhy()
    {
        var plan = For(1920f, 19);

        Assert.False(plan.CanWiden);
        Assert.False(plan.Widens);
        Assert.Equal(10, plan.Potential);
        Assert.NotNull(plan.WhyNot);
        Assert.Contains("1206", plan.WhyNot);
    }

    [Fact]
    public void AReserveBelowTheMinimumIsRaisedToIt()
    {
        Assert.Equal(
            StashPlan.For(LeftSide(2580f), Panel, Chrome, StashPlan.MinReservePerPanel, 0).Potential,
            StashPlan.For(LeftSide(2580f), Panel, Chrome, 100f, 0).Potential);
    }

    /// <summary>The gap is always what LeftSide gives up beyond what the panel takes.</summary>
    [Theory]
    [InlineData(2100f, 0)]
    [InlineData(2580f, 19)]
    [InlineData(2717.1f, 0)]
    [InlineData(3840f, 25)]
    [InlineData(5160f, 0)]
    public void ShrinkIsGrowPlusTheGap(float canvas, int grid)
    {
        var plan = For(canvas, grid);

        Assert.True(plan.Widens);
        Assert.Equal(plan.Grow + StashPlan.GapPixels, plan.Shrink);
        Assert.True(plan.Shrink <= LeftSide(canvas) - Reserve * 2f);
    }

    /// <summary>
    /// The server predicts the probe's answer before the client has ever run, with its
    /// own copies of these constants (the halves are different frameworks and cannot
    /// share them). If they drift, a first start sizes the grid for a panel the client
    /// will not build. Includes the two canvases either side of the first extra column.
    /// </summary>
    [Theory]
    [InlineData(1728)]
    [InlineData(1920)]
    [InlineData(2016)]
    [InlineData(2017)]
    [InlineData(2100)]
    [InlineData(2560)]
    [InlineData(2580)]
    [InlineData(3000)]
    [InlineData(3440)]
    [InlineData(3840)]
    [InlineData(5160)]
    public void ThePotentialIsWhatTheServerPredicts(int canvas)
    {
        Assert.Equal(StashFit.WidenedColumns(canvas), For(canvas, 0).Potential);
    }
}
