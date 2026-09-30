using UltrawideStash.Probe;

namespace UltrawideStash.Server.Tests;

/// <summary>
/// The screen table -- which of the six screens that draw a SimpleStashPanel gets its
/// panel widened, and which single one of them writes the measurement the server sizes
/// the grid from.
///
/// ## Why this is worth testing when the rest of the probe is not
///
/// Everything else the probe does needs a live RectTransform hierarchy and cannot be
/// checked without an engine. This part is a string in and a policy out, and it carries
/// two decisions that fail silently if they are wrong:
///
/// 1. The character screen must still be widened. That is the one arrangement verified
///    in game; a typo here turns the mod off and it looks like the mod is broken rather
///    than misconfigured.
/// 2. Exactly one screen may write the measurement. Two would mean whichever screen was
///    opened last decided how wide the server made the grid -- so opening a trader could
///    narrow the stash on the character screen, which had the room for it.
///
/// All in one class on purpose: <see cref="StashScreens.WidenTraderScreen"/> is mutable
/// static state, and xunit runs methods within a class one at a time.
/// </summary>
public class StashScreensTests
{
    /// <summary>
    /// Every screen the table is meant to know, with the short type name reflection
    /// hands over for it.
    /// </summary>
    private static readonly string[] AllScreenTypes =
    [
        "ItemsPanel",
        "InventoryScreen",
        "TraderDealScreen",
        "TransferItemsScreen",
        "ScavengerInventoryScreen",
        "PrestigeTransferItemsState",
        "BaseHideoutAreaTransferItemsScreen`2",
    ];

    public StashScreensTests()
    {
        // The plugin sets this from config before the first stash opens; tests that
        // change it put it back, and this covers the ones that throw first.
        StashScreens.WidenTraderScreen = true;
    }

    [Fact]
    public void TheCharacterScreenIsWidenedAndOwnsTheMeasurement()
    {
        var policy = StashScreens.For("ItemsPanel");

        Assert.True(policy.Recognised);
        Assert.True(policy.Widen);
        Assert.True(policy.OwnsMeasurement);
    }

    [Fact]
    public void TheOuterInventoryScreenIsAlsoTheCharacterScreen()
    {
        // ItemsPanel is the type carrying the SimpleStashPanel field today. Registering
        // InventoryScreen as well means a build that moves the field outward still
        // widens rather than falling through to unrecognised.
        var policy = StashScreens.For("InventoryScreen");

        Assert.Equal(StashScreens.For("ItemsPanel").Label, policy.Label);
        Assert.True(policy.Widen);
        Assert.True(policy.OwnsMeasurement);
    }

    [Fact]
    public void TheTraderScreenIsWidened()
    {
        var policy = StashScreens.For("TraderDealScreen");

        Assert.True(policy.Recognised);
        Assert.True(policy.Widen);
    }

    [Fact]
    public void TheTraderScreenDoesNotWriteTheMeasurement()
    {
        // The whole safety argument for widening a second screen. The server sizes the
        // grid from one number and the grid is that wide on every screen; if the trader
        // screen could write it, a trader panel with room for fewer columns would narrow
        // the stash everywhere.
        Assert.False(StashScreens.For("TraderDealScreen").OwnsMeasurement);
    }

    [Fact]
    public void ExactlyOneScreenWritesTheMeasurement()
    {
        var owners = AllScreenTypes
            .Where(type => StashScreens.For(type).OwnsMeasurement)
            .Select(type => StashScreens.For(type).Label)
            .Distinct()
            .ToList();

        Assert.Single(owners);
        Assert.Equal(StashScreens.For("ItemsPanel").Label, owners[0]);
    }

    [Theory]
    [InlineData("TransferItemsScreen")]
    [InlineData("ScavengerInventoryScreen")]
    [InlineData("PrestigeTransferItemsState")]
    public void TheScreensNobodyHasLookedAtAreRecognisedAndLeftAlone(string type)
    {
        var policy = StashScreens.For(type);

        // Recognised and not widened are different from unrecognised, and the log says
        // so differently: one is a decision, the other is something to go and look at.
        Assert.True(policy.Recognised);
        Assert.False(policy.Widen);
        Assert.False(policy.OwnsMeasurement);
    }

    [Fact]
    public void TheHideoutTransferScreenIsMatchedThroughItsGenericSuffix()
    {
        // BaseHideoutAreaTransferItemsScreen<,> arrives from reflection as
        // BaseHideoutAreaTransferItemsScreen`2, and the component on the object is some
        // closed derived type, so an exact-name match would miss it entirely.
        var policy = StashScreens.For("BaseHideoutAreaTransferItemsScreen`2");

        Assert.True(policy.Recognised);
        Assert.False(policy.Widen);
    }

    [Theory]
    [InlineData("")]
    [InlineData("SomeOtherPanel")]
    [InlineData("GridView")]
    [InlineData("RectTransform")]
    public void AnythingElseIsLeftStrictlyAlone(string type)
    {
        var policy = StashScreens.For(type);

        Assert.False(policy.Recognised);
        Assert.False(policy.Widen);
        Assert.False(policy.OwnsMeasurement);
    }

    [Fact]
    public void AnUnrecognisedScreenStillHasALabelToPrint()
    {
        // The label goes into a log line and into the per-screen bookkeeping key, so an
        // empty one would produce a sentence with a hole in it and a key that collides.
        Assert.False(string.IsNullOrEmpty(StashScreens.For("Nonsense").Label));
        Assert.False(string.IsNullOrEmpty(StashScreens.Unrecognised().Label));
    }

    [Fact]
    public void TurningTheTraderScreenOffLeavesItRecognisedButUntouched()
    {
        try
        {
            StashScreens.WidenTraderScreen = false;

            var policy = StashScreens.For("TraderDealScreen");

            Assert.True(policy.Recognised);
            Assert.False(policy.Widen);
            Assert.False(policy.OwnsMeasurement);
        }
        finally
        {
            StashScreens.WidenTraderScreen = true;
        }
    }

    [Fact]
    public void TurningTheTraderScreenOffDoesNotTouchTheCharacterScreen()
    {
        // The two are separately switchable because one is verified in game and the
        // other is not. A switch that took both down would be a worse trade than the
        // scrollbar it was turning off.
        try
        {
            StashScreens.WidenTraderScreen = false;

            Assert.True(StashScreens.For("ItemsPanel").Widen);
            Assert.True(StashScreens.For("ItemsPanel").OwnsMeasurement);
        }
        finally
        {
            StashScreens.WidenTraderScreen = true;
        }
    }

    [Fact]
    public void TwoScreensAtOneResolutionAreDifferentKeys()
    {
        // The report, the remembered chrome and the settle counter are all keyed on
        // this. Sharing a key between screens means the character screen's reading is
        // used to size the trader screen's panel, and only one of the two is ever
        // reported.
        var character = StashScreens.For("ItemsPanel").KeyFor(3440, 1440);
        var trader = StashScreens.For("TraderDealScreen").KeyFor(3440, 1440);

        Assert.NotEqual(character, trader);
    }

    [Fact]
    public void OneScreenAtTwoResolutionsAreDifferentKeys()
    {
        var wide = StashScreens.For("ItemsPanel").KeyFor(3440, 1440);
        var narrow = StashScreens.For("ItemsPanel").KeyFor(1920, 1080);

        Assert.NotEqual(wide, narrow);
    }

    [Fact]
    public void EveryScreenThatIsWidenedIsAlsoRecognised()
    {
        // Widening something the table does not know is the one combination that has no
        // meaning: the decision to rearrange a screen comes from being on the list.
        foreach (var type in AllScreenTypes)
        {
            var policy = StashScreens.For(type);

            if (policy.Widen) Assert.True(policy.Recognised, type);
        }

        Assert.False(StashScreens.Unrecognised().Widen);
    }

    [Fact]
    public void NoScreenWritesTheMeasurementWithoutBeingWidened()
    {
        // A screen that wrote the measurement without being widened would report the
        // columns that fit in an un-widened panel -- 10 -- and the server would read
        // that back forever. This is the 0.9.3 failure in a new place.
        foreach (var type in AllScreenTypes)
        {
            var policy = StashScreens.For(type);

            if (policy.OwnsMeasurement) Assert.True(policy.Widen, type);
        }
    }
}
