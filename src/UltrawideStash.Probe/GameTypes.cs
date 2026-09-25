using System;
using System.Reflection;
using HarmonyLib;

namespace UltrawideStash.Probe
{
    /// <summary>
    /// Every game type, method and field this plugin touches, resolved by name at
    /// runtime, in one place.
    ///
    /// ## Why nothing here is a compile-time reference
    ///
    /// The <c>Assembly-CSharp.dll</c> sitting in <c>Managed</c> is not the one the
    /// game runs. The SPT Launcher applies
    /// <c>SPT_Data\Launcher\Patches\SPT-core\...\Assembly-CSharp.dll.delta</c> at
    /// startup, and that delta renames obfuscated types -- so an install that has
    /// never been launched still holds the unpatched original, and a plugin compiled
    /// against it will not load.
    ///
    /// Resolving by patched name through <c>AccessTools</c> sidesteps that entirely:
    /// the plugin builds against any install, launched or not. The price is that
    /// there is no compile-time checking of any game member, which is why this file
    /// exists and why nothing is ever looked up at the patch site.
    ///
    /// A rename in a future EFT build turns the probe off with a log line rather than
    /// throwing into the stash screen.
    /// </summary>
    internal static class GameTypes
    {
        /// <summary>True when everything below resolved and the probe may run.</summary>
        internal static bool Ready { get; private set; }

        /// <summary>Why <see cref="Ready"/> is false, for the log.</summary>
        internal static string Failure { get; private set; } = string.Empty;

        /// <summary>
        /// <c>EFT.UI.SimpleStashPanel</c> -- the right-hand side of the inventory
        /// screen. Its <c>Show</c> is where the stash is built.
        /// </summary>
        internal static Type SimpleStashPanel { get; private set; }

        /// <summary>
        /// <c>EFT.UI.SimpleStashPanel.Show(CompoundItem, InventoryController,
        /// ItemContext, bool, SortingTable, EStashSearchAvailability,
        /// InventoryController, EItemsTab)</c>. There is only one Show on the type,
        /// so it is taken by name.
        /// </summary>
        internal static MethodInfo SimpleStashPanelShow { get; private set; }

        /// <summary>
        /// Where <c>inRaid</c> sits in <see cref="SimpleStashPanelShow"/>'s arguments.
        ///
        /// The same panel draws the stash in the menu and a looted crate in raid, and
        /// Loot In Vicinity shows its Nearby Items grid through it too. This flag is
        /// the game's own answer to which one it is, so the widening never has to
        /// guess from the grid.
        /// </summary>
        internal static int SimpleStashPanelShowInRaid { get; private set; } = -1;

        /// <summary>
        /// <c>EFT.UI.ItemsPanel.Show(..., bool inRaid, ...)</c> -- the whole inventory
        /// screen, called on every open in the menu and in raid.
        ///
        /// Optional. It is what puts the screen back when the inventory opens in raid
        /// with no crate, where <see cref="SimpleStashPanelShow"/> never runs but a
        /// widening done in the hideout would still be on screen. Without it, a crate
        /// or Loot In Vicinity's panel opening still puts it back.
        /// </summary>
        internal static MethodInfo ItemsPanelShow { get; private set; }

        /// <summary>Where <c>inRaid</c> sits in <see cref="ItemsPanelShow"/>'s arguments.</summary>
        internal static int ItemsPanelShowInRaid { get; private set; } = -1;

        /// <summary>
        /// <c>EFT.UI.DragAndDrop.GridView</c> -- one per grid drawn. The stash screen
        /// has one for the stash itself and more for any opened container.
        /// </summary>
        internal static Type GridView { get; private set; }

        /// <summary>
        /// <c>GridView.Grid</c>, a public field holding the
        /// <c>EFT.InventoryLogic.Grid</c> the view is drawing.
        /// </summary>
        internal static FieldInfo GridViewGrid { get; private set; }

        /// <summary><c>EFT.InventoryLogic.Grid.GridWidth</c>, public getter.</summary>
        internal static PropertyInfo GridWidth { get; private set; }

        /// <summary><c>EFT.InventoryLogic.Grid.GridHeight</c>, public getter.</summary>
        internal static PropertyInfo GridHeight { get; private set; }

        /// <summary>
        /// <c>EFT.InventoryLogic.Grid.OutOfBoundsItems</c>. Optional: it is the one
        /// direct answer to "did widening strand anything", so it is worth reporting
        /// when present, but the probe is still useful without it.
        /// </summary>
        internal static PropertyInfo OutOfBoundsItems { get; private set; }

        /// <summary>
        /// <c>EFT.InventoryLogic.Grid.Layout</c>, the occupancy bitmap.
        ///
        /// Optional, and reported because Advanced Stash Sorting asserts
        /// <c>Layout.Count == GridWidth * GridHeight</c> in its <c>CopyLayout</c> and
        /// throws "Grid layout dimensions are inconsistent" otherwise. That invariant
        /// should always hold -- the grid is built from the template before anything
        /// sees it -- but if a width change ever broke it, that mod's sort would be
        /// the visible symptom and this line is the direct evidence.
        /// </summary>
        internal static PropertyInfo GridLayout { get; private set; }

        /// <summary>
        /// <c>EFT.InventoryLogic.CompoundItem.Grids</c>, a public <c>Grid[]</c>.
        /// Optional: it gives the stash's width at <c>Show</c>, before any GridView
        /// exists, so the scav and mail screens can be sized on their first frame.
        /// Without it they are sized a few frames later instead.
        /// </summary>
        internal static FieldInfo CompoundItemGrids { get; private set; }

        /// <summary>
        /// Resolve everything. Safe to call more than once; only the first call does
        /// any work.
        /// </summary>
        internal static void Resolve()
        {
            if (Ready || Failure.Length > 0) return;

            try
            {
                SimpleStashPanel = AccessTools.TypeByName("EFT.UI.SimpleStashPanel");

                if (SimpleStashPanel == null)
                {
                    Fail("EFT.UI.SimpleStashPanel not found");
                    return;
                }

                SimpleStashPanelShow = AccessTools.Method(SimpleStashPanel, "Show");

                if (SimpleStashPanelShow == null)
                {
                    Fail("SimpleStashPanel.Show not found");
                    return;
                }

                SimpleStashPanelShowInRaid = InRaidIndex(SimpleStashPanelShow);

                // Required. Without it the probe cannot tell the stash from a crate
                // in raid, and widening a raid screen is exactly what must not happen.
                if (SimpleStashPanelShowInRaid < 0)
                {
                    Fail("SimpleStashPanel.Show has no inRaid parameter");
                    return;
                }

                var itemsPanel = AccessTools.TypeByName("EFT.UI.ItemsPanel");

                if (itemsPanel != null)
                {
                    ItemsPanelShow = AccessTools.Method(itemsPanel, "Show");

                    if (ItemsPanelShow != null)
                    {
                        ItemsPanelShowInRaid = InRaidIndex(ItemsPanelShow);
                    }
                }

                GridView = AccessTools.TypeByName("EFT.UI.DragAndDrop.GridView");

                if (GridView == null)
                {
                    Fail("EFT.UI.DragAndDrop.GridView not found");
                    return;
                }

                GridViewGrid = AccessTools.Field(GridView, "Grid");

                if (GridViewGrid == null)
                {
                    Fail("GridView.Grid not found");
                    return;
                }

                var grid = AccessTools.TypeByName("EFT.InventoryLogic.Grid");

                if (grid == null)
                {
                    Fail("EFT.InventoryLogic.Grid not found");
                    return;
                }

                GridWidth = AccessTools.Property(grid, "GridWidth");
                GridHeight = AccessTools.Property(grid, "GridHeight");

                if (GridWidth == null || GridHeight == null)
                {
                    Fail("Grid.GridWidth / Grid.GridHeight not found");
                    return;
                }

                // Optional -- absence costs one line of the report, not the report.
                OutOfBoundsItems = AccessTools.Property(grid, "OutOfBoundsItems");
                GridLayout = AccessTools.Property(grid, "Layout");

                var compound = AccessTools.TypeByName("EFT.InventoryLogic.CompoundItem");

                if (compound != null) CompoundItemGrids = AccessTools.Field(compound, "Grids");

                Ready = true;
            }
            catch (Exception e)
            {
                Fail(e.Message);
            }
        }

        /// <summary>
        /// The position of the <c>bool inRaid</c> parameter, or -1. Found by name and
        /// type rather than by position, so a reordered signature is caught instead of
        /// read as some other flag.
        /// </summary>
        private static int InRaidIndex(MethodInfo method)
        {
            var parameters = method.GetParameters();

            for (var i = 0; i < parameters.Length; i++)
            {
                if (parameters[i].ParameterType == typeof(bool)
                    && string.Equals(parameters[i].Name, "inRaid", StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// Whether a patched call's <c>inRaid</c> argument is true. An argument that
        /// cannot be read counts as in raid: leaving a menu screen vanilla is harmless,
        /// widening a raid screen is not.
        /// </summary>
        internal static bool InRaid(object[] args, int index)
        {
            if (args == null || index < 0 || index >= args.Length) return true;

            return !(args[index] is bool inRaid) || inRaid;
        }

        /// <summary>
        /// Width of an item's first grid -- the stash's own -- or 0 when it cannot be
        /// read. Never throws.
        /// </summary>
        internal static int FirstGridWidth(object compoundItem)
        {
            try
            {
                if (compoundItem == null || CompoundItemGrids == null) return 0;
                if (!CompoundItemGrids.DeclaringType.IsInstanceOfType(compoundItem)) return 0;

                var grids = CompoundItemGrids.GetValue(compoundItem) as Array;

                if (grids == null || grids.Length == 0 || grids.GetValue(0) == null) return 0;

                return (int)GridWidth.GetValue(grids.GetValue(0), null);
            }
            catch
            {
                return 0;
            }
        }

        private static void Fail(string why)
        {
            Ready = false;
            Failure = why;
        }
    }
}
