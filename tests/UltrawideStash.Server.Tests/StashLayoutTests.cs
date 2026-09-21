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

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(41)]
    [InlineData(1000)]
    [InlineData(-4)]
    public void ColumnsOutsideTheRangeAreRefused(int target)
    {
        var plan = StashLayout.For(10, 68, target, compensateRows: true, deepestOccupiedRow: 0);

        Assert.False(plan.Applied);
        Assert.Equal(10, plan.Columns);
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
    public void TheColumnCapIsTheUltrawideCeiling()
    {
        const float ultrawideCanvasWidth = 3440f / (1440f / 1080f);

        Assert.Equal(2580f, ultrawideCanvasWidth, 0.5);

        Assert.True(
            StashLayout.MaxColumns * 63 + 1 <= ultrawideCanvasWidth,
            "the cap should be reachable on the screen this mod was written for");

        Assert.True(
            (StashLayout.MaxColumns + 1) * 63 + 1 > ultrawideCanvasWidth,
            "one column past the cap should not fit");
    }
}
