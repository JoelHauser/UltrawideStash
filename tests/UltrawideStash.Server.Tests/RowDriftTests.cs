using UltrawideStash.Server;
using Xunit;

namespace UltrawideStash.Server.Tests;

/// <summary>
/// What happens to the row count across repeated server starts.
///
/// StashWidener recomputes the shape on every start: rows are the compensated target
/// clamped up to the deepest row anything is standing on. The clamp reads the profile
/// as it is *now*, so as the player's deepest item moves, the row count moves with it.
///
/// These pin the shape of that movement, because the shape is the whole question of
/// whether it matters: it is monotonically downward, it converges, it stops, and it
/// never crosses an item.
/// </summary>
public class RowDriftTests
{
    private const int EdgeOfDarknessRows = 68;
    private const int VanillaColumns = 10;

    /// <summary>One server start: the rows StashWidener would settle on.</summary>
    private static int RowsAfterStart(int columns, int vanillaRows, int deepestOccupied)
    {
        var plan = StashLayout.For(
            VanillaColumns, vanillaRows, columns,
            compensateRows: true,
            deepestOccupiedRow: deepestOccupied);

        return plan.Applied ? plan.Rows : vanillaRows;
    }

    [Fact]
    public void TheCompensatedTargetIsWhereItSettles()
    {
        // ceil(680 / 16) = 43.
        Assert.Equal(43, RowsAfterStart(16, EdgeOfDarknessRows, deepestOccupied: 0));
        Assert.Equal(43, RowsAfterStart(16, EdgeOfDarknessRows, deepestOccupied: 43));
    }

    [Fact]
    public void AStashDeeperThanTheTargetIsHeldOpenForIt()
    {
        // The guard doing its job: 60 rows of stuff means 60 rows of stash, not 43.
        Assert.Equal(60, RowsAfterStart(16, EdgeOfDarknessRows, deepestOccupied: 60));
    }

    [Fact]
    public void TidyingUpIsWhatShrinksTheStash()
    {
        // Install with items down to row 60 -> a 16x60 stash, well above the 43 the
        // capacity target wanted. Then the player empties the bottom over time, and
        // each restart settles lower. This is the drift, stated plainly.
        var sequence = new[] { 60, 58, 52, 47, 44, 43, 40, 12, 0 }
            .Select(deepest => RowsAfterStart(16, EdgeOfDarknessRows, deepest))
            .ToArray();

        Assert.Equal(new[] { 60, 58, 52, 47, 44, 43, 43, 43, 43 }, sequence);
    }

    [Fact]
    public void TheDriftIsOnlyEverDownwardAndStopsAtTheTarget()
    {
        // Emptying the stash one row at a time, from full to empty. The row count must
        // never rise, and must never fall below the compensated target.
        const int target = 43;
        var previous = int.MaxValue;

        for (var deepest = EdgeOfDarknessRows; deepest >= 0; deepest--)
        {
            var rows = RowsAfterStart(16, EdgeOfDarknessRows, deepest);

            Assert.True(rows <= previous, $"rows rose from {previous} to {rows}");
            Assert.True(rows >= target, $"rows fell to {rows}, below the target {target}");

            previous = rows;
        }

        Assert.Equal(target, previous);
    }

    [Fact]
    public void OnceItReachesTheTargetItCanNeverMoveAgain()
    {
        // The reason the drift is a transient and not a permanent wobble: at the
        // target the stash IS that tall, so nothing can be stored below it, so the
        // clamp can never exceed the target again. It is a fixed point.
        var rows = RowsAfterStart(16, EdgeOfDarknessRows, deepestOccupied: 0);

        for (var deepestPossible = rows; deepestPossible >= 0; deepestPossible--)
        {
            Assert.Equal(rows, RowsAfterStart(16, EdgeOfDarknessRows, deepestPossible));
        }
    }

    [Fact]
    public void ItNeverCrossesAnItem()
    {
        // The property that makes this a cosmetic annoyance rather than a data hazard:
        // whatever the drift does, the stash is always at least as deep as the deepest
        // thing in it, so the repack has nothing to relocate.
        foreach (var columns in new[] { 11, 12, 16, 20, 24, 30, 40 })
        {
            foreach (var vanillaRows in new[] { 30, 40, 50, 68, 72 })
            {
                for (var deepest = 0; deepest <= vanillaRows; deepest++)
                {
                    var rows = RowsAfterStart(columns, vanillaRows, deepest);

                    Assert.True(rows >= deepest,
                        $"{columns} cols, vanilla {vanillaRows} rows, {deepest} deep -> "
                        + $"{rows} rows, which would strand something");
                }
            }
        }
    }

    [Fact]
    public void WithoutRowCompensationThereIsNoDriftAtAll()
    {
        // compensateRows:false keeps every vanilla row, so the clamp never binds and
        // the row count is the same on every start regardless of what is stored.
        foreach (var deepest in new[] { 0, 20, 43, 60, 68 })
        {
            var plan = StashLayout.For(
                VanillaColumns, EdgeOfDarknessRows, 16,
                compensateRows: false,
                deepestOccupiedRow: deepest);

            Assert.Equal(EdgeOfDarknessRows, plan.Rows);
        }
    }

    [Fact]
    public void AStandardStashDriftsTooAndByProportionallyMore()
    {
        // Worth knowing for the edition question: a Standard stash is 10x30 = 300
        // cells, so at 16 columns the target is ceil(300/16) = 19. A player who was
        // using most of their vanilla 30 rows sees a bigger proportional drop than an
        // Edge of Darkness player does.
        Assert.Equal(19, RowsAfterStart(16, 30, deepestOccupied: 0));
        Assert.Equal(28, RowsAfterStart(16, 30, deepestOccupied: 28));

        // 28 -> 19 is a third of the visible stash, once they clear the bottom.
        Assert.Equal(19, RowsAfterStart(16, 30, deepestOccupied: 19));
    }
}
