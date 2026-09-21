using System.Reflection;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Common.Models.Logging;
using SPTarkov.Server.Core.Helpers.Server;

namespace UltrawideStash.Server;

/// <summary>
/// The mod, such as it is: at startup, change cellsH on the player stash templates.
///
/// ## Why this is the whole server half
///
/// The stash's width is not code anywhere. EFT's GridView sizes itself from whatever
/// dimensions it is handed -- <c>OnGridResized</c> sets <c>LayoutElement.minWidth</c>
/// and <c>RectTransform.sizeDelta</c> from <c>ItemViewFactory.GetCellPixelSize</c>,
/// which is <c>columns * 63 + 1</c> and knows nothing about 10. The 10 lives entirely
/// in the item template, the client is served that template over
/// <c>/client/items</c>, and so changing it here changes the grid everywhere.
///
/// What that does NOT do is make room for the result. Whether the wider grid is drawn
/// or clipped depends on how the stash panel's ScrollRect viewport is anchored in the
/// prefab, which is serialized data no amount of reading the assembly will reveal.
/// That is what the probe plugin is for.
///
/// ## Ordering
///
/// PostLoad, after profiles are loaded, because <see cref="StashOccupancy"/> needs to
/// see what the player has stored before it will agree to shorten anything. SPT's own
/// cutoff check (DatabaseIntegrityService.EnsureNoItemsAddedSinceProfilesLoaded)
/// objects to items being ADDED after that point; editing an existing template's grid
/// is not that, and this adds nothing.
/// </summary>
[Injectable(TypePriority = OnLoadOrder.PostLoad)]
public class StashWidener(
    ISptLogger<StashWidener> logger,
    TemplateTable templates,
    SaveServer saveServer,
    ModHelper modHelper)
    : IOnLoad
{
    /// <summary>
    /// The five stashes a player can actually own, by template id, with the edition
    /// each belongs to. Read out of items.json on 4.1.5 -- every one of them is
    /// cellsH 10, and they differ only in height.
    ///
    /// The developer stash (10x300) and the 8-wide containers are deliberately
    /// absent: not the stash screen, and being 8 wide they would trip the narrowing
    /// guard anyway.
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
        var configPath = System.IO.Path.Combine(
            modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly()),
            "ultrawidestash.config.json");

        var settings = StashSettings.Load(configPath, out var note);

        // Read the profiles off disk rather than asking SaveServer: this runs at
        // PostLoad (1,000,000) and SaveCallbacks -- which is what loads them -- sits at
        // the default priority of int.MaxValue, so the server's own dictionary is still
        // empty here. See ProfileScan for the whole story.
        var scan = ProfileScan.Run(ProfileDirectory(), SizeOf);

        if (!scan.Confident && settings.CompensateRows)
        {
            logger.Warning(
                $"[UltrawideStash] Not shortening any stash: {scan.Note}. Columns still widen; "
                + "rows are left alone because nothing here could confirm what is stored.");
        }

        var applied = 0;
        var refused = 0;

        foreach (var (id, edition) in PlayerStashes)
        {
            if (!templates.Items.TryGetValue(new MongoId(id), out var template))
            {
                logger.Warning($"[UltrawideStash] {edition} stash ({id}) is not in the database; skipped.");
                refused++;
                continue;
            }

            var grid = FirstGrid(template);

            if (grid?.Properties is null)
            {
                logger.Warning($"[UltrawideStash] {edition} stash has no grid; skipped.");
                refused++;
                continue;
            }

            var vanillaColumns = grid.Properties.CellsH ?? 0;
            var vanillaRows = grid.Properties.CellsV ?? 0;

            // When the scan cannot vouch for what is stored, claim the stash is
            // occupied to its very last row. StashLayout then clamps rows to vanilla
            // and nothing is ever cut out from under an item.
            var deepestRow = scan.Confident ? scan.DeepestFor(id) : (int)vanillaRows;

            var plan = StashLayout.For(
                (int)vanillaColumns,
                (int)vanillaRows,
                settings.Columns,
                settings.CompensateRows,
                deepestRow);

            if (!plan.Applied)
            {
                logger.Warning(
                    $"[UltrawideStash] {edition} stash left at {vanillaColumns}x{vanillaRows}: {plan.Reason}.");
                refused++;
                continue;
            }

            grid.Properties.CellsH = plan.Columns;
            grid.Properties.CellsV = plan.Rows;
            applied++;

            if (settings.Verbose)
            {
                logger.Info(
                    $"[UltrawideStash] {edition}: {vanillaColumns}x{vanillaRows} " +
                    $"({vanillaColumns * vanillaRows} cells) -> {plan.Columns}x{plan.Rows} " +
                    $"({plan.Capacity} cells, {plan.PixelWidth}px wide) -- {plan.Reason}.");
            }
        }

        var shape = settings.CompensateRows
            ? "capacity held at vanilla"
            : "rows kept, capacity increased";

        logger.Info(
            $"[UltrawideStash] {applied} stash template(s) set to {settings.Columns} columns, " +
            $"{shape}; {refused} skipped. ({note}.)");

        if (applied > 0)
        {
            logger.Info(
                "[UltrawideStash] Back up user/profiles before playing. Items placed past column 10 "
                + "are outside the grid if this mod is removed.");
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// The stash's own grid. Every player stash has exactly one; taking the first
    /// rather than assuming an index keeps this honest if that stops being true.
    /// </summary>
    private static Grid? FirstGrid(TemplateItem template)
    {
        var grids = template.Properties?.Grids;

        if (grids is null) return null;

        foreach (var g in grids) return g;

        return null;
    }

    /// <summary>
    /// Where SPT keeps profiles, taken from the server itself where possible.
    ///
    /// <c>SaveServer</c> holds the path in a private <c>profileFilepath</c>; its
    /// <c>RemoveProfile</c> spells the same thing as the literal <c>user/profiles/</c>,
    /// relative to the server's working directory. The field is read first because it
    /// is SPT's own answer, and the literal is the fallback if it is ever renamed.
    /// </summary>
    private string ProfileDirectory()
    {
        try
        {
            var field = typeof(SaveServer).GetField(
                "profileFilepath",
                BindingFlags.Instance | BindingFlags.NonPublic);

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

    /// <summary>
    /// An item template's footprint, defaulting to 1x1 for anything unrecognised.
    /// One cell is the smallest an item can be, so this under-reports rather than
    /// inventing depth that is not there.
    /// </summary>
    private (int Width, int Height) SizeOf(string templateId)
    {
        if (string.IsNullOrEmpty(templateId)) return (1, 1);

        if (!templates.Items.TryGetValue(new MongoId(templateId), out var template)) return (1, 1);

        var width = (int)(template.Properties?.Width ?? 1);
        var height = (int)(template.Properties?.Height ?? 1);

        return (width < 1 ? 1 : width, height < 1 ? 1 : height);
    }
}
