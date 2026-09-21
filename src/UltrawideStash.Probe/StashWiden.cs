using System;
using System.Text;
using UnityEngine;

namespace UltrawideStash.Probe
{
    /// <summary>
    /// Takes the horizontal slack out of the gear side of the inventory screen and
    /// gives it to the stash panel.
    ///
    /// ## Why there is slack to take
    ///
    /// The stash's neighbour is a single child of Items Panel called
    /// <c>LeftSide</c>, anchored to stretch, holding a <c>HorizontalLayoutGroup</c>
    /// over two panels: <c>Left Panel</c> (the character and gear doll) and
    /// <c>Containers Panel</c> (rig, pockets, belt, backpack). The group divides
    /// whatever width it is given between them.
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
    /// Narrows LeftSide back towards its 16:9 width and widens Stash Panel by exactly
    /// what LeftSide gave up. The layout group re-flows its two panels without being
    /// told to, which is why this is a change to two RectTransforms and not a rebuild
    /// of the screen.
    ///
    /// The reserve is per panel, not total, because the binding constraint is the
    /// wider of the two: <c>Gear Panel</c> is a fixed 494 px and does not shrink, so
    /// a reserve below that clips the character doll rather than the stash.
    /// </summary>
    internal static class StashWiden
    {
        /// <summary>
        /// The name of the stash's neighbour, as read off the live hierarchy.
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
        /// Clear air left between the gear side and the widened stash panel.
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
        /// Widen the stash panel in place.
        /// </summary>
        /// <param name="cap">The stash panel -- the pinned ancestor that clips.</param>
        /// <param name="chrome">Panel width the grid never gets.</param>
        /// <param name="reservePerPanel">What each gear panel keeps.</param>
        /// <param name="columns">Columns the widened panel can show.</param>
        /// <returns>A line for the log, whether or not anything moved.</returns>
        internal static string Apply(
            RectTransform cap,
            float chrome,
            float reservePerPanel,
            out int columns)
        {
            columns = 0;

            if (cap == null) return "cannot widen: no stash panel.";

            var parent = cap.parent as RectTransform;

            if (parent == null) return "cannot widen: the stash panel has no parent.";

            var leftSide = FindChild(parent, LeftSideName);

            if (leftSide == null)
            {
                return string.Format(
                    "cannot widen: no '{0}' beside the stash panel. The screen's layout has "
                    + "changed and widening it blind would be worse than leaving it alone.",
                    LeftSideName);
            }

            var stretches =
                Mathf.Abs(leftSide.anchorMax.x - leftSide.anchorMin.x) > 0.001f;

            if (!stretches)
            {
                return string.Format(
                    "cannot widen: '{0}' is pinned, so narrowing it would not give the "
                    + "stash the room back.", LeftSideName);
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
                    "nothing to widen: '{0}' is {1:0.0} px against a {2:0.0} px reserve, "
                    + "which is less than one column of slack. Stash stays at {3} columns.",
                    LeftSideName, leftSide.rect.width, target, columns);
            }

            // The gap has to be taken out of the slack, not added to the panel.
            //
            // LeftSide and Stash Panel are neighbours, so if the panel grows by
            // exactly what LeftSide gives up, the 10 px between them stays 10 px
            // however much moves -- the earlier version added the gap to the panel's
            // width and changed nothing except how much dead space ended up inside
            // it. Real clearance means the panel growing by *less* than LeftSide
            // shrinks, and the difference is the gap.
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
                    "nothing to widen: '{0}' has {1:0.0} px of slack, which after a "
                    + "{2:0.0} px gap is less than one more column. Stash stays at {3} "
                    + "columns.",
                    LeftSideName, slack, GapPixels, columns);
            }

            var shrink = grow + GapPixels;

            var before = cap.rect.width;

            try
            {
                // LeftSide stretches, so its width is the parent's less the two
                // offsets. Pushing the right offset in is what narrows it, and the
                // layout group redistributes its children on the next layout pass.
                var offsetMax = leftSide.offsetMax;
                leftSide.offsetMax = new Vector2(offsetMax.x - shrink, offsetMax.y);

                // Stash Panel is pinned to the right edge, so growing its width grows
                // it leftwards. It grows by less than LeftSide gave up; the remainder
                // is the gap between them.
                var size = cap.sizeDelta;
                cap.sizeDelta = new Vector2(size.x + grow, size.y);
            }
            catch (Exception e)
            {
                return "could not widen the stash panel -- " + e.Message;
            }

            columns = StashMeasure.ColumnsThatFit(cap.rect.width - chrome);

            var sb = new StringBuilder();

            sb.AppendLine(string.Format(
                "widened: '{0}' {1:0.0} -> {2:0.0} px, stash panel {3:0.0} -> {4:0.0} px, "
                + "gap between them {5:0.0} px.",
                LeftSideName,
                leftSide.rect.width + shrink,
                leftSide.rect.width,
                before,
                cap.rect.width,
                GapPixels + BaseGapPixels));

            sb.Append(string.Format(
                "the stash panel can now show {0} columns. The grid is still the width the "
                + "server gave it -- set columns to {0} and restart the server to fill it.",
                columns));

            return sb.ToString();
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
