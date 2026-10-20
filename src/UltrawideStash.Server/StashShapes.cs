namespace UltrawideStash.Server;

/// <summary>
/// One player's stash shape on every rung of the hideout ladder, worked out from that
/// player's own stash alone. Pure: no SPT type, no file system.
///
/// ## When this is used, and when it is not
///
/// A player whose width is the server-wide one shares the template plan
/// <see cref="StashWidener"/> makes, the one written into the database -- exactly what
/// every player got before 1.1.3. Only a player whose own screen gives a different width
/// gets a plan of their own, served to their game alone and used for their stash by the
/// server's own item placement (see <see cref="PlayerStashes"/>).
///
/// ## The ladder, for one player
///
/// The rung they are on is planned like the template plan: the compensated shape, deepened
/// only as far as the repack needs to fit what they own (<see cref="ShallowestThatFits"/>).
/// Every rung above it is at least that deep, because an upgrade in the hideout swaps
/// their template mid-session and nothing repacks until the next start -- see
/// <see cref="StashLadder"/>. Rungs below it are planned from nothing; the player cannot
/// go back down.
/// </summary>
public static class StashShapes
{
    /// <summary>A grid's size.</summary>
    public readonly record struct Shape(int Columns, int Rows);

    /// <summary>
    /// The shape of every rung for one player at <paramref name="columns"/> wide.
    /// <paramref name="vanilla"/> is each rung's size in the database before this mod
    /// touched it; a rung missing from it is left out. <paramref name="profile"/> may be
    /// null for a player who has no profile yet.
    /// </summary>
    public static Dictionary<string, Shape> For(
        ProfileStore.StashContents? profile,
        IReadOnlyDictionary<string, Shape> vanilla,
        int columns,
        bool compensateRows)
    {
        var shapes = new Dictionary<string, Shape>();

        var current = profile is null ? -1 : StashLadder.PositionOf(profile.StashTemplateId);
        var floor = 0;

        for (var i = 0; i < StashLadder.Rungs.Length; i++)
        {
            var id = StashLadder.Rungs[i].Id;

            if (!vanilla.TryGetValue(id, out var v)) continue;

            var rungFloor = 0;

            if (i == current)
            {
                var deepest = profile!.TemplateRowsNeeded();
                var wanted = StashLayout.For(v.Columns, v.Rows, columns, compensateRows, 0);

                rungFloor = wanted.Applied && compensateRows
                    ? ShallowestThatFits([profile], wanted.Columns, wanted.Rows, deepest)
                    : deepest;
            }
            else if (i > current && current >= 0)
            {
                rungFloor = floor;
            }

            var plan = StashLayout.For(v.Columns, v.Rows, columns, compensateRows, rungFloor);
            var shape = plan.Applied ? new Shape(plan.Columns, plan.Rows) : v;

            shapes[id] = shape;

            // What the rungs above have to hold: this rung once repacked into its shape.
            if (i == current) floor = shape.Rows;
            else if (i > current && current >= 0 && shape.Rows > floor) floor = shape.Rows;
        }

        return shapes;
    }

    /// <summary>
    /// The shallowest grid, from <paramref name="ideal"/> rows upward, that every
    /// profile here can actually be repacked into.
    ///
    /// Returns <paramref name="ideal"/> when the compact grid holds everything, which
    /// is the normal case: the cells do not go anywhere when a stash is reshaped, and
    /// compensateRows keeps the count. It climbs only for a stash that genuinely
    /// cannot be packed that tightly -- large items that will not tessellate, mostly
    /// -- and stops at <paramref name="ceiling"/>, the pre-move depth, which always
    /// fits because it is where the items already are.
    /// </summary>
    public static int ShallowestThatFits(
        IReadOnlyList<ProfileStore.StashContents> profiles,
        int columns,
        int ideal,
        int ceiling)
    {
        if (ceiling <= ideal) return ideal;

        for (var rows = ideal; rows < ceiling; rows++)
        {
            if (EverythingFits(profiles, columns, rows)) return rows;
        }

        return ceiling;
    }

    /// <summary>
    /// Whether a repack into this grid leaves nothing homeless. A dry run: it plans
    /// the moves and throws them away, writing nothing.
    /// </summary>
    public static bool EverythingFits(
        IReadOnlyList<ProfileStore.StashContents> profiles,
        int columns,
        int rows)
    {
        foreach (var profile in profiles)
        {
            var plan = StashRepack.For(profile.Items, columns, profile.RowsFor(rows));

            if (!plan.Complete || plan.Homeless.Count > 0) return false;
        }

        return true;
    }
}
