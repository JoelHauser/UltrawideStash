using UltrawideStash.Server;

namespace UltrawideStash.Server.Tests;

/// <summary>
/// How deep a stash's contents reach, as the server actually works it out:
/// <see cref="StashRepack.Placement"/>'s footprint and
/// <see cref="ProfileStore.StashContents.TemplateRowsNeeded"/>. Every one of these is about
/// the same failure: under-report the depth and rows get cut out from under the player's
/// belongings.
/// </summary>
public class StashDepthTests
{
    private static StashRepack.Placement At(int y, int w, int h, int r = 0)
        => new("item", 0, y, w, h, r);

    private static ProfileStore.StashContents Holding(params StashRepack.Placement[] items)
        => new("profile.json", "stash", "template", [.. items], null, []);

    [Fact]
    public void AnEmptyStashNeedsNothing()
    {
        Assert.Equal(0, Holding().TemplateRowsNeeded());
    }

    [Fact]
    public void AOneCellItemAtTheTopNeedsOneRow()
    {
        Assert.Equal(1, Holding(At(0, 1, 1)).TemplateRowsNeeded());
    }

    /// <summary>
    /// The arithmetic that is easy to get wrong: an item's depth is its Y plus its
    /// height, not its Y. A 4-high case at row 40 reaches row 43 and needs 44 rows.
    /// </summary>
    [Fact]
    public void DepthIncludesTheItemsHeight()
    {
        Assert.Equal(44, Holding(At(40, 2, 4)).TemplateRowsNeeded());
    }

    /// <summary>
    /// A rotated item stands on its width. Getting this backwards under-reports
    /// depth, which is the direction that loses items -- so it is checked both ways.
    /// </summary>
    [Fact]
    public void RotationSwapsTheFootprint()
    {
        // A 5x2 rifle laid flat at row 10 reaches row 11.
        var flat = At(10, 5, 2);
        Assert.Equal(2, flat.EffectiveHeight);
        Assert.Equal(12, Holding(flat).TemplateRowsNeeded());

        // Stood on end it reaches row 14.
        var upright = At(10, 5, 2, StashRepack.Vertical);
        Assert.Equal(5, upright.EffectiveHeight);
        Assert.Equal(2, upright.EffectiveWidth);
        Assert.Equal(15, Holding(upright).TemplateRowsNeeded());
    }

    [Fact]
    public void TheDeepestItemWins()
    {
        Assert.Equal(61, Holding(At(0, 1, 1), At(60, 1, 1), At(30, 2, 4)).TemplateRowsNeeded());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void AZeroSizedItemStillOccupiesOneCell(int size)
    {
        var item = At(5, size, size);

        Assert.Equal(1, item.EffectiveWidth);
        Assert.Equal(1, item.EffectiveHeight);
        Assert.Equal(6, Holding(item).TemplateRowsNeeded());
    }

    /// <summary>
    /// The case the floor exists for, end to end: a full Edge of Darkness stash meets a
    /// request for 16 columns, and the plan comes back honouring the items rather than
    /// the capacity target.
    /// </summary>
    [Fact]
    public void AFullStashRefusesToBeShortened()
    {
        // Something on the very last row of a 10x68 stash.
        var deepest = Holding(At(67, 1, 1)).TemplateRowsNeeded();

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
