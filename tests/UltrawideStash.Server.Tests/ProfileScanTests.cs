using System.Text.Json;
using UltrawideStash.Server;

namespace UltrawideStash.Server.Tests;

/// <summary>
/// The scan that replaced 0.2.0's broken occupancy guard, and the fail-safe around it.
///
/// The bug these exist to prevent: the old guard asked <c>SaveServer.GetProfiles()</c>
/// at <c>PostLoad</c>, which runs before <c>SaveCallbacks</c> loads them, so it always
/// saw nothing stored and always allowed rows to be cut. These tests pin both that the
/// scan reads a real profile correctly and that anything it cannot read stops the
/// shortening entirely.
/// </summary>
public class ProfileScanTests : IDisposable
{
    private const string EoD = "5811ce772459770e9e5f9532";

    private readonly string _dir = Directory.CreateTempSubdirectory("uws-scan-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
        GC.SuppressFinalize(this);
    }

    /// <summary>Everything is 1x1 unless a test says otherwise.</summary>
    private static (int, int) OneByOne(string _) => (1, 1);

    private string WriteProfile(string name, string json)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, json);
        return path;
    }

    /// <summary>A profile whose stash holds the given (y, tpl, r) placements.</summary>
    private static string ProfileJson(params (int Y, string Tpl, object R)[] items)
    {
        var entries = new List<string>
        {
            Obj("_id", "stash-1") + "," + Obj("_tpl", EoD)
        };

        var n = 0;

        foreach (var (y, tpl, r) in items)
        {
            var rJson = r is string s ? Quote(s) : r.ToString();

            entries.Add(string.Concat(
                Obj("_id", "i" + n++), ",",
                Obj("_tpl", tpl), ",",
                Obj("parentId", "stash-1"), ",",
                Obj("slotId", "hideout"), ",",
                Quote("location"), ":{",
                Quote("x"), ":0,", Quote("y"), ":", y.ToString(), ",", Quote("r"), ":", rJson,
                "}"));
        }

        var body = string.Join("},{", entries);

        return "{" + Quote("characters") + ":{" + Quote("pmc") + ":{" + Quote("Inventory") + ":{"
            + Quote("stash") + ":" + Quote("stash-1") + ","
            + Quote("items") + ":[{" + body + "}]}}}}";
    }

    private static string Quote(string v) => "\"" + v + "\"";

    private static string Obj(string k, string v) => Quote(k) + ":" + Quote(v);

    [Fact]
    public void AnEmptyStashNeedsNoRows()
    {
        WriteProfile("a.json", ProfileJson());

        var r = ProfileScan.Run(_dir, OneByOne);

        Assert.True(r.Confident);
        Assert.Equal(0, r.DeepestFor(EoD));
    }

    [Fact]
    public void TheDeepestItemSetsTheFloor()
    {
        WriteProfile("a.json", ProfileJson((0, "x", 0), (61, "x", 0), (12, "x", 0)));

        var r = ProfileScan.Run(_dir, OneByOne);

        Assert.True(r.Confident);
        Assert.Equal(62, r.DeepestFor(EoD));
    }

    [Fact]
    public void ItemHeightCounts()
    {
        // A 1x4 case at row 60 reaches row 63, so 64 rows are needed.
        WriteProfile("a.json", ProfileJson((60, "tall", 0)));

        var r = ProfileScan.Run(_dir, t => t == "tall" ? (1, 4) : (1, 1));

        Assert.Equal(64, r.DeepestFor(EoD));
    }

    [Theory]
    [InlineData(1)]         // numeric Vertical
    [InlineData("Vertical")]
    public void RotationSwapsTheFootprint(object rotation)
    {
        // A 5x2 rifle stood on end at row 60 occupies five rows, reaching row 64.
        WriteProfile("a.json", ProfileJson((60, "rifle", rotation)));

        var r = ProfileScan.Run(_dir, t => t == "rifle" ? (5, 2) : (1, 1));

        Assert.Equal(65, r.DeepestFor(EoD));
    }

    [Theory]
    [InlineData(0)]
    [InlineData("Horizontal")]
    public void UnrotatedKeepsItsOwnHeight(object rotation)
    {
        WriteProfile("a.json", ProfileJson((60, "rifle", rotation)));

        var r = ProfileScan.Run(_dir, t => t == "rifle" ? (5, 2) : (1, 1));

        Assert.Equal(62, r.DeepestFor(EoD));
    }

    [Fact]
    public void TheDeepestAcrossEveryProfileWins()
    {
        WriteProfile("a.json", ProfileJson((10, "x", 0)));
        WriteProfile("b.json", ProfileJson((55, "x", 0)));

        var r = ProfileScan.Run(_dir, OneByOne);

        Assert.True(r.Confident);
        Assert.Equal(56, r.DeepestFor(EoD));
    }

    /// <summary>
    /// A magazine stores <c>location</c> as a bare number rather than an object. It must
    /// be skipped rather than throwing the whole scan into "unsure".
    /// </summary>
    [Fact]
    public void ANumericLocationIsSkippedNotFatal()
    {
        var json = "{" + Quote("characters") + ":{" + Quote("pmc") + ":{" + Quote("Inventory") + ":{"
            + Quote("stash") + ":" + Quote("stash-1") + "," + Quote("items") + ":["
            + "{" + Obj("_id", "stash-1") + "," + Obj("_tpl", EoD) + "},"
            + "{" + Obj("_id", "m1") + "," + Obj("_tpl", "ammo") + "," + Obj("parentId", "stash-1")
                  + "," + Obj("slotId", "cartridges") + "," + Quote("location") + ":3},"
            + "{" + Obj("_id", "i1") + "," + Obj("_tpl", "x") + "," + Obj("parentId", "stash-1")
                  + "," + Obj("slotId", "hideout") + "," + Quote("location") + ":{"
                  + Quote("x") + ":0," + Quote("y") + ":20," + Quote("r") + ":0}}"
            + "]}}}}";

        WriteProfile("a.json", json);

        var r = ProfileScan.Run(_dir, OneByOne);

        Assert.True(r.Confident);
        Assert.Equal(21, r.DeepestFor(EoD));
    }

    /// <summary>
    /// Only the stash's own direct children count. An item deep inside a backpack has a
    /// y that belongs to the backpack's grid, and reading it as a stash row would report
    /// a depth that does not exist.
    /// </summary>
    [Fact]
    public void ItemsInOtherContainersAreIgnored()
    {
        var json = "{" + Quote("characters") + ":{" + Quote("pmc") + ":{" + Quote("Inventory") + ":{"
            + Quote("stash") + ":" + Quote("stash-1") + "," + Quote("items") + ":["
            + "{" + Obj("_id", "stash-1") + "," + Obj("_tpl", EoD) + "},"
            + "{" + Obj("_id", "bag") + "," + Obj("_tpl", "backpack") + "," + Obj("parentId", "stash-1")
                  + "," + Obj("slotId", "hideout") + "," + Quote("location") + ":{"
                  + Quote("x") + ":0," + Quote("y") + ":2," + Quote("r") + ":0}},"
            + "{" + Obj("_id", "deep") + "," + Obj("_tpl", "x") + "," + Obj("parentId", "bag")
                  + "," + Obj("slotId", "main") + "," + Quote("location") + ":{"
                  + Quote("x") + ":0," + Quote("y") + ":90," + Quote("r") + ":0}}"
            + "]}}}}";

        WriteProfile("a.json", json);

        var r = ProfileScan.Run(_dir, OneByOne);

        // The item inside the backpack is at y=90 in the BACKPACK, not the stash.
        Assert.Equal(3, r.DeepestFor(EoD));
    }

    // ---- the fail-safe ------------------------------------------------------

    [Fact]
    public void AMissingFolderIsNotConfident()
    {
        var r = ProfileScan.Run(Path.Combine(_dir, "nope"), OneByOne);

        Assert.False(r.Confident);
    }

    [Fact]
    public void AnUnparseableProfilePoisonsTheWholeAnswer()
    {
        WriteProfile("good.json", ProfileJson((5, "x", 0)));
        WriteProfile("bad.json", "{ this is not json");

        var r = ProfileScan.Run(_dir, OneByOne);

        // Not "we read the ones we could" -- the unreadable one could be the full stash.
        Assert.False(r.Confident);
    }

    [Fact]
    public void NoProfilesAtAllIsStillConfident()
    {
        var r = ProfileScan.Run(_dir, OneByOne);

        Assert.True(r.Confident);
        Assert.Equal(0, r.DeepestFor(EoD));
    }

    /// <summary>
    /// The whole point, end to end: an unsure scan must leave rows at vanilla, which is
    /// what <see cref="StashWidener"/> does by passing the vanilla row count as the
    /// occupancy floor.
    /// </summary>
    [Fact]
    public void AnUnsureScanLeavesRowsAtVanilla()
    {
        var unsure = ProfileScan.Unsure("pretend the folder was locked");

        Assert.False(unsure.Confident);

        var floor = unsure.Confident ? unsure.DeepestFor(EoD) : 68;

        var plan = StashLayout.For(10, 68, 16, compensateRows: true, deepestOccupiedRow: floor);

        Assert.True(plan.Applied);
        Assert.Equal(68, plan.Rows);
        Assert.Equal(16, plan.Columns);
        Assert.True(plan.Capacity >= 10 * 68);
    }

    /// <summary>
    /// And the bug as it actually was: a scan that wrongly reports nothing stored lets
    /// rows be cut to 43 on a stash that has items at row 60. This asserts the shape of
    /// the failure so it is recognisable if it ever returns.
    /// </summary>
    [Fact]
    public void AWronglyEmptyScanWouldHaveCutRows()
    {
        var wronglyEmpty = 0;

        var plan = StashLayout.For(10, 68, 16, compensateRows: true, deepestOccupiedRow: wronglyEmpty);

        Assert.Equal(43, plan.Rows);
        Assert.True(43 < 61, "this is what stranded an item stored at row 60");
    }
}
