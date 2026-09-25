using UltrawideStash.Probe;
using Xunit;

namespace UltrawideStash.Server.Tests;

/// <summary>
/// The planner behind the scav loot transfer and mail transfer screens.
///
/// The scav layout here is read off a 3440x1440 screenshot of the real screen
/// (2026-09-24), converted to canvas units: a 2580x1080 canvas centred on 0, y up.
/// It is an approximation -- the client measures the real one -- but it is the right
/// shape: a 16:9 frame in the middle with ~330 px of nothing either side, LeftSide
/// 10 px left of a 680 px stash panel, and Next poking up into the bottom of the
/// panel's span with Back and Sell All below it.
/// </summary>
public class ScreenLayoutTests
{
    private const float Chrome = 48f;

    private const float Extra = Chrome + ScreenLayout.ScrollSlack;

    private static readonly Box Canvas = new(-1290f, -540f, 1290f, 540f);

    private static readonly Box Panel = new(266f, -399f, 946f, 460f);

    private static Obstacle LeftSide(params Box[] inner) => new()
    {
        Name = "LeftSide",
        Box = new Box(-914f, -399f, 256f, 460f),
        Movable = true,
        Inner = inner,
    };

    private static Obstacle Next() => new() { Name = "Next", Box = new Box(-77f, -417f, 77f, -369f), IsButton = true };

    private static Obstacle Back() => new() { Name = "Back", Box = new Box(-65f, -485f, 65f, -440f), IsButton = true };

    private static Obstacle SellAll() => new() { Name = "Sell All", Box = new Box(155f, -485f, 342f, -440f), IsButton = true };

    /// <summary>The panel as planned: new edges, bottom brought up by the lift.</summary>
    private static Box Final(LayoutPlan plan, Box panel) => new(plan.Left, panel.YMin + plan.Lift, plan.Right, panel.YMax);

    [Fact]
    public void TheScavScreenGetsAllNineteenColumns()
    {
        var obstacles = new List<Obstacle> { LeftSide(), Next(), Back(), SellAll() };

        var plan = ScreenLayout.Plan(Canvas, Panel, obstacles, 19, Chrome);

        Assert.True(plan.Changed);
        Assert.Equal(19, plan.Columns);
        Assert.True(plan.Right <= Canvas.XMax - ScreenLayout.EdgeMargin);
        Assert.True(plan.Right - plan.Left >= ScreenLayout.WidthOfColumns(19) + Extra - 0.01f);
    }

    [Fact]
    public void TheScavScreenKeepsEveryButtonClear()
    {
        var obstacles = new List<Obstacle> { LeftSide(), Next(), Back(), SellAll() };

        var plan = ScreenLayout.Plan(Canvas, Panel, obstacles, 19, Chrome);
        var final = Final(plan, Panel);

        Assert.False(final.Overlaps(Next().Box));
        Assert.False(final.Overlaps(Back().Box));
        Assert.False(final.Overlaps(SellAll().Box));

        // Next reaches into the panel's new span, so the bottom came up to clear it,
        // and by no more than a row and a half.
        Assert.True(plan.Lift > 0f);
        Assert.True(plan.Lift <= ScreenLayout.MaxLift);
    }

    [Fact]
    public void LeftSideSlidesIntoItsOwnMarginAndStaysClear()
    {
        var obstacles = new List<Obstacle> { LeftSide(), Next(), Back(), SellAll() };

        var plan = ScreenLayout.Plan(Canvas, Panel, obstacles, 19, Chrome);

        Assert.Same(obstacles[0], plan.Neighbour);

        var slid = obstacles[0].Box.Shifted(-plan.Shift);

        Assert.True(slid.XMin >= Canvas.XMin + ScreenLayout.EdgeMargin);
        Assert.True(plan.Left - slid.XMax >= ScreenLayout.Gap - 0.01f);
    }

    /// <summary>
    /// The pouch sits at the bottom of the containers column. If sliding LeftSide
    /// would carry it under Next, the slide is refused and the panel takes fewer
    /// columns instead -- a narrower stash beats a button over a slot.
    /// </summary>
    [Fact]
    public void ASlideThatPutsNextOverASlotIsRefused()
    {
        // Just left of Next and clear of it today; any slide left keeps it clear,
        // so put it right of Next where a leftward slide carries it underneath.
        var slot = new Box(90f, -402f, 219f, -298f);
        var obstacles = new List<Obstacle> { LeftSide(slot), Next(), Back(), SellAll() };

        var plan = ScreenLayout.Plan(Canvas, Panel, obstacles, 19, Chrome);

        if (plan.Neighbour != null)
        {
            var moved = slot.Shifted(-plan.Shift);
            Assert.False(moved.Overlaps(Next().Box));
        }

        Assert.True(plan.Columns < 19);
        Assert.False(Final(plan, Panel).Overlaps(Next().Box));
    }

    [Fact]
    public void AnImmovableNeighbourLimitsTheStashToTheRightMargin()
    {
        var side = LeftSide();
        side.Movable = false;

        var plan = ScreenLayout.Plan(Canvas, Panel, new List<Obstacle> { side, Next(), Back(), SellAll() }, 19, Chrome);

        Assert.True(plan.Changed);
        Assert.Equal(Panel.XMin, plan.Left);
        Assert.Equal(ScreenLayout.ColumnsIn(Canvas.XMax - ScreenLayout.EdgeMargin - Panel.XMin, Extra), plan.Columns);
    }

    /// <summary>
    /// On a 16:9 screen the frame is the whole canvas: no margin on either side, so
    /// there is nowhere to grow and nothing moves.
    /// </summary>
    [Fact]
    public void Nothing16By9MovesAnything()
    {
        var canvas = new Box(-960f, -540f, 960f, 540f);
        var panel = new Box(266f, -399f, 946f, 460f);
        var side = new Obstacle { Name = "LeftSide", Box = new Box(-946f, -399f, 256f, 460f), Movable = true };

        var plan = ScreenLayout.Plan(canvas, panel, new List<Obstacle> { side, Next(), Back(), SellAll() }, 19, Chrome);

        Assert.False(plan.Changed);
    }

    [Fact]
    public void APanelThatAlreadyShowsTheGridIsLeftAlone()
    {
        var plan = ScreenLayout.Plan(Canvas, Panel, new List<Obstacle> { LeftSide(), Next() }, 10, Chrome);

        Assert.False(plan.Changed);
        Assert.Equal(10, plan.Columns);
    }

    /// <summary>
    /// A button beside the panel rather than below it -- taller than a lift can
    /// clear -- stops the growth at the button instead.
    /// </summary>
    [Fact]
    public void ATallButtonBesideThePanelStopsTheGrowth()
    {
        var tall = new Obstacle { Name = "Tall", Box = new Box(1100f, -300f, 1200f, 200f), IsButton = true };

        var plan = ScreenLayout.Plan(Canvas, Panel, new List<Obstacle> { LeftSide(), tall }, 19, Chrome);

        Assert.True(plan.Right <= tall.Box.XMin - ScreenLayout.Gap);
        Assert.Equal(0f, plan.Lift);
        Assert.False(Final(plan, Panel).Overlaps(tall.Box));
    }

    /// <summary>
    /// The mail screen's shape, as far as the assembly tells it: a 10-wide transfer
    /// grid on the left, the stash on the right, Accept and Receive All along the
    /// bottom. Whatever the real positions, no button may end up under the panel.
    /// </summary>
    [Fact]
    public void TheMailScreenKeepsItsButtonsClear()
    {
        var transfer = new Obstacle { Name = "Transfer", Box = new Box(-600f, -399f, 100f, 460f), Movable = true };
        var accept = new Obstacle { Name = "Accept", Box = new Box(150f, -460f, 330f, -410f), IsButton = true };
        var receive = new Obstacle { Name = "Receive All", Box = new Box(350f, -460f, 560f, -380f), IsButton = true };

        var plan = ScreenLayout.Plan(Canvas, Panel, new List<Obstacle> { transfer, accept, receive }, 19, Chrome);
        var final = Final(plan, Panel);

        Assert.Equal(19, plan.Columns);
        Assert.False(final.Overlaps(accept.Box));
        Assert.False(final.Overlaps(receive.Box));
    }

    /// <summary>
    /// The invariant the user asked for, swept: a button anywhere along the bottom of
    /// the screen, at any height, and the planned panel never covers it.
    /// </summary>
    [Fact]
    public void NoButtonPositionEverEndsUnderThePanel()
    {
        for (var x = -1200f; x <= 1200f; x += 37f)
        {
            for (var top = -520f; top <= 200f; top += 23f)
            {
                var button = new Obstacle { Name = "B", Box = new Box(x, top - 40f, x + 150f, top), IsButton = true };

                // Skip buttons the vanilla panel already sits on; they are not obstacles.
                if (button.Box.Overlaps(Panel)) continue;

                var plan = ScreenLayout.Plan(Canvas, Panel, new List<Obstacle> { LeftSide(), button }, 19, Chrome);

                Assert.False(Final(plan, Panel).Overlaps(button.Box), $"button at x {x}, top {top}");

                // A slid LeftSide may only sit on a button it already sat on.
                if (plan.Neighbour != null && !button.Box.Overlaps(plan.Neighbour.Box))
                {
                    var slid = plan.Neighbour.Box.Shifted(-plan.Shift);
                    Assert.False(slid.Overlaps(button.Box), $"slid LeftSide onto a button at x {x}, top {top}");
                }
            }
        }
    }
}
