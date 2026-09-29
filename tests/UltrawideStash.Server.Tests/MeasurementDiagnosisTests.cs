using Xunit;

namespace UltrawideStash.Server.Tests;

/// <summary>
/// The server log's explanation when the game could not widen its stash panel.
///
/// Prompted by Forge issue #1: a 5120x1440 player whose stash stayed 10 columns wide,
/// with no logs. The 10 was the client's honest measurement of an unwidened panel,
/// and the reason was only ever in the BepInEx log.
/// </summary>
public class MeasurementDiagnosisTests
{
    private static Measurement Measured(bool? canWiden, int canvas, string? whyNot = null) => new()
    {
        MaxColumns = 10,
        Screen = canvas == 1920 ? "2560x1440" : "3440x1440",
        CanvasWidth = canvas,
        CanWiden = canWiden,
        WhyNot = whyNot,
    };

    [Fact]
    public void AScreenThatWidenedHasNothingToExplain()
    {
        Assert.Null(Measured(true, 2580).Diagnosis());
    }

    /// <summary>Files from older probes do not say, and must not be read as "could not".</summary>
    [Fact]
    public void AnOlderFileHasNothingToExplain()
    {
        Assert.Null(Measured(null, 1920).Diagnosis());
    }

    [Fact]
    public void ASixteenByNineLayoutPointsAtTheGameResolution()
    {
        var text = Measured(false, 1920, "the gear side is 1206 px wide.").Diagnosis();

        Assert.NotNull(text);
        Assert.Contains("2560x1440", text);
        Assert.Contains("1920 px", text);
        Assert.Contains("the gear side is 1206 px wide", text);
        Assert.DoesNotContain("wide..", text);
        Assert.Contains("10 columns", text);
        Assert.Contains("16:9", text);
        Assert.Contains("Settings > Graphics", text);
    }

    /// <summary>
    /// A wide canvas that still could not widen is a different problem -- a changed
    /// layout, most likely -- and the resolution advice would send the player the
    /// wrong way.
    /// </summary>
    [Fact]
    public void AWideLayoutGetsTheReasonButNotTheResolutionAdvice()
    {
        var text = Measured(false, 2580, "the inventory screen has no 'LeftSide'").Diagnosis();

        Assert.NotNull(text);
        Assert.Contains("no 'LeftSide'", text);
        Assert.DoesNotContain("16:9", text);
    }

    [Fact]
    public void AMissingReasonIsSaidPlainly()
    {
        Assert.Contains("did not record why", Measured(false, 2580).Diagnosis());
    }

    /// <summary>
    /// The file as the probe writes it -- field order, casing and the quoting of a
    /// panel name are copied from MeasurementFile.Json.
    /// </summary>
    [Fact]
    public void TheNewFieldsReadBackFromTheProbesFile()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"uws-measured-{Guid.NewGuid():N}.json");

        try
        {
            File.WriteAllText(path, """
                {
                  "_comment": "Written by the Ultrawide Stash probe.",
                  "maxColumns": 10,
                  "screen": "2560x1440",
                  "canvasWidth": 1920,
                  "panelWidth": 680,
                  "canWiden": false,
                  "whyNot": "the gear side is 1206 px wide and keeps 1240 px for itself, which leaves no room for another column",
                  "measuredAtUtc": "2026-09-28T02:00:00Z",
                  "probeVersion": "1.0.3"
                }
                """);

            var read = Measurement.Read(path, out _);

            Assert.NotNull(read);
            Assert.False(read.CanWiden);
            Assert.StartsWith("the gear side is 1206 px wide", read.WhyNot);
            Assert.NotNull(read.Diagnosis());
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// Probes before 1.0.5 measured EFT's own 16:9 inventory frame, which they could
    /// not widen, and wrote 10 columns on every install without UIScale.Reloaded
    /// (Forge issues #1 and #2). Their files are ignored; 1.0.5's and later are not.
    /// </summary>
    [Theory]
    [InlineData("1.0.4", true)]
    [InlineData("1.0.2", true)]
    [InlineData("0.9.3", true)]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("not a version", true)]
    [InlineData("1.0.5", false)]
    [InlineData("1.0.5+92e252d", false)]
    [InlineData(" 1.0.6 ", false)]
    [InlineData("1.1.0", false)]
    [InlineData("2.0", false)]
    public void MeasurementsFromBeforeTheStretchAreNotTrusted(string? probeVersion, bool predates)
    {
        var measured = new Measurement { MaxColumns = 10, ProbeVersion = probeVersion };

        Assert.Equal(predates, measured.PredatesTheStretch());
    }

    [Fact]
    public void AnOldFileStillReads()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"uws-measured-{Guid.NewGuid():N}.json");

        try
        {
            File.WriteAllText(path, """
                {
                  "maxColumns": 19,
                  "screen": "3440x1440",
                  "canvasWidth": 2580,
                  "panelWidth": 1250,
                  "measuredAtUtc": "2026-09-25T01:12:15Z",
                  "probeVersion": "1.0.2"
                }
                """);

            var read = Measurement.Read(path, out _);

            Assert.NotNull(read);
            Assert.Equal(19, read.MaxColumns);
            Assert.Null(read.CanWiden);
            Assert.Null(read.Diagnosis());
        }
        finally
        {
            File.Delete(path);
        }
    }
}
