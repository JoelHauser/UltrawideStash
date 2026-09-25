#nullable disable

using System;
using System.Collections.Generic;

namespace UltrawideStash.Probe
{
    /// <summary>
    /// An axis-aligned box in canvas units, y up. Unity's <c>Rect</c> would do, but
    /// this file is also compiled into the test project, which has no engine.
    /// </summary>
    internal struct Box
    {
        internal readonly float XMin;
        internal readonly float YMin;
        internal readonly float XMax;
        internal readonly float YMax;

        internal Box(float xMin, float yMin, float xMax, float yMax)
        {
            XMin = xMin;
            YMin = yMin;
            XMax = xMax;
            YMax = yMax;
        }

        internal float Width => XMax - XMin;

        internal float Height => YMax - YMin;

        internal Box Shifted(float dx)
        {
            return new Box(XMin + dx, YMin, XMax + dx, YMax);
        }

        /// <summary>
        /// Overlap by more than a pixel either way. Touching edges, and the rounding
        /// that world-corner conversion leaves behind, are not an overlap.
        /// </summary>
        internal bool Overlaps(Box other)
        {
            return Math.Min(XMax, other.XMax) - Math.Max(XMin, other.XMin) > 1f
                && Math.Min(YMax, other.YMax) - Math.Max(YMin, other.YMin) > 1f;
        }

        /// <summary>Shares more than a pixel of height with the band from yMin to yMax.</summary>
        internal bool InBand(float yMin, float yMax)
        {
            return Math.Min(YMax, yMax) - Math.Max(YMin, yMin) > 1f;
        }

        public override string ToString()
        {
            return string.Format("x {0:0}..{1:0}, y {2:0}..{3:0}", XMin, XMax, YMin, YMax);
        }
    }

    /// <summary>Something on screen the stash panel must not grow over.</summary>
    internal sealed class Obstacle
    {
        internal string Name;

        internal Box Box;

        /// <summary>A button. Only for the log, and for never moving one.</summary>
        internal bool IsButton;

        /// <summary>
        /// Whether the planner may slide this sideways to make room. Only a panel
        /// that is a plain sibling in the stash panel's own layout, never a button.
        /// </summary>
        internal bool Movable;

        /// <summary>
        /// The drawn pieces inside a movable obstacle -- slots, grids, labels. Used to
        /// check that sliding it does not put a button over something that was clear
        /// of one before.
        /// </summary>
        internal Box[] Inner = new Box[0];
    }

    /// <summary>What <see cref="ScreenLayout.Plan"/> decided.</summary>
    internal sealed class LayoutPlan
    {
        /// <summary>False when the panel should be left exactly as it is.</summary>
        internal bool Changed;

        /// <summary>The panel's new horizontal edges, canvas units.</summary>
        internal float Left;

        internal float Right;

        /// <summary>How far the panel's bottom edge comes up to clear a button below it.</summary>
        internal float Lift;

        /// <summary>The obstacle slid left to make room, or null.</summary>
        internal Obstacle Neighbour;

        /// <summary>How far <see cref="Neighbour"/> slides left.</summary>
        internal float Shift;

        /// <summary>Columns the panel shows after the plan.</summary>
        internal int Columns;

        /// <summary>One line for the log, always set.</summary>
        internal string Why = string.Empty;
    }

    /// <summary>
    /// Where the stash panel can grow on a screen that is not the character screen:
    /// the scav loot transfer after a raid, and receiving items from mail.
    ///
    /// ## Why this is not <see cref="StashWiden"/>
    ///
    /// The character screen's layout is known, measured, and fixed: a stretching
    /// <c>LeftSide</c> with slack inside it. These screens are laid out differently,
    /// and differently from each other, in serialized prefab data the assembly does
    /// not carry. So instead of a recipe for one layout, this takes a map of what is
    /// actually on screen -- every drawn thing beside the panel, measured live -- and
    /// grows the panel only into space nothing occupies.
    ///
    /// Growth is rightward first, into the empty canvas an ultrawide has past the
    /// screen's 16:9 frame, then leftward. A panel standing in the way on the left
    /// may slide left into its own empty margin, keeping its size. Buttons are never
    /// moved and never covered: one that pokes up into the bottom of the panel's
    /// span is cleared by bringing the panel's bottom edge up, and one any taller
    /// than that stops the growth outright.
    ///
    /// Pure arithmetic, so the tests run it without an engine.
    /// </summary>
    internal static class ScreenLayout
    {
        /// <summary>One cell plus its border; see <see cref="StashMeasure.CellPixels"/>.</summary>
        internal const float CellPixels = 63f;

        /// <summary>Clear canvas kept at the screen's edge, as on the character screen.</summary>
        internal const float EdgeMargin = 12f;

        /// <summary>Clear air between the grown panel and anything beside it.</summary>
        internal const float Gap = 24f;

        /// <summary>Clear air between a button and the panel's bottom edge above it.</summary>
        internal const float LiftGap = 8f;

        /// <summary>
        /// The most the panel's bottom edge may come up to clear a button: a row and a
        /// half. The stash scrolls vertically anyway, so this costs a sliver of one
        /// row. Anything taller is not a button poking up from below, it is beside the
        /// panel, and the panel stops short of it instead.
        /// </summary>
        internal const float MaxLift = 96f;

        /// <summary>
        /// Slack kept between a grown panel's grid and its viewport, so the ScrollRect
        /// is never tipped into scrolling by rounding. Same four pixels as
        /// <see cref="StashWiden.ScrollSlackPixels"/>, and only for the grown size: the
        /// vanilla panel is 632 px of viewport round a 631 px grid, and counting slack
        /// there would call its 10 columns 9.
        /// </summary>
        internal const float ScrollSlack = 4f;

        /// <summary>How wide a grid of this many columns is drawn.</summary>
        internal static float WidthOfColumns(int columns)
        {
            return columns * CellPixels + 1f;
        }

        /// <summary>Columns fully visible in a panel this wide.</summary>
        internal static int ColumnsIn(float panelWidth, float extra)
        {
            var usable = panelWidth - extra - 1f;

            return usable < CellPixels ? 0 : (int)Math.Floor(usable / CellPixels);
        }

        /// <summary>
        /// Plan the panel's growth.
        /// </summary>
        /// <param name="canvas">The whole canvas.</param>
        /// <param name="panel">The stash panel as the game laid it out.</param>
        /// <param name="obstacles">Everything drawn that does not overlap the panel today.</param>
        /// <param name="gridColumns">How wide the stash grid is.</param>
        /// <param name="chrome">Panel width the grid never gets: toolbar strip and scrollbar.</param>
        internal static LayoutPlan Plan(
            Box canvas,
            Box panel,
            IList<Obstacle> obstacles,
            int gridColumns,
            float chrome)
        {
            var shown = ColumnsIn(panel.Width, chrome);
            var extra = chrome + ScrollSlack;
            var plan = new LayoutPlan { Left = panel.XMin, Right = panel.XMax, Columns = shown };

            if (shown >= gridColumns)
            {
                plan.Why = string.Format("already shows all {0} columns.", gridColumns);
                return plan;
            }

            var bottom = panel.YMin;
            var top = panel.YMax;

            var blockers = new List<Obstacle>();
            var lows = new List<Obstacle>();

            foreach (var o in obstacles)
            {
                if (!o.Box.InBand(bottom, top)) continue;

                if (Low(o, bottom)) lows.Add(o);
                else blockers.Add(o);
            }

            // Rightward: to the canvas edge, or short of the first thing in the way.
            var rightLimit = canvas.XMax - EdgeMargin;

            foreach (var o in blockers)
            {
                if (o.Box.XMin >= panel.XMax - 1f) rightLimit = Math.Min(rightLimit, o.Box.XMin - Gap);
            }

            var rightRoom = Math.Max(0f, rightLimit - panel.XMax);

            // Leftward: the nearest thing on the left, which may be able to move, and
            // whatever stands behind it, which may not.
            Obstacle nearest = null;
            var behind = canvas.XMin + EdgeMargin;

            foreach (var o in blockers)
            {
                if (o.Box.XMax > panel.XMin + 1f) continue;

                if (nearest == null || o.Box.XMax > nearest.Box.XMax) nearest = o;
            }

            foreach (var o in blockers)
            {
                if (o == nearest || o.Box.XMax > panel.XMin + 1f) continue;

                behind = Math.Max(behind, o.Box.XMax + Gap);
            }

            var neighbourRoom = nearest != null && nearest.Movable
                ? RoomToSlide(canvas, nearest, obstacles)
                : 0f;

            var why = string.Empty;

            for (var columns = gridColumns; columns > shown; columns--)
            {
                var grow = WidthOfColumns(columns) + extra - panel.Width;
                var right = Math.Min(grow, rightRoom);
                var left = grow - right;
                var newLeft = panel.XMin - left;
                var newRight = panel.XMax + right;

                if (newLeft < behind)
                {
                    why = "no room on the left";
                    continue;
                }

                var shift = 0f;

                if (left > 0f && nearest != null && newLeft < nearest.Box.XMax + Gap)
                {
                    shift = nearest.Box.XMax + Gap - newLeft;

                    if (!nearest.Movable)
                    {
                        why = string.Format("'{0}' is in the way on the left", nearest.Name);
                        continue;
                    }

                    if (shift > neighbourRoom)
                    {
                        why = string.Format("'{0}' has only {1:0} px to move into", nearest.Name, neighbourRoom);
                        continue;
                    }

                    var covered = NewlyCovered(nearest, shift, obstacles);

                    if (covered != null)
                    {
                        why = string.Format(
                            "sliding '{0}' would put '{1}' over something in it", nearest.Name, covered.Name);
                        continue;
                    }
                }

                var lift = 0f;
                var span = new Box(newLeft, bottom, newRight, top);

                foreach (var o in lows)
                {
                    if (!Overlaps(span, o.Box)) continue;

                    lift = Math.Max(lift, o.Box.YMax + LiftGap - bottom);
                }

                plan.Changed = true;
                plan.Left = newLeft;
                plan.Right = newRight;
                plan.Lift = lift;
                plan.Neighbour = shift > 0f ? nearest : null;
                plan.Shift = shift;
                plan.Columns = columns;
                plan.Why = columns == gridColumns
                    ? string.Format("room for all {0} columns.", columns)
                    : string.Format("room for {0} of {1} columns: {2}.", columns, gridColumns, why);

                return plan;
            }

            plan.Why = string.Format("no room for even one more column: {0}.", why.Length > 0 ? why : "nothing free");
            return plan;
        }

        /// <summary>
        /// A button poking up into the bottom of the panel's span, low enough to clear
        /// by bringing the bottom edge up rather than stopping the growth.
        /// </summary>
        private static bool Low(Obstacle o, float bottom)
        {
            return o.IsButton && o.Box.YMax + LiftGap - bottom <= MaxLift;
        }

        /// <summary>Horizontal overlap within the vertical band, edges not counted.</summary>
        private static bool Overlaps(Box span, Box o)
        {
            return Math.Min(span.XMax, o.XMax) - Math.Max(span.XMin, o.XMin) > 1f
                && o.InBand(span.YMin, span.YMax);
        }

        /// <summary>
        /// How far a panel can slide left, keeping its size: to the canvas margin or
        /// short of the first thing in its own band that it does not overlap today.
        /// Buttons count in full here -- a sliding panel cannot duck under one.
        /// </summary>
        private static float RoomToSlide(Box canvas, Obstacle n, IList<Obstacle> obstacles)
        {
            var limit = canvas.XMin + EdgeMargin;

            foreach (var o in obstacles)
            {
                if (o == n || o.Box.Overlaps(n.Box)) continue;
                if (!o.Box.InBand(n.Box.YMin, n.Box.YMax)) continue;
                if (o.Box.XMax > n.Box.XMin + 1f) continue;

                limit = Math.Max(limit, o.Box.XMax + Gap);
            }

            return Math.Max(0f, n.Box.XMin - limit);
        }

        /// <summary>
        /// Whether sliding <paramref name="n"/> puts anything it overlaps today --
        /// the Next button over the scav's pouch, say -- over a piece of it that was
        /// clear before. Returns the offender, or null. Something that already sat
        /// over part of it (Next over the empty bottom of the containers column) may
        /// keep doing so; it just may not land on anything new.
        /// </summary>
        private static Obstacle NewlyCovered(Obstacle n, float shift, IList<Obstacle> obstacles)
        {
            foreach (var o in obstacles)
            {
                if (o == n || !o.Box.Overlaps(n.Box)) continue;

                foreach (var inner in n.Inner)
                {
                    if (!inner.Overlaps(o.Box) && inner.Shifted(-shift).Overlaps(o.Box)) return o;
                }
            }

            return null;
        }
    }
}
