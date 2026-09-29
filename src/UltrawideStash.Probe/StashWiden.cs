using System;
using System.Text;
using UnityEngine;

namespace UltrawideStash.Probe
{
    /// <summary>
    /// Takes the horizontal slack out of the gear side of the inventory screen and
    /// gives it to the stash panel.
    ///
    /// ## The screen EFT ships is not the screen this was first built on
    ///
    /// In vanilla EFT the inventory screen (<c>InventoryScreen</c>, a nested canvas)
    /// is a fixed 1920 px frame centred on the canvas, whatever the monitor: on an
    /// ultrawide the whole screen is 16:9 with empty bars either side, and there is
    /// no slack anywhere in it. Its <c>LeftSide</c> is anchored at 0.48-0.79 of that
    /// frame. Read off a clean 5120x1440 install (Forge issue #2, 2026-09-29).
    ///
    /// Every version up to 1.0.4 was developed and tested on an install running
    /// <b>UIScale.Reloaded</b>, whose <c>InventoryStretchPatch</c> stretches that
    /// screen to the full canvas, anchors <c>LeftSide</c> 12 px from the left and
    /// 702 px from the right, and pins <c>Stash Panel</c> 680 px wide, 12 px from the
    /// right. So the mod worked there and nowhere else. It now makes that stretch
    /// itself when it finds the vanilla frame (<see cref="Stretch"/>), with the same
    /// edges, so every install gets the layout the arithmetic was calibrated on.
    /// With UIScale installed there is nothing to stretch and this leaves it alone.
    ///
    /// ## Why there is slack to take, once stretched
    ///
    /// <c>LeftSide</c> holds a <c>HorizontalLayoutGroup</c> over two panels:
    /// <c>Left Panel</c> (the character and gear doll) and <c>Containers Panel</c>
    /// (rig, pockets, belt, backpack). The group divides whatever width it is given
    /// between them. On a 3440x1440 canvas of 2580 px LeftSide is 1866 and each panel
    /// gets 930 -- the same contents with 300-odd px of air around them.
    ///
    /// ## What this does
    ///
    /// Narrows LeftSide back towards its 16:9 width and widens Stash Panel by what
    /// LeftSide gave up, less a gap -- and no further than the grid needs, so a grid
    /// narrower than the room available leaves the rest with the gear side instead of
    /// as dead space in the panel. The layout group re-flows its two panels without
    /// being told to. The arithmetic is <see cref="StashPlan"/>.
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

        /// <summary>The component on the inventory screen's root, found by name.</summary>
        private const string ScreenComponentName = "InventoryScreen";

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
        /// Clear air left between the gear side and the widened stash panel, on top of
        /// the seam the stretched layout keeps. See <see cref="StashPlan.GapPixels"/>.
        /// </summary>
        internal const float GapPixels = StashPlan.GapPixels;

        /// <summary>
        /// The seam between LeftSide and Stash Panel in the stretched layout, for
        /// reporting -- <see cref="GapPixels"/> is added on top of it.
        /// </summary>
        internal const float BaseGapPixels = StashPlan.StretchedSeam;

        /// <summary>
        /// Slack kept between the grid and the viewport, so a grid sized to the exact
        /// width of its ScrollRect cannot tip it into scrolling.
        /// </summary>
        internal const float ScrollSlackPixels = StashPlan.ScrollSlackPixels;

        /// <summary>
        /// The plan behind the most recent <see cref="Apply"/>, for the report and the
        /// measurement file. Null until the character screen's stash has been shown.
        /// </summary>
        internal static StashPlan? Last;

        /// <summary>
        /// Stretch the screen if it is the vanilla frame, then widen the stash panel as
        /// far as the grid needs and the gear side can spare.
        ///
        /// Idempotent: the screen is put back to what it was before this mod first
        /// touched it, and planned from there, on every call. So a second open, a
        /// resolution change, a different grid width or another mod rewriting the
        /// layout in between all land where a first open would.
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

            var root = FindScreenRoot(parent);

            string stretched;

            try
            {
                // Back to the untouched screen first, whatever an earlier call (or
                // another mod) left on it, and plan from that.
                if (_cap == cap && _leftSide == leftSide)
                {
                    PutBack();
                }
                else
                {
                    Remember(root, leftSide, cap);
                }

                stretched = Stretch(root, leftSide, cap);
            }
            catch (Exception e)
            {
                return "could not prepare the inventory screen -- " + e.Message;
            }

            var stretches =
                Mathf.Abs(leftSide.anchorMax.x - leftSide.anchorMin.x) > 0.001f;

            if (!stretches)
            {
                return Refuse(cap, chrome, string.Format(
                    "'{0}' is pinned, so narrowing it would not give the stash any room",
                    LeftSideName));
            }

            var vanillaLeft = leftSide.rect.width;
            var vanillaPanel = cap.rect.width;

            var plan = StashPlan.For(vanillaLeft, vanillaPanel, chrome, reservePerPanel, gridColumns);

            Last = plan;

            try
            {
                if (plan.Widens)
                {
                    // LeftSide stretches, so pushing its right offset in narrows it and
                    // the layout group re-flows its two panels on the next pass. Stash
                    // Panel is pinned right, so growing it grows it leftwards -- by less
                    // than LeftSide gave up, and the difference is the gap.
                    leftSide.offsetMax = new Vector2(leftSide.offsetMax.x - plan.Shrink, leftSide.offsetMax.y);
                    cap.sizeDelta = new Vector2(cap.sizeDelta.x + plan.Grow, cap.sizeDelta.y);
                }
                else if (stretched == null)
                {
                    // Nothing changed at all: nothing to put back in raid either.
                    Forget();
                }

                Applied(chrome, reservePerPanel, gridColumns);
            }
            catch (Exception e)
            {
                return "could not widen the stash panel -- " + e.Message;
            }

            var sb = new StringBuilder();

            if (stretched != null) sb.AppendLine(stretched);

            if (!plan.CanWiden)
            {
                sb.Append(string.Format(
                    "nothing to widen: {0}. Stash stays at {1} columns.", plan.WhyNot, plan.Shown));

                return sb.ToString();
            }

            if (!plan.Widens)
            {
                sb.Append(string.Format(
                    "not widened: the grid is {0} columns and the panel already shows {1}. "
                    + "There is room for {2}; with \"columns\": \"auto\" the server uses them "
                    + "from its next start.",
                    gridColumns, plan.Shown, plan.Potential));

                return sb.ToString();
            }

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

        /// <summary>
        /// Stretch the vanilla 16:9 frame to the full canvas, the way UIScale.Reloaded
        /// does, or leave the screen alone when it already fills it.
        ///
        /// Only the horizontal is touched. The screen's root gets x anchors 0..1 and no
        /// x offsets; LeftSide gets x anchors 0..1, a 12 px left margin, and a right
        /// edge a 10 px seam short of the stash panel. The stash panel is already
        /// anchored right in vanilla, so it rides out to the canvas edge by itself.
        /// </summary>
        /// <returns>A line for the log when it stretched, else null.</returns>
        private static string Stretch(RectTransform root, RectTransform leftSide, RectTransform cap)
        {
            if (root == null) return null;

            var host = root.parent as RectTransform;

            if (host == null) return null;

            var before = root.rect.width;

            // Already as wide as what holds it: UIScale.Reloaded, or a 16:9 screen
            // where the frame and the canvas are the same 1920.
            if (before >= host.rect.width - 1f) return null;

            // The stash panel must be pinned to the right edge for the stretch to
            // carry it there. It is in vanilla (anchor x 1..1); anything else is a
            // layout this has not seen, and stretching it would be a guess.
            if (Mathf.Abs(cap.anchorMin.x - 1f) > 0.001f || Mathf.Abs(cap.anchorMax.x - 1f) > 0.001f)
            {
                return null;
            }

            var panelRightMargin = -cap.offsetMax.x;

            root.anchorMin = new Vector2(0f, root.anchorMin.y);
            root.anchorMax = new Vector2(1f, root.anchorMax.y);
            root.offsetMin = new Vector2(0f, root.offsetMin.y);
            root.offsetMax = new Vector2(0f, root.offsetMax.y);

            leftSide.anchorMin = new Vector2(0f, leftSide.anchorMin.y);
            leftSide.anchorMax = new Vector2(1f, leftSide.anchorMax.y);
            leftSide.offsetMin = new Vector2(StashPlan.StretchedLeftMargin, leftSide.offsetMin.y);
            leftSide.offsetMax = new Vector2(
                StashPlan.StretchedLeftSideRightOffset(cap.rect.width, panelRightMargin),
                leftSide.offsetMax.y);

            return string.Format(
                "stretched: the inventory screen was a {0:0.0} px frame in a {1:0.0} px "
                + "canvas; it now fills it, as UIScale.Reloaded would make it.",
                before, root.rect.width);
        }

        /// <summary>Record a panel that cannot be widened, and say why.</summary>
        private static string Refuse(RectTransform cap, float chrome, string why)
        {
            var shown = cap != null ? StashPlan.ColumnsThatFit(cap.rect.width - chrome) : 0;

            Last = StashPlan.Refused(shown, why);

            return "cannot widen: " + why + ".";
        }

        /// <summary>
        /// The transforms <see cref="Apply"/> may change, and what they were before this
        /// mod first touched them.
        ///
        /// Kept because the inventory screen is one object for the whole session: the
        /// screen widened in the hideout is the same one opened in raid. Unity keeps
        /// no copy of a RectTransform's previous values, so this is the only record of
        /// what the screen looked like.
        /// </summary>
        private static RectTransform _root;

        private static RectTransform _leftSide;

        private static RectTransform _cap;

        private static Vector2 _rootAnchorMin;

        private static Vector2 _rootAnchorMax;

        private static Vector2 _rootOffsetMin;

        private static Vector2 _rootOffsetMax;

        private static Vector2 _leftAnchorMin;

        private static Vector2 _leftAnchorMax;

        private static Vector2 _leftOffsetMin;

        private static Vector2 _leftOffsetMax;

        private static Vector2 _capSizeDelta;

        /// <summary>
        /// Record the untouched geometry. Only called when nothing is held for these
        /// transforms, so a widened screen is never recorded as the original.
        /// </summary>
        private static void Remember(RectTransform root, RectTransform leftSide, RectTransform cap)
        {
            _root = root;
            _leftSide = leftSide;
            _cap = cap;

            if (root != null)
            {
                _rootAnchorMin = root.anchorMin;
                _rootAnchorMax = root.anchorMax;
                _rootOffsetMin = root.offsetMin;
                _rootOffsetMax = root.offsetMax;
            }

            _leftAnchorMin = leftSide.anchorMin;
            _leftAnchorMax = leftSide.anchorMax;
            _leftOffsetMin = leftSide.offsetMin;
            _leftOffsetMax = leftSide.offsetMax;
            _capSizeDelta = cap.sizeDelta;
        }

        /// <summary>Set every remembered transform back to its recorded values.</summary>
        private static void PutBack()
        {
            if (_root)
            {
                _root.anchorMin = _rootAnchorMin;
                _root.anchorMax = _rootAnchorMax;
                _root.offsetMin = _rootOffsetMin;
                _root.offsetMax = _rootOffsetMax;
            }

            if (_leftSide)
            {
                _leftSide.anchorMin = _leftAnchorMin;
                _leftSide.anchorMax = _leftAnchorMax;
                _leftSide.offsetMin = _leftOffsetMin;
                _leftSide.offsetMax = _leftOffsetMax;
            }

            if (_cap) _cap.sizeDelta = _capSizeDelta;
        }

        private static void Forget()
        {
            _root = null;
            _leftSide = null;
            _cap = null;
            _watchFrames = 0;
        }

        /// <summary>
        /// Put the screen back to what the game (or another mod) built.
        ///
        /// Called whenever the inventory opens in raid. The next stash open in the
        /// menu stretches and widens it again.
        /// </summary>
        /// <returns>A line for the log, or null when there was nothing to undo.</returns>
        internal static string Restore()
        {
            var cap = _cap;
            var leftSide = _leftSide;

            // Nothing changed, or destroyed with the screen -- and the screen that
            // replaces it is built vanilla.
            if (!cap || !leftSide)
            {
                Forget();
                return null;
            }

            try
            {
                PutBack();
            }
            catch (Exception e)
            {
                Forget();
                return "could not put the inventory screen back for the raid -- " + e.Message;
            }

            Forget();

            return string.Format(
                "in raid: inventory screen put back as it was, stash panel {0:0.0} px. "
                + "It widens again the next time the stash opens in the menu.",
                cap.rect.width);
        }

        // ---- the watch -------------------------------------------------------------
        //
        // UIScale.Reloaded applies its stretch three frames after InventoryScreen.Show,
        // from a coroutine. When the stash panel is shown inside that window, the
        // widening lands first and UIScale then rewrites LeftSide and the stash panel
        // back to its own 680 px layout. So for a short while after each widening the
        // geometry is checked, and if something has rewritten it, it is applied again.

        /// <summary>Frames left to watch, counted down by <see cref="Watch"/>.</summary>
        private static int _watchFrames;

        /// <summary>Re-applications left in this watch, so two mods cannot fight forever.</summary>
        private static int _watchRetries;

        private static float _watchChrome;

        private static float _watchReserve;

        private static int _watchGrid;

        private static Vector2 _setLeftOffsetMax;

        private static Vector2 _setLeftAnchorMin;

        private static Vector2 _setCapSizeDelta;

        /// <summary>How long after a widening to keep checking it.</summary>
        private const int WatchWindowFrames = 30;

        /// <summary>Record what <see cref="Apply"/> left, and start watching it.</summary>
        private static void Applied(float chrome, float reserve, int grid)
        {
            if (!_cap || !_leftSide) return;

            _watchChrome = chrome;
            _watchReserve = reserve;
            _watchGrid = grid;
            _setLeftOffsetMax = _leftSide.offsetMax;
            _setLeftAnchorMin = _leftSide.anchorMin;
            _setCapSizeDelta = _cap.sizeDelta;

            // A fresh budget per open; Watch restores its own count after it re-applies.
            _watchRetries = 3;
            _watchFrames = WatchWindowFrames;
        }

        /// <summary>
        /// Call every frame. Re-applies the widening if another mod rewrote the layout
        /// just after it. Returns a line for the log when it did, else null.
        /// </summary>
        internal static string Watch()
        {
            if (_watchFrames <= 0) return null;

            _watchFrames--;

            if (!_cap || !_leftSide)
            {
                _watchFrames = 0;
                return null;
            }

            var intact = Near(_leftSide.offsetMax, _setLeftOffsetMax)
                         && Near(_leftSide.anchorMin, _setLeftAnchorMin)
                         && Near(_cap.sizeDelta, _setCapSizeDelta);

            if (intact) return null;

            if (_watchRetries <= 0)
            {
                _watchFrames = 0;
                return "the inventory layout keeps being rewritten after it is widened -- "
                       + "another mod is resizing the same panels. Giving up for this open.";
            }

            var retries = _watchRetries - 1;

            // Another mod's values are now on screen, and what it left is the layout
            // that mod wants -- so whichever transform it rewrote becomes the untouched
            // geometry to plan from. Only that one: adopting a transform that still
            // holds this mod's widening would widen it twice. The root's record stays,
            // so a raid still puts back a stretch this mod made.
            if (!Near(_leftSide.offsetMax, _setLeftOffsetMax) || !Near(_leftSide.anchorMin, _setLeftAnchorMin))
            {
                _leftAnchorMin = _leftSide.anchorMin;
                _leftAnchorMax = _leftSide.anchorMax;
                _leftOffsetMin = _leftSide.offsetMin;
                _leftOffsetMax = _leftSide.offsetMax;
            }

            if (!Near(_cap.sizeDelta, _setCapSizeDelta)) _capSizeDelta = _cap.sizeDelta;

            var said = Apply(_cap, _watchChrome, _watchReserve, _watchGrid);

            _watchRetries = retries;

            return "the inventory layout was rewritten just after it was widened (UIScale.Reloaded "
                   + "does this a few frames after the screen opens), so it was widened again:\n"
                   + said;
        }

        private static bool Near(Vector2 a, Vector2 b)
        {
            return Mathf.Abs(a.x - b.x) < 0.5f && Mathf.Abs(a.y - b.y) < 0.5f;
        }

        /// <summary>
        /// The inventory screen's root: the nearest ancestor carrying a component named
        /// <c>InventoryScreen</c>. Matched by name because the probe references no game
        /// assembly. Null if there is none, and then nothing is stretched.
        /// </summary>
        private static RectTransform FindScreenRoot(RectTransform from)
        {
            for (var node = from; node != null; node = node.parent as RectTransform)
            {
                foreach (var c in node.GetComponents(typeof(Component)))
                {
                    if (c != null && c.GetType().Name == ScreenComponentName) return node;
                }
            }

            return null;
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
