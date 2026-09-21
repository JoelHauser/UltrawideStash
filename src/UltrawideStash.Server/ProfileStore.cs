using System.Text.Json;
using System.Text.Json.Nodes;

namespace UltrawideStash.Server;

/// <summary>
/// Reads the stash out of a profile file, and writes relocated items back.
///
/// ## Why this touches the file rather than the server
///
/// This mod runs at <c>OnLoadOrder.PostLoad</c> (1,000,000) and <c>SaveCallbacks</c> —
/// which calls <c>SaveServer.LoadAsync()</c> — carries a bare <c>[Injectable]</c>, so it
/// sits at the default priority of <c>int.MaxValue</c> and runs later. SPT orders
/// <c>IOnLoad</c> ascending, so **we run before any profile is loaded**.
///
/// That was a bug when 0.2.0 tried to read profiles through the server and got an empty
/// dictionary. Here it is exactly the property we want: edit the file on disk and the
/// server then loads the edited version. No conflict, no second write, nothing to
/// reconcile.
///
/// ## Writing someone's profile is not done lightly
///
/// Every write takes a timestamped backup alongside first, goes to a temporary file, and
/// only then replaces the original. Only <c>location.x</c>, <c>location.y</c> and
/// <c>location.r</c> of items the caller named are touched; the document is otherwise
/// round-tripped through <c>JsonNode</c> untouched. If any step throws, nothing is
/// replaced.
/// </summary>
public static class ProfileStore
{
    /// <summary>
    /// One profile's stash, as read, plus its Sorting Table — which is where anything
    /// that will not fit the stash gets put.
    /// </summary>
    public sealed record StashContents(
        string FilePath,
        string StashItemId,
        string StashTemplateId,
        List<StashRepack.Placement> Items,
        string? SortingTableId,
        List<StashRepack.Placement> SortingTableItems);

    /// <summary>
    /// Parse one profile. Returns null when it has no PMC stash to speak of, which is
    /// normal for a freshly created account and is not an error.
    /// </summary>
    public static StashContents? Read(string path, Func<string, (int Width, int Height)> sizeOf)
    {
        var root = JsonNode.Parse(File.ReadAllText(path));

        var inventory = root?["characters"]?["pmc"]?["Inventory"];
        var items = inventory?["items"]?.AsArray();
        var stashId = inventory?["stash"]?.GetValue<string>();

        if (items is null || string.IsNullOrEmpty(stashId)) return null;

        // Optional: a profile without one simply has no overflow available.
        var sortingTableId = inventory?["sortingTable"]?.GetValue<string>();

        string? stashTemplate = null;

        foreach (var node in items)
        {
            if (node?["_id"]?.GetValue<string>() != stashId) continue;

            stashTemplate = node["_tpl"]?.GetValue<string>();
            break;
        }

        if (string.IsNullOrEmpty(stashTemplate)) return null;

        var placements = new List<StashRepack.Placement>();
        var sortingTablePlacements = new List<StashRepack.Placement>();

        foreach (var node in items)
        {
            if (node is null) continue;

            var parent = node["parentId"]?.GetValue<string>();
            var intoSortingTable = sortingTableId is not null && parent == sortingTableId;

            if (parent != stashId && !intoSortingTable) continue;

            var location = node["location"];

            // A magazine stores location as a bare number. Only an object is a grid
            // placement, and only a grid placement can be out of bounds.
            if (location is not JsonObject loc) continue;

            var id = node["_id"]?.GetValue<string>();

            if (string.IsNullOrEmpty(id)) continue;

            var size = sizeOf(node["_tpl"]?.GetValue<string>() ?? string.Empty);

            var placement = new StashRepack.Placement(
                id!,
                ReadInt(loc, "x"),
                ReadInt(loc, "y"),
                size.Width,
                size.Height,
                ReadRotation(loc));

            (intoSortingTable ? sortingTablePlacements : placements).Add(placement);
        }

        return new StashContents(
            path, stashId!, stashTemplate!, placements, sortingTableId, sortingTablePlacements);
    }

    /// <summary>
    /// Apply relocations to a profile on disk, in one write. Returns how many items were
    /// changed.
    ///
    /// <paramref name="moves"/> reposition items within the stash.
    /// <paramref name="transfers"/> move items out of the stash and into the Sorting
    /// Table, which means re-parenting them as well as repositioning.
    ///
    /// Throws rather than half-writing: the caller treats an exception as "this profile
    /// was not changed", which is true because the original is only replaced on the very
    /// last step.
    /// </summary>
    public static int ApplyChanges(
        string path,
        IReadOnlyList<StashRepack.Move> moves,
        string? sortingTableId = null,
        IReadOnlyList<StashRepack.Transfer>? transfers = null)
    {
        var hasTransfers = transfers is { Count: > 0 } && !string.IsNullOrEmpty(sortingTableId);

        if (moves.Count == 0 && !hasTransfers) return 0;

        var root = JsonNode.Parse(File.ReadAllText(path));
        var items = root?["characters"]?["pmc"]?["Inventory"]?["items"]?.AsArray();

        if (items is null) return 0;

        var wantedMoves = new Dictionary<string, StashRepack.Move>(moves.Count);

        foreach (var move in moves) wantedMoves[move.ItemId] = move;

        var wantedTransfers = new Dictionary<string, StashRepack.Transfer>();

        if (hasTransfers)
        {
            foreach (var t in transfers!) wantedTransfers[t.ItemId] = t;
        }

        var written = 0;

        foreach (var node in items)
        {
            if (node is null) continue;

            var id = node["_id"]?.GetValue<string>();

            if (id is null) continue;
            if (node["location"] is not JsonObject loc) continue;

            if (wantedMoves.TryGetValue(id, out var move))
            {
                loc["x"] = move.ToX;
                loc["y"] = move.ToY;
                written++;
                continue;
            }

            if (!wantedTransfers.TryGetValue(id, out var transfer)) continue;

            // Into the Sorting Table: it is a different container, so the item is
            // re-parented. Its grid is named "hideout", the same as the stash's.
            node["parentId"] = sortingTableId;
            node["slotId"] = "hideout";
            loc["x"] = transfer.ToX;
            loc["y"] = transfer.ToY;
            written++;
        }

        if (written == 0) return 0;

        // Back up before replacing anything. A timestamp rather than a fixed name so a
        // second run cannot overwrite the copy taken before the first.
        var backup = $"{path}.ultrawidestash-{DateTime.UtcNow:yyyyMMdd-HHmmss}.bak";

        File.Copy(path, backup, overwrite: false);

        var temp = path + ".ultrawidestash.tmp";

        File.WriteAllText(temp, root!.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true,
        }));

        // Replace last, so a failure anywhere above leaves the original untouched.
        File.Copy(temp, path, overwrite: true);
        File.Delete(temp);

        return written;
    }

    private static int ReadInt(JsonObject loc, string name)
    {
        var node = loc[name];

        if (node is null) return 0;

        try { return node.GetValue<int>(); }
        catch { return 0; }
    }

    /// <summary>
    /// <c>ItemRotation</c> is <c>Horizontal = 0, Vertical = 1</c> and lands in the JSON
    /// as either the number or the name. Anything unrecognised is read as rotated,
    /// because that is the reading that claims the larger vertical footprint, and
    /// over-reporting height is the safe direction.
    /// </summary>
    private static int ReadRotation(JsonObject loc)
    {
        var node = loc["r"];

        if (node is null) return 0;

        try
        {
            var value = node.GetValue<object>();

            if (value is int i) return i;
        }
        catch
        {
            // Not a number; fall through to the string reading.
        }

        try
        {
            var s = node.GetValue<string>();

            if (string.Equals(s, "Horizontal", StringComparison.OrdinalIgnoreCase)) return 0;
            if (s == "0") return 0;

            return StashOccupancy.Vertical;
        }
        catch
        {
            return 0;
        }
    }
}
