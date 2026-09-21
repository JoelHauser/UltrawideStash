using System;
using System.Collections.Generic;
using System.Collections;
using System.Text;
using UnityEngine;

namespace UltrawideStash.Probe
{
    /// <summary>
    /// Measures the stash on screen and writes one report to the BepInEx log.
    ///
    /// ## Why measure rather than calculate
    ///
    /// How wide the stash panel is drawn depends on the CanvasScaler's mode and scale
    /// factor, on where the prefab anchors the ScrollRect's viewport, and on whatever
    /// layout groups sit in between -- all serialized data, none of it readable from
    /// the assembly. So this reads the RectTransforms the game actually built and
    /// reports what it finds.
    ///
    /// This is the same discipline as ScreenFit in the DeployScreen repo, and for the
    /// same reason: a number taken off the live hierarchy holds at any resolution and
    /// any screen shape without knowing how the UI was authored.
    ///
    /// ## What the report is for
    ///
    /// Two questions, and the fix depends on the answers:
    ///
    /// 1. Is the stash grid being clipped? The grid sizes itself
    ///    (<c>GridView.OnGridResized</c>), so a widened grid is always the width it
    ///    claims -- the question is whether an ancestor masks it.
    /// 2. How much room is there to the right? That is the column ceiling this
    ///    monitor can actually show.
    /// </summary>
    internal static class StashMeasure
    {
        /// <summary>
        /// One cell plus its border, from
        /// <c>EFT.UI.DragAndDrop.ItemViewFactory.GetCellPixelSize</c>, which is
        /// <c>columns * 63 + 1</c>. Hard-coded in the game, so hard-coded here.
        /// </summary>
        internal const int CellPixels = 63;

        /// <summary>The one extra pixel that grid width carries.</summary>
        internal const int GridBorderPixels = 1;

        /// <summary>
        /// Screen sizes already reported, so opening the stash forty times in a
        /// session writes one report rather than forty. A resolution change is a new
        /// key and gets measured again.
        /// </summary>
        private static readonly HashSet<string> Reported = new HashSet<string>();

        /// <summary>
        /// How many columns fit in <paramref name="availableWidth"/> canvas pixels.
        /// Pure arithmetic, kept separate so it can be checked without an engine.
        /// </summary>
        internal static int ColumnsThatFit(float availableWidth)
        {
            var usable = availableWidth - GridBorderPixels;

            if (usable < CellPixels) return 0;

            return (int)Math.Floor(usable / CellPixels);
        }

        /// <summary>How wide a grid of this many columns is drawn.</summary>
        internal static int WidthOfColumns(int columns)
        {
            return columns * CellPixels + GridBorderPixels;
        }

        /// <summary>
        /// Walk the panel, measure, and log.
        ///
        /// Returns true when there is nothing more to wait for -- either the report
        /// was written, this screen size has already been reported, or the attempt
        /// threw. False means the grid does not exist yet and the caller should try
        /// again next frame.
        ///
        /// Never throws into the caller: a probe that breaks the stash screen is
        /// worse than no probe.
        /// </summary>
        internal static bool Report(MonoBehaviour panel, Action<string> log)
        {
            try
            {
                if (panel == null) return true;

                var key = Screen.width + "x" + Screen.height;
                var reported = Reported.Contains(key);

                // Nothing left to do on this screen: WidenNow resized the panel during
                // Show and the report for this resolution is already written. Bail
                // before the grid search, which otherwise runs every frame of every
                // open for a result that is thrown away.
                if (reported && _widenSaid) return true;

                var gridView = FindStashGridView(panel);

                if (gridView == null)
                {
                    // Not fatal and not yet final -- Show builds the grid over
                    // several frames, so the caller retries.
                    return false;
                }

                if (_settleKey != key)
                {
                    _settleKey = key;
                    _settled = 0;
                }

                // Widening runs on every open, and deliberately not behind the
                // once-per-resolution gate the report uses. Whether the screen is
                // rebuilt between opens is not something the probe can know, and a
                // widening that ran only the first time would silently stop working
                // on the second. Apply is idempotent, so re-running it costs a
                // comparison. Only the first pass is logged: the settle loop calls
                // this four times and three of them have nothing left to do.
                // A safety net for the case where Show ran before the panel was in
                // its final place. Skipped outright when WidenNow already succeeded,
                // because the settle loop calls this four times and each call walks
                // the chain -- work that buys nothing on a screen that opened
                // normally, and that the player feels as a hitch.
                if (Widen != null && !_widenSaid)
                {
                    int widenedColumns;
                    ApplyWiden(gridView, Widen.Value, out widenedColumns);
                }

                if (reported) return true;

                // Let the layout pass run before mapping the screen.
                //
                // Widening sets offsetMax and sizeDelta, which update those transforms
                // at once, but the children of a HorizontalLayoutGroup are not moved
                // until Unity's next layout rebuild. Reporting in the same frame prints
                // the panel's new width beside its children's old ones -- in the 0.8.0
                // log, a 1240 px LeftSide holding two 930 px panels, one of them
                // apparently overflowing 626 px past its parent into the stash. Nothing
                // was wrong; the map was just taken mid-flight.
                if (_settled < SettleFrames)
                {
                    _settled++;
                    return false;
                }

                Reported.Add(key);

                log(Build(panel, gridView));

                return true;
            }
            catch (Exception e)
            {
                log("measurement failed: " + e.Message);

                // A throw here repeats every frame if we keep trying. Stop.
                return true;
            }
        }

        /// <summary>
        /// Clear the per-resolution memo. Called when the plugin's own settings
        /// change, so a report can be forced without restarting the game.
        /// </summary>
        internal static void Forget()
        {
            Reported.Clear();
        }

        /// <summary>
        /// The GridView drawing the stash itself.
        ///
        /// The stash screen holds more than one: opening a container adds its own.
        /// The stash is the tallest, always by a wide margin -- 30 rows at the very
        /// least against a backpack's handful -- so the tallest grid is the stash
        /// without needing to identify it by item.
        /// </summary>
        private static Component FindStashGridView(MonoBehaviour panel)
        {
            var views = panel.GetComponentsInChildren(GameTypes.GridView, true);

            Component best = null;
            var bestRows = -1;

            foreach (var view in views)
            {
                var grid = GameTypes.GridViewGrid.GetValue(view);

                if (grid == null) continue;

                var rows = (int)GameTypes.GridHeight.GetValue(grid, null);

                if (rows > bestRows)
                {
                    bestRows = rows;
                    best = view;
                }
            }

            return best;
        }

        /// <summary>
        /// Frames to wait between widening and mapping, so the layout groups have
        /// re-flowed. Three is comfortably more than the one Unity needs.
        /// </summary>
        private const int SettleFrames = 3;

        /// <summary>Frames waited so far, and the resolution they belong to.</summary>
        private static int _settled;

        private static string _settleKey;

        /// <summary>
        /// How much width each gear panel keeps, or null to leave the screen alone.
        /// Set from the plugin's config before the first stash opens.
        /// </summary>
        internal static float? Widen;

        /// <summary>Set once the widening has been logged, so it is said once.</summary>
        private static bool _widenSaid;

        /// <summary>
        /// Widen the panel during <c>Show</c>, before the frame is drawn.
        ///
        /// ## Why not from Update, where the rest of this runs
        ///
        /// Update is a frame late. The panel is shown at its vanilla 680 px, that
        /// frame is rendered, and only then does the widening land -- so the stash
        /// visibly snaps wider every time the character screen is opened. Nothing is
        /// wrong with the result, it just arrives one frame after the player can see
        /// it, which reads as unfinished.
        ///
        /// The measurement has to wait for Update because <c>Show</c> returns before
        /// the GridViews exist. The widening does not: it needs the stash panel and
        /// the viewport, both of which are in the prefab and present the moment Show
        /// runs. So the two are split -- resize now, measure when there is something
        /// to measure.
        /// </summary>
        internal static void WidenNow(MonoBehaviour panel, Action<string> log)
        {
            if (Widen == null || panel == null) return;

            try
            {
                var rect = panel.transform as RectTransform;

                if (rect == null) return;

                var canvas = panel.GetComponentInParent<Canvas>();
                var chain = AppendChain(new StringBuilder(), rect, canvas);

                if (chain.Cap == null) return;

                int columns;
                var said = StashWiden.Apply(chain.Cap, Chrome, Widen.Value, out columns);

                if (!_widenSaid)
                {
                    _widenSaid = true;
                    log(said);
                }
            }
            catch (Exception e)
            {
                log("could not widen the stash panel -- " + e.Message);
            }
        }

        /// <summary>
        /// The stash panel's chrome: toolbar strip and scrollbar, the width inside
        /// the panel that the grid never gets. 48 px on an untouched 4.1.5.
        ///
        /// ## Why this is a remembered number and not a measured one
        ///
        /// The widening runs from <c>Show</c>, and at Show the nodes inside the panel
        /// have not been laid out. Their rects hold whatever the prefab left there.
        /// Measuring chrome from them returned 600, then 38, then 10 across three
        /// attempts, each a different wrong answer from a different node, and each
        /// time the node looked like the bug. It was not: there is simply nothing
        /// inside the panel worth reading that early.
        ///
        /// What is reliable at Show is the panel itself and its siblings, which are
        /// already positioned. So chrome is not measured there at all. It is carried
        /// from the last time the screen was fully laid out -- <see cref="Build"/>
        /// takes it off the real chain and calls <see cref="LearnChrome"/> -- and
        /// defaults to the stock value until then.
        /// </summary>
        internal static float Chrome = DefaultChrome;

        /// <summary>Stash panel chrome on a stock 4.1.5: 680 px panel, 632 px viewport.</summary>
        internal const float DefaultChrome = 48f;

        /// <summary>
        /// Record the chrome seen on a properly laid out screen, so the next Show
        /// sizes the panel from a real number rather than the default.
        /// </summary>
        private static void LearnChrome(float measured)
        {
            // Bounded because this feeds a panel width. A chain that returns something
            // absurd should leave the known-good value alone rather than replace it.
            if (measured < 8f || measured > 200f) return;

            Chrome = measured;
        }

        /// <summary>
        /// Walk the chain and hand the clipping panel to <see cref="StashWiden"/>.
        ///
        /// The chain is walked into a throwaway buffer rather than reusing the
        /// report's, because this runs on every open and the report does not.
        /// </summary>
        private static string ApplyWiden(Component gridView, float reservePerPanel, out int columns)
        {
            columns = 0;

            var gridRect = gridView.transform as RectTransform;

            if (gridRect == null) return "cannot widen: the grid has no RectTransform.";

            var canvas = gridView.GetComponentInParent<Canvas>();
            var chain = AppendChain(new StringBuilder(), gridRect, canvas);

            return StashWiden.Apply(chain.Cap, chain.Chrome, reservePerPanel, out columns);
        }

        private static string Build(MonoBehaviour panel, Component gridView)
        {
            var sb = new StringBuilder();

            var grid = GameTypes.GridViewGrid.GetValue(gridView);
            var columns = (int)GameTypes.GridWidth.GetValue(grid, null);
            var rows = (int)GameTypes.GridHeight.GetValue(grid, null);

            var gridRect = gridView.transform as RectTransform;
            var canvas = gridView.GetComponentInParent<Canvas>();

            var scale = canvas != null ? canvas.scaleFactor : 1f;
            if (scale <= 0f) scale = 1f;

            sb.AppendLine("===== stash measurement =====");
            sb.AppendLine(string.Format(
                "screen {0}x{1}; canvas scale {2:0.000}; canvas logical {3:0}x{4:0}",
                Screen.width, Screen.height, scale,
                Screen.width / scale, Screen.height / scale));

            sb.AppendLine(string.Format(
                "stash grid {0}x{1} cells; rect {2:0.0}x{3:0.0} px (a {0}-wide grid draws at {4} px)",
                columns, rows,
                gridRect != null ? gridRect.rect.width : 0f,
                gridRect != null ? gridRect.rect.height : 0f,
                WidthOfColumns(columns)));

            AppendOutOfBounds(sb, grid);
            AppendLayoutConsistency(sb, grid, columns, rows);

            var companions = Companions.Describe();

            if (companions.Length > 0) sb.AppendLine(companions);

            sb.AppendLine("ancestors, grid outward -- name | rect | anchors | components:");

            var chain = AppendChain(sb, gridRect, canvas);

            sb.AppendLine(string.Format(
                "canvas width {0:0.0} px; widest ancestor that stretches with it: {1:0.0} px",
                canvas != null ? ((RectTransform)canvas.transform).rect.width : Screen.width / scale,
                chain.WidestStretch));

            if (chain.Cap != null)
            {
                sb.AppendLine(string.Format(
                    "but '{0}' is pinned at {1:0.0} px and clips the grid -- that, not the "
                    + "canvas, is the ceiling",
                    chain.Cap.name, chain.NearestFixed));
            }

            // Chrome comes off first. The toolbar strip and scrollbar are inside the
            // panel and the grid never gets them: at 680 px that is 10 columns either
            // way and the subtraction looks optional, but at 1300 it is the difference
            // between 20 and 19, and 20 is a column drawn under the scrollbar.
            // This screen is fully laid out, so its chrome is the real one. Remember
            // it for the next Show, which cannot measure its own.
            LearnChrome(chain.Chrome);

            var fits = ColumnsThatFit(chain.Usable - chain.Chrome);

            sb.AppendLine(string.Format(
                "columns that fit inside the clipping ancestor: {0} (you have {1}); "
                + "panel {2:0.0} px less {3:0.0} px of chrome",
                fits, columns, chain.Usable, chain.Chrome));

            if (chain.Cap != null && chain.WidestStretch > chain.NearestFixed + 1f)
            {
                sb.AppendLine(string.Format(
                    "there are {0:0.0} px of canvas past it, worth {1} more columns, but only "
                    + "if '{2}' is widened first -- see the panel map below",
                    chain.WidestStretch - chain.NearestFixed,
                    ColumnsThatFit(chain.WidestStretch - chain.Chrome) - fits,
                    chain.Cap.name));
            }

            AppendPanels(sb, chain.Cap, canvas);

            // Hand the number to the server half rather than leaving the player to
            // read it out of a log and copy it into a config. A measurement only takes
            // effect on the next server start -- the grid on screen was built before
            // this ran -- which is the same rule ScreenFit follows in LoadingRaid.
            var canvasWidth = canvas != null
                ? (int)Math.Round(((RectTransform)canvas.transform).rect.width)
                : (int)Math.Round(Screen.width / scale);

            // The one line that settles whether the stash looks right.
            //
            // Everything above is geometry that has to be reasoned about; this is the
            // answer measured directly off the screen in front of the player. The grid
            // is either narrower than its viewport, and there is dead space, or wider,
            // and there is a horizontal scrollbar. Three fixes in a row were argued
            // from the numbers above and three were wrong, so the check stopped being
            // a matter of reasoning and became a line in the log.
            if (gridRect != null)
            {
                var viewport = chain.Usable - chain.Chrome;
                var drawn = gridRect.rect.width;
                var spare = viewport - drawn;

                string verdict;

                if (spare < 0f)
                {
                    verdict = string.Format(
                        "OVERFLOW by {0:0.0} px -- the stash will show a horizontal scrollbar",
                        -spare);
                }
                else if (spare >= CellPixels)
                {
                    verdict = string.Format(
                        "DEAD SPACE {0:0.0} px -- {1} unusable column(s); the server is "
                        + "sizing the grid narrower than the panel can show",
                        spare, ColumnsThatFit(spare));
                }
                else
                {
                    verdict = string.Format("fits, {0:0.0} px spare", spare);
                }

                sb.AppendLine(string.Format(
                    "CHECK: viewport {0:0.0} px vs grid {1:0.0} px -- {2}",
                    viewport, drawn, verdict));
            }

            // The server reads this to size the grid. It gets the count for the panel
            // as it stands *after* any widening, because the widening has already run
            // by the time the report is built -- otherwise the grid would be sized for
            // a panel that no longer exists.
            sb.AppendLine(MeasurementFile.Write(
                fits, Screen.width, Screen.height, canvasWidth, chain.Usable));

            sb.Append("=============================");

            return sb.ToString();
        }

        /// <summary>
        /// The direct answer to "has widening stranded anything", when the game
        /// exposes it. An item out of bounds is an item the player cannot reach.
        /// </summary>
        private static void AppendOutOfBounds(StringBuilder sb, object grid)
        {
            if (GameTypes.OutOfBoundsItems == null) return;

            try
            {
                var items = GameTypes.OutOfBoundsItems.GetValue(grid, null) as IEnumerable;

                if (items == null) return;

                var count = 0;

                foreach (var _ in items) count++;

                sb.AppendLine(count == 0
                    ? "out-of-bounds items: none"
                    : "out-of-bounds items: " + count + " -- THESE ARE UNREACHABLE, restore a profile backup");
            }
            catch
            {
                // An optional diagnostic. Its absence is not worth a line.
            }
        }

        /// <summary>
        /// Whether <c>Grid.Layout</c> still matches the grid's dimensions.
        ///
        /// Advanced Stash Sorting asserts exactly this in its <c>CopyLayout</c> and
        /// throws "Grid layout dimensions are inconsistent" when it fails, so a
        /// mismatch here is the precise explanation for that mod refusing to sort.
        /// It should never happen -- the grid is built from the template before
        /// anything sees it -- but "should never" is why it is worth one line.
        /// </summary>
        private static void AppendLayoutConsistency(StringBuilder sb, object grid, int columns, int rows)
        {
            if (GameTypes.GridLayout == null) return;

            try
            {
                var layout = GameTypes.GridLayout.GetValue(grid, null) as ICollection;

                if (layout == null) return;

                var expected = columns * rows;

                sb.AppendLine(layout.Count == expected
                    ? $"grid layout: {layout.Count} cells, consistent with {columns}x{rows}"
                    : $"grid layout: {layout.Count} cells but {columns}x{rows} is {expected} -- INCONSISTENT, "
                      + "sorting mods will refuse to sort");
            }
            catch
            {
                // An optional diagnostic. Its absence is not worth a line.
            }
        }

        /// <summary>
        /// Print every sibling of the clipping panel, left to right, with the gaps
        /// between them.
        ///
        /// The ancestor chain answers "what boxes the grid in". It does not answer
        /// "what else is in the box, and where is the empty space" -- and on an
        /// ultrawide the empty space is the whole point. Widening the stash means
        /// moving a sibling out of the way, and a sibling cannot be moved by a name
        /// nobody has read. Every name in this block is a real name off the live
        /// hierarchy, which is what the patch half needs and what guessing against
        /// obfuscated UI never gives.
        ///
        /// Edges are reported in canvas space, so they can be compared against the
        /// canvas width directly and against a screenshot by eye.
        /// </summary>
        private static void AppendPanels(StringBuilder sb, RectTransform cap, Canvas canvas)
        {
            if (cap == null || canvas == null) return;

            var parent = cap.parent as RectTransform;

            if (parent == null) return;

            var canvasRect = (RectTransform)canvas.transform;

            sb.AppendLine(string.Format(
                "panel map -- '{0}' and below, left to right, in canvas px:", parent.name));

            AppendPanelLevel(sb, parent, cap, canvasRect, 0);

            sb.AppendLine(string.Format(
                "canvas spans 0.0 to {0:0.0} px", canvasRect.rect.width));
        }

        /// <summary>
        /// How far down the panel map goes. Three levels is enough to reach the
        /// individual gear boxes without printing every label and icon in the screen.
        /// </summary>
        private const int PanelMapDepth = 3;

        /// <summary>
        /// Panels narrower than this are furniture -- icons, dividers, labels -- and
        /// are not what a stash is going to be widened into.
        /// </summary>
        private const float PanelMapMinWidth = 120f;

        /// <summary>
        /// One level of the panel map, and then the levels below it.
        ///
        /// Recurses because the thing in the way is not always a sibling. On this
        /// screen the stash's only real neighbour is a single 1866 px 'LeftSide' that
        /// holds the character panel and the gear column both, so the empty space an
        /// ultrawide shows is inside it, not beside it. A one-level map says "there is
        /// nothing to move" and is wrong.
        /// </summary>
        private static void AppendPanelLevel(
            StringBuilder sb,
            RectTransform parent,
            RectTransform cap,
            RectTransform canvasRect,
            int depth)
        {
            var rows = new List<PanelRow>();

            for (var i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i) as RectTransform;

                if (child == null) continue;

                float left, right;

                if (!EdgesInCanvasSpace(child, canvasRect, out left, out right)) continue;

                rows.Add(new PanelRow(child, left, right));
            }

            rows.Sort((a, b) => a.Left.CompareTo(b.Left));

            var indent = new string(' ', 2 + (depth * 2));
            var previousRight = float.NaN;

            foreach (var row in rows)
            {
                var node = row.Node;
                var stretches = Mathf.Abs(node.anchorMax.x - node.anchorMin.x) > 0.001f;
                var isCap = ReferenceEquals(node, cap);

                // A gap wide enough for a column is room the stash could be using.
                if (!float.IsNaN(previousRight) && row.Left - previousRight >= CellPixels)
                {
                    sb.AppendLine(string.Format(
                        "{0}-- gap {1:0.0} px ({2} columns' worth) --",
                        indent,
                        row.Left - previousRight,
                        ColumnsThatFit(row.Left - previousRight)));
                }

                // Components are printed because anchors alone do not say whether a
                // panel will re-flow. A parent holding a HorizontalLayoutGroup
                // repositions its children when it is resized; one without it leaves
                // them where they were pinned, and the patch has to move each by hand.
                sb.AppendLine(string.Format(
                    "{0}{1}{2} | x {3:0.0} to {4:0.0} | w {5:0.0} | ax {6:0.00}-{7:0.00} {8}{9} | {10}",
                    indent,
                    node.name,
                    isCap ? "  <-- the stash panel" : string.Empty,
                    row.Left,
                    row.Right,
                    node.rect.width,
                    node.anchorMin.x,
                    node.anchorMax.x,
                    stretches ? "STRETCH" : "fixed",
                    node.gameObject.activeInHierarchy ? string.Empty : " (inactive)",
                    ComponentNames(node)));

                if (row.Right > previousRight || float.IsNaN(previousRight))
                {
                    previousRight = row.Right;
                }

                // The stash panel's own insides are already printed by the ancestor
                // chain, so descending into it would say everything twice.
                var worthDescending = !isCap
                    && depth + 1 < PanelMapDepth
                    && node.rect.width >= PanelMapMinWidth
                    && node.childCount > 0
                    && node.gameObject.activeInHierarchy;

                if (worthDescending)
                {
                    AppendPanelLevel(sb, node, cap, canvasRect, depth + 1);
                }
            }
        }

        /// <summary>
        /// True when this node's width is computed from its children rather than
        /// imposed on them, so it cannot be what clips the grid.
        ///
        /// Checked by component name because the probe does not reference
        /// UnityEngine.UI -- see the csproj on why nothing here is compiled against
        /// the game's own assemblies.
        /// </summary>
        private static bool SelfSizing(Component node)
        {
            foreach (var c in node.GetComponents(typeof(Component)))
            {
                if (c == null) continue;

                var name = c.GetType().Name;

                if (name == "ContentSizeFitter"
                    || name == "LayoutElement"
                    || name.EndsWith("LayoutGroup", StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>A child panel and where its edges land in canvas space.</summary>
        private struct PanelRow
        {
            internal readonly RectTransform Node;
            internal readonly float Left;
            internal readonly float Right;

            internal PanelRow(RectTransform node, float left, float right)
            {
                Node = node;
                Left = left;
                Right = right;
            }
        }

        /// <summary>
        /// Where a RectTransform's left and right edges fall in the canvas's own
        /// coordinates, with x measured from the canvas's left edge.
        ///
        /// Goes through world corners rather than anchoredPosition because the
        /// panels sit under layout groups and pivots that are not all the same, and
        /// world corners are the one reading that does not care about either.
        /// </summary>
        private static bool EdgesInCanvasSpace(
            RectTransform node,
            RectTransform canvasRect,
            out float left,
            out float right)
        {
            left = 0f;
            right = 0f;

            try
            {
                var corners = new Vector3[4];
                node.GetWorldCorners(corners);

                // Bottom-left and bottom-right, taken into the canvas's local space.
                var a = canvasRect.InverseTransformPoint(corners[0]).x;
                var b = canvasRect.InverseTransformPoint(corners[3]).x;

                var origin = canvasRect.rect.xMin;

                left = Mathf.Min(a, b) - origin;
                right = Mathf.Max(a, b) - origin;

                return true;
            }
            catch (Exception)
            {
                // A panel that will not report corners is one we cannot place, and
                // one unplaceable panel is not worth losing the rest of the map.
                return false;
            }
        }

        /// <summary>
        /// Print the RectTransform chain from the grid up to the canvas, and return
        /// the width of the widest ancestor that is anchored to stretch horizontally.
        ///
        /// Anchors are what answer the question the assembly cannot. An ancestor with
        /// <c>anchorMin.x == 0</c> and <c>anchorMax.x == 1</c> grows with its parent,
        /// so on an ultrawide it is already wider than it would be at 16:9 and the
        /// extra room is genuinely there. One with equal anchors is pinned to a fixed
        /// width and is what will clip a widened grid.
        /// </summary>
        private static Chain AppendChain(StringBuilder sb, RectTransform from, Canvas canvas)
        {
            var widestStretch = 0f;
            var nearestFixed = 0f;
            var viewport = float.MaxValue;
            RectTransform cap = null;
            var node = from;
            var depth = 0;

            while (node != null && depth < 24)
            {
                var stretches = Mathf.Abs(node.anchorMax.x - node.anchorMin.x) > 0.001f;

                if (stretches && node.rect.width > widestStretch)
                {
                    widestStretch = node.rect.width;
                }

                // Below the cap, the narrowest stretching node is the viewport the
                // grid is actually drawn into. The difference between it and the cap
                // is the panel's chrome -- toolbar strip and scrollbar -- which stays
                // the same width however wide the panel gets.
                if (stretches && cap == null && node.rect.width < viewport)
                {
                    viewport = node.rect.width;
                }

                // The first pinned ancestor is the one that clips, and nothing above
                // it can give the grid room. Anything wider found past this point is
                // canvas the grid cannot reach.
                //
                // Self-sizing nodes are passed over. A ContentSizeFitter or a layout
                // group computes its width *from* its children, so it reports the
                // grid's own width back and is pinned only in the sense that it was
                // never anchored. Counting one as the cap reads the grid's width as
                // the grid's limit: here 'Content' and 'Default Grids Template' both
                // sit at 630 px and would have answered 9 columns on a stash that
                // already shows 10.
                if (!stretches && cap == null && node != from && !SelfSizing(node))
                {
                    nearestFixed = node.rect.width;
                    cap = node;
                }

                sb.AppendLine(string.Format(
                    "  [{0}] {1} | {2:0.0}x{3:0.0} | ax {4:0.00}-{5:0.00} {6} | {7}",
                    depth,
                    node.name,
                    node.rect.width,
                    node.rect.height,
                    node.anchorMin.x,
                    node.anchorMax.x,
                    stretches ? "STRETCH" : "fixed",
                    ComponentNames(node)));

                if (canvas != null && ReferenceEquals(node.gameObject, canvas.gameObject)) break;

                node = node.parent as RectTransform;
                depth++;
            }

            // Nothing stretched anywhere up the chain: the panel is pinned, and the
            // canvas is the only honest ceiling to quote.
            if (widestStretch <= 0f && canvas != null)
            {
                widestStretch = ((RectTransform)canvas.transform).rect.width;
            }

            return new Chain(
                widestStretch,
                nearestFixed,
                cap,
                viewport == float.MaxValue ? 0f : viewport);
        }

        /// <summary>
        /// What the ancestor walk found: the widest ancestor that grows with the
        /// canvas, and the nearest one that does not.
        ///
        /// The second is the number that matters. 0.6.0 quoted the first and offered
        /// 40 columns on a 3440x1440 screen whose stash panel is pinned at 680 px --
        /// 30 of those columns would have been drawn behind a mask, with the server
        /// half relocating real items into them.
        /// </summary>
        internal struct Chain
        {
            internal readonly float WidestStretch;
            internal readonly float NearestFixed;
            internal readonly RectTransform Cap;

            /// <summary>The width the grid is drawn into, inside the cap.</summary>
            internal readonly float Viewport;

            internal Chain(
                float widestStretch,
                float nearestFixed,
                RectTransform cap,
                float viewport)
            {
                WidestStretch = widestStretch;
                NearestFixed = nearestFixed;
                Cap = cap;
                Viewport = viewport;
            }

            /// <summary>
            /// The panel width the grid does not get: toolbar strip and scrollbar.
            /// Constant as the panel widens, so it is subtracted once.
            /// </summary>
            internal float Chrome
            {
                get
                {
                    return NearestFixed > 0f && Viewport > 0f && NearestFixed > Viewport
                        ? NearestFixed - Viewport
                        : 0f;
                }
            }

            /// <summary>
            /// The width the grid can actually occupy today: the pinned ancestor when
            /// there is one, otherwise the room that stretches.
            /// </summary>
            internal float Usable
            {
                get { return NearestFixed > 0f ? NearestFixed : WidestStretch; }
            }
        }

        /// <summary>
        /// The short type names of the components on one node. This is what says
        /// which ancestor is the ScrollRect, which one masks, and which one imposes a
        /// layout -- the three things a width fix has to deal with.
        /// </summary>
        private static string ComponentNames(Component node)
        {
            var parts = new List<string>();

            foreach (var c in node.GetComponents(typeof(Component)))
            {
                if (c == null) continue;

                var name = c.GetType().Name;

                if (name == "RectTransform") continue;

                parts.Add(name);
            }

            return parts.Count == 0 ? "-" : string.Join(",", parts.ToArray());
        }
    }
}
