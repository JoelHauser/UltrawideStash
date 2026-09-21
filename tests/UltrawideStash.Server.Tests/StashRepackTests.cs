using UltrawideStash.Server;

namespace UltrawideStash.Server.Tests;

/// <summary>
/// The relocation that keeps items reachable when the grid changes size.
///
/// The case that matters most is narrowing back to vanilla — the uninstall path. If this
/// is wrong, a player sets columns to 10, removes the mod, and finds a stash short of
/// whatever used to live in columns 10 and beyond.
/// </summary>
public class StashRepackTests
{
    private static StashRepack.Placement At(string id, int x, int y, int w = 1, int h = 1, int r = 0)
    {
        return new StashRepack.Placement(id, x, y, w, h, r);
    }

    [Fact]
    public void NothingToDoWhenEverythingAlreadyFits()
    {
        var plan = StashRepack.For([At("a", 0, 0), At("b", 9, 67)], 10, 68);

        Assert.True(plan.Complete);
        Assert.Empty(plan.Moves);
    }

    /// <summary>The uninstall case, in one test.</summary>
    [Fact]
    public void NarrowingBackToVanillaBringsWideItemsHome()
    {
        var items = new[]
        {
            At("kept", 0, 0),
            At("wide1", 12, 0),
            At("wide2", 15, 4),
        };

        var plan = StashRepack.For(items, 10, 68);

        Assert.True(plan.Complete);
        Assert.Equal(2, plan.Moves.Count);

        foreach (var move in plan.Moves)
        {
            Assert.True(move.ToX is >= 0 and < 10, $"{move.ItemId} landed at x={move.ToX}");
            Assert.True(move.ToY is >= 0 and < 68);
        }

        // The one that already fitted is untouched.
        Assert.DoesNotContain(plan.Moves, m => m.ItemId == "kept");
    }

    [Fact]
    public void ItemsThatFitKeepTheirExactPosition()
    {
        var items = new[] { At("a", 3, 7), At("b", 14, 2) };

        var plan = StashRepack.For(items, 10, 68);

        Assert.Single(plan.Moves);
        Assert.Equal("b", plan.Moves[0].ItemId);
    }

    [Fact]
    public void RelocatedItemsNeverOverlapWhatStayed()
    {
        // Fill the whole first row, then displace something into the grid.
        var items = new List<StashRepack.Placement>();

        for (var x = 0; x < 10; x++) items.Add(At($"row0-{x}", x, 0));

        items.Add(At("displaced", 30, 0));

        var plan = StashRepack.For(items, 10, 68);

        Assert.True(plan.Complete);
        var move = Assert.Single(plan.Moves);

        Assert.Equal("displaced", move.ItemId);
        Assert.True(move.ToY >= 1, "it must not land on top of the full first row");
    }

    /// <summary>
    /// Rotation changes the footprint, so it changes what will fit. A 5x2 rifle stood on
    /// end is 2 wide and 5 tall, and needs five rows.
    /// </summary>
    [Fact]
    public void RotationIsRespectedWhenPlacing()
    {
        var upright = At("rifle", 20, 0, w: 5, h: 2, r: StashOccupancy.Vertical);

        Assert.Equal(2, upright.EffectiveWidth);
        Assert.Equal(5, upright.EffectiveHeight);

        var plan = StashRepack.For([upright], 10, 68);

        Assert.True(plan.Complete);
        var move = Assert.Single(plan.Moves);

        Assert.True(move.ToX + 2 <= 10);
        Assert.True(move.ToY + 5 <= 68);
    }

    [Fact]
    public void BiggerItemsArePlacedFirst()
    {
        // If the singles went first they would fragment row 0 and the 4-wide case could
        // be pushed further down than it needs to be.
        var items = new[]
        {
            At("small1", 20, 0),
            At("small2", 21, 0),
            At("case", 22, 0, w: 4, h: 2),
        };

        var plan = StashRepack.For(items, 10, 68);

        Assert.True(plan.Complete);

        var caseMove = plan.Moves.Single(m => m.ItemId == "case");

        Assert.Equal(0, caseMove.ToX);
        Assert.Equal(0, caseMove.ToY);
    }

    /// <summary>
    /// The refusal. A grid that genuinely cannot hold everything must report it rather
    /// than silently dropping something, so the caller can leave the stash alone.
    /// </summary>
    [Fact]
    public void ItemsWithNowhereToGoAreReportedNotDropped()
    {
        // A 2x2 grid with three 2x2 items: only one can fit.
        var items = new[]
        {
            At("a", 0, 0, w: 2, h: 2),
            At("b", 8, 0, w: 2, h: 2),
            At("c", 8, 4, w: 2, h: 2),
        };

        var plan = StashRepack.For(items, 2, 2);

        Assert.False(plan.Complete);
        Assert.NotEmpty(plan.Homeless);

        // Nothing is invented: every id is either staying, moved, or reported homeless.
        var accounted = plan.Moves.Select(m => m.ItemId).Concat(plan.Homeless).ToHashSet();

        Assert.All(items.Where(i => !i.FitsIn(2, 2)), i => Assert.Contains(i.ItemId, accounted));
    }

    [Theory]
    [InlineData(0, 68)]
    [InlineData(10, 0)]
    [InlineData(-1, -1)]
    public void ADegenerateGridIsRefused(int columns, int rows)
    {
        var plan = StashRepack.For([At("a", 0, 0)], columns, rows);

        Assert.False(plan.Complete);
    }

    /// <summary>
    /// A full vanilla stash narrowed from 16 wide: 680 single cells, which is exactly
    /// what a 10x68 grid holds, so every one of them must find a home.
    /// </summary>
    [Fact]
    public void AFullStashStillPacksExactly()
    {
        var items = new List<StashRepack.Placement>();

        // Laid out as if the stash had been 16 x 43.
        var n = 0;

        for (var y = 0; y < 43 && n < 680; y++)
        {
            for (var x = 0; x < 16 && n < 680; x++)
            {
                items.Add(At($"i{n++}", x, y));
            }
        }

        Assert.Equal(680, items.Count);

        var plan = StashRepack.For(items, 10, 68);

        Assert.True(plan.Complete, $"{plan.Homeless.Count} item(s) had nowhere to go");

        // Everything ends up inside, and nothing shares a cell.
        var used = new HashSet<(int, int)>();

        var final = items.ToDictionary(i => i.ItemId, i => (i.X, i.Y));

        foreach (var move in plan.Moves) final[move.ItemId] = (move.ToX, move.ToY);

        foreach (var (x, y) in final.Values)
        {
            Assert.InRange(x, 0, 9);
            Assert.InRange(y, 0, 67);
            Assert.True(used.Add((x, y)), $"two items both ended up at {x},{y}");
        }
    }
}
