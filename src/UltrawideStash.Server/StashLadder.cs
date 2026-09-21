namespace UltrawideStash.Server;

/// <summary>
/// The order a player's stash template can change in, and the row floor that order
/// forces on every rung.
///
/// ## Not everybody is Edge of Darkness
///
/// A Standard-edition player does not stay on the Standard stash. The hideout's Stash
/// area (<c>5d484fc0654e76006657e0ab</c>, area type 3) carries a <c>StashSize</c>
/// bonus on each stage whose <c>templateId</c> is the *next stash template* -- read
/// straight out of <c>database/hideout/areas.json</c>:
///
/// <code>
/// stage 1 -> 566abbc34bdc2d92178b4576   Standard             10x30
/// stage 2 -> 5811ce572459770cba1a34ea   Left Behind          10x40
/// stage 3 -> 5811ce662459770f6f490f32   Prepare for Escape   10x50
/// stage 4 -> 5811ce772459770e9e5f9532   Edge of Darkness     10x68
/// </code>
///
/// The bonus <c>value</c> is <c>0.0</c> on every stage, so this is a **template swap,
/// not an added row count**. Upgrading the hideout moves the player from one of the
/// templates this mod edits to another one of them.
///
/// ## The bug that caused
///
/// Row compensation is clamped up to the deepest row anything is stored on, and
/// before 0.6.0 that clamp was computed per template from the profiles *currently
/// sitting on it*. Which means:
///
/// <list type="bullet">
///   <item>A Standard player with items down to row 28 got Standard planned at
///   16x<b>28</b> -- the clamp beat the compensated 19.</item>
///   <item>Nothing was on Left Behind, so its clamp was 0 and it was planned at
///   16x<b>25</b>.</item>
///   <item>They upgrade the hideout. The game swaps their template. Their stash
///   silently loses three rows and everything on rows 25-27 is out of bounds, with no
///   repack until the next server start.</item>
/// </list>
///
/// ## The rule
///
/// A profile can only ever move **up** this ladder, so a rung must be at least as deep
/// as every rung below it. <see cref="RowFloors"/> walks the ladder in order carrying a
/// running maximum, and the floor for a rung is the deepest row of any profile on that
/// rung or any rung beneath it.
///
/// This is deliberately not a global maximum across all profiles. On a shared install,
/// one player's deep Edge of Darkness stash must not inflate another player's Standard
/// one -- they cannot reach each other's rung, and granting rows nobody needs is its
/// own kind of wrong.
/// </summary>
public static class StashLadder
{
    /// <summary>
    /// The five player stash templates, in the order a profile can travel through
    /// them. The first four are the hideout stage order above; The Unheard Edition is
    /// edition-only and sits at the top because it is the largest (10x72), so nothing
    /// can migrate off it to a shorter rung.
    /// </summary>
    public static readonly (string Id, string Edition)[] Rungs =
    [
        ("566abbc34bdc2d92178b4576", "Standard"),
        ("5811ce572459770cba1a34ea", "Left Behind"),
        ("5811ce662459770f6f490f32", "Prepare for Escape"),
        ("5811ce772459770e9e5f9532", "Edge of Darkness"),
        ("6602bcf19cc643f44a04274b", "The Unheard Edition"),
    ];

    /// <summary>
    /// The hideout Stash area, recorded so the link back to the database is findable.
    /// Nothing reads it; it is here because the ladder above is otherwise five magic
    /// strings.
    /// </summary>
    public const string HideoutStashAreaId = "5d484fc0654e76006657e0ab";

    /// <summary>Where a template sits on the ladder, or -1 if it is not a player stash.</summary>
    public static int PositionOf(string templateId)
    {
        for (var i = 0; i < Rungs.Length; i++)
        {
            if (Rungs[i].Id == templateId) return i;
        }

        return -1;
    }

    /// <summary>
    /// The row floor for every rung, keyed by template id.
    ///
    /// <paramref name="deepestByTemplate"/> is the deepest occupied row of the
    /// profiles currently on each template -- what the old code used directly. This
    /// turns it into a running maximum up the ladder, so a rung is never shorter than
    /// somewhere a profile could arrive from.
    /// </summary>
    public static IReadOnlyDictionary<string, int> RowFloors(
        IReadOnlyDictionary<string, int> deepestByTemplate)
    {
        var floors = new Dictionary<string, int>(Rungs.Length);
        var running = 0;

        foreach (var (id, _) in Rungs)
        {
            if (deepestByTemplate.TryGetValue(id, out var deepest) && deepest > running)
            {
                running = deepest;
            }

            floors[id] = running;
        }

        return floors;
    }
}
