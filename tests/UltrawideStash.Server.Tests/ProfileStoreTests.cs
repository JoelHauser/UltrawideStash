using System.Text.Json.Nodes;
using UltrawideStash.Server;

namespace UltrawideStash.Server.Tests;

/// <summary>
/// Reading a profile and writing relocations back to it.
///
/// These run against real files on disk, because the point of the exercise is the file:
/// this mod edits a player's profile, and the round trip has to preserve everything it
/// did not mean to touch.
/// </summary>
public class ProfileStoreTests : IDisposable
{
    private const string EoD = "5811ce772459770e9e5f9532";

    private readonly string _dir = Directory.CreateTempSubdirectory("uws-store-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
        GC.SuppressFinalize(this);
    }

    private static (int, int) OneByOne(string _) => (1, 1);

    /// <summary>
    /// A profile with a stash and the given placements, plus a scattering of unrelated
    /// content that the writer must leave exactly as it found it.
    /// </summary>
    private string Write(string name, params (string Id, int X, int Y)[] items)
    {
        var array = new JsonArray
        {
            new JsonObject { ["_id"] = "stash-1", ["_tpl"] = EoD },
        };

        foreach (var (id, x, y) in items)
        {
            array.Add(new JsonObject
            {
                ["_id"] = id,
                ["_tpl"] = "thing",
                ["parentId"] = "stash-1",
                ["slotId"] = "hideout",
                ["location"] = new JsonObject { ["x"] = x, ["y"] = y, ["r"] = 0 },
            });
        }

        // A magazine: location is a bare number, not an object.
        array.Add(new JsonObject
        {
            ["_id"] = "mag",
            ["_tpl"] = "ammo",
            ["parentId"] = "stash-1",
            ["slotId"] = "cartridges",
            ["location"] = 3,
        });

        var root = new JsonObject
        {
            ["info"] = new JsonObject { ["username"] = "TEST", ["edition"] = "Edge Of Darkness" },
            ["characters"] = new JsonObject
            {
                ["pmc"] = new JsonObject
                {
                    ["Inventory"] = new JsonObject
                    {
                        ["stash"] = "stash-1",
                        ["items"] = array,
                    },
                },
            },
            ["suits"] = new JsonArray { "a", "b" },
        };

        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, root.ToJsonString());
        return path;
    }

    [Fact]
    public void ReadsTheStashAndItsTemplate()
    {
        var path = Write("a.json", ("i1", 0, 0), ("i2", 14, 3));

        var stash = ProfileStore.Read(path, OneByOne);

        Assert.NotNull(stash);
        Assert.Equal(EoD, stash!.StashTemplateId);
        Assert.Equal("stash-1", stash.StashItemId);

        // The magazine's numeric location is skipped, not read as a placement.
        Assert.Equal(2, stash.Items.Count);
    }

    [Fact]
    public void AProfileWithNoStashIsNullNotAnError()
    {
        var path = Path.Combine(_dir, "stub.json");
        File.WriteAllText(path, "{\"info\":{\"username\":\"TEST\"}}");

        Assert.Null(ProfileStore.Read(path, OneByOne));
    }

    [Fact]
    public void MovesAreWrittenBack()
    {
        var path = Write("a.json", ("i1", 0, 0), ("i2", 14, 3));

        var written = ProfileStore.ApplyChanges(path, [new StashRepack.Move("i2", 14, 3, 1, 0)]);

        Assert.Equal(1, written);

        var after = ProfileStore.Read(path, OneByOne)!;
        var moved = after.Items.Single(i => i.ItemId == "i2");

        Assert.Equal(1, moved.X);
        Assert.Equal(0, moved.Y);

        // The one that did not move is untouched.
        var stayed = after.Items.Single(i => i.ItemId == "i1");
        Assert.Equal(0, stayed.X);
        Assert.Equal(0, stayed.Y);
    }

    [Fact]
    public void EverythingElseInTheProfileSurvives()
    {
        var path = Write("a.json", ("i1", 14, 3));

        ProfileStore.ApplyChanges(path, [new StashRepack.Move("i1", 14, 3, 0, 0)]);

        var root = JsonNode.Parse(File.ReadAllText(path))!;

        Assert.Equal("TEST", root["info"]!["username"]!.GetValue<string>());
        Assert.Equal("Edge Of Darkness", root["info"]!["edition"]!.GetValue<string>());
        Assert.Equal(2, root["suits"]!.AsArray().Count);

        // The magazine's numeric location is still a number, not mangled into an object.
        var mag = root["characters"]!["pmc"]!["Inventory"]!["items"]!.AsArray()
            .First(n => n!["_id"]!.GetValue<string>() == "mag")!;

        Assert.Equal(3, mag["location"]!.GetValue<int>());
    }

    /// <summary>
    /// A backup is written before the profile is replaced. This is the difference
    /// between an edit and a gamble.
    /// </summary>
    [Fact]
    public void ABackupIsTakenBeforeWriting()
    {
        var path = Write("a.json", ("i1", 14, 3));
        var original = File.ReadAllText(path);

        ProfileStore.ApplyChanges(path, [new StashRepack.Move("i1", 14, 3, 0, 0)]);

        var backups = Directory.GetFiles(_dir, "a.json.ultrawidestash-*.bak");

        var backup = Assert.Single(backups);
        Assert.Equal(original, File.ReadAllText(backup));
    }

    [Fact]
    public void NoMovesMeansNoWriteAndNoBackup()
    {
        var path = Write("a.json", ("i1", 0, 0));
        var before = File.GetLastWriteTimeUtc(path);

        Assert.Equal(0, ProfileStore.ApplyChanges(path, []));

        Assert.Empty(Directory.GetFiles(_dir, "*.bak"));
        Assert.Equal(before, File.GetLastWriteTimeUtc(path));
    }

    [Fact]
    public void NoTemporaryFileIsLeftBehind()
    {
        var path = Write("a.json", ("i1", 14, 3));

        ProfileStore.ApplyChanges(path, [new StashRepack.Move("i1", 14, 3, 0, 0)]);

        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
    }

    /// <summary>
    /// The whole uninstall path, through real files: a stash laid out 16 wide, narrowed
    /// to vanilla, ends with every item inside a 10-wide grid.
    /// </summary>
    [Fact]
    public void TheUninstallPathEndsWithEverythingReachable()
    {
        var placements = new List<(string, int, int)>();
        var n = 0;

        for (var y = 0; y < 6; y++)
        {
            for (var x = 0; x < 16; x++) placements.Add(($"i{n++}", x, y));
        }

        var path = Write("a.json", placements.ToArray());

        var before = ProfileStore.Read(path, OneByOne)!;
        Assert.Contains(before.Items, i => i.X >= 10);

        var plan = StashRepack.For(before.Items, 10, 68);
        Assert.True(plan.Complete);

        ProfileStore.ApplyChanges(path, plan.Moves);

        var after = ProfileStore.Read(path, OneByOne)!;

        Assert.Equal(before.Items.Count, after.Items.Count);
        Assert.All(after.Items, i => Assert.True(i.FitsIn(10, 68), $"{i.ItemId} at {i.X},{i.Y}"));

        // And no two items share a cell.
        var cells = after.Items.Select(i => (i.X, i.Y)).ToList();
        Assert.Equal(cells.Count, cells.Distinct().Count());
    }
}
