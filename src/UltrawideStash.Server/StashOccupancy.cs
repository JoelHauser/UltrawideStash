namespace UltrawideStash.Server;

/// <summary>
/// How deep into a stash the player's belongings actually reach.
///
/// ## Why this exists
///
/// Holding capacity while widening means shortening: an Edge of Darkness stash at
/// 10x68 becomes 16x42. On an empty profile that is free. On a played one it is
/// destructive -- everything below row 42 ends up outside the grid, and an item
/// outside the grid is an item the player no longer has.
///
/// So the mod measures before it cuts. <see cref="DeepestRow"/> returns one past the
/// last occupied row, and <see cref="StashLayout.For"/> never returns fewer rows than
/// that. Capacity is the goal, but not losing anything is the rule.
///
/// ## The arithmetic is not just Y
///
/// An item stands on the cells from its Y down to Y + height - 1, and a rotated item
/// swaps its width and height. A 1x4 rifle case at Y=40, rotated or not, is the
/// difference between "row 41" and "row 44".
/// </summary>
public static class StashOccupancy
{
    /// <summary>
    /// The horizontal rotation value EFT stores for an item laid on its side. The
    /// enum is <c>Horizontal = 0, Vertical = 1</c>; anything non-zero swaps the
    /// item's two dimensions.
    /// </summary>
    public const int Vertical = 1;

    /// <summary>
    /// One item's footprint, reduced to the only two numbers that matter here.
    /// </summary>
    public readonly record struct Placement(int Y, int Height, int Rotation)
    {
        /// <summary>
        /// The first row this item does NOT occupy -- so a 1-high item at Y=0 gives 1,
        /// and a grid needs at least this many rows to hold it.
        /// </summary>
        public int RowsNeeded => Y + Height;
    }

    /// <summary>
    /// The number of rows a grid must have for every one of these to still fit.
    /// Zero for an empty stash, which leaves <see cref="StashLayout.For"/> free to
    /// shorten as far as the capacity maths wants.
    /// </summary>
    public static int DeepestRow(IEnumerable<Placement> placements)
    {
        var deepest = 0;

        foreach (var p in placements)
        {
            if (p.RowsNeeded > deepest) deepest = p.RowsNeeded;
        }

        return deepest;
    }

    /// <summary>
    /// Turn one item's stored geometry into a <see cref="Placement"/>, applying the
    /// rotation swap. <paramref name="width"/> and <paramref name="height"/> are the
    /// item template's own, before rotation.
    /// </summary>
    public static Placement Place(int y, int width, int height, int rotation)
    {
        // Rotated, the template's width becomes the footprint's height. Getting this
        // backwards under-reports depth, which is the direction that loses items.
        var effectiveHeight = rotation == Vertical ? width : height;

        if (effectiveHeight < 1) effectiveHeight = 1;
        if (y < 0) y = 0;

        return new Placement(y, effectiveHeight, rotation);
    }
}
