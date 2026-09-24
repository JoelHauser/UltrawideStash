using System.Reflection;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Utils;

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
/// ## Ordering, which is load-bearing -- and was misread until 1.0.1
///
/// This runs at <c>OnLoadOrder.PostLoad</c> (1,000,000). <c>SaveCallbacks</c>, which
/// calls <c>SaveServer.LoadAsync()</c>, runs at <c>OnLoadOrder.SaveCallbacks</c>
/// (600,000) -- its attribute's <c>int.MaxValue</c> is a different argument -- and SPT
/// orders <c>IOnLoad</c> ascending. So **every profile is already loaded** by the time
/// this runs.
///
/// Up to 1.0.0 this edited only the files, and SPT's loaded copies never saw it: the
/// client was served the unmoved stash and the next save put it back. Now a loaded
/// profile is read from, moved in and saved through <c>SaveServer</c>; the file path is
/// kept only for a profile the server has not loaded. See <see cref="ProfileStore"/>.
/// </summary>
[Injectable(TypePriority = OnLoadOrder.PostLoad)]
public class StashWidener(
    ISptLogger<StashWidener> logger,
    TemplateTable templates,
    SaveServer saveServer,
    JsonUtil jsonUtil)
    : IOnLoad
{
    /// <summary>Where this install's profile backups go -- see <see cref="BackupStore"/>.</summary>
    private string backupDirectory = string.Empty;

    public async Task OnLoadAsync(CancellationToken cancellationToken)
    {
        var folder = ModFolder();

        backupDirectory = BackupStore.DirectoryFor(ProfileDirectory());

        var settings = StashSettings.Load(
            System.IO.Path.Combine(folder, "ultrawidestash.config.json"),
            out var note);

        // What the client last measured of its own stash panel. Absent on a first run,
        // on a dedicated server and whenever the probe is not installed -- all of which
        // fall back to the conservative estimate from the declared screen size.
        var measurement = Measurement.Read(
            System.IO.Path.Combine(folder, Measurement.FileName),
            out var measurementNote);

        // The screen the game will open on, so the very first start can size the stash
        // without waiting for the client to measure itself. Falls back to the declared
        // size in the config when it cannot be read.
        var detected = ScreenProbe.Detect(out var screenNote);

        var screenWidth = detected?.Width ?? settings.ScreenWidth;
        var screenHeight = detected?.Height ?? settings.ScreenHeight;

        // A measurement taken on a different screen describes a panel that no longer
        // exists. Keeping it would size the stash for the old monitor and, if that one
        // was wider, overflow the new one -- so it is dropped and the estimate used
        // until the client measures again.
        if (measurement is not null && detected is not null)
        {
            var now = $"{detected.Value.Width}x{detected.Value.Height}";
            var then = measurement.Screen;

            if (!string.IsNullOrEmpty(then)
                && !string.Equals(then, now, StringComparison.Ordinal))
            {
                logger.Info(
                    $"[UltrawideStash] The stored measurement was taken on {then} and this "
                    + $"machine is now {now}, so it has been ignored. Open your stash once and "
                    + "restart to measure the new screen.");

                measurement = null;
                measurementNote = $"measurement from {then} discarded";
            }
        }

        var choice = ColumnChoice.For(
            settings.Columns,
            measurement,
            screenWidth,
            screenHeight,
            settings.IgnoreMeasurement);

        WriteUninstallNote(folder);

        TidyBackups();

        var profiles = ReadProfiles(out var readable, out var why);

        if (!readable)
        {
            logger.Error(
                $"[UltrawideStash] Could not read profiles ({why}). Doing nothing at all this "
                + "start: without knowing what is stored, resizing the stash could put items "
                + "somewhere you cannot reach them.");
            return;
        }

        // The row floor for each rung of the hideout's stash ladder. A Standard player
        // climbs these templates as they upgrade the Stash area, so a rung must never
        // be planned shorter than the rung below it -- see StashLadder.
        var floors = StashLadder.RowFloors(DeepestByTemplate(profiles));

        var applied = 0;
        var moved = 0;

        // Loaded profiles changed in memory, saved once the loop is done.
        var toSave = new List<MongoId>();

        foreach (var (id, edition) in StashLadder.Rungs)
        {
            if (!templates.Items.TryGetValue(new MongoId(id), out var template)) continue;

            var grid = FirstGrid(template);

            if (grid?.Properties is null) continue;

            var vanillaColumns = grid.Properties.CellsH ?? 0;
            var vanillaRows = grid.Properties.CellsV ?? 0;

            if (vanillaColumns <= 0 || vanillaRows <= 0) continue;

            var mine = profiles.Where(p => p.StashTemplateId == id).ToList();

            var floor = floors.TryGetValue(id, out var f) ? f : DeepestRow(mine);

            // How deep items sit *now* is not how deep they need to sit once the
            // stash is wider.
            //
            // The floor is the deepest occupied row before anything has been moved,
            // and passing it straight to the planner defeats the whole point of
            // compensateRows: a 10x68 stash with something parked at row 64 plans as
            // 19x64, which is 1216 cells against vanilla's 680. The player asked for
            // a wider stash and got a near-doubled one.
            //
            // It was never the right question. StashRepack leaves items that already
            // fit exactly where they are and moves only the ones that do not, so
            // widening to 19 columns pulls those deep items up into the free space
            // the extra width opened. What matters is the shallowest grid they can be
            // repacked into, which is found by asking, not by assuming the worst.
            var wanted = StashLayout.For(
                vanillaColumns, vanillaRows, choice.Columns, settings.CompensateRows, 0);

            var settledFloor = floor;

            if (wanted.Applied && settings.CompensateRows)
            {
                settledFloor = ShallowestThatFits(mine, wanted.Columns, wanted.Rows, floor);
            }

            var plan = StashLayout.For(
                vanillaColumns, vanillaRows, choice.Columns, settings.CompensateRows,
                settledFloor);

            // Whether or not the template changes, the profile may hold items from a
            // previous configuration, so the repack is driven by the FINAL size.
            var finalColumns = plan.Applied ? plan.Columns : vanillaColumns;
            var finalRows = plan.Applied ? plan.Rows : vanillaRows;

            if (!Relocate(mine, finalColumns, finalRows, edition, toSave, ref moved))
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

        // The moves are already in the loaded profiles, which is what the client is
        // served; this only puts them on disk now rather than at SPT's next save. A
        // failure here loses nothing -- that save will write them anyway.
        foreach (var id in toSave.Distinct())
        {
            try
            {
                await saveServer.SaveProfileAsync(id, cancellationToken);
            }
            catch (Exception e)
            {
                logger.Warning(
                    $"[UltrawideStash] Could not save profile {id} straight away ({e.Message}). "
                    + "The relocation is in the running server and will be written at its next save.");
            }
        }

        var shape = settings.CompensateRows ? "capacity held" : "rows kept";

        logger.Info(
            $"[UltrawideStash] {applied} stash template(s) at {choice.Columns} columns "
            + $"({choice.PixelWidth}px), {shape}"
            + (moved > 0 ? $"; {moved} item(s) relocated to stay reachable" : string.Empty)
            + $". ({note}; {measurementNote}.)");

        // Always say how the width was arrived at. The failure this guards against is
        // silent -- a grid too wide for the panel is clipped, not resized -- so the
        // reasoning has to be in the log whether or not anything looks wrong.
        logger.Info($"[UltrawideStash] Width: {choice.Reason}. ({screenNote}.)");

        if (choice.Source == ColumnChoice.Origin.Clamped)
        {
            logger.Warning(
                "[UltrawideStash] The configured width was reduced. Nothing is lost and nothing "
                + "is stranded -- the stash is simply narrower than asked for."
                + (moved > 0
                    ? $" The {moved} item(s) relocated above were sitting in columns that the "
                      + "narrower stash does not have, and they have been packed back inside it."
                    : string.Empty));
        }

        // The first run after an install, where nothing has measured the panel yet.
        //
        // This is the single most confusing state the mod has: the player installs a
        // mod called Ultrawide Stash, starts the server, and it reports a vanilla
        // 10-column stash with no explanation. It is not a failure -- the width comes
        // from the client measuring its own panel, which cannot have happened before
        // the client has run -- but saying so quietly, in the same tone as a normal
        // result, reads as the mod not working.
        //
        // Warning rather than Info because the player has to do something.
        if (measurement is null && applied == 0 && choice.IsNoOp)
        {
            logger.Warning(
                "[UltrawideStash] NOT WIDENED YET -- expected on a first run, and one more "
                + "step finishes it. The width is measured from your real stash panel, so the "
                + "client has to run once before the server can know it.");
            logger.Warning(
                "[UltrawideStash]   1. Check WidenStashPanel is true in BepInEx/config/"
                + "com.mybutthasarash.ultrawidestash.cfg");
            logger.Warning(
                "[UltrawideStash]   2. Start the game and open your stash once.");
            logger.Warning(
                "[UltrawideStash]   3. Quit, restart this server, and the stash will be its "
                + "full width.");
            logger.Warning(
                "[UltrawideStash] If step 1 is false the panel is never widened, the probe "
                + "measures the vanilla one, and every later start reads 10 columns back "
                + "from that measurement.");
        }

        // Explain a do-nothing run, but only when it really did nothing. A run that
        // clamped an over-wide config back to vanilla also changes no template, and
        // saying "no change was made" there would contradict the relocation count.
        if (measurement is not null && applied == 0 && moved == 0 && choice.IsNoOp)
        {
            logger.Info(
                "[UltrawideStash] No change was made, and on a 16:9 screen that is the correct "
                + "default: EFT scales its menu by min(width/1920, height/1080), so 1080p, 1440p "
                + "and 4K all get a canvas exactly 1920 units wide and have no spare room to "
                + "widen into. Only a wider-than-16:9 screen gains any. Install the probe, open "
                + "your stash once and restart the server -- if the panel does have slack of its "
                + "own, the measurement will find it and auto will use it.");
        }

        if (applied > 0)
        {
            logger.Info(
                "[UltrawideStash] To remove this mod safely: set columns to 10, start the server "
                + "once so items are packed back into a vanilla stash, then delete the files.");
        }
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
        List<MongoId> toSave,
        ref int moved)
    {
        // Plan every profile before writing any of them, so a stash that cannot be
        // packed stops the whole thing rather than leaving some files already edited.
        var planned = new List<(ProfileStore.StashContents Profile,
            IReadOnlyList<StashRepack.Move> Moves,
            IReadOnlyList<StashRepack.Transfer> Transfers)>();

        foreach (var profile in profiles)
        {
            // Against this player's grid: the template's rows plus their own StashRows
            // bonus. Planning without the bonus moved items the player had put in those
            // rows on every start.
            var profileRows = profile.RowsFor(rows);
            var plan = StashRepack.For(profile.Items, columns, profileRows);

            var transfers = new List<StashRepack.Transfer>();

            if (plan.Homeless.Count > 0)
            {
                // The stash itself cannot hold everything. Rather than refusing and
                // leaving the player stuck, overflow into the Sorting Table: it grows
                // vertically without bound, this mod never touches it, and it is
                // somewhere they will actually look.
                if (string.IsNullOrEmpty(profile.SortingTableId))
                {
                    logger.Error(
                        $"[UltrawideStash] {edition}: {plan.Homeless.Count} item(s) in "
                        + $"'{System.IO.Path.GetFileName(profile.FilePath)}' do not fit "
                        + $"{columns}x{profileRows} and that profile has no sorting table to put "
                        + "them in. Free some space, or raise columns, and start again.");
                    return false;
                }

                var byId = profile.Items.ToDictionary(i => i.ItemId);

                var homeless = plan.Homeless
                    .Where(byId.ContainsKey)
                    .Select(id => byId[id])
                    .ToList();

                transfers = StashRepack.IntoSortingTable(
                    homeless, profile.SortingTableItems, out var tooWide);

                if (tooWide.Count > 0)
                {
                    logger.Error(
                        $"[UltrawideStash] {edition}: {tooWide.Count} item(s) in "
                        + $"'{System.IO.Path.GetFileName(profile.FilePath)}' fit neither "
                        + $"{columns}x{profileRows} nor the sorting table. Nothing was changed.");
                    return false;
                }
            }

            if (plan.Moves.Count > 0 || transfers.Count > 0)
            {
                planned.Add((profile, plan.Moves, transfers));
            }
        }

        foreach (var (profile, moves, transfers) in planned)
        {
            try
            {
                int written;
                var loaded = LoadedProfile(profile.FilePath, out var id);

                if (loaded?.CharacterData?.PmcData?.Inventory?.Items is { } items)
                {
                    // The copy the client is served and SPT will save. Backed up first,
                    // exactly as the file path is, then moved in place.
                    BackupStore.Backup(profile.FilePath, backupDirectory);

                    written = ProfileStore.ApplyToItems(
                        items, moves, profile.SortingTableId, transfers);

                    if (written > 0) toSave.Add(id);
                }
                else
                {
                    written = ProfileStore.ApplyChanges(
                        profile.FilePath, moves, profile.SortingTableId, transfers,
                        backupDirectory);
                }

                moved += written;

                var where = transfers.Count > 0
                    ? $"{moves.Count} back inside the {edition} stash and {transfers.Count} "
                      + "into the sorting table"
                    : $"{written} back inside the {edition} stash";

                logger.Info(
                    $"[UltrawideStash] Moved {where} in "
                    + $"'{System.IO.Path.GetFileName(profile.FilePath)}' (backed up first, to "
                    + $"{backupDirectory}).");
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
    /// The deepest occupied row on each stash template, keyed by template id.
    ///
    /// Feeds <see cref="StashLadder.RowFloors"/>, which turns it into the running
    /// maximum up the hideout upgrade ladder. Templates nobody is on are simply
    /// absent, which the ladder reads as a depth of zero.
    /// </summary>
    private static Dictionary<string, int> DeepestByTemplate(
        List<ProfileStore.StashContents> profiles)
    {
        var deepest = new Dictionary<string, int>();

        foreach (var profile in profiles)
        {
            if (string.IsNullOrEmpty(profile.StashTemplateId)) continue;

            // Bonus rows are the player's own, so they come off before the template's
            // depth is decided.
            var needed = profile.TemplateRowsNeeded();

            if (!deepest.TryGetValue(profile.StashTemplateId, out var already) || needed > already)
            {
                deepest[profile.StashTemplateId] = needed;
            }
        }

        return deepest;
    }

    /// <summary>
    /// How many rows the deepest stored item needs across these profiles. Only a
    /// fallback now -- <see cref="StashLadder.RowFloors"/> is what the loop uses --
    /// kept for the case where a template is somehow not on the ladder.
    /// </summary>
    /// <summary>
    /// The shallowest grid, from <paramref name="ideal"/> rows upward, that every
    /// profile on this template can actually be repacked into.
    ///
    /// Returns <paramref name="ideal"/> when the compact grid holds everything, which
    /// is the normal case: the cells do not go anywhere when a stash is reshaped, and
    /// compensateRows keeps the count. It climbs only for a stash that genuinely
    /// cannot be packed that tightly -- large items that will not tessellate, mostly
    /// -- and stops at <paramref name="ceiling"/>, the pre-move depth, which always
    /// fits because it is where the items already are.
    /// </summary>
    private static int ShallowestThatFits(
        List<ProfileStore.StashContents> profiles,
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
    private static bool EverythingFits(
        List<ProfileStore.StashContents> profiles,
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

    private static int DeepestRow(List<ProfileStore.StashContents> profiles)
    {
        var deepest = 0;

        foreach (var profile in profiles)
        {
            var needed = profile.TemplateRowsNeeded();

            if (needed > deepest) deepest = needed;
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
                // The loaded copy when there is one: it is what the client will be
                // served, and SPT may have migrated or cleaned it since reading the file.
                var loaded = LoadedProfile(file, out _);
                var json = loaded is null ? null : jsonUtil.Serialize(loaded);

                var contents = json is null
                    ? ProfileStore.Read(file, SizeOf)
                    : ProfileStore.Parse(json, file, SizeOf);

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
    /// The server's loaded copy of the profile stored in <paramref name="file"/>, or null
    /// when it has none -- a file SPT skipped, or one it marked invalid, which it will
    /// neither serve nor save. Profiles are named for their id.
    /// </summary>
    private SptProfile? LoadedProfile(string file, out MongoId id)
    {
        id = default;

        var name = System.IO.Path.GetFileNameWithoutExtension(file);

        if (!MongoId.IsValidMongoId(name)) return null;

        id = new MongoId(name);

        if (!saveServer.ProfileExists(id) || saveServer.IsProfileInvalidOrUnloadable(id)) return null;

        return saveServer.GetProfile(id);
    }

    /// <summary>
    /// Every profile's backups out of the profiles folder and down to its original plus
    /// the newest few -- see <see cref="BackupStore"/>. Run on every start, not only when
    /// something is moved, so what older versions left behind is cleared even on a
    /// profile that is never relocated again. Best effort: a failure here must never stop
    /// the mod.
    /// </summary>
    private void TidyBackups()
    {
        var moved = 0;
        var deleted = 0;

        try
        {
            // Every profile with backups, deleted ones included -- see BackupStore.TidyAll.
            var tidied = BackupStore.TidyAll(ProfileDirectory(), backupDirectory);

            moved = tidied.Moved;
            deleted = tidied.Deleted;
        }
        catch (Exception e)
        {
            logger.Warning(
                $"[UltrawideStash] Could not tidy profile backups ({e.Message}). They are left "
                + "as they are, and the next start tries again.");
        }

        if (moved > 0)
        {
            logger.Info(
                $"[UltrawideStash] Moved {moved} profile backup(s) out of the profiles folder, "
                + $"to {backupDirectory}.");
        }

        if (deleted > 0)
        {
            logger.Info(
                $"[UltrawideStash] Removed {deleted} old profile backup(s). Each profile keeps "
                + "its .ultrawidestash-original.bak and the "
                + $"{BackupStore.RecentKept} most recent, in {backupDirectory}.");
        }
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

    /// <summary>
    /// Drop a plain-text uninstall note beside the DLL, rewritten on every start.
    ///
    /// The one thing that must not be got wrong is removing the mod while items are
    /// still out in the extra columns: nothing is deleted, but they become unreachable
    /// until it is reinstalled. Saying so in the README and in the startup log is not
    /// enough, because the person about to delete this folder is looking at **this
    /// folder**, not at either of those.
    ///
    /// Best effort. A failure here must never stop the mod loading.
    /// </summary>
    private void WriteUninstallNote(string folder)
    {
        const string name = "HOW-TO-UNINSTALL.txt";

        var text = string.Join(Environment.NewLine,
        [
            "Ultrawide Stash -- how to remove this mod safely",
            "================================================",
            "",
            "DO NOT just delete this folder while your stash is wider than 10 columns.",
            "",
            "Nothing would be deleted -- the SPT server never removes items -- but anything",
            "sitting in column 10 or beyond would become invisible and unreachable, because",
            "a vanilla stash is only 10 wide and the game's own recovery for this does not",
            "work (the Sorting Table it tries to move things into has a 0x0 grid).",
            "",
            "Instead:",
            "",
            "  1. Open ultrawidestash.config.json (in this folder) and set:  \"columns\": 10",
            "     Use the number 10, NOT \"auto\" -- auto means \"as wide as this screen",
            "     allows\", which is the opposite of what an uninstall needs.",
            "  2. Start the SPT server once and wait for it to finish loading.",
            "     The log will say how many items it moved back into the stash.",
            "  3. Stop the server, then delete:",
            "       - this folder",
            "       - BepInEx/plugins/UltrawideStash.Probe.dll",
            "       - ultrawidestash.measured.json, if it is still here",
            "",
            "That is all. After step 2 the mod is not changing anything, so removing it",
            "changes nothing either.",
            "",
            "ALREADY DELETED IT AND THINGS ARE MISSING?",
            "",
            "Nothing is lost. Reinstall the mod with the same \"columns\" value you were",
            "using, start the server, and everything will be exactly where you left it.",
            "Then follow the steps above.",
            "",
            "Profile backups this mod has taken are kept in:",
            $"  {backupDirectory}",
            "one <profile>.json.ultrawidestash-original.bak per profile (before the mod",
            "first changed it) plus the two most recent. That folder is outside SPT, so",
            "deleting the mod leaves it behind: delete it too once you are happy.",
            "",
        ]);

        try
        {
            var path = System.IO.Path.Combine(folder, name);

            // Only rewrite when it differs, so the file's timestamp stays meaningful.
            if (File.Exists(path) && File.ReadAllText(path) == text) return;

            File.WriteAllText(path, text);
        }
        catch (Exception e)
        {
            logger.Warning($"[UltrawideStash] Could not write {name} ({e.Message}).");
        }
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
