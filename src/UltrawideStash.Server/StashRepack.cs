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
/// EFT does try to rescue such items on its own, through
/// <c>MainMenuShowOperation.MoveBrokenItemsToSortingTable</c>. That rescue cannot be
/// relied on: the Sorting Table's template grid is <c>0 x 0</c>, a stash grid has no
/// horizontal stretch, and <c>FindFreeSpaceInGrid</c> never grows a grid — so it usually
/// logs "Cannot find free space on sorting table for a bad item" and gives up.
///
/// So the mod does it instead, and does it before anything can see the bad state: the
/// relocation is written to the profile on disk at <c>PostLoad</c>, which runs before
/// <c>SaveCallbacks</c> loads profiles into the server.
///
/// ## The important consequence
///
/// This is what makes the mod safe to remove. Set <c>columns</c> back to 10, start the
/// server once, and every item that was living in the extra columns is packed back into
/// a vanilla-shaped stash. Then the DLLs can go.
///
/// ## Nothing here is destructive
///
/// The worst case is <see cref="Plan.Homeless"/> — items with nowhere to go, because the
/// smaller grid genuinely cannot hold everything. Those are left exactly where they are
/// and reported, and <see cref="Plan.Complete"/> is false. The caller then declines to
/// shrink at all rather than proceeding and stranding them.
/// </summary>
public static class StashRepack
{
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
            Math.Max(1, Rotation == StashOccupancy.Vertical ? Height : Width);

        /// <summary>Cells down, after rotation.</summary>
        public int EffectiveHeight =>
            Math.Max(1, Rotation == StashOccupancy.Vertical ? Width : Height);

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
