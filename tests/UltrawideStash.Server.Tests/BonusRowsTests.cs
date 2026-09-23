using System.Text.Json.Nodes;
using UltrawideStash.Server;

namespace UltrawideStash.Server.Tests;

/// <summary>
/// A profile's <c>StashRows</c> bonus: rows the player has on top of the template.
///
/// Up to 1.0.1 the repack ignored it. On the live install, SCOOP's Edge of Darkness
/// stash is 19x36 by template plus a +2 bonus, and a 5x7 Pilgrim the player had placed
/// at row 31 was "moved back inside" on every start -- pinned, locked or not.
/// </summary>
public class BonusRowsTests
{
    private const string EoD = "5811ce772459770e9e5f9532";
    private const string Pilgrim = "59e763f286f7742ee57895da";

    private static (int, int) Sizes(string tpl) => tpl == Pilgrim ? (5, 7) : (1, 1);

    /// <summary>A profile with one Pilgrim at (0, 31) and the given bonuses.</summary>
    private static string Profile(JsonArray? bonuses)
    {
        var pmc = new JsonObject
        {
            ["Inventory"] = new JsonObject
            {
                ["stash"] = "stash-1",
                ["items"] = new JsonArray
                {
                    new JsonObject { ["_id"] = "stash-1", ["_tpl"] = EoD },
                    new JsonObject
                    {
                        ["_id"] = "pilgrim",
                        ["_tpl"] = Pilgrim,
                        ["parentId"] = "stash-1",
                        ["slotId"] = "hideout",
                        ["location"] = new JsonObject { ["x"] = 0, ["y"] = 31, ["r"] = "Horizontal" },
                        ["upd"] = new JsonObject { ["PinLockState"] = 1 },
                    },
                },
            },
        };

        if (bonuses is not null) pmc["Bonuses"] = bonuses;

        return new JsonObject
        {
            ["characters"] = new JsonObject { ["pmc"] = pmc },
        }.ToJsonString();
    }

    private static JsonObject Bonus(string type, double value) => new()
    {
        ["id"] = Guid.NewGuid().ToString("N"),
        ["type"] = type,
        ["value"] = value,
    };

    private static JsonObject StashSize(string template) => new()
    {
        ["id"] = Guid.NewGuid().ToString("N"),
        ["type"] = "StashSize",
        ["templateId"] = template,
    };

    [Fact]
    public void TheReportedCaseIsLeftWhereThePlayerPutIt()
    {
        var contents = ProfileStore.Parse(
            Profile([StashSize(EoD), Bonus("StashRows", 2)]), "p.json", Sizes)!;

        Assert.Equal(2, contents.BonusRows);

        var plan = StashRepack.For(contents.Items, 19, contents.RowsFor(36));

        Assert.True(plan.Complete);
        Assert.Empty(plan.Moves);
    }

    /// <summary>The same stash planned the 1.0.1 way moves it, which was the bug.</summary>
    [Fact]
    public void WithoutTheBonusItWouldBeMoved()
    {
        var contents = ProfileStore.Parse(
            Profile([Bonus("StashRows", 2)]), "p.json", Sizes)!;

        var plan = StashRepack.For(contents.Items, 19, 36);

        Assert.Single(plan.Moves);
    }

    [Fact]
    public void NoBonusesMeansNoExtraRows()
    {
        Assert.Equal(0, ProfileStore.Parse(Profile(null), "p.json", Sizes)!.BonusRows);
        Assert.Equal(0, ProfileStore.Parse(Profile([StashSize(EoD)]), "p.json", Sizes)!.BonusRows);
    }

    /// <summary>
    /// SPT's <c>GetPlayerStashSize</c> takes the first <c>StashRows</c> bonus only, so
    /// this does too: the repack's grid has to be the server's grid.
    /// </summary>
    [Fact]
    public void OnlyTheFirstStashRowsBonusCounts_AsInSpt()
    {
        var contents = ProfileStore.Parse(
            Profile([Bonus("StashRows", 2), Bonus("StashRows", 5)]), "p.json", Sizes)!;

        Assert.Equal(2, contents.BonusRows);
    }

    [Fact]
    public void AFractionalValueIsTruncated_AsInSpt()
    {
        var contents = ProfileStore.Parse(
            Profile([Bonus("StashRows", 2.9)]), "p.json", Sizes)!;

        Assert.Equal(2, contents.BonusRows);
    }

    /// <summary>
    /// The bonus is one player's, so it must not deepen the template for everyone else
    /// on it: the Pilgrim reaches row 38, of which 36 are the template's.
    /// </summary>
    [Fact]
    public void TheTemplateDepthExcludesTheBonus()
    {
        var withBonus = ProfileStore.Parse(
            Profile([Bonus("StashRows", 2)]), "p.json", Sizes)!;
        var without = ProfileStore.Parse(Profile(null), "p.json", Sizes)!;

        Assert.Equal(36, withBonus.TemplateRowsNeeded());
        Assert.Equal(38, without.TemplateRowsNeeded());
    }
}
