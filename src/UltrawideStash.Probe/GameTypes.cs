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

                Ready = true;
            }
            catch (Exception e)
            {
                Fail(e.Message);
            }
        }

        private static void Fail(string why)
        {
            Ready = false;
            Failure = why;
        }
    }
}
