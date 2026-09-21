namespace UltrawideStash.Server;

/// <summary>
/// The whole shape decision, as pure arithmetic over ints.
///
/// This is deliberately separate from <see cref="StashWidener"/> and touches no SPT
/// type, because it is the only part worth testing and the rest is plumbing. Feed it
/// a stash's vanilla dimensions and the player's wishes and it hands back the new
/// dimensions, or a refusal with a reason.
/// </summary>
public static class StashLayout
{
    /// <summary>
    /// The smallest width worth calling a stash. Below this the grid stops being a
    /// stash and starts being a column.
    /// </summary>
    public const int MinColumns = StashFit.MinColumns;

    /// <summary>
    /// A typo guard, and explicitly **not** the real limit.
    ///
    /// Until 0.6.0 this was 40, worked out from a 3440x1440 canvas of 2580 logical
    /// units. That is the ceiling for one monitor: on the 1920-wide canvas every 16:9
    /// screen gets -- 1080p, 1440p and 4K alike -- 40 columns is 2521px, some 601px
    /// wider than the whole screen, so the cap protected precisely the people who
    /// needed it least.
    ///
    /// The screen-aware ceiling now lives in <see cref="ColumnChoice"/>, which prefers
    /// the probe's measurement and falls back to <see cref="StashFit.ConservativeColumns"/>.
    /// What is left here is only a bound on absurdity.
    /// </summary>
    public const int MaxColumns = StashFit.AbsoluteMaxColumns;

    /// <summary>
    /// The result of planning one stash. <see cref="Applied"/> is false when the
    /// plan was refused, and <see cref="Reason"/> then says why in a line fit for
    /// the server log.
    /// </summary>
    public readonly record struct Plan(
        int Columns,
        int Rows,
        bool Applied,
        string Reason)
    {
        public int Capacity => Columns * Rows;

        /// <summary>The grid's drawn width in canvas pixels, border included.</summary>
        public int PixelWidth => Columns * 63 + 1;
    }

    /// <summary>
    /// Work out what one stash should become.
    /// </summary>
    /// <param name="vanillaColumns">The template's own cellsH, always 10 in 4.1.5.</param>
    /// <param name="vanillaRows">The template's own cellsV -- 30, 40, 50, 68 or 72.</param>
    /// <param name="columns">The width the player asked for.</param>
    /// <param name="compensateRows">
    /// True to hold total capacity at vanilla by shortening the stash as it widens.
    /// False to keep every row and simply gain columns.
    /// </param>
    /// <param name="deepestOccupiedRow">
    /// One past the last row that must stay addressable, or 0 when nothing is stored.
    /// Rows are never cut below this, because an item outside the grid is an item the
    /// player has lost. See <see cref="StashOccupancy"/>.
    ///
    /// The caller must pass the **ladder-aware** floor from
    /// <see cref="StashLadder.RowFloors"/>, not just the depth of the profiles sitting
    /// on this template. A Standard-edition player moves up through these templates as
    /// they upgrade the hideout's Stash area, so a rung planned shorter than the rung
    /// below it strands their items the moment they upgrade.
    /// </param>
    public static Plan For(
        int vanillaColumns,
        int vanillaRows,
        int columns,
        bool compensateRows,
        int deepestOccupiedRow)
    {
        if (vanillaColumns <= 0 || vanillaRows <= 0)
        {
            return new Plan(vanillaColumns, vanillaRows, false,
                $"template reports {vanillaColumns}x{vanillaRows}, which is not a grid");
        }

        if (columns < MinColumns || columns > MaxColumns)
        {
            return new Plan(vanillaColumns, vanillaRows, false,
                $"columns {columns} is outside {MinColumns}-{MaxColumns}");
        }

        // Narrowing stands items in the cut-off columns outside the grid, exactly as
        // shortening does. This mod exists to make the stash wider; it declines to be
        // the thing that makes it narrower.
        if (columns < vanillaColumns)
        {
            return new Plan(vanillaColumns, vanillaRows, false,
                $"columns {columns} is narrower than vanilla {vanillaColumns}; refusing to shrink");
        }

        if (columns == vanillaColumns)
        {
            return new Plan(vanillaColumns, vanillaRows, false,
                $"columns {columns} already matches vanilla; nothing to do");
        }

        var rows = vanillaRows;

        if (compensateRows)
        {
            // Ceiling, not floor, and this is a correctness matter rather than
            // generosity.
            //
            // Auto-sort empties the grid and re-places everything
            // (ItemManipulator.Sort -> Grid.AddAnywhere). Advanced Stash Sorting
            // does the same through its own layout engine and raises
            // InsufficientSortSpaceError when it cannot fit. Flooring loses up to
            // columns-1 cells, so a nearly-full stash that sorted before the mod
            // could fail to sort after it -- a visible, confusing regression bought
            // for nothing.
            //
            // Ceiling guarantees the new grid is never smaller than the old one. The
            // price is under one row of extra space (8 cells on a 10x68 stash at 16
            // columns, about 1%), which is not a balance change anyone will notice.
            var wanted = (vanillaColumns * vanillaRows + columns - 1) / columns;

            // The guard that makes this safe to run against a stash with things in
            // it. Holding capacity is the goal; not losing anything is the rule, and
            // the rule wins.
            rows = wanted < deepestOccupiedRow ? deepestOccupiedRow : wanted;

            if (rows < 1) rows = 1;
        }

        var reason = compensateRows
            ? (rows > (vanillaColumns * vanillaRows + columns - 1) / columns
                ? $"rows held at {rows} to clear items stored as deep as row {deepestOccupiedRow}"
                : "capacity held at vanilla")
            : "rows unchanged";

        return new Plan(columns, rows, true, reason);
    }
}
