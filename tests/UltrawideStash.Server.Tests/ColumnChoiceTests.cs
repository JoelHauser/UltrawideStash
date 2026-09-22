using UltrawideStash.Server;
using Xunit;

namespace UltrawideStash.Server.Tests;

/// <summary>
/// The decision that keeps a player from setting a width their screen cannot draw.
///
/// The failure being guarded is silent and looks like data loss: EFT's canvas is
/// <c>ConstantPixelSize</c>, so an over-wide grid does not shrink its cells to fit --
/// it overflows the panel and gets clipped, and the columns past the edge look exactly
/// like a stash that has eaten someone's things.
/// </summary>
public class ColumnChoiceTests
{
    private static Measurement Measured(int maxColumns) =>
        new() { MaxColumns = maxColumns, Screen = "3440x1440", CanvasWidth = 2580 };

    [Fact]
    public void AutoOnSixteenByNineWithNoMeasurementChangesNothing()
    {
        // The single most important case: someone on 1080p installs this, edits
        // nothing, and their stash is untouched rather than clipped.
        var choice = ColumnChoice.For(null, null, 1920, 1080, false);

        Assert.Equal(10, choice.Columns);
        Assert.True(choice.IsNoOp);
        Assert.Equal(ColumnChoice.Origin.Estimated, choice.Source);
    }

    [Fact]
    public void AutoOnAnUltrawideWithNoMeasurementStillWidens()
    {
        var choice = ColumnChoice.For(null, null, 3440, 1440, false);

        // 19, not the 20 this asserted before. The old estimate assumed the stash panel
        // was pinned and granted every pixel of canvas past 16:9; the client now widens
        // the panel, and 19 is what that widening actually leaves room for -- measured
        // on a live 3440x1440 client, viewport 1202 px around a 1198 px grid. Asking for
        // 20 would have overflowed the panel by a column and grown a scrollbar.
        Assert.Equal(19, choice.Columns);
        Assert.False(choice.IsNoOp);
        Assert.Equal(ColumnChoice.Origin.Estimated, choice.Source);
    }

    [Fact]
    public void AMeasurementBeatsTheEstimateInBothDirections()
    {
        // More, when the panel turns out to have slack of its own.
        var generous = ColumnChoice.For(null, Measured(24), 3440, 1440, false);

        Assert.Equal(24, generous.Columns);
        Assert.Equal(ColumnChoice.Origin.Measured, generous.Source);

        // And less, when the panel is pinned narrower than the canvas.
        var pinned = ColumnChoice.For(null, Measured(12), 3440, 1440, false);

        Assert.Equal(12, pinned.Columns);
        Assert.Equal(ColumnChoice.Origin.Measured, pinned.Source);
    }

    [Fact]
    public void AMeasurementLetsASixteenByNineScreenWidenAfterAll()
    {
        // The estimate assumes the panel has no slack because the server cannot see
        // it. If the probe finds some, that is real evidence and it wins.
        var choice = ColumnChoice.For(null, Measured(14), 1920, 1080, false);

        Assert.Equal(14, choice.Columns);
        Assert.Equal(ColumnChoice.Origin.Measured, choice.Source);
    }

    [Fact]
    public void AnExplicitWidthThatFitsIsHonoured()
    {
        var choice = ColumnChoice.For(16, Measured(20), 3440, 1440, false);

        Assert.Equal(16, choice.Columns);
        Assert.Equal(ColumnChoice.Origin.Requested, choice.Source);
    }

    [Fact]
    public void AnExplicitWidthThatDoesNotFitIsClampedAndExplained()
    {
        var choice = ColumnChoice.For(30, Measured(20), 3440, 1440, false);

        Assert.Equal(20, choice.Columns);
        Assert.Equal(ColumnChoice.Origin.Clamped, choice.Source);
        Assert.Contains("30", choice.Reason);
        Assert.Contains("20", choice.Reason);
        Assert.Contains("ignoreMeasurement", choice.Reason);
    }

    [Fact]
    public void TheOldDefaultOfSixteenIsClampedOnASixteenByNineScreen()
    {
        // 0.5.0 shipped columns:16 as the default. On an unmeasured 16:9 screen that
        // is 1009px of grid where vanilla is 631, with no spare canvas to take it.
        var choice = ColumnChoice.For(16, null, 1920, 1080, false);

        Assert.Equal(10, choice.Columns);
        Assert.Equal(ColumnChoice.Origin.Clamped, choice.Source);
    }

    [Fact]
    public void IgnoreMeasurementHonoursTheNumberExactly()
    {
        var choice = ColumnChoice.For(30, Measured(20), 3440, 1440, true);

        Assert.Equal(30, choice.Columns);
        Assert.Equal(ColumnChoice.Origin.Unchecked, choice.Source);
    }

    [Fact]
    public void IgnoreMeasurementStillCannotProduceAnAbsurdWidth()
    {
        var choice = ColumnChoice.For(99999, null, 3440, 1440, true);

        Assert.Equal(StashFit.AbsoluteMaxColumns, choice.Columns);
    }

    [Fact]
    public void IgnoreMeasurementWithAutoIsStillAuto()
    {
        // There is no number to honour, so the ceiling is all there is.
        var choice = ColumnChoice.For(null, Measured(20), 3440, 1440, true);

        Assert.Equal(20, choice.Columns);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(-5)]
    public void AnAbsurdlySmallWidthNeverNarrowsTheStash(int requested)
    {
        var choice = ColumnChoice.For(requested, Measured(20), 3440, 1440, false);

        // Clamped up to MinColumns by the choice, and StashLayout then refuses it for
        // being narrower than vanilla. Either way nothing narrows.
        Assert.True(choice.Columns >= StashFit.MinColumns);

        var plan = StashLayout.For(10, 68, choice.Columns, true, 0);

        Assert.False(plan.Applied);
        Assert.Equal(10, plan.Columns);
    }

    [Fact]
    public void TheCeilingItselfNeverFallsBelowVanilla()
    {
        // A probe that somehow reports 3 must not become a reason to narrow anyone's
        // stash -- StashLayout would refuse it, but the ceiling should not propose it.
        var choice = ColumnChoice.For(null, Measured(3), 1920, 1080, false);

        Assert.Equal(StashFit.VanillaColumns, choice.Ceiling);
        Assert.True(choice.Columns >= StashFit.VanillaColumns);
    }

    [Fact]
    public void EveryChoiceCarriesAReasonFitForTheLog()
    {
        foreach (var choice in new[]
                 {
                     ColumnChoice.For(null, null, 1920, 1080, false),
                     ColumnChoice.For(null, Measured(20), 3440, 1440, false),
                     ColumnChoice.For(16, Measured(20), 3440, 1440, false),
                     ColumnChoice.For(30, Measured(20), 3440, 1440, false),
                     ColumnChoice.For(30, Measured(20), 3440, 1440, true),
                 })
        {
            Assert.False(string.IsNullOrWhiteSpace(choice.Reason));
        }
    }

    [Fact]
    public void WhateverIsChosenActuallyFitsTheScreenUnlessOverridden()
    {
        // The invariant the whole type exists for.
        foreach (var (w, h) in new[]
                 {
                     (1920, 1080), (2560, 1440), (3840, 2160), (1920, 1200),
                     (2560, 1080), (3440, 1440), (5120, 1440),
                 })
        {
            foreach (int? requested in new int?[] { null, 10, 16, 24, 40, 100 })
            {
                var choice = ColumnChoice.For(requested, null, w, h, false);

                Assert.True(
                    StashFit.WidthOfColumns(choice.Columns) <= StashFit.CanvasWidth(w, h),
                    $"{choice.Columns} columns does not fit a {w}x{h} screen");
            }
        }
    }
}
