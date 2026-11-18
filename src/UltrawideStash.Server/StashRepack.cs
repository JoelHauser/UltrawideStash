namespace UltrawideStash.Server;

/// <summary>
/// Moves items that would fall outside a resized stash back into it.
///
/// ## Why this exists
///
/// Changing a grid's size can leave items outside it, and an item outside the grid is
/// invisible. It is not deleted — the SPT server has no out-of-bounds concept and never
/// prunes, so it sits in the profile JSON with its old coordinates — but the player
/// cannot see or reach it, which is indistinguishable from losing it while it lasts.
///
/// EFT does rescue such items on its own, through
/// <c>MainMenuShowOperation.MoveBrokenItemsToSortingTable</c> -- seen working in game on
/// 2026-09-22 -- but it piles them onto the 7-wide Sorting Table rather than leaving them
/// in the stash the player arranged.
///
/// So the mod does it first, at server start, against the stash's final size: in the
/// server's loaded copy of each profile, saved through SPT (see
/// <see cref="StashWidener"/>), so the client is never served the bad state.
///
/// ## The important consequence
///
/// This is what makes the mod safe to remove. Set <c>columns</c> back to 10, start the
/// server once, and every item that was living in the extra columns is packed back into
/// a vanilla-shaped stash. Then the DLLs can go.
///
/// ## Nothing here is destructive
///
/// <see cref="Plan.Homeless"/> lists items the smaller grid genuinely cannot hold. Those
/// go to the Sorting Table through <see cref="IntoSortingTable"/>, which stretches
/// vertically without bound and is somewhere the player will actually find them. Only if
/// even that fails — an item wider than the table — does the caller decline to resize.
/// </summary>
public static class StashRepack
{
    /// <summary>
    /// The rotation value EFT stores for an item stood on end. The enum is
    /// <c>Horizontal = 0, Vertical = 1</c>, and a vertical item swaps its two dimensions.
    /// Getting that backwards under-reports depth, which is the direction that loses items.
    /// </summary>
    public const int Vertical = 1;

    /// <summary>One item as the profile stores it, with its template footprint.</summary>
    public readonly record struct Placement(
        string ItemId,
        int X,
        int Y,
        int Width,
        int Height,
        int Rotation)
    {
        /// <summary>Cells across, after rotation.</summary>
        public int EffectiveWidth =>
            Math.Max(1, Rotation == Vertical ? Height : Width);

        /// <summary>Cells down, after rotation.</summary>
        public int EffectiveHeight =>
            Math.Max(1, Rotation == Vertical ? Width : Height);

        /// <summary>Whether this sits wholly inside a grid of the given size.</summary>
        public bool FitsIn(int columns, int rows)
        {
            return X >= 0
                && Y >= 0
                && X + EffectiveWidth <= columns
                && Y + EffectiveHeight <= rows;
        }
    }

    /// <summary>Where one item has to go.</summary>
    public readonly record struct Move(string ItemId, int FromX, int FromY, int ToX, int ToY);

    /// <summary>
    /// What a repack would do. <see cref="Complete"/> is false when something could not
    /// be placed, and then the caller must not apply any of it.
    /// </summary>
    public readonly record struct Plan(
        IReadOnlyList<Move> Moves,
        IReadOnlyList<string> Homeless,
        bool Complete)
    {
        public static Plan Nothing => new([], [], true);
    }

    /// <summary>
    /// Work out how to fit every item into a <paramref name="columns"/> x
    /// <paramref name="rows"/> grid, moving only the ones that do not already fit.
    ///
    /// Items already inside keep their exact position — a repack that shuffled a whole
    /// stash the player had arranged deliberately would be its own kind of damage.
    /// </summary>
    public static Plan For(IReadOnlyList<Placement> items, int columns, int rows)
    {
        if (columns <= 0 || rows <= 0) return new Plan([], [], false);

        var staying = new List<Placement>();
        var displaced = new List<Placement>();

        foreach (var item in items)
        {
            if (item.FitsIn(columns, rows)) staying.Add(item);
            else displaced.Add(item);
        }

        if (displaced.Count == 0) return Plan.Nothing;

        // Occupancy of everything that is not moving.
        var occupied = new bool[columns * rows];

        foreach (var item in staying)
        {
            Fill(occupied, columns, item.X, item.Y, item.EffectiveWidth, item.EffectiveHeight);
        }

        // Biggest first: a 5x2 weapon case placed after fifty single cells has nowhere
        // left to go, and the whole repack fails on an item that would have fitted.
        displaced.Sort(static (a, b) =>
        {
            var areaA = a.EffectiveWidth * a.EffectiveHeight;
            var areaB = b.EffectiveWidth * b.EffectiveHeight;

            if (areaA != areaB) return areaB - areaA;

            // Stable-ish tiebreak so a repack is reproducible run to run.
            return string.CompareOrdinal(a.ItemId, b.ItemId);
        });

        var moves = new List<Move>();
        var homeless = new List<string>();

        foreach (var item in displaced)
        {
            if (TryFind(occupied, columns, rows, item.EffectiveWidth, item.EffectiveHeight,
                    out var x, out var y))
            {
                Fill(occupied, columns, x, y, item.EffectiveWidth, item.EffectiveHeight);
                moves.Add(new Move(item.ItemId, item.X, item.Y, x, y));
            }
            else
            {
                homeless.Add(item.ItemId);
            }
        }

        return new Plan(moves, homeless, homeless.Count == 0);
    }

    /// <summary>
    /// How wide the Sorting Table is. <c>SortingTableWindow.ShowGrid</c> calls
    /// <c>SortingTable.ClampSize(7, 7)</c> — a hardcoded 7 — and the grid's template
    /// declares <c>cellsH: 0, cellsV: 0</c>, which <c>GridSerializer.Deserialize</c>
    /// turns into stretch flags on both axes. So it is 7 across whenever it is shown,
    /// and grows downward as far as it needs to.
    /// </summary>
    public const int SortingTableColumns = 7;

    /// <summary>An item being moved out of the stash and into the Sorting Table.</summary>
    public readonly record struct Transfer(string ItemId, int ToX, int ToY);

    /// <summary>
    /// Place items into the Sorting Table, which is the overflow when the stash itself
    /// cannot hold them.
    ///
    /// ## Why this is worth having
    ///
    /// Without it, narrowing a stash that is nearly full has no safe outcome: the mod
    /// refuses, and the player is told their stash is too full to uninstall. The Sorting
    /// Table stretches vertically without bound, so it always has room — and it is
    /// somewhere the player will actually find things, which is the whole point.
    ///
    /// The Sorting Table is not touched by this mod, so anything left there stays
    /// reachable after the mod is removed.
    /// </summary>
    /// <param name="incoming">Items that did not fit the stash.</param>
    /// <param name="alreadyThere">What the Sorting Table is already holding.</param>
    /// <param name="tooWide">
    /// Items wider than the table itself, which nothing can place. Empty in practice —
    /// it takes an item more than 7 cells across — but reported rather than dropped.
    /// </param>
    public static List<Transfer> IntoSortingTable(
        IReadOnlyList<Placement> incoming,
        IReadOnlyList<Placement> alreadyThere,
        out List<string> tooWide)
    {
        tooWide = [];

        var transfers = new List<Transfer>();

        if (incoming.Count == 0) return transfers;

        // Deep enough for everything already present plus everything arriving, one cell
        // per row in the worst case.
        var rows = 1;

        foreach (var item in alreadyThere) rows = Math.Max(rows, item.Y + item.EffectiveHeight);

        foreach (var item in incoming) rows += item.EffectiveHeight;

        var occupied = new bool[SortingTableColumns * rows];

        foreach (var item in alreadyThere)
        {
            Fill(occupied, SortingTableColumns, item.X, item.Y,
                item.EffectiveWidth, item.EffectiveHeight);
        }

        var ordered = incoming.OrderByDescending(i => i.EffectiveWidth * i.EffectiveHeight)
            .ThenBy(i => i.ItemId, StringComparer.Ordinal)
            .ToList();

        foreach (var item in ordered)
        {
            if (item.EffectiveWidth > SortingTableColumns)
            {
                tooWide.Add(item.ItemId);
                continue;
            }

            if (TryFind(occupied, SortingTableColumns, rows,
                    item.EffectiveWidth, item.EffectiveHeight, out var x, out var y))
            {
                Fill(occupied, SortingTableColumns, x, y,
                    item.EffectiveWidth, item.EffectiveHeight);
                transfers.Add(new Transfer(item.ItemId, x, y));
            }
            else
            {
                // Cannot happen with the row budget above, but never drop silently.
                tooWide.Add(item.ItemId);
            }
        }

        return transfers;
    }

    /// <summary>
    /// First free top-left position that holds a <paramref name="w"/> x
    /// <paramref name="h"/> block — the same shape of search the game's own
    /// <c>Grid.FindFreeSpaceInGrid</c> does, so the result looks like something the game
    /// would have chosen.
    /// </summary>
    private static bool TryFind(
        bool[] occupied, int columns, int rows, int w, int h, out int fx, out int fy)
    {
        for (var y = 0; y + h <= rows; y++)
        {
            for (var x = 0; x + w <= columns; x++)
            {
                if (!IsFree(occupied, columns, x, y, w, h)) continue;

                fx = x;
                fy = y;
                return true;
            }
        }

        fx = 0;
        fy = 0;
        return false;
    }

    private static bool IsFree(bool[] occupied, int columns, int x, int y, int w, int h)
    {
        for (var j = y; j < y + h; j++)
        {
            for (var i = x; i < x + w; i++)
            {
                if (occupied[j * columns + i]) return false;
            }
        }

        return true;
    }

    private static void Fill(bool[] occupied, int columns, int x, int y, int w, int h)
    {
        var rows = occupied.Length / columns;

        for (var j = y; j < y + h; j++)
        {
            if (j < 0 || j >= rows) continue;

            for (var i = x; i < x + w; i++)
            {
                if (i < 0 || i >= columns) continue;

                occupied[j * columns + i] = true;
            }
        }
    }
}
