using System.Text.Json;
using System.Text.Json.Nodes;
using SPTarkov.Server.Core.Extensions;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;

namespace UltrawideStash.Server;

/// <summary>
/// Reads the stash out of a profile, and writes relocated items back -- into the
/// server's loaded copy when there is one, into the file when there is not.
///
/// ## Why the loaded copy comes first (corrected after 1.0.0)
///
/// Up to 1.0.0 this edited only the file, on the belief that it ran before SPT loaded
/// any profile. **It never did.** <c>SaveCallbacks</c> is
/// <c>[Injectable(InjectionType.Transient, int.MaxValue, TypePriority = 600000)]</c> --
/// the <c>int.MaxValue</c> is a different positional argument, and the named
/// <c>TypePriority</c> puts it at <c>OnLoadOrder.SaveCallbacks</c>, well before our
/// <c>PostLoad</c> (1,000,000). True of 4.1.2 and 4.1.6 alike.
///
/// So every profile was already in memory when the file was edited. The client was
/// served the unmoved copy, and SPT's next save wrote it straight back over the edit:
/// the same items were "moved" on every start, a .bak accrued each time, and SPT logged
/// an <c>[OOB]</c> error per stranded item whenever anything was added to that stash.
/// <see cref="ApplyToItems"/> is the fix; <see cref="ApplyChanges"/> remains for a
/// profile the server has not loaded.
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
    ///
    /// <paramref name="BonusRows"/> is the profile's own <c>StashRows</c> bonus: rows the
    /// player has on top of the template, which every other profile on that template does
    /// not. Missing it made the repack move anything the player kept in those rows on
    /// every start -- see <see cref="StashRowsBonus"/>.
    /// </summary>
    public sealed record StashContents(
        string FilePath,
        string StashItemId,
        string StashTemplateId,
        List<StashRepack.Placement> Items,
        string? SortingTableId,
        List<StashRepack.Placement> SortingTableItems,
        int BonusRows = 0)
    {
        /// <summary>
        /// The rows this player's stash really has when the template has
        /// <paramref name="templateRows"/>: the same sum SPT's
        /// <c>InventoryHelper.GetPlayerStashSize</c> makes.
        /// </summary>
        public int RowsFor(int templateRows) => templateRows + BonusRows;

        /// <summary>
        /// How many template rows the deepest item needs, once this profile's bonus rows
        /// are taken off. The bonus is the player's, not the template's, so it must not
        /// raise the template for everyone else.
        /// </summary>
        public int TemplateRowsNeeded()
        {
            var needed = 0;

            foreach (var item in Items)
            {
                var bottom = item.Y + item.EffectiveHeight;

                if (bottom > needed) needed = bottom;
            }

            return Math.Max(0, needed - BonusRows);
        }
    }

    /// <summary>
    /// Parse one profile. Returns null when it has no PMC stash to speak of, which is
    /// normal for a freshly created account and is not an error.
    /// </summary>
    public static StashContents? Read(string path, Func<string, (int Width, int Height)> sizeOf)
        => Parse(File.ReadAllText(path), path, sizeOf);

    /// <summary>
    /// <see cref="Read"/> from text already in hand -- which is how the server's loaded
    /// copy is read: serialised by SPT's own JsonUtil, it is exactly the shape SPT writes
    /// to disk. <paramref name="path"/> is only carried through for naming and backups.
    /// </summary>
    public static StashContents? Parse(
        string json, string path, Func<string, (int Width, int Height)> sizeOf)
    {
        var root = JsonNode.Parse(json);

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
            path, stashId!, stashTemplate!, placements, sortingTableId, sortingTablePlacements,
            StashRowsBonus(root?["characters"]?["pmc"]?["Bonuses"]));
    }

    /// <summary>
    /// The extra stash rows a profile's <c>StashRows</c> bonus grants, or 0.
    ///
    /// ## Why this matters (fixed in 1.0.2)
    ///
    /// SPT's <c>InventoryHelper.GetPlayerStashSize</c> takes the template's
    /// <c>cellsV</c> and adds the value of the profile's **first** <c>StashRows</c>
    /// bonus, and the game draws those rows too. Up to 1.0.1 the repack planned against
    /// the template alone, so on a 19x36 Edge of Darkness stash with a +2 bonus a Pilgrim
    /// the player had placed at row 31 (reaching row 37, inside the real 38) was "moved
    /// back inside" on every start. Pinning or locking it did not help, which is how it
    /// was reported.
    ///
    /// First only, and cast to int, exactly as SPT does it, so the repack's idea of the
    /// grid is the server's.
    /// </summary>
    private static int StashRowsBonus(JsonNode? bonuses)
    {
        if (bonuses is not JsonArray array) return 0;

        foreach (var bonus in array)
        {
            if (bonus is not JsonObject b) continue;

            string? type;

            try { type = b["type"]?.GetValue<string>(); }
            catch { continue; }

            if (!string.Equals(type, "StashRows", StringComparison.Ordinal)) continue;

            try { return Math.Max(0, (int)(b["value"]?.GetValue<double>() ?? 0)); }
            catch { return 0; }
        }

        return 0;
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

        Backup(path);

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

    /// <summary>
    /// Copies the profile file aside before anything changes it. A timestamp rather than
    /// a fixed name so a second run cannot overwrite the copy taken before the first.
    /// Throws if the copy fails, and callers treat that as "change nothing".
    /// </summary>
    public static void Backup(string path)
    {
        var backup = $"{path}.ultrawidestash-{DateTime.UtcNow:yyyyMMdd-HHmmss}.bak";

        File.Copy(path, backup, overwrite: false);
    }

    /// <summary>
    /// <see cref="ApplyChanges"/> for a profile the server has loaded: the same moves and
    /// transfers, made to its live items. Returns how many items were changed. The
    /// caller saves the profile afterwards through <c>SaveServer</c>, so SPT writes the
    /// file itself and nothing is left to overwrite the move.
    ///
    /// Same rule as the file path: only an item whose location is an object is a grid
    /// placement. A magazine's bare-number location is left alone.
    /// </summary>
    public static int ApplyToItems(
        IEnumerable<Item?> items,
        IReadOnlyList<StashRepack.Move> moves,
        string? sortingTableId = null,
        IReadOnlyList<StashRepack.Transfer>? transfers = null)
    {
        var hasTransfers = transfers is { Count: > 0 } && !string.IsNullOrEmpty(sortingTableId);

        if (moves.Count == 0 && !hasTransfers) return 0;

        var wantedMoves = new Dictionary<string, StashRepack.Move>(moves.Count);

        foreach (var move in moves) wantedMoves[move.ItemId] = move;

        var wantedTransfers = new Dictionary<string, StashRepack.Transfer>();

        if (hasTransfers)
        {
            foreach (var t in transfers!) wantedTransfers[t.ItemId] = t;
        }

        var written = 0;

        foreach (var item in items)
        {
            if (item is null) continue;

            var id = item.Id.ToString();
            var isMove = wantedMoves.TryGetValue(id, out var move);
            var isTransfer = !isMove && wantedTransfers.ContainsKey(id);

            if (!isMove && !isTransfer) continue;

            var loc = GridLocationOf(item);

            if (loc is null) continue;

            if (isMove)
            {
                loc.X = move.ToX;
                loc.Y = move.ToY;
            }
            else
            {
                var transfer = wantedTransfers[id];

                // Into the Sorting Table, re-parented as in ApplyChanges. Its grid is
                // named "hideout", the same as the stash's.
                item.ParentId = sortingTableId;
                item.SlotId = "hideout";
                loc.X = transfer.ToX;
                loc.Y = transfer.ToY;
            }

            // Assigned back even when it was already an ItemLocation: a freshly loaded
            // profile holds a JsonElement here, and editing a parsed copy of that would
            // change nothing.
            item.Location = loc;
            written++;
        }

        return written;
    }

    /// <summary>
    /// The item's grid placement, or null when it has none. SPT holds a location as a
    /// raw <see cref="JsonElement"/> until something parses it, and as an
    /// <see cref="ItemLocation"/> after; a magazine's is a bare number either way.
    /// </summary>
    private static ItemLocation? GridLocationOf(Item item) => item.Location switch
    {
        ItemLocation loc => loc,
        JsonElement { ValueKind: JsonValueKind.Object } => item.GetParsedLocation(),
        _ => null,
    };

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
