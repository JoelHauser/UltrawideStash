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
    public const int MinColumns = 2;

    /// <summary>
    /// A cell is 63px plus a 1px border (EFT.UI.DragAndDrop.ItemViewFactory.
    /// GetCellPixelSize: <c>X * 63 + 1</c>), and the menu canvas at 3440x1440 is
    /// 2580 logical units wide -- EFT scales it by min(w/1920, h/1080), so the
    /// height alone sets the scale and an ultrawide simply gets more room.
    ///
    /// 40 columns is 2521px and fits that canvas exactly; 41 is 2584px and does not.
    /// So the cap is the real ceiling for the screen this was written for, rather
    /// than a round number, and it exists so a typo in the config cannot produce a
    /// grid the UI has no hope of drawing.
    /// </summary>
    public const int MaxColumns = 40;

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
    /// One past the last row any profile has an item standing on, or 0 when nothing
    /// is stored. Rows are never cut below this, because an item outside the grid is
    /// an item the player has lost. See <see cref="StashOccupancy"/>.
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
            // Floor, not round: capacity may come in a little under vanilla but can
            // never come in over it, so "same capacity" is a promise rather than an
            // approximation in the player's favour.
            var wanted = vanillaColumns * vanillaRows / columns;

            // The guard that makes this safe to run against a stash with things in
            // it. Holding capacity is the goal; not losing anything is the rule, and
            // the rule wins.
            rows = wanted < deepestOccupiedRow ? deepestOccupiedRow : wanted;

            if (rows < 1) rows = 1;
        }

        var reason = compensateRows
            ? (rows > vanillaColumns * vanillaRows / columns
                ? $"rows held at {rows} to clear items stored as deep as row {deepestOccupiedRow}"
                : "capacity held at vanilla")
            : "rows unchanged";

        return new Plan(columns, rows, true, reason);
    }
}
