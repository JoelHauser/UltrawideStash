using System.Reflection;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Servers;

namespace UltrawideStash.Server;

/// <summary>
/// At startup: set the stash width, then make sure every stored item actually fits it.
///
/// ## The two halves, and why the second is not optional
///
/// Changing <c>cellsH</c> on the stash template is the whole feature. EFT's
/// <c>GridView.OnGridResized</c> sizes itself from whatever dimensions it is handed
/// (<c>ItemViewFactory.GetCellPixelSize</c> is literally <c>columns * 63 + 1</c>), and
/// the 10 lives only in the item template, which the client is served.
///
/// But a grid that changes size can leave items outside it, and an item outside the grid
/// is invisible. Not deleted -- the SPT server has no out-of-bounds concept and never
/// prunes -- but unreachable, which is what it feels like. EFT's own rescue
/// (<c>MoveBrokenItemsToSortingTable</c>) cannot be relied on: the Sorting Table it moves
/// things into has a <c>0 x 0</c> template grid, a stash grid has no horizontal stretch,
/// and <c>FindFreeSpaceInGrid</c> never grows a grid.
///
/// So <see cref="StashRepack"/> runs on every start against the dimensions the stash will
/// actually have, and relocates anything that no longer fits.
///
/// ## Why it runs even when nothing changed
///
/// The profile can hold items from a *previous* configuration. Drop <c>columns</c> from
/// 16 back to 10 and this mod changes nothing about the template -- it is already 10 wide
/// -- while the profile is full of items at x >= 10. So the repack is driven by the
/// grid's final dimensions, not by whether this run altered them. **That is what makes
/// the mod safe to uninstall:** set <c>columns</c> to 10, start the server once, and
/// everything is packed back into a vanilla stash.
///
/// ## Ordering, which is load-bearing
///
/// This runs at <c>OnLoadOrder.PostLoad</c> (1,000,000). <c>SaveCallbacks</c>, which
/// calls <c>SaveServer.LoadAsync()</c>, carries a bare <c>[Injectable]</c> and so sits at
/// the default <c>int.MaxValue</c>; SPT orders <c>IOnLoad</c> ascending. We therefore run
/// **before any profile is loaded**, which is precisely why editing the files on disk is
/// correct and needs no reconciliation with the server's own copy.
/// </summary>
[Injectable(TypePriority = OnLoadOrder.PostLoad)]
public class StashWidener(
    ISptLogger<StashWidener> logger,
    TemplateTable templates,
    SaveServer saveServer)
    : IOnLoad
{
    /// <summary>
    /// The five stashes a player can own, by template id. All are <c>cellsH: 10</c> in
    /// 4.1.5 and differ only in height; verified by <c>scripts/test-database.ps1</c>.
    /// </summary>
    private static readonly (string Id, string Edition)[] PlayerStashes =
    [
        ("566abbc34bdc2d92178b4576", "Standard"),
        ("5811ce572459770cba1a34ea", "Left Behind"),
        ("5811ce662459770f6f490f32", "Prepare for Escape"),
        ("5811ce772459770e9e5f9532", "Edge of Darkness"),
        ("6602bcf19cc643f44a04274b", "The Unheard Edition"),
    ];

    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        var settings = StashSettings.Load(
            System.IO.Path.Combine(ModFolder(), "ultrawidestash.config.json"),
            out var note);

        var profiles = ReadProfiles(out var readable, out var why);

        if (!readable)
        {
            logger.Error(
                $"[UltrawideStash] Could not read profiles ({why}). Doing nothing at all this "
                + "start: without knowing what is stored, resizing the stash could put items "
                + "somewhere you cannot reach them.");
            return Task.CompletedTask;
        }

        var applied = 0;
        var moved = 0;

        foreach (var (id, edition) in PlayerStashes)
        {
            if (!templates.Items.TryGetValue(new MongoId(id), out var template)) continue;

            var grid = FirstGrid(template);

            if (grid?.Properties is null) continue;

            var vanillaColumns = grid.Properties.CellsH ?? 0;
            var vanillaRows = grid.Properties.CellsV ?? 0;

            if (vanillaColumns <= 0 || vanillaRows <= 0) continue;

            var mine = profiles.Where(p => p.StashTemplateId == id).ToList();

            var plan = StashLayout.For(
                vanillaColumns, vanillaRows, settings.Columns, settings.CompensateRows,
                DeepestRow(mine));

            // Whether or not the template changes, the profile may hold items from a
            // previous configuration, so the repack is driven by the FINAL size.
            var finalColumns = plan.Applied ? plan.Columns : vanillaColumns;
            var finalRows = plan.Applied ? plan.Rows : vanillaRows;

            if (!Relocate(mine, finalColumns, finalRows, edition, ref moved))
            {
                logger.Error(
                    $"[UltrawideStash] {edition} stash left at {vanillaColumns}x{vanillaRows}: "
                    + $"items would not fit {finalColumns}x{finalRows}, so nothing was changed.");
                continue;
            }

            if (!plan.Applied)
            {
                if (settings.Verbose) logger.Info($"[UltrawideStash] {edition}: {plan.Reason}.");

                continue;
            }

            grid.Properties.CellsH = plan.Columns;
            grid.Properties.CellsV = plan.Rows;
            applied++;

            if (settings.Verbose)
            {
                logger.Info(
                    $"[UltrawideStash] {edition}: {vanillaColumns}x{vanillaRows} -> "
                    + $"{plan.Columns}x{plan.Rows} ({plan.Capacity} cells, {plan.PixelWidth}px) "
                    + $"-- {plan.Reason}.");
            }
        }

        var shape = settings.CompensateRows ? "capacity held" : "rows kept";

        logger.Info(
            $"[UltrawideStash] {applied} stash template(s) at {settings.Columns} columns, {shape}"
            + (moved > 0 ? $"; {moved} item(s) relocated to stay reachable" : string.Empty)
            + $". ({note}.)");

        if (applied > 0)
        {
            logger.Info(
                "[UltrawideStash] To remove this mod safely: set columns to 10, start the server "
                + "once so items are packed back into a vanilla stash, then delete the files.");
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Relocate anything that does not fit, writing the profile back.
    ///
    /// Returns false when some item has nowhere to go -- the caller then leaves that
    /// stash alone entirely rather than applying a size that would hide something.
    /// </summary>
    private bool Relocate(
        List<ProfileStore.StashContents> profiles,
        int columns,
        int rows,
        string edition,
        ref int moved)
    {
        // Plan every profile before writing any of them, so a stash that cannot be
        // packed stops the whole thing rather than leaving some files already edited.
        var planned = new List<(ProfileStore.StashContents Profile, StashRepack.Plan Plan)>();

        foreach (var profile in profiles)
        {
            var plan = StashRepack.For(profile.Items, columns, rows);

            if (!plan.Complete)
            {
                logger.Error(
                    $"[UltrawideStash] {edition}: {plan.Homeless.Count} item(s) in "
                    + $"'{System.IO.Path.GetFileName(profile.FilePath)}' have nowhere to go in "
                    + $"{columns}x{rows}. Free some space, or raise columns, and start again.");
                return false;
            }

            if (plan.Moves.Count > 0) planned.Add((profile, plan));
        }

        foreach (var (profile, plan) in planned)
        {
            try
            {
                var written = ProfileStore.ApplyMoves(profile.FilePath, plan.Moves);

                moved += written;

                logger.Info(
                    $"[UltrawideStash] Moved {written} item(s) back inside the {edition} stash in "
                    + $"'{System.IO.Path.GetFileName(profile.FilePath)}' (a .bak was written "
                    + "alongside it).");
            }
            catch (Exception e)
            {
                logger.Error(
                    $"[UltrawideStash] Could not rewrite '{profile.FilePath}' ({e.Message}). "
                    + "That profile is unchanged; the stash is being left alone.");
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// How many rows the deepest stored item needs across these profiles. Row
    /// compensation is clamped to this, so the ordinary case needs no relocation at all.
    /// </summary>
    private static int DeepestRow(List<ProfileStore.StashContents> profiles)
    {
        var deepest = 0;

        foreach (var profile in profiles)
        {
            foreach (var item in profile.Items)
            {
                var needed = item.Y + item.EffectiveHeight;

                if (needed > deepest) deepest = needed;
            }
        }

        return deepest;
    }

    /// <summary>
    /// Every profile's stash. <paramref name="readable"/> is false if any single file
    /// could not be parsed -- one unreadable profile could be the full one, and acting on
    /// a partial picture is how items get hidden.
    /// </summary>
    private List<ProfileStore.StashContents> ReadProfiles(out bool readable, out string why)
    {
        var found = new List<ProfileStore.StashContents>();
        var directory = ProfileDirectory();

        if (!Directory.Exists(directory))
        {
            readable = true;
            why = "no profile folder yet";
            return found;
        }

        foreach (var file in Directory.GetFiles(directory, "*.json"))
        {
            try
            {
                var contents = ProfileStore.Read(file, SizeOf);

                if (contents is not null) found.Add(contents);
            }
            catch (Exception e)
            {
                readable = false;
                why = $"'{System.IO.Path.GetFileName(file)}': {e.Message}";
                return found;
            }
        }

        readable = true;
        why = $"{found.Count} profile(s)";
        return found;
    }

    /// <summary>
    /// SPT's profile folder, from <c>SaveServer</c>'s own private <c>profileFilepath</c>
    /// where it can be read, else the literal <c>user/profiles</c> that
    /// <c>SaveServer.RemoveProfile</c> uses, relative to the server's directory.
    /// </summary>
    private string ProfileDirectory()
    {
        try
        {
            var field = typeof(SaveServer).GetField(
                "profileFilepath", BindingFlags.Instance | BindingFlags.NonPublic);

            if (field?.GetValue(saveServer) is string path && !string.IsNullOrWhiteSpace(path))
            {
                return System.IO.Path.IsPathRooted(path)
                    ? path
                    : System.IO.Path.Combine(AppContext.BaseDirectory, path);
            }
        }
        catch
        {
            // Fall through to the literal.
        }

        return System.IO.Path.Combine(AppContext.BaseDirectory, "user", "profiles");
    }

    private static string ModFolder()
    {
        return System.IO.Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? ".";
    }

    /// <summary>The stash's own grid; every player stash has exactly one.</summary>
    private static Grid? FirstGrid(TemplateItem template)
    {
        var grids = template.Properties?.Grids;

        if (grids is null) return null;

        foreach (var g in grids) return g;

        return null;
    }

    /// <summary>
    /// An item template's footprint, 1x1 for anything unrecognised -- the smallest an
    /// item can be, so this under-reports rather than inventing size.
    /// </summary>
    private (int Width, int Height) SizeOf(string templateId)
    {
        if (string.IsNullOrEmpty(templateId)) return (1, 1);

        if (!templates.Items.TryGetValue(new MongoId(templateId), out var template)) return (1, 1);

        var width = template.Properties?.Width ?? 1;
        var height = template.Properties?.Height ?? 1;

        return (width < 1 ? 1 : width, height < 1 ? 1 : height);
    }
}
