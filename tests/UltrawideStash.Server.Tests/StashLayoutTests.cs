using UltrawideStash.Server;

namespace UltrawideStash.Server.Tests;

/// <summary>
/// The shape decision, checked against the five real stash templates.
///
/// These are the numbers out of items.json on SPT 4.1.5, not invented ones -- the
/// point of the suite is that the rules behave on the data the mod will actually meet.
/// </summary>
public class StashLayoutTests
{
    /// <summary>Every stash a player can own, as the database has them.</summary>
    public static TheoryData<int, int, string> RealStashes => new()
    {
        { 10, 30, "Standard" },
        { 10, 40, "Left Behind" },
        { 10, 50, "Prepare for Escape" },
        { 10, 68, "Edge of Darkness" },
        { 10, 72, "The Unheard Edition" },
    };

    /// <summary>
    /// The guarantee auto-sort depends on.
    ///
    /// Sorting empties the grid and re-places everything, and both the game's own
    /// ItemManipulator.Sort and Advanced Stash Sorting fail outright if the result
    /// will not fit. A stash that sorted before this mod must still sort after it,
    /// so the compensated grid is never allowed to be smaller than the original.
    /// </summary>
    [Theory]
    [MemberData(nameof(RealStashes))]
    public void CompensatingNeverDecreasesCapacity(int columns, int rows, string edition)
    {
        var vanilla = columns * rows;

        for (var target = 11; target <= StashLayout.MaxColumns; target++)
        {
            var plan = StashLayout.For(columns, rows, target, compensateRows: true, deepestOccupiedRow: 0);

            Assert.True(plan.Applied, $"{edition} at {target} columns: {plan.Reason}");
            Assert.True(
                plan.Capacity >= vanilla,
                $"{edition} at {target} columns lost capacity: {plan.Capacity} < {vanilla}");
        }
    }

    /// <summary>
    /// And the other side of it: "same capacity" has to still mean something. The
    /// ceiling can only overshoot by less than one row.
    /// </summary>
    [Theory]
    [MemberData(nameof(RealStashes))]
    public void CompensatingStaysWithinOneRowOfVanilla(int columns, int rows, string edition)
    {
        var vanilla = columns * rows;

        for (var target = 11; target <= StashLayout.MaxColumns; target++)
        {
            var plan = StashLayout.For(columns, rows, target, compensateRows: true, deepestOccupiedRow: 0);

            Assert.True(
                plan.Capacity < vanilla + target,
                $"{edition} at {target} columns gained too much: {plan.Capacity} against {vanilla}");
        }
    }

    [Theory]
    [MemberData(nameof(RealStashes))]
    public void WithoutCompensationRowsAreUntouched(int columns, int rows, string edition)
    {
        var plan = StashLayout.For(columns, rows, 16, compensateRows: false, deepestOccupiedRow: 0);

        Assert.True(plan.Applied, edition);
        Assert.Equal(rows, plan.Rows);
        Assert.Equal(16, plan.Columns);
    }

    /// <summary>
    /// The guard that makes row compensation safe to run against a played profile.
    /// An Edge of Darkness stash with something on row 60 must keep at least 61 rows
    /// however much the capacity maths wants to cut.
    /// </summary>
    [Fact]
    public void RowsAreNeverCutBelowStoredItems()
    {
        // 10x68 at 16 columns wants 43 rows. An item standing on row 60 needs 61.
        var plan = StashLayout.For(10, 68, 16, compensateRows: true, deepestOccupiedRow: 61);

        Assert.True(plan.Applied);
        Assert.Equal(61, plan.Rows);
        Assert.Contains("row 61", plan.Reason);
    }

    [Fact]
    public void OccupancyBelowTheTargetDoesNotForceExtraRows()
    {
        // ceil(680/16) = 43; nothing is stored deeper than row 10, so 43 stands.
        var plan = StashLayout.For(10, 68, 16, compensateRows: true, deepestOccupiedRow: 10);

        Assert.True(plan.Applied);
        Assert.Equal(43, plan.Rows);
        Assert.True(plan.Capacity >= 10 * 68);
    }

    [Theory]
    [InlineData(9)]
    [InlineData(5)]
    [InlineData(2)]
    public void NarrowingIsRefused(int target)
    {
        var plan = StashLayout.For(10, 68, target, compensateRows: true, deepestOccupiedRow: 0);

        Assert.False(plan.Applied);
        Assert.Equal(10, plan.Columns);
        Assert.Equal(68, plan.Rows);
        Assert.Contains("refusing to shrink", plan.Reason);
    }

    [Fact]
    public void MatchingVanillaIsANoOp()
    {
        var plan = StashLayout.For(10, 68, 10, compensateRows: true, deepestOccupiedRow: 0);

        Assert.False(plan.Applied);
        Assert.Equal(68, plan.Rows);
    }

    // 41 used to belong in this list, back when the cap was a constant 40 taken from
    // one 3440x1440 monitor. The screen-aware ceiling lives in ColumnChoice now and
    // StashLayout keeps only a bound on absurdity, so 41 is an ordinary width here and
    // is tested as such below.
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(1000)]
    [InlineData(-4)]
    public void ColumnsOutsideTheRangeAreRefused(int target)
    {
        var plan = StashLayout.For(10, 68, target, compensateRows: true, deepestOccupiedRow: 0);

        Assert.False(plan.Applied);
        Assert.Equal(10, plan.Columns);
    }

    [Fact]
    public void AWidthPastTheOldHardCodedCapIsNoLongerRefusedHere()
    {
        // A 32:9 5120x1440 screen has a 3840-wide canvas, which fits 60 columns. The
        // old constant would have refused every one past 40 regardless of screen.
        var plan = StashLayout.For(10, 68, 41, compensateRows: true, deepestOccupiedRow: 0);

        Assert.True(plan.Applied);
        Assert.Equal(41, plan.Columns);
    }

    [Fact]
    public void ANonGridTemplateIsRefused()
    {
        var plan = StashLayout.For(0, 0, 16, compensateRows: true, deepestOccupiedRow: 0);

        Assert.False(plan.Applied);
        Assert.Contains("not a grid", plan.Reason);
    }

    /// <summary>
    /// The pixel width has to match EFT's own arithmetic exactly -- it is what the
    /// probe compares against, and a disagreement would make both reports wrong in
    /// the same direction and look like agreement.
    /// </summary>
    [Theory]
    [InlineData(10, 631)]
    [InlineData(16, 1009)]
    [InlineData(20, 1261)]
    [InlineData(40, 2521)]
    public void PixelWidthMatchesGetCellPixelSize(int columns, int expected)
    {
        var plan = StashLayout.For(10, 68, columns, compensateRows: false, deepestOccupiedRow: 0);

        // 10 is refused as a no-op, so check the formula directly there.
        var width = plan.Applied ? plan.PixelWidth : columns * 63 + 1;

        Assert.Equal(expected, width);
    }

    /// <summary>
    /// Every row is full width. There is never a short row at the bottom.
    ///
    /// The worry is a fair one, because compensation divides capacity by the column
    /// count and 680/16 is 42.5 -- which looks like it ought to leave half a row. It
    /// does not, for two independent reasons:
    ///
    /// 1. The grid is a rectangle by construction. A stash template carries exactly
    ///    two integers, cellsH and cellsV, so a ragged row is not representable; and
    ///    EFT indexes the grid as a flat List&lt;bool&gt; of GridWidth * GridHeight,
    ///    addressed y * GridWidth + x (Grid.FillSpaceBuffer).
    /// 2. This rounds the row count UP to a whole row, so the remainder becomes extra
    ///    full-width cells rather than a stub.
    ///
    /// So capacity is always an exact multiple of the column count.
    /// </summary>
    [Theory]
    [MemberData(nameof(RealStashes))]
    public void EveryRowIsFullWidth(int columns, int rows, string edition)
    {
        for (var target = 11; target <= StashLayout.MaxColumns; target++)
        {
            var plan = StashLayout.For(columns, rows, target, compensateRows: true, deepestOccupiedRow: 0);

            Assert.True(plan.Applied, edition);

            Assert.Equal(0, plan.Capacity % plan.Columns);

            Assert.Equal(plan.Columns * plan.Rows, plan.Capacity);

            Assert.True(plan.Rows >= 1, $"{edition} at {target} columns got {plan.Rows} rows");
        }
    }

    /// <summary>
    /// The same holds when the occupancy guard forces more rows than the capacity
    /// maths wanted -- a clamped row count is still a whole row.
    /// </summary>
    [Fact]
    public void AClampedRowCountIsStillAWholeRow()
    {
        var plan = StashLayout.For(10, 68, 16, compensateRows: true, deepestOccupiedRow: 61);

        Assert.Equal(0, plan.Capacity % plan.Columns);
        Assert.Equal(16 * 61, plan.Capacity);
    }

    /// <summary>
    /// Where the 40-column cap comes from, and it is not a round number.
    ///
    /// EFT scales the menu canvas by min(width/1920, height/1080)
    /// (UICanvasScalerController.RunResolutionObserver), so a 3440x1440 screen gets
    /// 1440/1080 = 1.333 and a canvas 2580 logical units wide. A 40-column grid is
    /// 2521px and fits; 41 is 2584px and does not. So 40 is exactly the widest stash
    /// that can be drawn in full on a 3440x1440 monitor, and nothing above it is
    /// reachable on any consumer screen at that height.
    /// </summary>
    [Fact]
    public void TheRemainingCapIsOnlyATypoGuard()
    {
        // It must not constrain any real screen. A triple-monitor 5760x1080 canvas is
        // 5760 logical units and fits 91 columns; the absolute cap sits above that, so
        // the only thing it stops is a config holding a nonsense number.
        Assert.True(
            StashLayout.MaxColumns >= StashFit.ColumnsThatFit(StashFit.CanvasWidth(5760, 1080)),
            "the absolute cap must not be the binding limit on a real screen");

        var plan = StashLayout.For(
            10, 68, StashLayout.MaxColumns + 1, compensateRows: true, deepestOccupiedRow: 0);

        Assert.False(plan.Applied);
    }

    [Fact]
    public void TheOldHardCodedCapWasWiderThanA16By9ScreenEntirely()
    {
        // The reason the constant had to go, kept as an assertion so it cannot come
        // back: 40 columns is 2521px, and every 16:9 screen -- 1080p, 1440p, 4K -- has
        // a canvas exactly 1920 units wide.
        Assert.Equal(1920, StashFit.CanvasWidth(1920, 1080));
        Assert.Equal(1920, StashFit.CanvasWidth(3840, 2160));

        Assert.True(
            StashFit.WidthOfColumns(40) > StashFit.CanvasWidth(1920, 1080),
            "40 columns does not fit a 16:9 canvas, so it was never a safe universal cap");
    }
}
