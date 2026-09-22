using UltrawideStash.Server;
using Xunit;

namespace UltrawideStash.Server.Tests;

/// <summary>
/// The prediction that lets the first server start size the stash correctly, before
/// any client has measured anything.
///
/// These numbers are not a model of what the client *should* do. They are what a live
/// 3440x1440 client was observed doing, and the server has to agree with it or the grid
/// and the panel disagree on screen -- too many columns and the stash grows a horizontal
/// scrollbar, too few and it shows a dead column. Both were shipped, in that order,
/// before this arithmetic existed.
/// </summary>
public class WidenedColumnsTests
{
    /// <summary>
    /// The one measured case. A 3440x1440 client widened its panel to 1250 px, leaving
    /// a 1202 px viewport around a 1198 px grid: 19 columns with 4 px to spare.
    /// If this ever fails, the client and the server have drifted apart.
    /// </summary>
    [Fact]
    public void MatchesTheMeasuredUltrawideClient()
    {
        Assert.Equal(19, StashFit.WidenedColumnsForScreen(3440, 1440));
    }

    [Theory]
    // Every 16:9 screen scales to a 1920-wide canvas whatever its resolution, and a
    // 1920 canvas has no room to widen into: LeftSide is 1206 against a 1240 reserve.
    // A 4K monitor gains nothing, which is the part players find surprising.
    [InlineData(1920, 1080)]
    [InlineData(2560, 1440)]
    [InlineData(3840, 2160)]
    // 16:10 is taller than 16:9, so the height-driven scale still lands on 1920.
    [InlineData(1920, 1200)]
    public void SixteenByNineGainsNothing(int width, int height)
    {
        Assert.Equal(StashFit.VanillaColumns, StashFit.WidenedColumnsForScreen(width, height));
    }

    [Theory]
    // Canvas 2560 against 3440x1440's 2580 -- 20 px narrower, which is not a column,
    // so it lands on the same 19.
    [InlineData(2560, 1080, 19)]
    // 32:9. Canvas 3840, and the extra 1260 px is twenty more columns.
    [InlineData(5120, 1440, 39)]
    [InlineData(3840, 1080, 39)]
    public void WiderScreensGainProportionally(int width, int height, int expected)
    {
        Assert.Equal(expected, StashFit.WidenedColumnsForScreen(width, height));
    }

    /// <summary>
    /// Never narrower than vanilla. Shrinking the stash is how items get stranded, and
    /// a prediction is not a good enough reason to risk it.
    /// </summary>
    [Theory]
    [InlineData(640, 480)]
    [InlineData(1024, 768)]
    [InlineData(1280, 1024)]
    public void NeverBelowVanilla(int width, int height)
    {
        Assert.True(StashFit.WidenedColumnsForScreen(width, height) >= StashFit.VanillaColumns);
    }

    /// <summary>
    /// The predicted grid has to fit the panel the client will build for it. This is
    /// the invariant the scrollbar bug violated: grid wider than viewport.
    /// </summary>
    [Theory]
    [InlineData(3440, 1440)]
    [InlineData(2560, 1080)]
    [InlineData(5120, 1440)]
    [InlineData(3840, 1080)]
    public void PredictedGridFitsThePanelItPredicts(int width, int height)
    {
        var canvas = StashFit.CanvasWidth(width, height);
        var columns = StashFit.WidenedColumns(canvas);

        // What the client will do with that canvas, in its own terms.
        var leftSide = canvas - 714;
        var slack = leftSide - 1240;
        var panel = 680 + slack - 24;
        var viewport = panel - 48;

        var grid = StashFit.WidthOfColumns(columns);

        Assert.True(
            grid <= viewport,
            $"{width}x{height}: grid {grid} px does not fit viewport {viewport} px");
    }

    /// <summary>
    /// And it must not leave a whole column's worth of unused panel, which is the other
    /// way this goes wrong and the one the player notices first.
    /// </summary>
    [Theory]
    [InlineData(3440, 1440)]
    [InlineData(2560, 1080)]
    [InlineData(5120, 1440)]
    public void PredictedGridLeavesNoDeadColumn(int width, int height)
    {
        var canvas = StashFit.CanvasWidth(width, height);
        var columns = StashFit.WidenedColumns(canvas);

        var viewport = 680 + (canvas - 714 - 1240) - 24 - 48;
        var spare = viewport - StashFit.WidthOfColumns(columns);

        Assert.True(
            spare < StashFit.CellPixels,
            $"{width}x{height}: {spare} px spare is room for another column");
    }
}
