using UltrawideStash.Server;
using Xunit;
using Shape = UltrawideStash.Server.StashShapes.Shape;

namespace UltrawideStash.Server.Tests;

/// <summary>
/// The width is per player from 1.1.3 (Forge issue #4: under Fika a 16:9 friend got
/// the host's 21:9 stash and a sideways scrollbar). These pin the two pure halves: what
/// width a player's own record gives, and the shapes planned from their own stash.
/// </summary>
public class PerPlayerTests
{
    private static readonly Dictionary<string, Shape> Vanilla = new()
    {
        ["566abbc34bdc2d92178b4576"] = new Shape(10, 30),
        ["5811ce572459770cba1a34ea"] = new Shape(10, 40),
        ["5811ce662459770f6f490f32"] = new Shape(10, 50),
        ["5811ce772459770e9e5f9532"] = new Shape(10, 68),
        ["6602bcf19cc643f44a04274b"] = new Shape(10, 72),
    };

    private const string Standard = "566abbc34bdc2d92178b4576";
    private const string EdgeOfDarkness = "5811ce772459770e9e5f9532";
    private const string Unheard = "6602bcf19cc643f44a04274b";

    private static PlayerRecord Record(string screen, Measurement? measurement = null)
        => new() { Screen = screen, ProbeVersion = "1.1.3", Measurement = measurement };

    private static Measurement Measured(int columns, string screen, string probe = "1.1.3")
        => new() { MaxColumns = columns, Screen = screen, ProbeVersion = probe, CanWiden = columns > 10 };

    private static ProfileStore.StashContents Stash(string template, params StashRepack.Placement[] items)
        => new("p.json", "stash", template, items.ToList(), "table", new List<StashRepack.Placement>());

    [Fact]
    public void AFriendOnSixteenByNineGetsTheVanillaStash()
    {
        var choice = PlayerRecord.ChoiceFor(Record("1920x1080"), new StashSettings(), out _);

        Assert.NotNull(choice);
        Assert.Equal(10, choice.Value.Columns);
    }

    [Fact]
    public void AnUltrawidePlayerGetsTheirOwnWidth()
    {
        var choice = PlayerRecord.ChoiceFor(Record("3440x1440"), new StashSettings(), out _);

        Assert.Equal(19, choice!.Value.Columns);
    }

    [Fact]
    public void TheirOwnMeasurementWinsOnTheScreenItWasTakenOn()
    {
        var choice = PlayerRecord.ChoiceFor(
            Record("3440x1440", Measured(18, "3440x1440")), new StashSettings(), out _);

        Assert.Equal(18, choice!.Value.Columns);
        Assert.Equal(ColumnChoice.Origin.Measured, choice.Value.Source);
    }

    [Fact]
    public void AMeasurementFromAnotherScreenIsIgnored()
    {
        // Measured on the ultrawide, now playing on a 16:9 screen.
        var choice = PlayerRecord.ChoiceFor(
            Record("1920x1080", Measured(19, "3440x1440")), new StashSettings(), out var reason);

        Assert.Equal(10, choice!.Value.Columns);
        Assert.Contains("ignored", reason);
    }

    [Fact]
    public void AMeasurementFromBeforeTheStretchIsIgnored()
    {
        var choice = PlayerRecord.ChoiceFor(
            Record("3440x1440", Measured(10, "3440x1440", "1.0.4")), new StashSettings(), out _);

        Assert.Equal(19, choice!.Value.Columns);
    }

    [Fact]
    public void NoReportedScreenKeepsTheServerWideWidth()
    {
        Assert.Null(PlayerRecord.ChoiceFor(null, new StashSettings(), out _));
        Assert.Null(PlayerRecord.ChoiceFor(new PlayerRecord(), new StashSettings(), out _));
        Assert.Null(PlayerRecord.ChoiceFor(Record("wide"), new StashSettings(), out _));
    }

    [Fact]
    public void AConfiguredNumberIsClampedToEachPlayersOwnScreen()
    {
        var settings = new StashSettings { Columns = 15 };

        Assert.Equal(10, PlayerRecord.ChoiceFor(Record("1920x1080"), settings, out _)!.Value.Columns);
        Assert.Equal(15, PlayerRecord.ChoiceFor(Record("3440x1440"), settings, out _)!.Value.Columns);
    }

    [Fact]
    public void ThePlayersOwnPanelSettingsCount()
    {
        var record = Record("3440x1440");
        record.WidenStashPanel = false;

        Assert.Equal(10, PlayerRecord.ChoiceFor(record, new StashSettings(), out _)!.Value.Columns);
    }

    [Fact]
    public void TenColumnsIsVanillaOnEveryRung()
    {
        var shapes = StashShapes.For(
            Stash(EdgeOfDarkness, new StashRepack.Placement("a", 12, 30, 2, 2, 0)), Vanilla, 10, true);

        Assert.Equal(Vanilla, shapes);
    }

    [Fact]
    public void TheirRungIsCompensatedAndTheRungsAboveAreNoShallower()
    {
        var shapes = StashShapes.For(
            Stash(Standard, new StashRepack.Placement("a", 0, 0, 1, 1, 0)), Vanilla, 19, true);

        // ceil(300 / 19) = 16; Left Behind ceil(400 / 19) = 22, and so on up.
        Assert.Equal(new Shape(19, 16), shapes[Standard]);
        Assert.Equal(new Shape(19, 22), shapes["5811ce572459770cba1a34ea"]);
        Assert.Equal(new Shape(19, 36), shapes[EdgeOfDarkness]);

        var rows = StashLadder.Rungs.Select(r => shapes[r.Id].Rows).ToList();

        Assert.Equal(rows.OrderBy(r => r), rows);
    }

    [Fact]
    public void ADeepStashThatCannotPackTighterHoldsItsDepthUpTheLadder()
    {
        // Ten one-wide, 40-tall pieces: no repack fits them under 40 rows.
        var items = Enumerable.Range(0, 10)
            .Select(i => new StashRepack.Placement($"i{i}", i, 0, 1, 40, 0))
            .ToArray();

        var shapes = StashShapes.For(Stash(Standard, items), Vanilla, 19, true);

        Assert.Equal(40, shapes[Standard].Rows);
        Assert.True(shapes["5811ce572459770cba1a34ea"].Rows >= 40);
        Assert.True(shapes[Unheard].Rows >= 40);
    }

    [Fact]
    public void ANewPlayerWithNoStashIsPlannedFromNothing()
    {
        var shapes = StashShapes.For(null, Vanilla, 19, true);

        Assert.Equal(new Shape(19, 16), shapes[Standard]);
        Assert.Equal(new Shape(19, 36), shapes[EdgeOfDarkness]);
    }

    [Fact]
    public void ARecordSurvivesARoundTrip()
    {
        var folder = Path.Combine(Path.GetTempPath(), "uws-records-" + Guid.NewGuid().ToString("N"));

        try
        {
            var record = Record("2560x1080", Measured(14, "2560x1080"));
            record.GearPanelReserve = 700f;

            PlayerRecord.Write(folder, "abc", record);

            var back = PlayerRecord.Read(folder, "abc");

            Assert.NotNull(back);
            Assert.Equal("2560x1080", back.Screen);
            Assert.Equal(700f, back.GearPanelReserve);
            Assert.Equal(14, back.Measurement!.MaxColumns);
            Assert.Null(PlayerRecord.Read(folder, "missing"));
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
        }
    }
}
