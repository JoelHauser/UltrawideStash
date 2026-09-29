using Xunit;

namespace UltrawideStash.Server.Tests;

/// <summary>
/// The probe's BepInEx settings, read by the server so its first-start prediction
/// matches what the probe will draw.
///
/// Prompted by a 5120x1440 tester on 1.0.5 whose <c>GearPanelReserve</c> was about 1000:
/// the server predicted 39 columns, the probe widened the panel to 27, and the stash
/// scrolled sideways until the reserve went back to 620.
/// </summary>
public class ClientSettingsTests
{
    /// <summary>The file as BepInEx writes it -- copied from the live install.</summary>
    private static readonly string[] LiveFile =
    [
        "## Settings file was created by plugin Ultrawide Stash Probe v1.0.2",
        "## Plugin GUID: com.mybutthasarash.ultrawidestash",
        "",
        "[Layout]",
        "",
        "## Narrow the gear side of the inventory screen and give the width to the stash panel.",
        "# Setting type: Boolean",
        "# Default value: true",
        "WidenStashPanel = true",
        "",
        "## Canvas px each gear panel keeps when the stash is widened.",
        "# Setting type: Single",
        "# Default value: 620",
        "GearPanelReserve = 620",
        "",
        "# Setting type: Boolean",
        "# Default value: true",
        "WidenTransferScreens = true",
    ];

    private static string[] With(string key, string value) =>
        LiveFile.Select(l => l.StartsWith(key + " =", StringComparison.Ordinal) ? $"{key} = {value}" : l).ToArray();

    [Fact]
    public void TheLiveFileReadsAsTheDefaults()
    {
        var read = ClientSettings.Parse(LiveFile, out var note);

        Assert.True(read.Found);
        Assert.True(read.WidenStashPanel);
        Assert.Equal(620f, read.GearPanelReserve);
        Assert.False(read.ReserveChanged);
        Assert.Contains("GearPanelReserve 620", note);
    }

    [Fact]
    public void ARaisedReserveIsRead()
    {
        var read = ClientSettings.Parse(With("GearPanelReserve", "1000"), out _);

        Assert.Equal(1000f, read.GearPanelReserve);
        Assert.True(read.ReserveChanged);
    }

    /// <summary>BepInEx writes invariant decimals whatever the machine's culture.</summary>
    [Fact]
    public void ADecimalReserveIsReadInvariantly()
    {
        Assert.Equal(700.5f, ClientSettings.Parse(With("GearPanelReserve", "700.5"), out _).GearPanelReserve);
    }

    [Fact]
    public void WidenStashPanelFalseIsRead()
    {
        Assert.False(ClientSettings.Parse(With("WidenStashPanel", "false"), out _).WidenStashPanel);
    }

    /// <summary>Anything unparseable keeps the default, as BepInEx itself would.</summary>
    [Theory]
    [InlineData("GearPanelReserve", "lots")]
    [InlineData("GearPanelReserve", "-5")]
    [InlineData("WidenStashPanel", "maybe")]
    public void AnUnreadableValueKeepsTheDefault(string key, string value)
    {
        var read = ClientSettings.Parse(With(key, value), out _);

        Assert.True(read.WidenStashPanel);
        Assert.Equal(620f, read.GearPanelReserve);
    }

    [Fact]
    public void AKeyOutsideLayoutIsIgnored()
    {
        var read = ClientSettings.Parse(["[Other]", "GearPanelReserve = 1000"], out _);

        Assert.Equal(620f, read.GearPanelReserve);
    }

    [Fact]
    public void AMissingFileIsTheDefaults()
    {
        var read = ClientSettings.Read(System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"uws-none-{Guid.NewGuid():N}.cfg"), out var note);

        Assert.False(read.Found);
        Assert.Equal(ClientSettings.Defaults, read);
        Assert.Contains("defaults", note);
    }

    /// <summary><c>&lt;SPT&gt;/SPT_Runtime/user/mods/UltrawideStash</c> up four is the SPT root.</summary>
    [Fact]
    public void TheConfigIsFoundFromTheModFolder()
    {
        var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "uws-root");
        var mod = System.IO.Path.Combine(root, "SPT_Runtime", "user", "mods", "UltrawideStash");

        Assert.Equal(
            System.IO.Path.Combine(root, "BepInEx", "config", ClientSettings.ConfigFileName),
            ClientSettings.PathFor(mod));
    }

    /// <summary>
    /// The tester's case: 1000 at 5120x1440 is 27 columns, not 39 -- the panel measured
    /// off their screenshot was 1754 canvas px, which is 27 columns plus chrome.
    /// </summary>
    [Fact]
    public void TheTestersReserveIsPredictedAt27()
    {
        Assert.Equal(27, StashFit.WidenedColumnsForScreen(5120, 1440, 1000f));
        Assert.Equal(39, StashFit.WidenedColumnsForScreen(5120, 1440));
    }

    /// <summary>The probe raises anything below 520 to 520, so the server does too.</summary>
    [Fact]
    public void AReserveBelowTheFloorIsRaised()
    {
        Assert.Equal(
            StashFit.WidenedColumns(2580, 520f),
            StashFit.WidenedColumns(2580, 100f));
    }

    [Fact]
    public void ThePredictionFollowsTheReserve()
    {
        var client = new ClientSettings(true, 1000f, true);

        var choice = ColumnChoice.For(null, null, 5120, 1440, false, client);

        Assert.Equal(27, choice.Columns);
        Assert.Equal(ColumnChoice.Origin.Estimated, choice.Source);
    }

    /// <summary>A probe that will not widen leaves no room: predict vanilla.</summary>
    [Fact]
    public void WidenStashPanelFalsePredictsVanilla()
    {
        var client = new ClientSettings(false, 620f, true);

        Assert.Equal(StashFit.VanillaColumns, ColumnChoice.For(null, null, 3440, 1440, false, client).Columns);
    }

    /// <summary>A measurement is what the probe actually drew, reserve and all, so it still wins.</summary>
    [Fact]
    public void AMeasurementStillWinsOverThePrediction()
    {
        var measured = new Measurement { MaxColumns = 27, ProbeVersion = "1.0.5" };
        var client = new ClientSettings(true, 620f, true);

        Assert.Equal(27, ColumnChoice.For(null, measured, 5120, 1440, false, client).Columns);
    }

    [Fact]
    public void TheWarningNamesTheCostAndTheFix()
    {
        var text = StashWidener.ClientSettingsWarning(new ClientSettings(true, 1000f, true), 5120, 1440);

        Assert.NotNull(text);
        Assert.Contains("GearPanelReserve is 1000", text);
        Assert.Contains("27 columns wide instead of 39", text);
        Assert.Contains("Set it back to 620", text);
    }

    [Fact]
    public void TheDefaultsNeedNoWarning()
    {
        Assert.Null(StashWidener.ClientSettingsWarning(ClientSettings.Defaults, 3440, 1440));
    }

    [Fact]
    public void WidenStashPanelFalseIsWarnedAbout()
    {
        Assert.Contains(
            "WidenStashPanel is false",
            StashWidener.ClientSettingsWarning(new ClientSettings(false, 620f, true), 3440, 1440));
    }

    /// <summary>On 16:9 the reserve changes nothing, and the warning says so rather than alarming.</summary>
    [Fact]
    public void AChangedReserveOnSixteenByNineSaysItMakesNoDifference()
    {
        Assert.Contains(
            "makes no difference",
            StashWidener.ClientSettingsWarning(new ClientSettings(true, 1000f, true), 2560, 1440));
    }
}
