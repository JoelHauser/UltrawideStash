using UltrawideStash.Server;
using Xunit;

namespace UltrawideStash.Server.Tests;

/// <summary>
/// The canvas arithmetic, which is what decides whether a width is safe for a screen.
///
/// The claim these exist to pin down is the one that surprised the user: **resolution
/// does not matter, aspect ratio does.** A 4K 16:9 monitor has exactly the same width
/// budget as a 1080p one, and it is zero.
/// </summary>
public class StashFitTests
{
    [Theory]
    // Every 16:9 screen scales to the same 1920-wide canvas, whatever its resolution.
    [InlineData(1280, 720, 1920)]
    [InlineData(1920, 1080, 1920)]
    [InlineData(2560, 1440, 1920)]
    [InlineData(3840, 2160, 1920)]
    // 16:10 and 4:3 gain height, not width, so the canvas stays at the reference width.
    [InlineData(1920, 1200, 1920)]
    [InlineData(2560, 1600, 1920)]
    [InlineData(1600, 1200, 1920)]
    // Wider than 16:9 is where the extra canvas appears. 1080 * aspect, in effect.
    [InlineData(2560, 1080, 2560)]
    [InlineData(3440, 1440, 2580)]
    [InlineData(3840, 1080, 3840)]
    [InlineData(5120, 1440, 3840)]
    [InlineData(5760, 1080, 5760)]
    public void TheCanvasWidthFollowsAspectRatioNotResolution(int w, int h, int expected)
    {
        Assert.Equal(expected, StashFit.CanvasWidth(w, h));
    }

    [Fact]
    public void FourKAndTenEightyHaveIdenticalWidthBudgets()
    {
        Assert.Equal(
            StashFit.CanvasWidth(1920, 1080),
            StashFit.CanvasWidth(3840, 2160));

        Assert.Equal(
            StashFit.ConservativeColumnsForScreen(1920, 1080),
            StashFit.ConservativeColumnsForScreen(3840, 2160));
    }

    [Fact]
    public void AGarbageScreenSizeFallsBackToTheReferenceWidth()
    {
        Assert.Equal(StashFit.ReferenceWidth, StashFit.CanvasWidth(0, 0));
        Assert.Equal(StashFit.ReferenceWidth, StashFit.CanvasWidth(-1, 1080));
        Assert.Equal(StashFit.ReferenceWidth, StashFit.CanvasWidth(1920, 0));
    }

    [Theory]
    // The whole point of the new default: a 16:9 screen gets vanilla, so an unmeasured
    // install on the commonest hardware in the world changes nothing at all.
    [InlineData(1920, 1080, 10)]
    [InlineData(2560, 1440, 10)]
    [InlineData(3840, 2160, 10)]
    [InlineData(1920, 1200, 10)]
    // 660 extra canvas px / 63 = 10 extra columns.
    [InlineData(3440, 1440, 20)]
    [InlineData(2560, 1080, 20)]
    // 1920 extra / 63 = 30 extra columns.
    [InlineData(5120, 1440, 40)]
    public void TheConservativeCeilingGrantsOnlyWidthBeyondSixteenByNine(int w, int h, int expected)
    {
        Assert.Equal(expected, StashFit.ConservativeColumnsForScreen(w, h));
    }

    [Fact]
    public void TheConservativeCeilingNeverGoesBelowVanilla()
    {
        foreach (var (w, h) in new[] { (800, 600), (1024, 768), (1280, 1024), (640, 480) })
        {
            Assert.True(
                StashFit.ConservativeColumnsForScreen(w, h) >= StashFit.VanillaColumns,
                $"{w}x{h} should never suggest narrowing the stash");
        }
    }

    [Fact]
    public void AGrantedColumnAlwaysActuallyFitsTheCanvas()
    {
        // The ceiling has to be honest in the direction that matters: whatever it
        // allows must draw inside the canvas, on every shape.
        foreach (var (w, h) in new[]
                 {
                     (1920, 1080), (2560, 1440), (3840, 2160), (1920, 1200),
                     (2560, 1080), (3440, 1440), (3840, 1080), (5120, 1440), (5760, 1080),
                 })
        {
            var canvas = StashFit.CanvasWidth(w, h);
            var columns = StashFit.ConservativeColumnsForScreen(w, h);

            Assert.True(
                StashFit.WidthOfColumns(columns) <= canvas,
                $"{columns} columns is {StashFit.WidthOfColumns(columns)}px, past the "
                + $"{canvas}px canvas of a {w}x{h} screen");
        }
    }

    [Theory]
    [InlineData(10, 631)]
    [InlineData(16, 1009)]
    [InlineData(20, 1261)]
    [InlineData(40, 2521)]
    public void GridWidthIsColumnsTimesSixtyThreePlusOne(int columns, int expected)
    {
        Assert.Equal(expected, StashFit.WidthOfColumns(columns));
    }

    [Fact]
    public void ColumnsThatFitIsTheInverseOfWidthOfColumns()
    {
        for (var columns = StashFit.MinColumns; columns <= 80; columns++)
        {
            var width = StashFit.WidthOfColumns(columns);

            Assert.Equal(columns, StashFit.ColumnsThatFit(width));

            // One pixel short and it must report one fewer, never round up.
            Assert.Equal(columns - 1, StashFit.ColumnsThatFit(width - 1));
        }
    }

    [Fact]
    public void ANarrowSpanFitsNothing()
    {
        Assert.Equal(0, StashFit.ColumnsThatFit(0));
        Assert.Equal(0, StashFit.ColumnsThatFit(63));
        Assert.Equal(1, StashFit.ColumnsThatFit(64));
    }
}
