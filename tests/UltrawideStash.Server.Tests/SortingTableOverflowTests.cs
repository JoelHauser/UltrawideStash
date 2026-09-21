using System.Text.Json.Nodes;
using UltrawideStash.Server;

namespace UltrawideStash.Server.Tests;

/// <summary>
/// The overflow: when the stash itself cannot hold everything, items go to the Sorting
/// Table rather than the mod refusing.
///
/// Without this, a nearly-full stash has no safe way back to vanilla — the player is told
/// their stash is too full to uninstall, which is a dead end. The Sorting Table stretches
/// vertically without bound and this mod never touches it, so anything parked there stays
/// reachable afterwards.
/// </summary>
public class SortingTableOverflowTests : IDisposable
{
    private const string EoD = "5811ce772459770e9e5f9532";

    private readonly string _dir = Directory.CreateTempSubdirectory("uws-overflow-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
        GC.SuppressFinalize(this);
    }

    private static StashRepack.Placement At(string id, int x, int y, int w = 1, int h = 1, int r = 0)
    {
        return new StashRepack.Placement(id, x, y, w, h, r);
    }

    [Fact]
    public void TheSortingTableIsSevenWide()
    {
        // SortingTableWindow.ShowGrid calls SortingTable.ClampSize(7, 7).
        Assert.Equal(7, StashRepack.SortingTableColumns);
    }

    [Fact]
    public void OverflowItemsLandInsideTheTable()
    {
        var incoming = new[] { At("a", 0, 0), At("b", 0, 0, w: 3, h: 2), At("c", 0, 0) };

        var transfers = StashRepack.IntoSortingTable(incoming, [], out var tooWide);

        Assert.Empty(tooWide);
        Assert.Equal(3, transfers.Count);

        foreach (var t in transfers)
        {
            Assert.InRange(t.ToX, 0, StashRepack.SortingTableColumns - 1);
            Assert.True(t.ToY >= 0);
        }
    }

    [Fact]
    public void OverflowDoesNotLandOnWhatIsAlreadyThere()
    {
        // The table's first row is full.
        var existing = new List<StashRepack.Placement>();

        for (var x = 0; x < StashRepack.SortingTableColumns; x++) existing.Add(At($"old{x}", x, 0));

        var transfers = StashRepack.IntoSortingTable([At("new", 0, 0)], existing, out var tooWide);

        Assert.Empty(tooWide);
        var t = Assert.Single(transfers);
        Assert.True(t.ToY >= 1, "it must not sit on top of the occupied first row");
    }

    [Fact]
    public void OverflowGrowsDownwardAsFarAsNeeded()
    {
        // Far more than one screenful: the table stretches vertically without bound.
        var incoming = Enumerable.Range(0, 200).Select(i => At($"i{i}", 0, 0)).ToList();

        var transfers = StashRepack.IntoSortingTable(incoming, [], out var tooWide);

        Assert.Empty(tooWide);
        Assert.Equal(200, transfers.Count);

        var cells = transfers.Select(t => (t.ToX, t.ToY)).ToList();
        Assert.Equal(cells.Count, cells.Distinct().Count());
    }

    [Fact]
    public void SomethingWiderThanTheTableIsReportedNotDropped()
    {
        var transfers = StashRepack.IntoSortingTable(
            [At("huge", 0, 0, w: 9, h: 1)], [], out var tooWide);

        Assert.Empty(transfers);
        Assert.Contains("huge", tooWide);
    }

    [Fact]
    public void RotationIsRespectedInTheTable()
    {
        // 5x2 stood on end is 2 wide, 5 tall — fits a 7-wide table.
        var upright = At("rifle", 0, 0, w: 5, h: 2, r: StashOccupancy.Vertical);

        var transfers = StashRepack.IntoSortingTable([upright], [], out var tooWide);

        Assert.Empty(tooWide);
        var t = Assert.Single(transfers);
        Assert.True(t.ToX + 2 <= StashRepack.SortingTableColumns);
    }

    /// <summary>
    /// End to end through a real profile: a stash too full to narrow, with the excess
    /// re-parented into the Sorting Table and everything still accounted for.
    /// </summary>
    [Fact]
    public void AnOverfullStashOverflowsIntoTheTableOnDisk()
    {
        var items = new JsonArray
        {
            new JsonObject { ["_id"] = "stash-1", ["_tpl"] = EoD },
            new JsonObject { ["_id"] = "table-1", ["_tpl"] = "602543c13fee350cd564d032" },
        };

        // A 4x2 stash's worth of items, about to be squeezed into 2x2.
        var ids = new List<string>();

        for (var y = 0; y < 2; y++)
        {
            for (var x = 0; x < 4; x++)
            {
                var id = $"i{x}-{y}";
                ids.Add(id);

                items.Add(new JsonObject
                {
                    ["_id"] = id,
                    ["_tpl"] = "thing",
                    ["parentId"] = "stash-1",
                    ["slotId"] = "hideout",
                    ["location"] = new JsonObject { ["x"] = x, ["y"] = y, ["r"] = 0 },
                });
            }
        }

        var root = new JsonObject
        {
            ["characters"] = new JsonObject
            {
                ["pmc"] = new JsonObject
                {
                    ["Inventory"] = new JsonObject
                    {
                        ["stash"] = "stash-1",
                        ["sortingTable"] = "table-1",
                        ["items"] = items,
                    },
                },
            },
        };

        var path = Path.Combine(_dir, "full.json");
        File.WriteAllText(path, root.ToJsonString());

        var before = ProfileStore.Read(path, _ => (1, 1))!;

        Assert.Equal("table-1", before.SortingTableId);
        Assert.Equal(8, before.Items.Count);

        // Squeeze to 2x2: four fit, four must overflow.
        var plan = StashRepack.For(before.Items, 2, 2);

        Assert.False(plan.Complete);
        Assert.Equal(4, plan.Homeless.Count);

        var byId = before.Items.ToDictionary(i => i.ItemId);
        var homeless = plan.Homeless.Select(id => byId[id]).ToList();

        var transfers = StashRepack.IntoSortingTable(
            homeless, before.SortingTableItems, out var tooWide);

        Assert.Empty(tooWide);
        Assert.Equal(4, transfers.Count);

        ProfileStore.ApplyChanges(path, plan.Moves, before.SortingTableId, transfers);

        var after = ProfileStore.Read(path, _ => (1, 1))!;

        // Nothing vanished: still eight items across the two containers.
        Assert.Equal(8, after.Items.Count + after.SortingTableItems.Count);

        Assert.Equal(4, after.SortingTableItems.Count);
        Assert.All(after.Items, i => Assert.True(i.FitsIn(2, 2)));
        Assert.All(after.SortingTableItems,
            i => Assert.True(i.FitsIn(StashRepack.SortingTableColumns, 1000)));

        // The transferred ones really were re-parented, not just repositioned.
        var raw = JsonNode.Parse(File.ReadAllText(path))!;
        var arr = raw["characters"]!["pmc"]!["Inventory"]!["items"]!.AsArray();

        var reparented = arr.Count(n =>
            n!["parentId"]?.GetValue<string>() == "table-1"
            && n["slotId"]?.GetValue<string>() == "hideout");

        Assert.Equal(4, reparented);
    }
}
