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
    /// Narrows LeftSide back towards its 16:9 width and widens Stash Panel by what
    /// LeftSide gave up, less a gap -- and no further than the grid needs, so a grid
    /// narrower than the room available leaves the rest with the gear side instead of
    /// as dead space in the panel. The layout group re-flows its two panels without
    /// being told to, which is why this is a change to two RectTransforms and not a
    /// rebuild of the screen. The arithmetic is <see cref="StashPlan"/>.
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
        internal const float MinReservePerPanel = StashPlan.MinReservePerPanel;

        /// <summary>
        /// Clear air left between the gear side and the widened stash panel.
        ///
        /// The screen ships with 10 px between LeftSide and Stash Panel, which reads
        /// as a seam rather than a gap once the stash has been pulled left until it
        /// nearly touches the special slots. This is on top of that 10.
        /// </summary>
        internal const float GapPixels = StashPlan.GapPixels;

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
        internal const float ScrollSlackPixels = StashPlan.ScrollSlackPixels;

        /// <summary>
        /// The plan behind the most recent <see cref="Apply"/>, for the report and the
        /// measurement file. Null until the character screen's stash has been shown.
        /// </summary>
        internal static StashPlan? Last;

        /// <summary>
        /// Widen the stash panel in place, as far as the grid needs and the gear side
        /// can spare. See <see cref="StashPlan"/> for the arithmetic.
        ///
        /// Idempotent: the plan is always made from the untouched geometry (remembered
        /// from the first widening, or read off the screen before one) and applied as
        /// offsets from it, so a second open, a resolution change or a different grid
        /// width all land where a first open would.
        /// </summary>
        /// <param name="cap">The stash panel -- the pinned ancestor that clips.</param>
        /// <param name="chrome">Panel width the grid never gets.</param>
        /// <param name="reservePerPanel">What each gear panel keeps.</param>
        /// <param name="gridColumns">The stash grid's width, or 0 if not known.</param>
        /// <returns>A line for the log, whether or not anything moved.</returns>
        internal static string Apply(
            RectTransform cap,
            float chrome,
            float reservePerPanel,
            int gridColumns)
        {
            if (cap == null) return Refuse(null, chrome, "no stash panel was found");

            var parent = cap.parent as RectTransform;

            if (parent == null) return Refuse(cap, chrome, "the stash panel has no parent");

            var leftSide = FindChild(parent, LeftSideName);

            if (leftSide == null)
            {
                return Refuse(cap, chrome, string.Format(
                    "the inventory screen has no '{0}' beside the stash panel, so its layout "
                    + "has changed and widening it blind would be worse than leaving it alone",
                    LeftSideName));
            }

            var stretches =
                Mathf.Abs(leftSide.anchorMax.x - leftSide.anchorMin.x) > 0.001f;

            if (!stretches)
            {
                return Refuse(cap, chrome, string.Format(
                    "'{0}' is pinned, so narrowing it would not give the stash any room",
                    LeftSideName));
            }

            // The untouched geometry. Offsets are absolute and survive a resolution
            // change; widths follow the canvas, so the vanilla widths are the current
            // ones with this mod's own change taken back off.
            var ours = _cap == cap && _leftSide == leftSide;
            var baseOffsetMax = ours ? _leftSideOffsetMax : leftSide.offsetMax;
            var baseSizeDelta = ours ? _capSizeDelta : cap.sizeDelta;

            var vanillaPanel = cap.rect.width - (cap.sizeDelta.x - baseSizeDelta.x);
            var vanillaLeft = leftSide.rect.width + (baseOffsetMax.x - leftSide.offsetMax.x);

            var plan = StashPlan.For(vanillaLeft, vanillaPanel, chrome, reservePerPanel, gridColumns);

            Last = plan;

            try
            {
                if (plan.Widens)
                {
                    Remember(leftSide, cap);

                    // LeftSide stretches, so pushing its right offset in narrows it and
                    // the layout group re-flows its two panels on the next pass. Stash
                    // Panel is pinned right, so growing it grows it leftwards -- by less
                    // than LeftSide gave up, and the difference is the gap.
                    leftSide.offsetMax = new Vector2(baseOffsetMax.x - plan.Shrink, leftSide.offsetMax.y);
                    cap.sizeDelta = new Vector2(baseSizeDelta.x + plan.Grow, cap.sizeDelta.y);
                }
                else if (ours)
                {
                    // Widened earlier and nothing to widen now: put it back rather than
                    // leave a panel sized for a screen or a grid that has gone.
                    leftSide.offsetMax = baseOffsetMax;
                    cap.sizeDelta = baseSizeDelta;
                    _cap = null;
                    _leftSide = null;
                }
            }
            catch (Exception e)
            {
                return "could not widen the stash panel -- " + e.Message;
            }

            if (!plan.CanWiden)
            {
                return string.Format(
                    "nothing to widen: {0}. Stash stays at {1} columns.", plan.WhyNot, plan.Shown);
            }

            if (!plan.Widens)
            {
                return string.Format(
                    "not widened: the grid is {0} columns and the vanilla panel already shows "
                    + "{1}. There is room for {2}; with \"columns\": \"auto\" the server uses "
                    + "them from its next start.",
                    gridColumns, plan.Shown, plan.Potential);
            }

            var sb = new StringBuilder();

            sb.AppendLine(string.Format(
                "widened: '{0}' {1:0.0} -> {2:0.0} px, stash panel {3:0.0} -> {4:0.0} px, "
                + "gap between them {5:0.0} px.",
                LeftSideName,
                vanillaLeft,
                leftSide.rect.width,
                vanillaPanel,
                cap.rect.width,
                GapPixels + BaseGapPixels));

            if (plan.Columns < plan.Potential)
            {
                sb.Append(string.Format(
                    "the panel shows {0} columns, the width of the grid. There is room for {1}; "
                    + "with \"columns\": \"auto\" the server uses them from its next start.",
                    plan.Columns, plan.Potential));
            }
            else if (gridColumns > plan.Potential)
            {
                sb.Append(string.Format(
                    "the panel shows {0} columns, but the grid is {1} -- wider than this screen "
                    + "can show, so it will scroll sideways. Either the grid was sized for a "
                    + "wider screen (restart the server and \"auto\" will size it for this one), "
                    + "or it was set by hand (set \"columns\" to \"auto\" and ignoreMeasurement "
                    + "to false in ultrawidestash.config.json, then restart the server).",
                    plan.Potential, gridColumns));
            }
            else
            {
                sb.Append(string.Format("the panel shows {0} columns.", plan.Columns));
            }

            return sb.ToString();
        }

        /// <summary>Record a panel that cannot be widened, and say why.</summary>
        private static string Refuse(RectTransform cap, float chrome, string why)
        {
            var shown = cap != null ? StashPlan.ColumnsThatFit(cap.rect.width - chrome) : 0;

            Last = StashPlan.Refused(shown, why);

            return "cannot widen: " + why + ".";
        }

        /// <summary>
        /// The two transforms <see cref="Apply"/> changed, and what they were before.
        ///
        /// Kept because the inventory screen is one object for the whole session: the
        /// screen widened in the hideout is the same one opened in raid. Unity keeps
        /// no copy of a RectTransform's previous values, so this is the only record of
        /// what vanilla looked like.
        /// </summary>
        private static RectTransform _leftSide;

        private static RectTransform _cap;

        private static Vector2 _leftSideOffsetMax;

        private static Vector2 _capSizeDelta;

        /// <summary>
        /// Record the untouched geometry, once per widening.
        ///
        /// Only when nothing is held for this panel yet: a resolution change can widen
        /// an already-widened panel further, and recording then would overwrite the
        /// vanilla values with widened ones and make them unrestorable.
        /// </summary>
        private static void Remember(RectTransform leftSide, RectTransform cap)
        {
            if (_cap == cap && _leftSide == leftSide) return;

            _leftSide = leftSide;
            _cap = cap;
            _leftSideOffsetMax = leftSide.offsetMax;
            _capSizeDelta = cap.sizeDelta;
        }

        /// <summary>
        /// Put the screen back to the width the game built it at.
        ///
        /// Called whenever the inventory opens in raid. The next stash open in the
        /// menu widens it again, because <see cref="Apply"/> finds the slack back.
        /// </summary>
        /// <returns>A line for the log, or null when there was nothing to undo.</returns>
        internal static string Restore()
        {
            var cap = _cap;
            var leftSide = _leftSide;

            _cap = null;
            _leftSide = null;

            // Nothing widened, or destroyed with the screen -- and the screen that
            // replaces it is built vanilla.
            if (!cap || !leftSide) return null;

            try
            {
                leftSide.offsetMax = _leftSideOffsetMax;
                cap.sizeDelta = _capSizeDelta;
            }
            catch (Exception e)
            {
                return "could not put the stash panel back for the raid -- " + e.Message;
            }

            return string.Format(
                "in raid: inventory screen put back to vanilla, stash panel {0:0.0} px. "
                + "It widens again the next time the stash opens in the menu.",
                cap.rect.width);
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
