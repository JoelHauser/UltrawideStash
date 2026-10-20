using System.Text.Json.Nodes;
using UltrawideStash.Server;

namespace UltrawideStash.Server.Tests;

/// <summary>
/// The repack must size an item the way SPT does -- a weapon with its attachments on --
/// or it parks a modded gun half outside the grid. Found in the 1.1.3 live test: a modded
/// MP5 (6 wide with its parts) was moved to x 5 of a 10-wide stash, and SPT logged it
/// [OOB] on every stash write.
/// </summary>
public class WeaponSizeTests
{
    private const string EoD = "5811ce772459770e9e5f9532";
    private const string Mp5 = "5926bb2186f7744b1c6c6e60";

    /// <summary>A stash with four 1x1s at x 0-3, row 0, and the MP5 out past the edge at x 12.</summary>
    private static string Profile()
    {
        var items = new JsonArray { new JsonObject { ["_id"] = "stash-1", ["_tpl"] = EoD } };

        for (var x = 0; x < 4; x++)
        {
            items.Add(new JsonObject
            {
                ["_id"] = $"small{x}",
                ["_tpl"] = "small",
                ["parentId"] = "stash-1",
                ["slotId"] = "hideout",
                ["location"] = new JsonObject { ["x"] = x, ["y"] = 0, ["r"] = "Horizontal" },
            });
        }

        items.Add(new JsonObject
        {
            ["_id"] = "mp5",
            ["_tpl"] = Mp5,
            ["parentId"] = "stash-1",
            ["slotId"] = "hideout",
            ["location"] = new JsonObject { ["x"] = 12, ["y"] = 0, ["r"] = "Horizontal" },
        });

        return new JsonObject
        {
            ["characters"] = new JsonObject
            {
                ["pmc"] = new JsonObject
                {
                    ["Inventory"] = new JsonObject { ["stash"] = "stash-1", ["items"] = items },
                },
            },
        }.ToJsonString();
    }

    [Fact]
    public void TheSizeIsAskedForByItemSoAttachmentsCount()
    {
        var asked = new List<string>();

        var contents = ProfileStore.Parse(Profile(), "p.json", (id, tpl) =>
        {
            asked.Add(id);
            return id == "mp5" ? (6, 2) : (1, 1);
        })!;

        Assert.Contains("mp5", asked);
        Assert.Equal(6, contents.Items.Single(i => i.ItemId == "mp5").Width);
    }

    [Fact]
    public void AModdedWeaponIsRepackedWhereItsWholeLengthFits()
    {
        var contents = ProfileStore.Parse(Profile(), "p.json", (id, _) => id == "mp5" ? (6, 2) : (1, 1))!;

        var plan = StashRepack.For(contents.Items, 10, 68);
        var move = plan.Moves.Single(m => m.ItemId == "mp5");

        Assert.True(move.ToX + 6 <= 10, $"moved to x {move.ToX}, which runs past column 10");
    }

    [Fact]
    public void TheTemplateOnlyOverloadStillWorks()
    {
        var contents = ProfileStore.Parse(Profile(), "p.json", tpl => tpl == Mp5 ? (3, 2) : (1, 1))!;

        Assert.Equal(3, contents.Items.Single(i => i.ItemId == "mp5").Width);
    }
}
