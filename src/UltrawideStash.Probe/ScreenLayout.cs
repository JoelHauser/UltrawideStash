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
        /// Whether this may be carried along when the obstacle it sits inside slides:
        /// a plain sibling nothing lays out. The trader's buy panel is a separate
        /// sibling stacked exactly over the deal column, and must go with it.
        /// </summary>
        internal bool CanRide;

        /// <summary>
        /// The drawn pieces inside a movable obstacle -- slots, grids, labels. Used to
        /// check that sliding it does not put a button over something that was clear
        /// of one before.
        /// </summary>
        internal Box[] Inner = new Box[0];

        /// <summary>Names for <see cref="Inner"/>, same order, for the log. May be shorter or empty.</summary>
        internal string[] InnerNames = new string[0];
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

        /// <summary>
        /// Panels further left that <see cref="Neighbour"/> pushes ahead of it, nearest
        /// first, each sliding by the same <see cref="Shift"/>. Empty unless the
        /// neighbour's own margin was not enough.
        /// </summary>
        internal List<Obstacle> Pushed = new List<Obstacle>();

        /// <summary>Obstacles drawn over <see cref="Neighbour"/> or a pushed panel that slide with it.</summary>
        internal List<Obstacle> Riders = new List<Obstacle>();

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
    /// may slide left into its own empty margin, keeping its size, pushing the panels
    /// behind it along when that margin is theirs (the scav screen). Buttons are never
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

        /// <summary>
        /// How far a screen's left edge has to move left, and its right edge right, for
        /// it to span the canvas. False when it already does, to within a pixel.
        ///
        /// At 21:9 the trader screen spans the canvas; at 32:9 (5120x1440, canvas 3840)
        /// it is a 1920 px frame in the middle, with the showcase and the stash pinned
        /// to the frame's edges and 960 px of nothing either side of it. The planner
        /// slides one neighbour into its own margin, so it never reaches that space --
        /// 11 of 39 columns in the first 32:9 log. Spanning the canvas gives the screen
        /// the shape it has at 21:9, which is the one the planner was verified on.
        /// </summary>
        internal static bool StretchToCanvas(Box screen, Box canvas, out float left, out float right)
        {
            left = Math.Max(0f, screen.XMin - canvas.XMin);
            right = Math.Max(0f, canvas.XMax - screen.XMax);

            return left + right > 1f;
        }

        /// <summary>
        /// How far the viewport falls short of a grid of the columns the plan sized it
        /// for: chrome the plan did not know about. Zero when the plan widened nothing.
        ///
        /// Not the grid's overflow. A plan that ran out of room gives fewer columns than
        /// the grid on purpose, and the overflow then says nothing about the chrome.
        /// Adding it anyway is what planned 889 px of "chrome" on the first 32:9 trader
        /// log, and why every open there flipped the panel between two widths.
        /// </summary>
        internal static float ShortOfPlan(int plannedColumns, float viewport)
        {
            if (plannedColumns <= 0) return 0f;

            return Math.Max(0f, WidthOfColumns(plannedColumns) - viewport);
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
        /// <param name="leftOverhang">
        /// How far the panel's own drawing reaches past its left edge -- the trader
        /// screen's filter strip hangs outside it. Kept clear like the panel itself.
        /// </param>
        internal static LayoutPlan Plan(
            Box canvas,
            Box panel,
            IList<Obstacle> obstacles,
            int gridColumns,
            float chrome,
            float leftOverhang = 0f)
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

            // What slides when the nearest has to: the nearest itself, then, if its own
            // margin is not enough, the panel standing behind it, and so on.
            var trains = nearest != null && nearest.Movable
                ? Trains(canvas, panel, nearest, blockers, obstacles)
                : new List<Train>();

            foreach (var o in blockers)
            {
                if (o == nearest || o.Box.XMax > panel.XMin + 1f) continue;
                if (trains.Count > 0 && trains[0].Riders.Contains(o)) continue;

                behind = Math.Max(behind, o.Box.XMax + Gap);
            }

            var why = string.Empty;

            for (var columns = gridColumns; columns > shown; columns--)
            {
                var grow = WidthOfColumns(columns) + extra - panel.Width;
                var right = Math.Min(grow, rightRoom);
                var left = grow - right;
                var newLeft = panel.XMin - left;
                var newRight = panel.XMax + right;
                var reach = newLeft - leftOverhang;

                if (reach < behind)
                {
                    why = "no room on the left";
                    continue;
                }

                var shift = 0f;
                Train train = null;

                if (left > 0f && nearest != null && reach < nearest.Box.XMax + Gap)
                {
                    shift = nearest.Box.XMax + Gap - reach;

                    if (!nearest.Movable)
                    {
                        why = string.Format("'{0}' is in the way on the left", nearest.Name);
                        continue;
                    }

                    // The shortest train that makes the room, so nothing moves that
                    // did not have to. Why the longest one failed is the reason given:
                    // a shorter one failing in the iteration that succeeds says nothing,
                    // and reporting it is what made the first pushing log read
                    // "'Containers Panel' has only 0 px" while the containers column slid.
                    var failed = string.Empty;

                    foreach (var t in trains)
                    {
                        if (reach < t.Behind)
                        {
                            failed = "no room on the left";
                            continue;
                        }

                        if (shift > t.Room)
                        {
                            failed = string.Format("{0} only {1:0} px to move into", t.Describe(), t.Room);
                            continue;
                        }

                        var covered = t.NewlyCovered(shift, obstacles);

                        if (covered != null)
                        {
                            failed = string.Format("sliding {0} would put {1}", t.Describe(), covered);
                            continue;
                        }

                        train = t;
                        break;
                    }

                    if (train == null)
                    {
                        why = failed;
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
                plan.Neighbour = train != null ? nearest : null;
                plan.Shift = train != null ? shift : 0f;
                plan.Pushed = train != null ? train.Members.GetRange(1, train.Members.Count - 1) : new List<Obstacle>();
                plan.Riders = train != null ? train.Riders : new List<Obstacle>();
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
        /// Something poking up into the bottom of the panel's span, low enough to clear
        /// by bringing the bottom edge up rather than stopping the growth. Usually a
        /// button, but not only: the scav screen's Next and Back sit in a
        /// <c>ButtonsPanel</c> that is not itself one, and counting that bar as a wall
        /// held the stash to 15 of 19 columns in the first scav log.
        /// </summary>
        private static bool Low(Obstacle o, float bottom)
        {
            return o.Box.YMax + LiftGap - bottom <= MaxLift;
        }

        /// <summary>Whether <paramref name="inner"/> lies within <paramref name="outer"/>, give or take 2 px.</summary>
        private static bool Inside(Box inner, Box outer)
        {
            return inner.XMin >= outer.XMin - 2f && inner.XMax <= outer.XMax + 2f
                && inner.YMin >= outer.YMin - 2f && inner.YMax <= outer.YMax + 2f;
        }

        /// <summary>Horizontal overlap within the vertical band, edges not counted.</summary>
        private static bool Overlaps(Box span, Box o)
        {
            return Math.Min(span.XMax, o.XMax) - Math.Max(span.XMin, o.XMin) > 1f
                && o.InBand(span.YMin, span.YMax);
        }

        /// <summary>
        /// Whether <paramref name="o"/> is drawn mostly over <paramref name="n"/>: more
        /// than half its own area. The scav screen's <c>Left Glow</c> sits over the top
        /// of the scav's gear column and pokes out above it, so it is not inside it, but
        /// it belongs to it and has to slide with it.
        /// </summary>
        private static bool MostlyOver(Box o, Box n)
        {
            var w = Math.Min(o.XMax, n.XMax) - Math.Max(o.XMin, n.XMin);
            var h = Math.Min(o.YMax, n.YMax) - Math.Max(o.YMin, n.YMin);
            var area = o.Width * o.Height;

            return w > 0f && h > 0f && area > 0f && w * h > area / 2f;
        }

        /// <summary>
        /// Panels that slide left together by one amount, keeping the spacing the game
        /// gave them: the stash's nearest neighbour first, then whatever it would run
        /// into, and so on. Riders are what is drawn over a member and goes with it.
        /// </summary>
        private sealed class Train
        {
            internal readonly List<Obstacle> Members = new List<Obstacle>();

            internal readonly List<Obstacle> Riders = new List<Obstacle>();

            /// <summary>How far the whole train can slide.</summary>
            internal float Room;

            /// <summary>What the stash panel's grown left edge must stay clear of, the train aside.</summary>
            internal float Behind;

            internal bool Moves(Obstacle o)
            {
                return Members.Contains(o) || Riders.Contains(o);
            }

            internal string Describe()
            {
                return Members.Count == 1
                    ? string.Format("'{0}' has", Members[0].Name)
                    : string.Format(
                        "'{0}' and the {1} behind it have", Members[0].Name,
                        Members.Count == 2 ? "panel" : (Members.Count - 1) + " panels");
            }

            /// <summary>
            /// Whether sliding the train puts anything a member overlaps today -- the
            /// Next button over the scav's pouch, say -- over a piece of it that was
            /// clear before. Returns what for the log, or null. Something that already
            /// sat over part of it (Next over the empty bottom of the containers column)
            /// may keep doing so; it just may not land on anything new.
            ///
            /// Only pieces worth keeping clear count (<see cref="Piece"/>): what the
            /// member draws within its own box, and not a sliver. The second scav run
            /// stopped at 16 columns because a full-height line at the containers
            /// column's right edge would have met the bottom of Next / Back's holder.
            /// </summary>
            internal string NewlyCovered(float shift, IList<Obstacle> obstacles)
            {
                foreach (var n in Members)
                {
                    foreach (var o in obstacles)
                    {
                        if (Moves(o) || !o.Box.Overlaps(n.Box)) continue;

                        for (var i = 0; i < n.Inner.Length; i++)
                        {
                            Box inner;

                            if (!Piece(n.Inner[i], n.Box, out inner)) continue;

                            if (!inner.Overlaps(o.Box) && inner.Shifted(-shift).Overlaps(o.Box))
                            {
                                return string.Format(
                                    "'{0}' over '{1}' ({2}) in it", o.Name,
                                    i < n.InnerNames.Length ? n.InnerNames[i] : "a piece", inner);
                            }
                        }
                    }
                }

                return null;
            }
        }

        /// <summary>Narrower than this, a drawn piece is a line or a scrollbar, not something a button may not cover.</summary>
        internal const float MinPieceWidth = 16f;

        /// <summary>Shorter than this, a drawn piece is a rule under a heading.</summary>
        internal const float MinPieceHeight = 4f;

        /// <summary>
        /// The part of a drawn piece worth keeping clear of buttons: clipped to the
        /// panel that draws it (anything outside is masked scroll content), and false
        /// for a sliver.
        /// </summary>
        internal static bool Piece(Box inner, Box owner, out Box piece)
        {
            piece = new Box(
                Math.Max(inner.XMin, owner.XMin), Math.Max(inner.YMin, owner.YMin),
                Math.Min(inner.XMax, owner.XMax), Math.Min(inner.YMax, owner.YMax));

            return piece.Width >= MinPieceWidth && piece.Height >= MinPieceHeight;
        }

        /// <summary>The most panels one slide may push, the stash's neighbour included.</summary>
        private const int MaxTrain = 4;

        /// <summary>
        /// Every train worth trying, shortest first. The first is the nearest alone
        /// with what rides on it -- all the trader and mail screens have ever needed.
        /// Each next one adds the movable panel that stopped the one before.
        ///
        /// The scav screen is why there is more than one: its gear column, containers
        /// column and stash stand 4 and 7 px apart in a 16:9 frame, so the containers
        /// column has no margin of its own, and every pixel the stash needs on the left
        /// has to come from pushing both columns into the ~350 px left of the frame.
        /// </summary>
        private static List<Train> Trains(
            Box canvas, Box panel, Obstacle nearest, List<Obstacle> blockers, IList<Obstacle> obstacles)
        {
            var trains = new List<Train>();
            var members = new List<Obstacle> { nearest };

            while (true)
            {
                var train = new Train();
                train.Members.AddRange(members);

                foreach (var o in blockers)
                {
                    if (members.Contains(o) || !o.CanRide) continue;

                    foreach (var m in members)
                    {
                        if (Inside(o.Box, m.Box) || MostlyOver(o.Box, m.Box))
                        {
                            train.Riders.Add(o);
                            break;
                        }
                    }
                }

                Obstacle limiter = null;
                train.Room = float.MaxValue;

                foreach (var m in members)
                {
                    Obstacle by;
                    var room = RoomToSlide(canvas, m, obstacles, train, out by);

                    if (room < train.Room)
                    {
                        train.Room = room;
                        limiter = by;
                    }
                }

                foreach (var r in train.Riders)
                {
                    Obstacle by;
                    train.Room = Math.Min(train.Room, RoomToSlide(canvas, r, obstacles, train, out by));
                }

                train.Behind = canvas.XMin + EdgeMargin;

                foreach (var o in blockers)
                {
                    if (train.Moves(o) || o.Box.XMax > panel.XMin + 1f) continue;

                    train.Behind = Math.Max(train.Behind, o.Box.XMax + Gap);
                }

                trains.Add(train);

                if (limiter == null || !limiter.Movable || train.Moves(limiter) || members.Count >= MaxTrain) break;

                members.Add(limiter);
            }

            return trains;
        }

        /// <summary>
        /// How far a panel can slide left, keeping its size: to the canvas margin or
        /// short of the first thing in its own band that it does not overlap today and
        /// that does not slide with it. Buttons count in full here -- a sliding panel
        /// cannot duck under one.
        /// </summary>
        /// <param name="by">What stops it, or null for the canvas margin.</param>
        private static float RoomToSlide(Box canvas, Obstacle n, IList<Obstacle> obstacles, Train train, out Obstacle by)
        {
            var limit = canvas.XMin + EdgeMargin;
            by = null;

            foreach (var o in obstacles)
            {
                if (o == n || train.Moves(o) || o.Box.Overlaps(n.Box)) continue;
                if (!o.Box.InBand(n.Box.YMin, n.Box.YMax)) continue;
                if (o.Box.XMax > n.Box.XMin + 1f) continue;

                if (o.Box.XMax + Gap > limit)
                {
                    limit = o.Box.XMax + Gap;
                    by = o;
                }
            }

            return Math.Max(0f, n.Box.XMin - limit);
        }
    }
}
