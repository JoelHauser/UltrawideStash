using System.Text.Json;

namespace UltrawideStash.Server;

/// <summary>
/// How deep each stash template's items reach, read straight off the profile JSON.
///
/// ## Why this does not ask the server
///
/// 0.2.0 asked <c>SaveServer.GetProfiles()</c> and was wrong to. SPT runs its
/// <c>IOnLoad</c> implementors through <c>Enumerable.OrderBy</c> on
/// <c>Injectable.TypePriority</c> — ascending — and the default priority is
/// <c>int.MaxValue</c> (2147483647). <c>SaveCallbacks</c>, which is the thing that calls
/// <c>SaveServer.LoadAsync()</c> and populates that dictionary, carries a bare
/// <c>[Injectable]</c> and therefore sits at that default.
///
/// This mod runs at <c>OnLoadOrder.PostLoad</c>, which is 1,000,000. One million sorts
/// before two billion, so **we run first and the profile dictionary is empty when we
/// look at it**. The guard that was supposed to stop rows being cut out from under a
/// player's belongings was reading an empty collection and always answering "nothing is
/// stored". It was not a guard at all.
///
/// Reading the files ourselves takes the ordering question off the table entirely.
///
/// ## Fail safe, not fail quiet
///
/// Any doubt at all — directory missing, a file that will not parse, a shape that is not
/// what we expect — and <see cref="Result.Confident"/> comes back false, which
/// <see cref="StashWidener"/> turns into "do not shorten anything". The cost of being
/// wrong in that direction is a stash with more rows than the config asked for. The cost
/// of being wrong in the other direction is the player's items ending up somewhere they
/// did not put them.
/// </summary>
public static class ProfileScan
{
    /// <summary>What a scan found, and whether it can be trusted.</summary>
    public readonly record struct Result(
        Dictionary<string, int> DeepestRowByStashTemplate,
        bool Confident,
        string Note)
    {
        public int DeepestFor(string templateId)
        {
            return DeepestRowByStashTemplate.TryGetValue(templateId, out var rows) ? rows : 0;
        }
    }

    /// <summary>
    /// A scan that declines to vouch for anything, so nothing gets shortened.
    /// </summary>
    public static Result Unsure(string why)
    {
        return new Result(new Dictionary<string, int>(), false, why);
    }

    /// <summary>
    /// Walk every profile in <paramref name="profileDirectory"/> and work out, per stash
    /// template, how many rows the deepest stored item needs.
    /// </summary>
    /// <param name="profileDirectory">
    /// SPT's own <c>user/profiles</c>. The server writes and reads it with that literal
    /// path relative to its working directory.
    /// </param>
    /// <param name="sizeOf">
    /// Item template id to its (width, height) before rotation, from the database.
    /// </param>
    public static Result Run(string profileDirectory, Func<string, (int Width, int Height)> sizeOf)
    {
        if (string.IsNullOrWhiteSpace(profileDirectory) || !Directory.Exists(profileDirectory))
        {
            return Unsure($"profile folder '{profileDirectory}' is not there");
        }

        string[] files;

        try
        {
            files = Directory.GetFiles(profileDirectory, "*.json");
        }
        catch (Exception e)
        {
            return Unsure($"could not list '{profileDirectory}' ({e.Message})");
        }

        var deepest = new Dictionary<string, int>();
        var read = 0;

        foreach (var file in files)
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file));

                ScanProfile(doc.RootElement, sizeOf, deepest);
                read++;
            }
            catch (Exception e)
            {
                // One unreadable profile poisons the whole answer. It could be the one
                // with a full stash, and we cannot tell.
                return Unsure($"'{Path.GetFileName(file)}' could not be read ({e.Message})");
            }
        }

        return new Result(deepest, true,
            read == 0 ? "no profiles yet" : $"{read} profile(s) read");
    }

    private static void ScanProfile(
        JsonElement root,
        Func<string, (int Width, int Height)> sizeOf,
        Dictionary<string, int> deepest)
    {
        if (!root.TryGetProperty("characters", out var characters)) return;
        if (!characters.TryGetProperty("pmc", out var pmc)) return;
        if (!pmc.TryGetProperty("Inventory", out var inventory)) return;
        if (!inventory.TryGetProperty("items", out var items)) return;
        if (items.ValueKind != JsonValueKind.Array) return;

        if (!inventory.TryGetProperty("stash", out var stashEl)) return;

        var stashId = stashEl.ValueKind == JsonValueKind.String ? stashEl.GetString() : null;

        if (string.IsNullOrEmpty(stashId)) return;

        // Which template this profile's stash is. Editions differ, and only the one the
        // player owns needs protecting from that player's items.
        string? stashTemplate = null;

        foreach (var item in items.EnumerateArray())
        {
            if (Str(item, "_id") != stashId) continue;

            stashTemplate = Str(item, "_tpl");
            break;
        }

        if (string.IsNullOrEmpty(stashTemplate)) return;

        var needed = 0;

        foreach (var item in items.EnumerateArray())
        {
            if (Str(item, "parentId") != stashId) continue;

            if (!item.TryGetProperty("location", out var loc)) continue;

            // A magazine stores its location as a plain number. Only a grid placement is
            // an object with x/y, and only that can be out of bounds vertically.
            if (loc.ValueKind != JsonValueKind.Object) continue;

            var y = Int(loc, "y");
            var r = Rotation(loc);

            var size = sizeOf(Str(item, "_tpl") ?? string.Empty);

            var placement = StashOccupancy.Place(y, size.Width, size.Height, r);

            if (placement.RowsNeeded > needed) needed = placement.RowsNeeded;
        }

        if (!deepest.TryGetValue(stashTemplate!, out var already) || needed > already)
        {
            deepest[stashTemplate!] = needed;
        }
    }

    private static string? Str(JsonElement el, string name)
    {
        return el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;
    }

    private static int Int(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var v)) return 0;

        return v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : 0;
    }

    /// <summary>
    /// The stored rotation. <c>ItemRotation</c> is <c>Horizontal = 0, Vertical = 1</c>,
    /// and depending on the serializer it lands as either the number or the name, so
    /// both are accepted. Anything unrecognised is read as rotated, because rotation is
    /// the reading that reports the greater depth and depth is what we are protecting.
    /// </summary>
    private static int Rotation(JsonElement loc)
    {
        if (!loc.TryGetProperty("r", out var r)) return 0;

        if (r.ValueKind == JsonValueKind.Number)
        {
            return r.TryGetInt32(out var i) ? i : StashOccupancy.Vertical;
        }

        if (r.ValueKind == JsonValueKind.String)
        {
            var s = r.GetString();

            if (string.Equals(s, "Horizontal", StringComparison.OrdinalIgnoreCase)) return 0;
            if (string.Equals(s, "0", StringComparison.Ordinal)) return 0;

            return StashOccupancy.Vertical;
        }

        return 0;
    }
}
