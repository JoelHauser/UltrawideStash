using System;
using System.Text;
using UnityEngine;

namespace UltrawideStash.Probe
{
    /// <summary>
    /// Takes the horizontal slack out of the left half of a stash screen and gives it
    /// to the stash panel.
    ///
    /// ## Why there is slack to take
    ///
    /// On the character screen the stash's neighbour is a single child of Items Panel
    /// called <c>LeftSide</c>, anchored to stretch, holding a
    /// <c>HorizontalLayoutGroup</c> over two panels: <c>Left Panel</c> (the character
    /// and gear doll) and <c>Containers Panel</c> (rig, pockets, belt, backpack). The
    /// group divides whatever width it is given between them.
    ///
    /// At 16:9 that width is about 1206 px and each panel gets roughly 600, which is
    /// the layout the game ships and everything fits. On a 3440x1440 canvas of
    /// 2580 px, LeftSide stretches to 1866 and each panel gets 930 -- the same
    /// contents with 300-odd px of air around them. That air is the empty space an
    /// ultrawide player is looking at, and the stash cannot reach it, because
    /// <c>Stash Panel</c> is pinned at 680 px against the right edge.
    ///
    /// ## What this does
    ///
    /// Narrows the left half back towards its 16:9 width and widens the stash panel by
    /// slightly less than what it gave up. The layout group re-flows its children
    /// without being told to, which is why this is a change to two RectTransforms and
    /// not a rebuild of the screen.
    ///
    /// The reserve is per panel, not total, because on the character screen the
    /// binding constraint is the wider of the two: <c>Gear Panel</c> is a fixed 494 px
    /// and does not shrink, so a reserve below that clips the character doll rather
    /// than the stash.
    ///
    /// ## More than one screen
    ///
    /// Six screens draw a <c>SimpleStashPanel</c> and the grid is <c>columns</c> wide
    /// on every one of them, so a widened grid needs a widened panel on every screen it
    /// is drawn on or it overflows into a horizontal scrollbar. Which screens are in
    /// scope is <see cref="StashScreens"/>'s decision, not this file's; what this file
    /// adds is that the neighbour no longer has to be called <c>LeftSide</c>. It is
    /// found by geometry when the name is not there, because only the character
    /// screen's layout was ever read off a live hierarchy and the names on the others
    /// are not knowable from here.
    /// </summary>
    internal static class StashWiden
    {
        /// <summary>
        /// The name of the stash's neighbour on the character screen, as read off the
        /// live hierarchy. Tried first; <see cref="FindNeighbour"/> falls back to
        /// geometry.
        /// </summary>
        private const string LeftSideName = "LeftSide";

        /// <summary>
        /// What each of the two gear panels keeps, in canvas px.
        ///
        /// 620 rather than a tighter number because the game's own 16:9 layout gives
        /// them about 600 each and is known to fit; this keeps that and adds a little.
        /// Going below <c>Gear Panel</c>'s fixed 494 px would clip the character doll.
        /// </summary>
        internal const float DefaultReservePerPanel = 620f;

        /// <summary>The narrowest reserve that still clears Gear Panel's 494 px.</summary>
        internal const float MinReservePerPanel = 520f;

        /// <summary>
        /// Clear air left between the left half and the widened stash panel.
        ///
        /// The screen ships with 10 px between LeftSide and Stash Panel, which reads
        /// as a seam rather than a gap once the stash has been pulled left until it
        /// nearly touches the special slots. This is on top of that 10.
        /// </summary>
        internal const float GapPixels = 24f;

        /// <summary>
        /// The gap the screen already ships with between LeftSide and Stash Panel,
        /// for reporting only -- <see cref="GapPixels"/> is added on top of it.
        /// </summary>
        internal const float BaseGapPixels = 10f;

        /// <summary>
        /// Slack kept between the grid and the viewport, so a grid sized to the exact
        /// width of its ScrollRect cannot tip it into scrolling.
        ///
        /// Vanilla ships a 632 px viewport around a 631 px grid, so one pixel is the
        /// game's own answer; four is that with room for rounding. The cost is four
        /// pixels nobody can see, against a horizontal scrollbar that defeats the
        /// entire point of the mod.
        /// </summary>
        internal const float ScrollSlackPixels = 4f;

        /// <summary>
        /// The narrowest a discovered neighbour is allowed to be before it is not
        /// believed to be the screen's left half.
        ///
        /// The character screen's LeftSide is 1206 px at 16:9, so anything much
        /// narrower than that is a toolbar, a divider or a filter strip rather than
        /// the half of the screen the stash is competing with. This only rules
        /// candidates out; the width actually kept is the reserve.
        /// </summary>
        private const float MinNeighbourWidth = 400f;

        /// <summary>
        /// How far a candidate's right edge may overhang the stash panel's left edge
        /// and still count as being beside it rather than behind it.
        ///
        /// A full-screen backdrop stretches straight across the panel and must never
        /// be mistaken for the left half -- narrowing one would move artwork and leave
        /// the layout exactly as it was. A few pixels of tolerance covers a seam.
        /// </summary>
        private const float OverhangTolerance = 4f;

        /// <summary>
        /// Widen the stash panel in place.
        /// </summary>
        /// <param name="cap">The stash panel -- the pinned ancestor that clips.</param>
        /// <param name="chrome">Panel width the grid never gets.</param>
        /// <param name="reservePerPanel">What each half of the left side keeps.</param>
        /// <param name="screen">Which screen this is, for the log.</param>
        /// <param name="columns">Columns the widened panel can show.</param>
        /// <returns>A line for the log, whether or not anything moved.</returns>
        internal static string Apply(
            RectTransform cap,
            float chrome,
            float reservePerPanel,
            ScreenPolicy screen,
            out int columns)
        {
            columns = 0;

            if (cap == null) return "cannot widen: no stash panel.";

            var parent = cap.parent as RectTransform;

            if (parent == null) return "cannot widen: the stash panel has no parent.";

            string how;
            var leftSide = FindNeighbour(parent, cap, out how);

            if (leftSide == null)
            {
                return string.Format(
                    "cannot widen the {0}: {1}. Widening it blind would be worse than "
                    + "leaving it alone.",
                    screen.Label, how);
            }

            if (reservePerPanel < MinReservePerPanel)
            {
                reservePerPanel = MinReservePerPanel;
            }

            var target = reservePerPanel * 2f;
            var slack = leftSide.rect.width - target;

            columns = StashMeasure.ColumnsThatFit(cap.rect.width - chrome);

            // Already narrow, or already done. Opening the stash twice must not widen
            // it twice, and this is the check that makes the whole thing idempotent.
            if (slack < StashMeasure.CellPixels)
            {
                return string.Format(
                    "nothing to widen on the {0}: '{1}' is {2:0.0} px against a {3:0.0} px "
                    + "reserve, which is less than one column of slack. Stash panel stays "
                    + "at {4} columns.",
                    screen.Label, leftSide.name, leftSide.rect.width, target, columns);
            }

            // The gap has to be taken out of the slack, not added to the panel.
            //
            // The two are neighbours, so if the panel grows by exactly what the left
            // side gives up, the 10 px between them stays 10 px however much moves --
            // an earlier version added the gap to the panel's width and changed
            // nothing except how much dead space ended up inside it. Real clearance
            // means the panel growing by *less* than the left side shrinks, and the
            // difference is the gap.
            var usable = cap.rect.width + slack - GapPixels;

            // Take only what turns into columns. A grid is a whole number of 63 px
            // cells, so a panel sized to the last available pixel ends in a strip too
            // narrow to hold a column: width taken off the gear side, then spent on
            // nothing, against the side that has slots up against its edge.
            var fits = StashMeasure.ColumnsThatFit(usable - chrome - ScrollSlackPixels);
            var wanted = StashMeasure.WidthOfColumns(fits) + chrome + ScrollSlackPixels;

            var grow = wanted - cap.rect.width;

            if (grow < StashMeasure.CellPixels)
            {
                return string.Format(
                    "nothing to widen on the {0}: '{1}' has {2:0.0} px of slack, which after "
                    + "a {3:0.0} px gap is less than one more column. Stash panel stays at "
                    + "{4} columns.",
                    screen.Label, leftSide.name, slack, GapPixels, columns);
            }

            var shrink = grow + GapPixels;

            var before = cap.rect.width;

            try
            {
                // The left side stretches, so its width is the parent's less the two
                // offsets. Pushing the right offset in is what narrows it, and the
                // layout group redistributes its children on the next layout pass.
                var offsetMax = leftSide.offsetMax;
                leftSide.offsetMax = new Vector2(offsetMax.x - shrink, offsetMax.y);

                // The panel grows leftwards, by less than the left side gave up; the
                // remainder is the gap between them.
                //
                // Written through offsetMin rather than sizeDelta. The cap is always
                // horizontally pinned -- that is what made it the cap -- so its two
                // offsets are both measured from the same anchor and moving offsetMin
                // moves the left edge and nothing else, whatever the pivot is. On the
                // character screen, whose panel has its pivot at the right edge, that
                // is the same arithmetic the verified 1.0.0 sizeDelta write performed.
                // On a screen whose pivot is anywhere else it is the difference between
                // growing leftwards and growing half off the side of the monitor.
                var offsetMin = cap.offsetMin;
                cap.offsetMin = new Vector2(offsetMin.x - grow, offsetMin.y);
            }
            catch (Exception e)
            {
                return "could not widen the stash panel -- " + e.Message;
            }

            columns = StashMeasure.ColumnsThatFit(cap.rect.width - chrome);

            var sb = new StringBuilder();

            sb.AppendLine(string.Format(
                "widened the {0}: '{1}' {2:0.0} -> {3:0.0} px ({4}), stash panel "
                + "{5:0.0} -> {6:0.0} px, gap between them {7:0.0} px.",
                screen.Label,
                leftSide.name,
                leftSide.rect.width + shrink,
                leftSide.rect.width,
                how,
                before,
                cap.rect.width,
                GapPixels + BaseGapPixels));

            sb.Append(string.Format(
                "the stash panel can now show {0} columns on the {1}.",
                columns, screen.Label));

            if (screen.OwnsMeasurement)
            {
                sb.Append(string.Format(
                    " The grid is still the width the server gave it -- set columns to {0} "
                    + "and restart the server to fill it.",
                    columns));
            }

            return sb.ToString();
        }

        /// <summary>
        /// The stash panel's left-hand neighbour: the node whose width can be taken.
        ///
        /// ## The name first, then the geometry
        ///
        /// <c>LeftSide</c> is tried by name because that is the node the character
        /// screen's widening was measured and verified against, and matching it
        /// exactly means this change cannot move that screen by so much as a pixel.
        ///
        /// Everywhere else the name is unknown -- it is serialized prefab data, which
        /// is the entire reason <see cref="StashMeasure"/> exists -- so the neighbour
        /// is identified by what it has to be rather than what it is called: a sibling
        /// of the stash panel, anchored to stretch (so narrowing it actually hands the
        /// width over), lying wholly to the left of the panel, and wide enough to be
        /// the screen's other half. The widest candidate wins.
        ///
        /// The left-of test is what keeps a full-screen backdrop out. A backdrop
        /// stretches and is the widest thing on the screen, so width and anchors alone
        /// would pick it every time, and narrowing it would move artwork while leaving
        /// the layout exactly where it was.
        /// </summary>
        private static RectTransform FindNeighbour(
            RectTransform parent,
            RectTransform cap,
            out string how)
        {
            var named = FindChild(parent, LeftSideName);

            if (named != null && Stretches(named))
            {
                how = "by name";
                return named;
            }

            float capLeft, capRight;

            if (!StashMeasure.EdgesIn(cap, parent, out capLeft, out capRight))
            {
                how = "the stash panel would not report its own edges";
                return null;
            }

            RectTransform best = null;
            var bestWidth = 0f;
            var candidates = 0;

            for (var i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i) as RectTransform;

                if (child == null || ReferenceEquals(child, cap)) continue;
                if (!child.gameObject.activeInHierarchy) continue;
                if (!Stretches(child)) continue;
                if (child.rect.width < MinNeighbourWidth) continue;

                float left, right;

                if (!StashMeasure.EdgesIn(child, parent, out left, out right)) continue;

                // Wholly to the left of the panel, give or take a seam.
                if (right > capLeft + OverhangTolerance) continue;

                candidates++;

                if (child.rect.width > bestWidth)
                {
                    bestWidth = child.rect.width;
                    best = child;
                }
            }

            if (best == null)
            {
                how = string.Format(
                    "nothing beside the stash panel is both stretching and wholly to its "
                    + "left, out of {0} sibling(s)",
                    parent.childCount - 1);

                return null;
            }

            how = candidates > 1
                ? string.Format("widest of {0} stretching siblings to its left", candidates)
                : "the one stretching sibling to its left";

            return best;
        }

        /// <summary>
        /// Whether a node's width grows with its parent's. A pinned node has nothing
        /// to give: narrowing it would leave the width unclaimed rather than handing
        /// it to the stash.
        /// </summary>
        private static bool Stretches(RectTransform node)
        {
            return Mathf.Abs(node.anchorMax.x - node.anchorMin.x) > 0.001f;
        }

        /// <summary>
        /// A direct child by name. Not <c>Find</c>, because that matches a path and
        /// would quietly return null on a name with a space in it.
        /// </summary>
        private static RectTransform FindChild(RectTransform parent, string name)
        {
            for (var i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i) as RectTransform;

                if (child != null
                    && string.Equals(child.name, name, StringComparison.Ordinal))
                {
                    return child;
                }
            }

            return null;
        }
    }
}
