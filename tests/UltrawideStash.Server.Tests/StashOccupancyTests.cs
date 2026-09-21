using UltrawideStash.Server;

namespace UltrawideStash.Server.Tests;

/// <summary>
/// The occupancy guard. Every one of these is about the same failure: under-report
/// the depth and the mod cuts rows out from under the player's belongings.
/// </summary>
public class StashOccupancyTests
{
    [Fact]
    public void AnEmptyStashNeedsNothing()
    {
        Assert.Equal(0, StashOccupancy.DeepestRow([]));
    }

    [Fact]
    public void AOneCellItemAtTheTopNeedsOneRow()
    {
        var p = StashOccupancy.Place(y: 0, width: 1, height: 1, rotation: 0);

        Assert.Equal(1, p.RowsNeeded);
    }

    /// <summary>
    /// The arithmetic that is easy to get wrong: an item's depth is its Y plus its
    /// height, not its Y. A 4-high case at row 40 reaches row 43 and needs 44 rows.
    /// </summary>
    [Fact]
    public void DepthIncludesTheItemsHeight()
    {
        var p = StashOccupancy.Place(y: 40, width: 2, height: 4, rotation: 0);

        Assert.Equal(44, p.RowsNeeded);
    }

    /// <summary>
    /// A rotated item stands on its width. Getting this backwards under-reports
    /// depth, which is the direction that loses items -- so it is checked both ways.
    /// </summary>
    [Fact]
    public void RotationSwapsTheFootprint()
    {
        // A 5x2 rifle laid flat at row 10 reaches row 11.
        var flat = StashOccupancy.Place(y: 10, width: 5, height: 2, rotation: 0);
        Assert.Equal(12, flat.RowsNeeded);

        // Stood on end it reaches row 14.
        var upright = StashOccupancy.Place(y: 10, width: 5, height: 2, rotation: StashOccupancy.Vertical);
        Assert.Equal(15, upright.RowsNeeded);
    }

    [Fact]
    public void TheDeepestItemWins()
    {
        var placements = new[]
        {
            StashOccupancy.Place(0, 1, 1, 0),
            StashOccupancy.Place(60, 1, 1, 0),
            StashOccupancy.Place(30, 2, 4, 0),
        };

        Assert.Equal(61, StashOccupancy.DeepestRow(placements));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-1, 0)]
    [InlineData(-50, 0)]
    public void NegativeCoordinatesAreClampedRatherThanTrusted(int y, int expectedY)
    {
        var p = StashOccupancy.Place(y, 1, 1, 0);

        Assert.Equal(expectedY, p.Y);
        Assert.True(p.RowsNeeded >= 1);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void AZeroSizedItemStillOccupiesOneRow(int height)
    {
        var p = StashOccupancy.Place(y: 5, width: 1, height: height, rotation: 0);

        Assert.Equal(6, p.RowsNeeded);
    }

    /// <summary>
    /// The case the guard exists for, end to end: a full Edge of Darkness stash
    /// meets a request for 16 columns, and the plan comes back honouring the items
    /// rather than the capacity target.
    /// </summary>
    [Fact]
    public void AFullStashRefusesToBeShortened()
    {
        // Something on the very last row of a 10x68 stash.
        var placements = new[] { StashOccupancy.Place(y: 67, width: 1, height: 1, rotation: 0) };

        var deepest = StashOccupancy.DeepestRow(placements);

        Assert.Equal(68, deepest);

        var plan = StashLayout.For(10, 68, 16, compensateRows: true, deepestOccupiedRow: deepest);

        Assert.True(plan.Applied);
        Assert.Equal(68, plan.Rows);
        Assert.Equal(16, plan.Columns);

        // Capacity genuinely goes up here, and that is correct: the alternative is
        // losing the item.
        Assert.True(plan.Capacity > 10 * 68);
    }
}
