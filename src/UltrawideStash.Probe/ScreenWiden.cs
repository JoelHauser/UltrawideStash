using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace UltrawideStash.Probe
{
    /// <summary>Which screen a stash panel is being shown on.</summary>
    internal enum StashScreen
    {
        /// <summary>None of the screens below. Treated as the character screen always was.</summary>
        Unknown,

        /// <summary><c>EFT.UI.InventoryScreen</c>, the character screen.</summary>
        Inventory,

        /// <summary><c>EFT.UI.ScavengerInventoryScreen</c>: "Raid ended -- scav loot transfer".</summary>
        ScavTransfer,

        /// <summary><c>EFT.UI.TransferItemsScreen</c>: receiving items from mail.</summary>
        MailTransfer,

        /// <summary>
        /// <c>UI.Hideout.HideoutAreaTransferItemsScreen</c> and its generic bases: putting
        /// items into a hideout area.
        /// </summary>
        HideoutTransfer,

        /// <summary>A trader, prestige, or the in-raid transit transfer.</summary>
        Other,
    }

    /// <summary>
    /// Widens the stash panel on the scav loot transfer, the mail transfer and the
    /// hideout area transfer screens.
    ///
    /// ## Why these two needed their own code
    ///
    /// The grid is one item and its width lives in the template, so it is 19 wide on
    /// every screen that draws it -- but only the character screen's panel was ever
    /// widened. Everywhere else the player got the vanilla 680 px panel and a
    /// horizontal scrollbar. The scav screen was worse than that: the game shows it
    /// with <c>inRaid: true</c> (<c>ScavengerInventoryScreen.Show</c> passes it to
    /// <c>ItemsPanel.Show</c>, which hands it to <c>SimpleStashPanel.Show</c>), so
    /// the in-raid guard left it alone as though it were a crate.
    ///
    /// Neither screen is laid out like the character screen: both sit in a 16:9 frame
    /// in the middle of the canvas with empty space either side, and both have buttons
    /// along the bottom -- Next, Back and Sell All on one, Accept and Receive All on
    /// the other -- that a grown panel must not cover. Where those are is prefab data.
    /// So the layout is read off the live screen and handed to
    /// <see cref="ScreenLayout.Plan"/>, which grows the panel only into space nothing
    /// is drawn in.
    ///
    /// ## Why never in raid
    ///
    /// The scav screen exists only after a raid, back in the menu. The mail screen is
    /// matched by its exact type, so the in-raid transit transfer
    /// (<c>TransferItemsInRaidScreen</c>) is not it. Neither screen shares objects with
    /// the character screen, so nothing done here survives into a raid.
    ///
    /// ## Why nothing here writes the measurement
    ///
    /// The server sizes the grid from the character screen's panel. A panel measured
    /// here describes a different screen, and handing it to the server would size
    /// every stash for the wrong panel.
    /// </summary>
    internal static class ScreenWiden
    {
        /// <summary>Set from the plugin's config. False leaves both screens vanilla.</summary>
        internal static bool Enabled = true;

        private const string ScavScreenName = "EFT.UI.ScavengerInventoryScreen";

        private const string MailScreenName = "EFT.UI.TransferItemsScreen";

        private const string InventoryScreenName = "EFT.UI.InventoryScreen";

        /// <summary>
        /// Screens that draw the stash and are left alone: there is no free width
        /// beside the stash on them, or they are in raid. Matched by simple type name
        /// anywhere in a component's base chain, generic arity stripped.
        /// </summary>
        private static readonly HashSet<string> OtherScreens = new HashSet<string>(StringComparer.Ordinal)
        {
            "TraderDealScreen",
            "PrestigeTransferItemsState",
            "TransferItemsInRaidScreen",
        };

        /// <summary>
        /// The hideout area transfer screen. Matched through the base chain like the
        /// others above: the concrete screen derives from two generic bases, and every
        /// screen built on them is a hideout screen, never a raid one.
        /// </summary>
        private static readonly HashSet<string> HideoutScreens = new HashSet<string>(StringComparer.Ordinal)
        {
            "HideoutAreaTransferItemsScreen",
            "BaseHideoutAreaTransferItemsScreen",
            "AbstractHideoutAreaTransferItemsScreen",
        };

        /// <summary>
        /// The screen above a stash panel, nearest first. The scav and mail screens
        /// are matched on their exact type, so a subclass of either -- the in-raid
        /// transit transfer, if it is one -- is never taken for them.
        /// </summary>
        internal static StashScreen Identify(Component from, out Component screen)
        {
            screen = null;

            if (from == null) return StashScreen.Unknown;

            var node = from.transform.parent;

            for (var depth = 0; node != null && depth < 40; depth++, node = node.parent)
            {
                foreach (var c in node.GetComponents<MonoBehaviour>())
                {
                    if (c == null) continue;

                    var type = c.GetType();

                    switch (type.FullName)
                    {
                        case ScavScreenName:
                            screen = c;
                            return StashScreen.ScavTransfer;
                        case MailScreenName:
                            screen = c;
                            return StashScreen.MailTransfer;
                        case InventoryScreenName:
                            screen = c;
                            return StashScreen.Inventory;
                    }

                    for (var t = type; t != null && t != typeof(MonoBehaviour); t = t.BaseType)
                    {
                        if (HideoutScreens.Contains(SimpleName(t)))
                        {
                            screen = c;
                            return StashScreen.HideoutTransfer;
                        }

                        if (OtherScreens.Contains(SimpleName(t)))
                        {
                            screen = c;
                            return StashScreen.Other;
                        }
                    }
                }
            }

            return StashScreen.Unknown;
        }

        private static string SimpleName(Type t)
        {
            var name = t.Name;
            var tick = name.IndexOf('`');

            return tick < 0 ? name : name.Substring(0, tick);
        }

        // ---- the open in progress -------------------------------------------------

        private static MonoBehaviour _panel;

        private static Component _screen;

        private static StashScreen _kind;

        private static int _frames;

        private static int _phase;

        private static bool _retried;

        private static float _extraBoost;

        /// <summary>Screen kinds already given a full report at this resolution.</summary>
        private static readonly HashSet<string> Reported = new HashSet<string>();

        /// <summary>What the last arrange found, for the check after it settles.</summary>
        private static List<Obstacle> _buttons = new List<Obstacle>();

        /// <summary>
        /// The transform behind each obstacle. The planner is pure and holds no Unity
        /// objects, so they are kept here rather than found again by name -- the
        /// hideout screen moves its tabs between parents, and a lookup by path would
        /// quietly miss them.
        /// </summary>
        private static readonly Dictionary<Obstacle, RectTransform> Refs = new Dictionary<Obstacle, RectTransform>();

        /// <summary>The screen's layout signature when last planned; see <see cref="Signature"/>.</summary>
        private static int _signature;

        private static string _arranged = string.Empty;

        /// <summary>
        /// Called from <c>SimpleStashPanel.Show</c> on one of the two screens. Sizes the
        /// panel at once, so it is already wide on the first frame, then leaves the
        /// rest to <see cref="Tick"/>: the grid does not exist yet and the screen is not
        /// laid out, so the plan is redone once it is.
        /// </summary>
        internal static void Begin(MonoBehaviour panel, StashScreen kind, Component screen, int gridColumns, Action<string> log)
        {
            _panel = panel;
            _screen = screen;
            _kind = kind;
            _frames = 0;
            _phase = 0;
            _retried = false;
            _extraBoost = 0f;
            _signature = 0;

            if (!Enabled)
            {
                var cap = CapOf(panel);

                if (cap != null) Restore(cap);

                _panel = null;
                return;
            }

            if (gridColumns <= 0) return;

            try
            {
                Arrange(panel, screen, gridColumns, StashMeasure.Chrome);
            }
            catch (Exception e)
            {
                log(Label(kind) + ": could not widen the stash panel -- " + e.Message);
                _panel = null;
            }
        }

        /// <summary>
        /// Once per frame while an open is in progress. Returns quickly when there is
        /// none.
        ///
        /// Phase 0 waits for the grid and a few frames of layout, then plans again off
        /// the settled screen with the chrome measured there. Phase 1 waits for that to
        /// settle and checks the result off the screen: viewport against grid, and each
        /// button against the panel. One overflow gets one corrective pass.
        /// </summary>
        internal static void Tick(Action<string> log)
        {
            if (_panel == null) return;

            if (!_panel)
            {
                _panel = null;
                return;
            }

            try
            {
                if (_phase == 2)
                {
                    Watch(log);
                    return;
                }

                var gridView = StashMeasure.StashGridView(_panel);

                if (gridView == null)
                {
                    if (++_frames > 120)
                    {
                        log(Label(_kind) + ": gave up waiting for the stash grid after 120 frames.");
                        _panel = null;
                    }

                    return;
                }

                if (++_frames < SettleFrames) return;

                _frames = 0;

                if (_phase == 0)
                {
                    var grid = GameTypes.GridViewGrid.GetValue(gridView);
                    var columns = (int)GameTypes.GridWidth.GetValue(grid, null);

                    var gridRect = gridView.transform as RectTransform;
                    var chain = StashMeasure.ChainOf(gridRect, gridView.GetComponentInParent<Canvas>());

                    // Measured on the laid-out screen, before the restore inside
                    // Arrange changes the panel. Bounded like LearnChrome: an absurd
                    // number keeps the known one.
                    var chrome = chain.Chrome >= 8f && chain.Chrome <= 200f ? chain.Chrome : StashMeasure.Chrome;

                    _arranged = Arrange(_panel, _screen, columns, chrome + _extraBoost);
                    _signature = Signature(_screen);
                    _phase = 1;
                    return;
                }

                var said = Check(gridView);

                if (said.Overflow > 0f && !_retried)
                {
                    // The chrome here differs from what was measured. Plan again with
                    // the difference added; the check after it is the last word.
                    _retried = true;
                    _extraBoost += said.Overflow + 1f;
                    _phase = 0;
                    return;
                }

                var key = _kind + "@" + Screen.width + "x" + Screen.height;

                if (Reported.Add(key))
                {
                    log(_arranged + said.Text);
                }
                else
                {
                    log(Label(_kind) + ": " + said.Summary);
                }

                // The hideout screen swaps its area grid on a tab change without
                // showing the stash again, and opens a filter window beside it. Keep
                // an eye on both while the screen is open.
                if (_kind == StashScreen.HideoutTransfer)
                {
                    _phase = 2;
                    return;
                }

                _panel = null;
            }
            catch (Exception e)
            {
                log(Label(_kind) + ": widening failed -- " + e.Message);
                _panel = null;
            }
        }

        private const int SettleFrames = 3;

        /// <summary>Frames between looks at an open hideout screen. A field read and a few rects.</summary>
        private const int WatchFrames = 15;

        /// <summary>
        /// While a hideout transfer screen stays open: plan again whenever its area
        /// grid is replaced or resized, or the filter window opens or closes. The grown
        /// panel was planned around whatever was there at the time, and a wider area
        /// grid from another tab would otherwise slide under it.
        /// </summary>
        private static void Watch(Action<string> log)
        {
            if (!_panel.isActiveAndEnabled || _screen == null)
            {
                _panel = null;
                return;
            }

            if (++_frames < WatchFrames) return;

            _frames = 0;

            var now = Signature(_screen);

            if (now == _signature) return;

            log(Label(_kind) + ": the area grid or filter window changed, planning again.");

            _signature = now;
            _retried = false;
            _phase = 0;
        }

        private static readonly Dictionary<Type, FieldInfo[]> WatchedFields = new Dictionary<Type, FieldInfo[]>();

        /// <summary>
        /// A number that changes when the hideout screen's layout does: the grids view
        /// under <c>_parent</c> (replaced on every tab change) and the filter window
        /// <c>_handoverItemsWindow</c>, by identity, visibility and size. Zero for any
        /// other screen, or when the fields are not there.
        /// </summary>
        private static int Signature(Component screen)
        {
            if (_kind != StashScreen.HideoutTransfer || screen == null) return 0;

            var type = screen.GetType();

            if (!WatchedFields.TryGetValue(type, out var fields))
            {
                fields = new[]
                {
                    AccessTools.Field(type, "_parent"),
                    AccessTools.Field(type, "_handoverItemsWindow"),
                };
                WatchedFields[type] = fields;
            }

            unchecked
            {
                var hash = 17;

                foreach (var field in fields)
                {
                    var value = field != null ? field.GetValue(screen) as Component : null;

                    if (value == null)
                    {
                        hash = hash * 31;
                        continue;
                    }

                    var node = value.transform;

                    hash = hash * 31 + (value.gameObject.activeInHierarchy ? 1 : 2);
                    hash = hash * 31 + Size(node);

                    for (var i = 0; i < node.childCount; i++)
                    {
                        var child = node.GetChild(i);

                        hash = hash * 31 + child.GetInstanceID();
                        hash = hash * 31 + (child.gameObject.activeInHierarchy ? 1 : 2);
                        hash = hash * 31 + Size(child);
                    }
                }

                return hash;
            }
        }

        private static int Size(Transform t)
        {
            var rt = t as RectTransform;

            if (rt == null) return 0;

            var r = rt.rect;

            return (int)Math.Round(r.width) * 7919 + (int)Math.Round(r.height);
        }

        // ---- planning and applying -------------------------------------------------

        /// <summary>
        /// Put the panel back to vanilla, map the screen, plan, and apply. Returns the
        /// report's opening, for the log.
        /// </summary>
        private static string Arrange(MonoBehaviour panel, Component screen, int gridColumns, float chrome)
        {
            var sb = new StringBuilder();

            sb.AppendLine(string.Format("===== {0}: stash panel =====", Label(_kind)));

            var cap = CapOf(panel);

            if (cap == null)
            {
                sb.AppendLine("cannot widen: no pinned stash panel above the grid.");
                return sb.ToString();
            }

            Restore(cap);

            var canvasRt = RootCanvas(panel);

            if (canvasRt == null || screen == null)
            {
                sb.AppendLine("cannot widen: no canvas or no screen above the stash panel.");
                return sb.ToString();
            }

            // The screen may have been shown this frame; its layout groups have not
            // placed the buttons yet.
            Canvas.ForceUpdateCanvases();

            var canvas = Measure(canvasRt, canvasRt);
            var panelBox = Measure(cap, canvasRt);

            sb.AppendLine(string.Format(
                "screen {0}x{1}; canvas {2:0}x{3:0}; stash panel '{4}' {5} ({6:0} px); grid {7} columns "
                + "needs {8:0} px with {9:0.0} px of chrome and {10:0} px of slack",
                Screen.width, Screen.height, canvas.Width, canvas.Height, cap.name, panelBox, panelBox.Width,
                gridColumns, ScreenLayout.WidthOfColumns(gridColumns) + chrome + ScreenLayout.ScrollSlack, chrome,
                ScreenLayout.ScrollSlack));

            if (panelBox.Width < 300f || panelBox.Width > canvas.Width * 0.6f)
            {
                sb.AppendLine(string.Format(
                    "cannot widen: '{0}' is {1:0} px, which is not a stash panel. The screen's layout "
                    + "has changed and widening it blind would be worse than leaving it alone.",
                    cap.name, panelBox.Width));
                return sb.ToString();
            }

            var driver = LayoutDriver(cap);

            if (driver != null)
            {
                sb.AppendLine(string.Format(
                    "cannot widen: '{0}' is sized by {1}, which would undo any change.", cap.name, driver));
                return sb.ToString();
            }

            var obstacles = new List<Obstacle>();

            Refs.Clear();
            Collect(screen.transform as RectTransform, cap, panelBox, canvas, canvasRt, obstacles, 0);

            foreach (var o in obstacles)
            {
                if (!o.Movable) continue;

                o.Inner = InnerBoxes(Refs[o], canvasRt);
            }

            _buttons = obstacles.FindAll(o => o.IsButton);

            sb.AppendLine("beside the stash panel (canvas units, y up):");

            foreach (var o in obstacles)
            {
                if (!o.IsButton && !o.Box.InBand(panelBox.YMin, panelBox.YMax)) continue;

                sb.AppendLine(string.Format(
                    "  {0} | {1}{2}{3}",
                    o.Name,
                    o.Box,
                    o.IsButton ? " | button" : string.Empty,
                    o.Movable ? " | may slide" : string.Empty));
            }

            var plan = ScreenLayout.Plan(canvas, panelBox, obstacles, gridColumns, chrome);

            sb.AppendLine("plan: " + plan.Why);

            if (!plan.Changed) return sb.ToString();

            var neighbour = plan.Neighbour != null ? Refs[plan.Neighbour] : null;

            Remember(cap, cap);

            if (neighbour != null) Remember(cap, neighbour);

            var parentScale = ScaleOf(cap.parent, canvasRt);

            var min = cap.offsetMin;
            var max = cap.offsetMax;

            cap.offsetMin = new Vector2(
                min.x + (plan.Left - panelBox.XMin) / parentScale.x,
                min.y + plan.Lift / parentScale.y);
            cap.offsetMax = new Vector2(max.x + (plan.Right - panelBox.XMax) / parentScale.x, max.y);

            if (neighbour != null)
            {
                var scale = ScaleOf(neighbour.parent, canvasRt).x;
                var dx = plan.Shift / scale;

                neighbour.offsetMin = new Vector2(neighbour.offsetMin.x - dx, neighbour.offsetMin.y);
                neighbour.offsetMax = new Vector2(neighbour.offsetMax.x - dx, neighbour.offsetMax.y);
            }

            // Anchors, pivots or a driver the checks above missed would put the panel
            // somewhere other than planned. Better vanilla than somewhere unplanned.
            var after = Measure(cap, canvasRt);

            if (Math.Abs(after.XMin - plan.Left) > 2f
                || Math.Abs(after.XMax - plan.Right) > 2f
                || Math.Abs(after.YMin - (panelBox.YMin + plan.Lift)) > 2f)
            {
                Restore(cap);
                sb.AppendLine(string.Format(
                    "put back: the panel landed at {0}, not where it was planned. Left vanilla.", after));
                return sb.ToString();
            }

            sb.AppendLine(string.Format(
                "widened: stash panel {0:0} -> {1:0} px ({2} columns){3}{4}",
                panelBox.Width,
                after.Width,
                plan.Columns,
                plan.Lift > 0f ? string.Format(", bottom edge up {0:0} px to clear the buttons", plan.Lift) : string.Empty,
                neighbour != null ? string.Format(", '{0}' slid {1:0} px left", neighbour.name, plan.Shift) : string.Empty));

            return sb.ToString();
        }

        /// <summary>
        /// Walk the screen and list everything drawn that the grown panel must stay
        /// clear of.
        ///
        /// A node that holds the stash panel, or already overlaps it, is not an
        /// obstacle -- it is a frame or a background the panel already sits in -- so
        /// its children are looked at instead. A node that overlaps nothing and draws
        /// something is an obstacle whole, children and all. That is what lets a
        /// button sitting inside a full-width bottom bar be found as itself, rather
        /// than the bar blocking the entire bottom of the screen.
        /// </summary>
        private static void Collect(
            RectTransform node,
            RectTransform cap,
            Box panel,
            Box canvas,
            RectTransform canvasRt,
            List<Obstacle> into,
            int depth)
        {
            if (node == null || depth > 40) return;

            for (var i = 0; i < node.childCount; i++)
            {
                var child = node.GetChild(i) as RectTransform;

                if (child == null || child == cap || !child.gameObject.activeInHierarchy) continue;

                var box = Measure(child, canvasRt);
                var empty = box.Width < 1f || box.Height < 1f;

                if (cap.IsChildOf(child) || empty || box.Overlaps(panel))
                {
                    Collect(child, cap, panel, canvas, canvasRt, into, depth + 1);
                    continue;
                }

                // Parked off the canvas, or drawing nothing: not in anybody's way.
                if (!box.Overlaps(canvas) || !Draws(child)) continue;

                var obstacle = new Obstacle
                {
                    Name = PathOf(child, canvasRt),
                    Box = box,
                    IsButton = IsButton(child),
                    Movable = !IsButton(child) && cap.IsChildOf(child.parent) && LayoutDriver(child) == null,
                };

                into.Add(obstacle);
                Refs[obstacle] = child;
            }
        }

        /// <summary>
        /// The drawn pieces inside a movable obstacle, for the check that sliding it
        /// does not put a button over one of them.
        /// </summary>
        private static Box[] InnerBoxes(RectTransform root, RectTransform canvasRt)
        {
            if (root == null) return new Box[0];

            var boxes = new List<Box>();

            foreach (var b in root.GetComponentsInChildren<Behaviour>(false))
            {
                if (b == null || !b.isActiveAndEnabled || !IsA(b.GetType(), GraphicTypeName)) continue;

                var rt = b.transform as RectTransform;

                if (rt == null) continue;

                var box = Measure(rt, canvasRt);

                if (box.Width >= 1f && box.Height >= 1f) boxes.Add(box);
            }

            return boxes.ToArray();
        }

        // ---- the check -------------------------------------------------------------

        private struct Verdict
        {
            internal string Text;
            internal string Summary;
            internal float Overflow;
        }

        /// <summary>
        /// The outcome, measured off the screen rather than argued: does the grid fit
        /// its viewport, and is every button clear of the panel.
        /// </summary>
        private static Verdict Check(Component gridView)
        {
            var sb = new StringBuilder();
            var gridRect = gridView.transform as RectTransform;
            var chain = StashMeasure.ChainOf(gridRect, gridView.GetComponentInParent<Canvas>());

            var viewport = chain.Usable - chain.Chrome;
            var drawn = gridRect != null ? gridRect.rect.width : 0f;
            var spare = viewport - drawn;

            var fit = spare < 0f
                ? string.Format("OVERFLOW by {0:0.0} px -- a horizontal scrollbar", -spare)
                : string.Format("fits, {0:0.0} px spare", spare);

            var line = string.Format("CHECK: viewport {0:0.0} px vs grid {1:0.0} px -- {2}", viewport, drawn, fit);

            sb.AppendLine(line);

            var canvasRt = RootCanvas(gridView);
            var cap = chain.Cap;
            var buttons = new StringBuilder();

            if (cap != null && canvasRt != null)
            {
                var panel = Measure(cap, canvasRt);
                var clear = new List<string>();
                var hit = new List<string>();

                foreach (var b in _buttons)
                {
                    RectTransform rt;

                    if (!Refs.TryGetValue(b, out rt) || !rt || !rt.gameObject.activeInHierarchy) continue;

                    var box = Measure(rt, canvasRt);
                    var name = rt.name;

                    if (box.Overlaps(panel)) hit.Add(name + " " + box);
                    else clear.Add(name);
                }

                buttons.Append(hit.Count == 0
                    ? string.Format("buttons clear of the panel: {0}",
                        clear.Count == 0 ? "none found" : string.Join(", ", clear.ToArray()))
                    : string.Format("BUTTON OVERLAP: {0}", string.Join("; ", hit.ToArray())));

                sb.AppendLine(buttons.ToString());
            }

            sb.Append("=============================");

            return new Verdict
            {
                Text = sb.ToString(),
                Summary = line + "; " + buttons,
                Overflow = spare < 0f ? -spare : 0f,
            };
        }

        // ---- remembering vanilla ---------------------------------------------------

        private struct Saved
        {
            internal RectTransform Rect;
            internal Vector2 OffsetMin;
            internal Vector2 OffsetMax;
        }

        /// <summary>Vanilla geometry of everything moved, per stash panel.</summary>
        private static readonly Dictionary<RectTransform, List<Saved>> Touched =
            new Dictionary<RectTransform, List<Saved>>();

        /// <summary>
        /// Record a transform's vanilla offsets, once. Always called after
        /// <see cref="Restore"/>, so what is recorded is vanilla.
        /// </summary>
        private static void Remember(RectTransform cap, RectTransform rect)
        {
            if (!Touched.TryGetValue(cap, out var list))
            {
                list = new List<Saved>();
                Touched[cap] = list;
            }

            if (list.Exists(s => s.Rect == rect)) return;

            list.Add(new Saved { Rect = rect, OffsetMin = rect.offsetMin, OffsetMax = rect.offsetMax });
        }

        /// <summary>
        /// Put back everything moved for this panel. Every open plans from vanilla,
        /// which is what makes opening the screen twice the same as opening it once.
        /// </summary>
        private static void Restore(RectTransform cap)
        {
            if (!Touched.TryGetValue(cap, out var list)) return;

            Touched.Remove(cap);

            foreach (var s in list)
            {
                if (!s.Rect) continue;

                s.Rect.offsetMin = s.OffsetMin;
                s.Rect.offsetMax = s.OffsetMax;
            }
        }

        // ---- geometry and names ----------------------------------------------------

        private static readonly Vector3[] Corners = new Vector3[4];

        /// <summary>A transform's rect in the root canvas's own units.</summary>
        private static Box Measure(RectTransform rt, RectTransform canvasRt)
        {
            rt.GetWorldCorners(Corners);

            var a = canvasRt.InverseTransformPoint(Corners[0]);
            var b = canvasRt.InverseTransformPoint(Corners[2]);

            return new Box(Math.Min(a.x, b.x), Math.Min(a.y, b.y), Math.Max(a.x, b.x), Math.Max(a.y, b.y));
        }

        /// <summary>Canvas units per local unit of <paramref name="parent"/>. 1 on any sane screen.</summary>
        private static Vector2 ScaleOf(Transform parent, RectTransform canvasRt)
        {
            if (parent == null) return Vector2.one;

            var p = parent.lossyScale;
            var c = canvasRt.lossyScale;

            var x = c.x != 0f ? p.x / c.x : 1f;
            var y = c.y != 0f ? p.y / c.y : 1f;

            return new Vector2(x > 0.001f ? x : 1f, y > 0.001f ? y : 1f);
        }

        /// <summary>The pinned ancestor that clips the grid, found the way the character screen's is.</summary>
        private static RectTransform CapOf(MonoBehaviour panel)
        {
            var rect = panel != null ? panel.transform as RectTransform : null;

            if (rect == null) return null;

            return StashMeasure.ChainOf(rect, panel.GetComponentInParent<Canvas>()).Cap;
        }

        private static RectTransform RootCanvas(Component c)
        {
            var canvas = c.GetComponentInParent<Canvas>();

            return canvas != null ? canvas.rootCanvas.transform as RectTransform : null;
        }

        /// <summary>The path from the root canvas, for the log.</summary>
        private static string PathOf(Transform t, Transform root)
        {
            var parts = new List<string>();

            for (var n = t; n != null && n != root; n = n.parent) parts.Add(n.name);

            parts.Reverse();

            return string.Join("/", parts.ToArray());
        }

        private static string Label(StashScreen kind)
        {
            switch (kind)
            {
                case StashScreen.ScavTransfer: return "scav loot transfer";
                case StashScreen.MailTransfer: return "mail items transfer";
                case StashScreen.HideoutTransfer: return "hideout area transfer";
                default: return kind.ToString();
            }
        }

        private const string GraphicTypeName = "UnityEngine.UI.Graphic";

        /// <summary>
        /// Whether anything under this node draws or takes clicks. A layout-only node
        /// with nothing in it is in nobody's way.
        /// </summary>
        private static bool Draws(RectTransform node)
        {
            foreach (var b in node.GetComponentsInChildren<Behaviour>(false))
            {
                if (b != null && b.isActiveAndEnabled && IsA(b.GetType(), GraphicTypeName)) return true;
            }

            return false;
        }

        /// <summary>
        /// A button: the game's own <c>DefaultUIButton</c>, or anything Unity would
        /// call selectable. Looked for on the node itself, not its children, so a
        /// panel holding a button is not itself a button.
        /// </summary>
        private static bool IsButton(RectTransform node)
        {
            foreach (var b in node.GetComponents<Behaviour>())
            {
                if (b == null) continue;

                var type = b.GetType();

                if (type.Name.IndexOf("Button", StringComparison.Ordinal) >= 0) return true;
                if (IsA(type, "UnityEngine.UI.Selectable")) return true;
            }

            return false;
        }

        /// <summary>
        /// What would overwrite a change to this rect: a layout group on its parent, or
        /// a fitter on itself. Null when nothing does.
        /// </summary>
        private static string LayoutDriver(RectTransform rect)
        {
            if (rect.parent != null)
            {
                foreach (var b in rect.parent.GetComponents<Behaviour>())
                {
                    if (b != null && b.enabled && IsA(b.GetType(), "UnityEngine.UI.LayoutGroup"))
                    {
                        return b.GetType().Name + " on '" + rect.parent.name + "'";
                    }
                }
            }

            foreach (var b in rect.GetComponents<Behaviour>())
            {
                if (b == null || !b.enabled) continue;

                var type = b.GetType();

                if (IsA(type, "UnityEngine.UI.ContentSizeFitter") || IsA(type, "UnityEngine.UI.AspectRatioFitter"))
                {
                    return type.Name;
                }
            }

            return null;
        }

        private static readonly Dictionary<string, bool> IsACache = new Dictionary<string, bool>();

        /// <summary>
        /// Type test by name. The probe does not reference UnityEngine.UI, and adding
        /// it for three type checks is not worth another assembly to keep in step.
        /// </summary>
        private static bool IsA(Type type, string fullName)
        {
            var key = type.AssemblyQualifiedName + "|" + fullName;

            if (IsACache.TryGetValue(key, out var yes)) return yes;

            yes = false;

            for (var t = type; t != null; t = t.BaseType)
            {
                if (t.FullName == fullName)
                {
                    yes = true;
                    break;
                }
            }

            IsACache[key] = yes;

            return yes;
        }
    }
}
