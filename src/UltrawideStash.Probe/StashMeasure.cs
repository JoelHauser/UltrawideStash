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

                if (Reported.Contains(key)) return true;

                var gridView = FindStashGridView(panel);

                if (gridView == null)
                {
                    // Not fatal and not yet final -- Show builds the grid over
                    // several frames, so the caller retries.
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

            var widestStretch = AppendChain(sb, gridRect, canvas);

            sb.AppendLine(string.Format(
                "canvas width {0:0.0} px; widest ancestor that stretches with it: {1:0.0} px",
                canvas != null ? ((RectTransform)canvas.transform).rect.width : Screen.width / scale,
                widestStretch));

            sb.AppendLine(string.Format(
                "columns that would fit the widest stretching ancestor: {0} (you have {1})",
                ColumnsThatFit(widestStretch), columns));

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
        /// Print the RectTransform chain from the grid up to the canvas, and return
        /// the width of the widest ancestor that is anchored to stretch horizontally.
        ///
        /// Anchors are what answer the question the assembly cannot. An ancestor with
        /// <c>anchorMin.x == 0</c> and <c>anchorMax.x == 1</c> grows with its parent,
        /// so on an ultrawide it is already wider than it would be at 16:9 and the
        /// extra room is genuinely there. One with equal anchors is pinned to a fixed
        /// width and is what will clip a widened grid.
        /// </summary>
        private static float AppendChain(StringBuilder sb, RectTransform from, Canvas canvas)
        {
            var widestStretch = 0f;
            var node = from;
            var depth = 0;

            while (node != null && depth < 24)
            {
                var stretches = Mathf.Abs(node.anchorMax.x - node.anchorMin.x) > 0.001f;

                if (stretches && node.rect.width > widestStretch)
                {
                    widestStretch = node.rect.width;
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

            return widestStretch;
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
