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

        /// <summary>
        /// <c>EFT.UI.TraderDealScreen</c>: buying from and selling to a trader. The stash
        /// sits at the right edge, the deal panel in the middle, the showcase on the left.
        /// </summary>
        Trader,

        /// <summary>Prestige, or the in-raid transit transfer.</summary>
        Other,
    }

    /// <summary>
    /// Widens the stash panel on the scav loot transfer, the mail transfer, the
    /// hideout area transfer and the trader screens.
    ///
    /// The trader screen (added in 1.1.0) is laid out the other way round: it
    /// already fills the canvas, with the stash at the right edge, so there is no
    /// room on the right. The panel grows left into the gap beside the deal panel,
    /// and the deal panel may slide left into the gap beside the showcase.
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

        /// <summary>The trader screen, matched through the base chain like the ones below.</summary>
        private const string TraderScreenName = "TraderDealScreen";

        /// <summary>
        /// Screens that draw the stash and are left alone: there is no free width
        /// beside the stash on them, or they are in raid. Matched by simple type name
        /// anywhere in a component's base chain, generic arity stripped.
        /// </summary>
        private static readonly HashSet<string> OtherScreens = new HashSet<string>(StringComparer.Ordinal)
        {
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

                        if (SimpleName(t) == TraderScreenName)
                        {
                            screen = c;
                            return StashScreen.Trader;
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
            _rewriteSaid = false;
            _rewrites = 0;

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
                Arrange(panel, screen, gridColumns, SteadyChrome(panel, StashMeasure.Chrome));
                SayMoved(log);
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

                var gridView = GridViewOf(_panel, _screen);

                if (gridView == null)
                {
                    if (++_frames > 120)
                    {
                        // The trader screen is laid out from its own Show, before the
                        // trader's goods have loaded; the stash grid comes with them,
                        // and its own Show begins again. Nothing went wrong.
                        if (_kind != StashScreen.Trader)
                        {
                            log(Label(_kind) + ": gave up waiting for the stash grid after 120 frames.");
                        }

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

                    // Measured on the laid-out screen, before the restore inside
                    // Arrange changes the panel. Bounded like LearnChrome: an absurd
                    // number keeps the known one.
                    var measured = ChromeOf(gridView, _panel);
                    var fallback = measured >= 8f && measured <= 200f ? measured : StashMeasure.Chrome;
                    var chrome = SteadyChrome(_panel, fallback);

                    _arranged = Arrange(_panel, _screen, columns, chrome + _extraBoost);
                    SayMoved(log);
                    _signature = Signature(_screen);
                    _phase = 1;
                    return;
                }

                var said = Check(gridView);

                // A panel something else has resized since it was planned says nothing
                // about the chrome; the guard puts it back. Adding its overflow to the
                // chrome is what once left a 19-column grid in a 1296 px panel with an
                // 84 px black strip beside it.
                var resized = _guardedCap != null && Math.Abs(_guardedCap.rect.width - _planned) > 2f;

                if (said.Overflow > 0f && resized)
                {
                    _phase = 0;
                    return;
                }

                var shortBy = ScreenLayout.ShortOfPlan(_plannedColumns, said.Viewport);

                if (said.Overflow > 0f && shortBy > 0f && !_retried)
                {
                    // The chrome here differs from what was measured: the columns the
                    // plan made room for do not fit. Plan again with the difference
                    // added; the check after it is the last word. An overflow from a
                    // plan that ran out of room is not this, and is left alone.
                    _retried = true;
                    _extraBoost += shortBy + 1f;
                    _phase = 0;
                    return;
                }

                if (Reported.Add(ReportKey()))
                {
                    log(_arranged + said.Text);
                }
                else
                {
                    log(Label(_kind) + ": " + said.Summary);
                }

                // The hideout screen swaps its area grid on a tab change without
                // showing the stash again, and opens a filter window beside it. The
                // trader screen swaps its deal panel for the sell table the same way
                // on Buy / Sell. Keep an eye on both while the screen is open.
                if (_kind == StashScreen.HideoutTransfer || _kind == StashScreen.Trader)
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

        /// <summary>
        /// The stash panel's width less its clipping mask's: the width the grid never
        /// gets. It needs no grid, so it is the same number at <c>Show</c> as three
        /// frames later, and the first plan is the final one. When the two differed
        /// (48 px guessed, 10 px measured on the trader screen) the panel was sized
        /// twice per open and the deal column jumped 38 px on every trader click.
        /// </summary>
        private static float SteadyChrome(MonoBehaviour panel, float fallback)
        {
            var cap = CapOf(panel);
            var mask = MaskOf(panel);

            if (cap == null || mask == null) return fallback;

            var chrome = cap.rect.width - mask.rect.width;

            return chrome >= 8f && chrome <= 200f ? chrome : fallback;
        }

        /// <summary>
        /// The mask the stash grid scrolls inside: the first one under the panel,
        /// nearest first, wider than a few cells.
        /// </summary>
        private static RectTransform MaskOf(MonoBehaviour panel)
        {
            var root = panel != null ? panel.transform as RectTransform : null;

            if (root == null) return null;

            var level = new List<RectTransform> { root };

            for (var depth = 0; depth < 5 && level.Count > 0; depth++)
            {
                var next = new List<RectTransform>();

                foreach (var node in level)
                {
                    if (node != root && Clips(node) && node.rect.width >= 200f) return node;

                    for (var i = 0; i < node.childCount; i++)
                    {
                        var child = node.GetChild(i) as RectTransform;

                        if (child != null) next.Add(child);
                    }
                }

                level = next;
            }

            return null;
        }

        /// <summary>
        /// The stash panel's width less the viewport the grid is drawn into. Taken
        /// against the panel <see cref="Arrange"/> resizes, not the first pinned node
        /// above the grid: on the trader screen that is a holder sized to the grid
        /// itself (1206 px round a 1198 px grid), which made the chrome 259 px rather
        /// than the scrollbar's 10.
        /// </summary>
        private static float ChromeOf(Component gridView, MonoBehaviour panel)
        {
            var chain = StashMeasure.ChainOf(gridView.transform as RectTransform, gridView.GetComponentInParent<Canvas>());
            var cap = CapOf(panel);

            if (cap == null || chain.Viewport <= 0f) return chain.Chrome;

            return cap.rect.width - chain.Viewport;
        }

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

            log(Label(_kind) + (_kind == StashScreen.Trader
                ? ": the deal panel changed (Buy / Sell), planning again."
                : ": the area grid or filter window changed, planning again."));

            _signature = now;
            _retried = false;
            _phase = 0;
        }

        private static readonly Dictionary<Type, FieldInfo[]> WatchedFields = new Dictionary<Type, FieldInfo[]>();

        /// <summary>
        /// A number that changes when a watched screen's layout does. Hideout: the
        /// grids view under <c>_parent</c> (replaced on every tab change) and the filter
        /// window <c>_handoverItemsWindow</c>, by identity, visibility and size of each
        /// child. Trader: the buy panel <c>_barterSchemePanel</c> and the sell table
        /// <c>_tradingTable</c>, by visibility and size only -- their children are
        /// rebuilt on every item clicked, and that is no reason to plan again. Zero for
        /// any other screen, or when the fields are not there.
        /// </summary>
        private static int Signature(Component screen)
        {
            if (screen == null) return 0;

            var trader = _kind == StashScreen.Trader;

            if (_kind != StashScreen.HideoutTransfer && !trader) return 0;

            var type = screen.GetType();

            if (!WatchedFields.TryGetValue(type, out var fields))
            {
                fields = trader
                    ? new[]
                    {
                        AccessTools.Field(type, "_barterSchemePanel"),
                        AccessTools.Field(type, "_tradingTable"),
                    }
                    : new[]
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

                    if (trader) continue;

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

        private static readonly Dictionary<Type, FieldInfo> StashGridFields = new Dictionary<Type, FieldInfo>();

        /// <summary>
        /// The stash's GridView: the tallest under the panel, as everywhere else. The
        /// trader screen shows its stash grid itself (<c>_stashGridView</c>, right after
        /// the panel), so if it is not under the panel it is read off the screen.
        /// </summary>
        private static Component GridViewOf(MonoBehaviour panel, Component screen)
        {
            var view = StashMeasure.StashGridView(panel);

            if (view != null || _kind != StashScreen.Trader || screen == null) return view;

            var type = screen.GetType();

            if (!StashGridFields.TryGetValue(type, out var field))
            {
                field = AccessTools.Field(type, "_stashGridView");
                StashGridFields[type] = field;
            }

            var fromScreen = field != null ? field.GetValue(screen) as Component : null;

            if (fromScreen == null || !fromScreen.gameObject.activeInHierarchy) return null;

            return GameTypes.GridViewGrid.GetValue(fromScreen) != null ? fromScreen : null;
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

            Applied.Clear();
            _plannedColumns = 0;

            var cap = CapOf(panel);

            if (cap == null)
            {
                sb.AppendLine("cannot widen: no pinned stash panel above the grid.");
                return sb.ToString();
            }

            var canvasRt = RootCanvas(panel) ?? (screen != null ? RootCanvas(screen) : null);
            var before = canvasRt != null ? Placed(cap, canvasRt) : null;

            Restore(cap);

            if (canvasRt == null || screen == null)
            {
                sb.AppendLine("cannot widen: no canvas or no screen above the stash panel.");
                return sb.ToString();
            }

            var root = screen.transform as RectTransform;
            var stretched = _kind == StashScreen.Trader ? StretchScreen(cap, root, canvasRt) : null;

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

            if (stretched != null) sb.AppendLine(stretched);

            // The trader screen's layout has never been read off a live game. Say what
            // it is, once per resolution, so the first log answers any question the
            // plan below leaves open.
            if (_kind == StashScreen.Trader && !Reported.Contains(ReportKey()))
            {
                sb.AppendLine("trader screen, as laid out (canvas units, y up):");
                AppendTree(sb, screen.transform as RectTransform, cap, canvasRt, 0);
            }

            if (panelBox.Width < 300f || panelBox.Width > canvas.Width * 0.6f)
            {
                sb.AppendLine(string.Format(
                    "cannot widen: '{0}' is {1:0} px, which is not a stash panel. The screen's layout "
                    + "has changed and widening it blind would be worse than leaving it alone.",
                    cap.name, panelBox.Width));
                if (stretched != null) Restore(cap);
                return sb.ToString();
            }

            var driver = LayoutDriver(cap);

            if (driver != null)
            {
                sb.AppendLine(string.Format(
                    "cannot widen: '{0}' is sized by {1}, which would undo any change.", cap.name, driver));
                if (stretched != null) Restore(cap);
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

            var overhang = LeftOverhang(cap, canvasRt, panelBox);

            if (overhang > 0f)
            {
                sb.AppendLine(string.Format(
                    "the panel draws {0:0} px past its own left edge (a toolbar strip); kept clear too", overhang));
            }

            var plan = ScreenLayout.Plan(canvas, panelBox, obstacles, gridColumns, chrome, overhang);

            sb.AppendLine("plan: " + plan.Why);

            if (!plan.Changed)
            {
                // A stretch that buys nothing only moves the showcase to the edge.
                if (stretched != null)
                {
                    Restore(cap);
                    sb.AppendLine("the stretch is undone: it made no room the plan could use.");
                }

                return sb.ToString();
            }

            var neighbour = plan.Neighbour != null ? Refs[plan.Neighbour] : null;

            Remember(cap, cap);

            var slid = new List<RectTransform>();

            if (neighbour != null)
            {
                slid.Add(neighbour);

                foreach (var r in plan.Riders) slid.Add(Refs[r]);
            }

            foreach (var rt in slid) Remember(cap, rt);

            var parentScale = ScaleOf(cap.parent, canvasRt);

            var min = cap.offsetMin;
            var max = cap.offsetMax;

            cap.offsetMin = new Vector2(
                min.x + (plan.Left - panelBox.XMin) / parentScale.x,
                min.y + plan.Lift / parentScale.y);
            cap.offsetMax = new Vector2(max.x + (plan.Right - panelBox.XMax) / parentScale.x, max.y);

            foreach (var rt in slid)
            {
                var scale = ScaleOf(rt.parent, canvasRt).x;
                var dx = plan.Shift / scale;

                rt.offsetMin = new Vector2(rt.offsetMin.x - dx, rt.offsetMin.y);
                rt.offsetMax = new Vector2(rt.offsetMax.x - dx, rt.offsetMax.y);
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

            _moved = before != null ? MovedOnScreen(before, Placed(cap, canvasRt)) : null;

            // What the guard holds the screen to until the next plan.
            Applied.Add(new Saved { Rect = cap, OffsetMin = cap.offsetMin, OffsetMax = cap.offsetMax });

            foreach (var rt in slid) Applied.Add(new Saved { Rect = rt, OffsetMin = rt.offsetMin, OffsetMax = rt.offsetMax });

            if (stretched != null) Applied.Add(new Saved { Rect = root, OffsetMin = root.offsetMin, OffsetMax = root.offsetMax });

            _plannedColumns = plan.Columns;
            _guarded = panel;
            _guardedScreen = screen;
            _guardedColumns = gridColumns;
            _guardedChrome = chrome;
            _guardedCap = cap;
            _planned = after.Width;

            sb.AppendLine(string.Format(
                "widened: stash panel {0:0} -> {1:0} px ({2} columns){3}{4}",
                panelBox.Width,
                after.Width,
                plan.Columns,
                plan.Lift > 0f ? string.Format(", bottom edge up {0:0} px to clear the buttons", plan.Lift) : string.Empty,
                neighbour != null
                    ? string.Format(", '{0}' slid {1:0} px left{2}", neighbour.name, plan.Shift,
                        plan.Riders.Count > 0
                            ? " with " + string.Join(", ", plan.Riders.ConvertAll(r => "'" + Refs[r].name + "'").ToArray())
                            : string.Empty)
                    : string.Empty));

            return sb.ToString();
        }

        /// <summary>
        /// Make the trader screen span the canvas when it is a narrower frame inside it,
        /// as it is at 32:9. Horizontal only, and through offsets, so it is the same
        /// whatever the screen's anchors are: the stash panel, pinned to the screen's
        /// right edge, rides out to the canvas edge, the showcase to the left one, and
        /// the deal column stays in the middle -- the 21:9 shape.
        ///
        /// Remembered like everything else moved for the panel, so the next
        /// <see cref="Restore"/> puts it back. Nothing past the frame clips: the first
        /// 32:9 screenshot shows the stash drawn out to the monitor's right edge.
        /// </summary>
        /// <returns>A line for the log when it stretched, else null.</returns>
        private static string StretchScreen(RectTransform cap, RectTransform root, RectTransform canvasRt)
        {
            if (root == null || root == canvasRt) return null;

            // The stash panel has to be pinned to this screen's right edge for the
            // stretch to carry it there. It is on the trader screen; anything else is
            // a layout this has not seen.
            if (cap.parent != root) return null;
            if (Math.Abs(cap.anchorMin.x - 1f) > 0.001f || Math.Abs(cap.anchorMax.x - 1f) > 0.001f) return null;

            var screenBox = Measure(root, canvasRt);
            var canvas = Measure(canvasRt, canvasRt);

            if (!ScreenLayout.StretchToCanvas(screenBox, canvas, out var left, out var right)) return null;

            Remember(cap, root);

            var scale = ScaleOf(root.parent, canvasRt).x;

            root.offsetMin = new Vector2(root.offsetMin.x - left / scale, root.offsetMin.y);
            root.offsetMax = new Vector2(root.offsetMax.x + right / scale, root.offsetMax.y);

            return string.Format(
                "stretched: the trader screen was a {0:0} px frame in a {1:0} px canvas; it now spans it.",
                screenBox.Width, canvas.Width);
        }

        private static string ReportKey()
        {
            return _kind + "@" + Screen.width + "x" + Screen.height;
        }

        /// <summary>
        /// The screen's active nodes, four levels down, skipping slivers narrower than
        /// 40 px. Diagnostic only.
        /// </summary>
        private static void AppendTree(StringBuilder sb, RectTransform node, RectTransform cap, RectTransform canvasRt, int depth, int maxDepth = 4)
        {
            if (node == null || depth > maxDepth) return;

            for (var i = 0; i < node.childCount; i++)
            {
                var child = node.GetChild(i) as RectTransform;

                if (child == null || !child.gameObject.activeInHierarchy) continue;

                var box = Measure(child, canvasRt);

                if (box.Width < 40f && child != cap && !cap.IsChildOf(child)) continue;

                var names = new List<string>();

                foreach (var c in child.GetComponents<Component>())
                {
                    if (c != null && !(c is Transform)) names.Add(c.GetType().Name);
                }

                sb.AppendLine(string.Format(
                    "  {0}{1}{2} | {3} | ax {4:0.00}-{5:0.00}{6} | {7}",
                    new string(' ', depth * 2),
                    child.name,
                    child == cap ? "  <-- the stash panel" : string.Empty,
                    box,
                    child.anchorMin.x,
                    child.anchorMax.x,
                    LayoutDriver(child) != null ? " | driven" : string.Empty,
                    string.Join(",", names.ToArray())));

                // Nothing inside a mask or a grid is laid out; it is scrolled content.
                if (!Clips(child) && child.GetComponent(GameTypes.GridView) == null)
                {
                    AppendTree(sb, child, cap, canvasRt, depth + 1, maxDepth);
                }
            }
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
                    CanRide = cap.IsChildOf(child.parent) && LayoutDriver(child) == null,
                };

                into.Add(obstacle);
                Refs[obstacle] = child;
            }
        }

        /// <summary>
        /// How far anything the stash panel draws reaches left of the panel's own
        /// rect. The trader screen's filter strip hangs about 36 px outside it. What
        /// sits inside a mask, or inside a grid, is scrolled content and is skipped;
        /// more than 150 px is not a strip and is ignored.
        ///
        /// Read whether or not the panel is showing, and the widest ever seen per
        /// screen is kept. The trader screen is laid out from its own Show, while the
        /// stash panel is still hidden; reading only what was drawn found no strip
        /// there, found it when the stash appeared, and slid the deal column 38 px
        /// more on every trader's first load.
        /// </summary>
        private static float LeftOverhang(RectTransform cap, RectTransform canvasRt, Box panel)
        {
            var left = panel.XMin;

            OverhangWalk(cap, canvasRt, ref left, 0);

            var overhang = panel.XMin - left;

            overhang = overhang > 1f && overhang <= 150f ? overhang : 0f;

            float seen;

            if (OverhangSeen.TryGetValue(_kind, out seen) && seen > overhang) return seen;

            OverhangSeen[_kind] = overhang;

            return overhang;
        }

        private static readonly Dictionary<StashScreen, float> OverhangSeen = new Dictionary<StashScreen, float>();

        private static void OverhangWalk(RectTransform node, RectTransform canvasRt, ref float left, int depth)
        {
            if (depth > 6) return;

            for (var i = 0; i < node.childCount; i++)
            {
                var child = node.GetChild(i) as RectTransform;

                // activeSelf, not activeInHierarchy: the panel itself may be hidden.
                if (child == null || !child.gameObject.activeSelf) continue;
                if (child.GetComponent(GameTypes.GridView) != null) continue;

                if (DrawsItself(child))
                {
                    var box = Measure(child, canvasRt);

                    if (box.Width >= 1f && box.Height >= 1f) left = Math.Min(left, box.XMin);
                }

                if (!Clips(child)) OverhangWalk(child, canvasRt, ref left, depth + 1);
            }
        }

        /// <summary>A mask: whatever is under it is drawn only inside it.</summary>
        private static bool Clips(RectTransform node)
        {
            foreach (var b in node.GetComponents<Behaviour>())
            {
                if (b == null || !b.enabled) continue;

                var name = b.GetType().Name;

                if (name == "Mask" || name == "RectMask2D") return true;
            }

            return false;
        }

        /// <summary>
        /// An enabled graphic on the node itself, not its children -- enabled, not
        /// necessarily drawn yet, for the same reason as the walk above.
        /// </summary>
        private static bool DrawsItself(RectTransform node)
        {
            foreach (var b in node.GetComponents<Behaviour>())
            {
                if (b != null && b.enabled && IsA(b.GetType(), GraphicTypeName)) return true;
            }

            return false;
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

        // ---- holding the layout ----------------------------------------------------

        /// <summary>The offsets last applied: the panel and everything slid for it.</summary>
        private static readonly List<Saved> Applied = new List<Saved>();

        private static MonoBehaviour _guarded;

        private static Component _guardedScreen;

        private static int _guardedColumns;

        private static float _guardedChrome;

        private static RectTransform _guardedCap;

        /// <summary>The panel's planned width, in its own units.</summary>
        private static float _planned;

        /// <summary>Columns the last plan made room for; zero when it widened nothing.</summary>
        private static int _plannedColumns;

        private static bool _rewriteSaid;

        /// <summary>Re-layouts the guard has done this open; see <see cref="MaxRewrites"/>.</summary>
        private static int _rewrites;

        /// <summary>
        /// Something rewriting the layout every frame would be fought every frame. Two
        /// seconds of that is enough to say so and stop.
        /// </summary>
        private const int MaxRewrites = 120;

        /// <summary>
        /// From the plugin's LateUpdate: after the game's Update, its coroutines and its
        /// animations, before the frame is drawn. If anything has put back what the
        /// last plan set -- on the trader screen the stash panel ('Right Person') is
        /// returned to its own width when the screen is shown again -- plan again now,
        /// so the frame is drawn as planned. Four float compares per moved rect while a
        /// widened screen is showing; nothing otherwise.
        /// </summary>
        internal static void Guard(Action<string> log)
        {
            if (Applied.Count == 0) return;

            if (!_guarded || _guardedCap == null)
            {
                Applied.Clear();
                return;
            }

            if (!_guardedCap.gameObject.activeInHierarchy) return;

            string changed = null;

            foreach (var a in Applied)
            {
                if (!a.Rect) continue;

                if ((a.Rect.offsetMin - a.OffsetMin).sqrMagnitude > 0.25f || (a.Rect.offsetMax - a.OffsetMax).sqrMagnitude > 0.25f)
                {
                    changed = a.Rect.name;
                    break;
                }
            }

            if (changed == null) return;

            if (++_rewrites > MaxRewrites)
            {
                Applied.Clear();
                log(string.Format(
                    "{0}: '{1}' keeps being moved back by something else; stopped holding the layout for this open.",
                    Label(_kind), changed));
                return;
            }

            try
            {
                Arrange(_guarded, _guardedScreen, _guardedColumns, _guardedChrome);
                _moved = null;

                if (!_rewriteSaid)
                {
                    _rewriteSaid = true;
                    log(string.Format(
                        "{0}: something else moved '{1}' after it was laid out; laid out again before the frame was drawn.",
                        Label(_kind), changed));
                }
            }
            catch (Exception e)
            {
                Applied.Clear();
                log(Label(_kind) + ": could not hold the layout -- " + e.Message);
            }
        }

        // ---- seeing a re-plan move things --------------------------------------------

        /// <summary>
        /// Set by <see cref="Arrange"/> when a re-plan left something visibly
        /// somewhere else than it was: a jiggle the player can see. Logged by the
        /// caller, which has the log.
        /// </summary>
        private static string _moved;

        /// <summary>
        /// Where the panel and everything moved for it sit now, while the screen is
        /// showing. Null when it is not showing or nothing has been moved yet.
        /// </summary>
        private static Dictionary<RectTransform, Box> Placed(RectTransform cap, RectTransform canvasRt)
        {
            if (!cap.gameObject.activeInHierarchy || !Touched.TryGetValue(cap, out var list)) return null;

            var placed = new Dictionary<RectTransform, Box>();

            foreach (var saved in list)
            {
                if (saved.Rect && saved.Rect.gameObject.activeInHierarchy) placed[saved.Rect] = Measure(saved.Rect, canvasRt);
            }

            return placed;
        }

        private static string MovedOnScreen(Dictionary<RectTransform, Box> before, Dictionary<RectTransform, Box> after)
        {
            if (after == null) return null;

            var moved = new List<string>();

            foreach (var pair in before)
            {
                if (!after.TryGetValue(pair.Key, out var now)) continue;

                var was = pair.Value;

                if (Math.Abs(was.XMin - now.XMin) > 1f || Math.Abs(was.XMax - now.XMax) > 1f)
                {
                    moved.Add(string.Format("'{0}' x {1:0}..{2:0} -> {3:0}..{4:0}", pair.Key.name, was.XMin, was.XMax, now.XMin, now.XMax));
                }
            }

            return moved.Count == 0 ? null : "re-plan moved things on screen: " + string.Join("; ", moved.ToArray());
        }

        private static void SayMoved(Action<string> log)
        {
            if (_moved == null) return;

            log(Label(_kind) + ": " + _moved);
            _moved = null;
        }

        // ---- the check -------------------------------------------------------------

        private struct Verdict
        {
            internal string Text;
            internal string Summary;
            internal float Overflow;
            internal float Viewport;
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

            var mask = _panel ? MaskOf(_panel) : null;
            var viewport = mask != null ? mask.rect.width : chain.Usable - chain.Chrome;
            var drawn = gridRect != null ? gridRect.rect.width : 0f;
            var spare = viewport - drawn;

            var fit = spare < 0f
                ? string.Format("OVERFLOW by {0:0.0} px -- a horizontal scrollbar", -spare)
                : string.Format("fits, {0:0.0} px spare", spare);

            var line = string.Format("CHECK: viewport {0:0.0} px vs grid {1:0.0} px -- {2}", viewport, drawn, fit);

            sb.AppendLine(line);

            var canvasRt = RootCanvas(gridView);
            var cap = _panel ? CapOf(_panel) : chain.Cap;
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
                Viewport = viewport,
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
                case StashScreen.Trader: return "trader";
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
