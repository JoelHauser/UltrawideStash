using System.Text.Json;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using UltrawideStash.Server;

namespace UltrawideStash.Server.Tests;

/// <summary>
/// Moving items in the server's loaded copy of a profile.
///
/// Up to 1.0.0 the mod edited only the file, believing it ran before SPT loaded any
/// profile. It ran after, so SPT kept serving -- and then saving -- the unmoved copy,
/// and the same items were "moved" on every start. These pin the in-memory half that
/// replaced it.
/// </summary>
public class LoadedProfileTests
{
    private const string Stash = "6a0000000000000000000001";
    private const string Table = "6a0000000000000000000002";
    private const string Tank = "6a0000000000000000000003";
    private const string Mag = "6a0000000000000000000004";

    private static Item At(string id, object? location, string parent = Stash) => new()
    {
        Id = new MongoId(id),
        Template = new MongoId("5d1b36a186f7742523398433"),
        ParentId = parent,
        SlotId = "hideout",
        Location = location,
    };

    [Fact]
    public void AMoveLandsInAnItemLocation()
    {
        var tank = At(Tank, new ItemLocation { X = 3, Y = 36, R = ItemRotation.Vertical });

        var written = ProfileStore.ApplyToItems([tank], [new StashRepack.Move(Tank, 3, 36, 11, 2)]);

        Assert.Equal(1, written);

        var loc = Assert.IsType<ItemLocation>(tank.Location);
        Assert.Equal(11, loc.X);
        Assert.Equal(2, loc.Y);
        Assert.Equal(ItemRotation.Vertical, loc.R);
    }

    /// <summary>
    /// A freshly loaded profile holds each location as a raw JsonElement. Editing a
    /// parsed copy of that and not assigning it back would change nothing -- the exact
    /// shape of "the log says moved and the stash says otherwise".
    /// </summary>
    [Fact]
    public void AMoveLandsInALocationStillHeldAsJson()
    {
        var raw = JsonDocument.Parse("{\"x\":3,\"y\":36,\"r\":\"Vertical\",\"isSearched\":true}").RootElement;
        var tank = At(Tank, raw);

        var written = ProfileStore.ApplyToItems([tank], [new StashRepack.Move(Tank, 3, 36, 11, 2)]);

        Assert.Equal(1, written);

        var loc = Assert.IsType<ItemLocation>(tank.Location);
        Assert.Equal(11, loc.X);
        Assert.Equal(2, loc.Y);
    }

    [Fact]
    public void ATransferIsReparentedIntoTheSortingTable()
    {
        var tank = At(Tank, new ItemLocation { X = 3, Y = 36 });

        var written = ProfileStore.ApplyToItems(
            [tank], [], Table, [new StashRepack.Transfer(Tank, 0, 4)]);

        Assert.Equal(1, written);
        Assert.Equal(Table, tank.ParentId);
        Assert.Equal("hideout", tank.SlotId);

        var loc = Assert.IsType<ItemLocation>(tank.Location);
        Assert.Equal(0, loc.X);
        Assert.Equal(4, loc.Y);
    }

    /// <summary>A magazine's location is a bare number and never a grid placement.</summary>
    [Fact]
    public void ANumericLocationIsLeftAlone()
    {
        var mag = At(Mag, 3);

        var written = ProfileStore.ApplyToItems([mag], [new StashRepack.Move(Mag, 0, 0, 5, 5)]);

        Assert.Equal(0, written);
        Assert.Equal(3, mag.Location);
    }

    [Fact]
    public void ItemsNobodyNamedAreUntouchedAndNullsAreSkipped()
    {
        var tank = At(Tank, new ItemLocation { X = 3, Y = 36 });
        var other = At(Mag, new ItemLocation { X = 1, Y = 1 });

        var written = ProfileStore.ApplyToItems(
            [null, other, tank], [new StashRepack.Move(Tank, 3, 36, 0, 0)]);

        Assert.Equal(1, written);

        var untouched = Assert.IsType<ItemLocation>(other.Location);
        Assert.Equal(1, untouched.X);
        Assert.Equal(1, untouched.Y);
    }

    /// <summary>
    /// The loaded copy is read by serialising it and parsing the text, so Parse has to
    /// read exactly what Read reads from the file.
    /// </summary>
    [Fact]
    public void ParsingTextReadsWhatReadingTheFileReads()
    {
        var dir = Directory.CreateTempSubdirectory("uws-parse-").FullName;

        try
        {
            var path = System.IO.Path.Combine(dir, "p.json");
            File.WriteAllText(path,
                "{\"characters\":{\"pmc\":{\"Inventory\":{\"stash\":\"s\",\"items\":["
                + "{\"_id\":\"s\",\"_tpl\":\"5811ce772459770e9e5f9532\"},"
                + "{\"_id\":\"a\",\"_tpl\":\"t\",\"parentId\":\"s\",\"slotId\":\"hideout\",\"location\":{\"x\":2,\"y\":38,\"r\":0}}"
                + "]}}}}");

            var fromFile = ProfileStore.Read(path, _ => (2, 2))!;
            var fromText = ProfileStore.Parse(File.ReadAllText(path), path, _ => (2, 2))!;

            Assert.Equal(fromFile.StashTemplateId, fromText.StashTemplateId);
            Assert.Equal(fromFile.Items, fromText.Items);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
        }
    }
}
