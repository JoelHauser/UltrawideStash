using UltrawideStash.Server;
using Xunit;

namespace UltrawideStash.Server.Tests;

/// <summary>
/// The hideout stash ladder, and the stranding it exists to prevent.
///
/// Not everybody plays Edge of Darkness. A Standard-edition player climbs from the
/// Standard stash template to the Edge of Darkness one as they upgrade the hideout's
/// Stash area, because the area's <c>StashSize</c> bonus carries a <c>templateId</c>
/// rather than a row count. Those are the same templates this mod edits, so the mod
/// has to plan them as a sequence and not five independent grids.
/// </summary>
public class StashLadderTests
{
    private const string Standard = "566abbc34bdc2d92178b4576";
    private const string LeftBehind = "5811ce572459770cba1a34ea";
    private const string Prepare = "5811ce662459770f6f490f32";
    private const string EdgeOfDarkness = "5811ce772459770e9e5f9532";
    private const string Unheard = "6602bcf19cc643f44a04274b";

    /// <summary>Vanilla dimensions, as verified against items.json.</summary>
    private static readonly (string Id, int Rows)[] Vanilla =
    [
        (Standard, 30),
        (LeftBehind, 40),
        (Prepare, 50),
        (EdgeOfDarkness, 68),
        (Unheard, 72),
    ];

    [Fact]
    public void TheLadderIsInHideoutUpgradeOrder()
    {
        Assert.Equal(
            new[] { Standard, LeftBehind, Prepare, EdgeOfDarkness, Unheard },
            StashLadder.Rungs.Select(r => r.Id).ToArray());
    }

    [Fact]
    public void TheLadderRisesInCapacity()
    {
        // A profile only ever moves up, so each rung must be at least as large as the
        // one below. If this ever fails, an upgrade is a downgrade.
        var rows = StashLadder.Rungs
            .Select(r => Vanilla.First(v => v.Id == r.Id).Rows)
            .ToArray();

        for (var i = 1; i < rows.Length; i++)
        {
            Assert.True(rows[i] >= rows[i - 1],
                $"rung {i} has {rows[i]} rows, fewer than rung {i - 1}'s {rows[i - 1]}");
        }
    }

    [Fact]
    public void AFloorPropagatesUpTheLadderButNotDown()
    {
        var floors = StashLadder.RowFloors(new Dictionary<string, int>
        {
            [Prepare] = 44,
        });

        // Nothing below Prepare can reach it, so they are unaffected.
        Assert.Equal(0, floors[Standard]);
        Assert.Equal(0, floors[LeftBehind]);

        // Prepare and everything a Prepare profile can upgrade into must hold 44.
        Assert.Equal(44, floors[Prepare]);
        Assert.Equal(44, floors[EdgeOfDarkness]);
        Assert.Equal(44, floors[Unheard]);
    }

    [Fact]
    public void OnePlayersDeepStashDoesNotInflateAnothersLowerRung()
    {
        // A shared install. The Edge of Darkness player's depth must not hand the
        // Standard player rows they never earned -- they cannot reach that rung.
        var floors = StashLadder.RowFloors(new Dictionary<string, int>
        {
            [EdgeOfDarkness] = 60,
            [Standard] = 12,
        });

        Assert.Equal(12, floors[Standard]);
        Assert.Equal(12, floors[LeftBehind]);
        Assert.Equal(12, floors[Prepare]);
        Assert.Equal(60, floors[EdgeOfDarkness]);
    }

    [Fact]
    public void FloorsAreMonotonicForAnyInput()
    {
        var floors = StashLadder.RowFloors(new Dictionary<string, int>
        {
            [Standard] = 28,
            [LeftBehind] = 3,
            [Prepare] = 44,
            [EdgeOfDarkness] = 12,
            [Unheard] = 1,
        });

        var previous = 0;

        foreach (var (id, _) in StashLadder.Rungs)
        {
            Assert.True(floors[id] >= previous,
                $"{id} floors at {floors[id]}, below the {previous} beneath it");
            previous = floors[id];
        }
    }

    /// <summary>
    /// The regression this whole file is for.
    ///
    /// Before 0.6.0 the row clamp was per template, from the profiles currently on it.
    /// A Standard player with items down to row 28 got Standard planned at 16x28,
    /// while Left Behind -- which nobody was on -- was planned at 16x25. Upgrading the
    /// hideout swapped them onto Left Behind and three rows of their belongings went
    /// out of bounds, mid-session, with no repack until the next server start.
    /// </summary>
    [Fact]
    public void UpgradingTheHideoutNeverShortensAStandardPlayersStash()
    {
        const int columns = 16;

        var floors = StashLadder.RowFloors(new Dictionary<string, int>
        {
            [Standard] = 28,
        });

        var planned = new Dictionary<string, int>();

        foreach (var (id, _) in StashLadder.Rungs)
        {
            var vanillaRows = Vanilla.First(v => v.Id == id).Rows;

            var plan = StashLayout.For(
                10, vanillaRows, columns, compensateRows: true,
                deepestOccupiedRow: floors[id]);

            planned[id] = plan.Applied ? plan.Rows : vanillaRows;
        }

        // The compensated target for Left Behind is ceil(400/16) = 25, which is what
        // the old code produced and which would have stranded rows 25 to 27.
        Assert.True(planned[LeftBehind] >= 28,
            $"Left Behind planned at {planned[LeftBehind]} rows, below the 28 the player "
            + "is standing on");

        var previous = 0;

        foreach (var (id, _) in StashLadder.Rungs)
        {
            Assert.True(planned[id] >= previous,
                $"upgrading into {id} would shrink the stash from {previous} rows to {planned[id]}");
            previous = planned[id];
        }
    }

    [Theory]
    [InlineData(11)]
    [InlineData(16)]
    [InlineData(20)]
    [InlineData(24)]
    [InlineData(40)]
    public void NoWidthMakesAnyUpgradeAShrink(int columns)
    {
        // Every depth a player could plausibly have, at every width, across the whole
        // ladder. An upgrade must never reduce the row count.
        foreach (var depth in new[] { 0, 5, 19, 25, 28, 40, 50, 68, 72 })
        {
            foreach (var (startId, _) in StashLadder.Rungs)
            {
                var floors = StashLadder.RowFloors(
                    new Dictionary<string, int> { [startId] = depth });

                var previous = 0;

                foreach (var (id, _) in StashLadder.Rungs)
                {
                    var vanillaRows = Vanilla.First(v => v.Id == id).Rows;

                    var plan = StashLayout.For(
                        10, vanillaRows, columns, compensateRows: true,
                        deepestOccupiedRow: floors[id]);

                    var rows = plan.Applied ? plan.Rows : vanillaRows;

                    Assert.True(rows >= previous,
                        $"at {columns} columns with depth {depth} on {startId}, upgrading into "
                        + $"{id} drops from {previous} rows to {rows}");

                    previous = rows;
                }
            }
        }
    }

    [Fact]
    public void ARungAlwaysClearsTheDepthOfEveryRungBeneathIt()
    {
        // The property the floor is for, stated directly: whatever a profile is
        // standing on, the rung it can upgrade into has room for it.
        foreach (var columns in new[] { 11, 16, 20, 30 })
        {
            foreach (var depth in new[] { 10, 28, 45, 66 })
            {
                foreach (var (startId, _) in StashLadder.Rungs)
                {
                    var start = StashLadder.PositionOf(startId);

                    var floors = StashLadder.RowFloors(
                        new Dictionary<string, int> { [startId] = depth });

                    for (var i = start; i < StashLadder.Rungs.Length; i++)
                    {
                        var id = StashLadder.Rungs[i].Id;
                        var vanillaRows = Vanilla.First(v => v.Id == id).Rows;

                        var plan = StashLayout.For(
                            10, vanillaRows, columns, compensateRows: true,
                            deepestOccupiedRow: floors[id]);

                        var rows = plan.Applied ? plan.Rows : vanillaRows;

                        Assert.True(rows >= depth,
                            $"a profile {depth} rows deep on {startId} would not fit {id} "
                            + $"at {columns} columns ({rows} rows)");
                    }
                }
            }
        }
    }

    [Fact]
    public void PositionOfFindsEveryRungAndNothingElse()
    {
        for (var i = 0; i < StashLadder.Rungs.Length; i++)
        {
            Assert.Equal(i, StashLadder.PositionOf(StashLadder.Rungs[i].Id));
        }

        Assert.Equal(-1, StashLadder.PositionOf("5c0a596086f7747bef5731c2")); // the dev stash
        Assert.Equal(-1, StashLadder.PositionOf(string.Empty));
    }

    /// <summary>
    /// Proof that the bug above was real, by reproducing the old behaviour here.
    ///
    /// The repo's notes record a case where a fix and its regression test were both
    /// reverted because the test passed against the *unfixed* code. So this asserts
    /// the failure directly: with the pre-0.6.0 per-template clamp, upgrading the
    /// hideout shortens the stash. If this ever stops failing in the old shape, the
    /// scenario has been misunderstood and the fix above is guarding nothing.
    /// </summary>
    [Fact]
    public void TheOldPerTemplateClampGenuinelyDidStrandItems()
    {
        const int columns = 16;
        const int standardDepth = 28;

        // The old code: each template clamped only by the profiles sitting on it.
        var standard = StashLayout.For(
            10, 30, columns, compensateRows: true, deepestOccupiedRow: standardDepth);

        var leftBehind = StashLayout.For(
            10, 40, columns, compensateRows: true, deepestOccupiedRow: 0);

        Assert.Equal(28, standard.Rows);   // clamp beat the compensated 19
        Assert.Equal(25, leftBehind.Rows); // nothing on it, so no clamp at all

        Assert.True(leftBehind.Rows < standard.Rows,
            "if this does not shrink, the stranding scenario has been misread");

        Assert.True(leftBehind.Rows < standardDepth,
            $"rows {leftBehind.Rows} to {standardDepth - 1} held items and would have gone "
            + "out of bounds on upgrade");

        // And the fix: the same situation with the ladder floor applied.
        var floors = StashLadder.RowFloors(
            new Dictionary<string, int> { [Standard] = standardDepth });

        var fixedLeftBehind = StashLayout.For(
            10, 40, columns, compensateRows: true, deepestOccupiedRow: floors[LeftBehind]);

        Assert.True(fixedLeftBehind.Rows >= standard.Rows);
    }

    [Fact]
    public void AnEmptyOccupancyFloorsEverythingAtZero()
    {
        var floors = StashLadder.RowFloors(new Dictionary<string, int>());

        foreach (var (id, _) in StashLadder.Rungs)
        {
            Assert.Equal(0, floors[id]);
        }
    }
}
