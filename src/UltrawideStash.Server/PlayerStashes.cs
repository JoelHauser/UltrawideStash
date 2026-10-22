using System.Collections.Concurrent;
using System.Globalization;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Helpers.Profile;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Utils;
using SPTarkov.Server.Core.Utils.Cloners;
using Shape = UltrawideStash.Server.StashShapes.Shape;

namespace UltrawideStash.Server;

/// <summary>
/// Each player's stash width, for a server several people play on (Fika).
///
/// ## What is per player and what is not
///
/// The database holds one plan for the server-wide width, made by
/// <see cref="StashWidener"/> at startup -- the width every player had before 1.1.3.
/// A player whose own screen (from <see cref="PlayerRecord"/>) gives that same width
/// shares it. A player whose screen gives a different one gets their own shapes, and
/// two patches make the server agree with itself about them:
///
/// <list type="bullet">
///   <item><see cref="ServeStashPatch"/>: their game is served copies of the five stash
///   templates at their own size over <c>/client/items</c>. Every other template is the
///   database's own object, untouched.</item>
///   <item><see cref="StashSizePatch"/>: SPT's own <c>InventoryHelper.GetPlayerStashSize</c>
///   -- what trader purchases, mail, quest rewards and anything else that puts items
///   in a stash on the server use to find room -- answers with their size, so the server
///   never places something in a column their game does not have.</item>
/// </list>
///
/// ## When a player's width changes
///
/// At their game's start, from <c>/ultrawidestash/hello</c>, which the probe sends from
/// its Awake: before the game has asked for its items or its profile. If the width
/// differs from the one their stash is in, the stash is repacked then, in the loaded
/// profile, with the same backup and the same rules as the startup repack. A
/// measurement taken during a session is stored and used from the next game start.
/// </summary>
[Injectable(InjectionType.Singleton)]
public class PlayerStashes(
    ISptLogger<PlayerStashes> logger,
    TemplateTable templates,
    SaveServer saveServer,
    JsonUtil jsonUtil,
    ICloner cloner,
    InventoryHelper inventoryHelper)
{
    /// <summary>One player's stash, as the server is serving it.</summary>
    public sealed class Entry
    {
        public required int Columns { get; init; }

        /// <summary>Null when this player shares the database's plan.</summary>
        public IReadOnlyDictionary<string, Shape>? Own { get; init; }

        /// <summary>The five templates at <see cref="Own"/>'s sizes, made on first serve.</summary>
        public Dictionary<MongoId, TemplateItem>? Served { get; set; }
    }

    private readonly ConcurrentDictionary<string, Entry> entries = new();

    private readonly object gate = new();

    private Dictionary<string, Shape> vanilla = new();

    private StashSettings settings = new();

    private int defaultColumns = StashFit.VanillaColumns;

    private string recordFolder = string.Empty;

    private string backupDirectory = string.Empty;

    /// <summary>False until startup has run; nothing is served differently before then.</summary>
    public bool Ready { get; private set; }

    /// <summary>
    /// Startup state from <see cref="StashWidener"/>: the database's sizes before it
    /// changed them, the config, the server-wide width, and where records and backups go.
    /// </summary>
    public void Begin(
        Dictionary<string, Shape> vanillaShapes,
        StashSettings stashSettings,
        int serverColumns,
        string modFolder,
        string backups)
    {
        vanilla = vanillaShapes;
        settings = stashSettings;
        defaultColumns = serverColumns;
        recordFolder = System.IO.Path.Combine(modFolder, PlayerRecord.FolderName);
        backupDirectory = backups;
    }

    /// <summary>Startup is done; per-player shapes are live.</summary>
    public void Open() => Ready = true;

    /// <summary>The server-wide width.</summary>
    public int DefaultColumns => defaultColumns;

    /// <summary>The record for a profile, or null.</summary>
    public PlayerRecord? RecordFor(string profileId) => PlayerRecord.Read(recordFolder, profileId);

    /// <summary>
    /// The width a player with this record gets, or null when the record says too
    /// little and the player keeps the server-wide width. <paramref name="reason"/> is
    /// for the log.
    /// </summary>
    public ColumnChoice.Choice? ChoiceFor(PlayerRecord? record, out string reason)
        => PlayerRecord.ChoiceFor(record, settings, out reason);

    /// <summary>A player who shares the database's plan.</summary>
    public void Share(string profileId, int columns)
        => entries[profileId] = new Entry { Columns = columns };

    /// <summary>A player with shapes of their own.</summary>
    public void Give(string profileId, int columns, IReadOnlyDictionary<string, Shape> shapes)
        => entries[profileId] = new Entry { Columns = columns, Own = shapes };

    /// <summary>
    /// The item templates to serve this session, or null to serve the database as it is.
    /// </summary>
    public Dictionary<MongoId, TemplateItem>? ItemsFor(MongoId sessionId)
    {
        if (!Ready || !entries.TryGetValue(sessionId.ToString(), out var entry) || entry.Own is null) return null;

        var served = entry.Served;

        if (served is null)
        {
            served = new Dictionary<MongoId, TemplateItem>();

            foreach (var (id, shape) in entry.Own)
            {
                if (!templates.Items.TryGetValue(new MongoId(id), out var template)) continue;

                var copy = cloner.Clone(template);
                var grid = FirstGrid(copy);

                if (copy is null || grid?.Properties is null) continue;

                grid.Properties.CellsH = shape.Columns;
                grid.Properties.CellsV = shape.Rows;
                served[copy.Id] = copy;
            }

            entry.Served = served;
        }

        // Once per game start: the line that shows, in the server log, that this
        // player's game got their own stash and not the server's.
        logger.Info(
            $"[UltrawideStash] {NameOf(sessionId)}: serving their own stash, {entry.Columns} columns "
            + $"({string.Join(", ", entry.Own.Select(s => $"{s.Value.Columns}x{s.Value.Rows}"))}).");

        var items = new Dictionary<MongoId, TemplateItem>(templates.Items);

        foreach (var (id, template) in served) items[id] = template;

        return items;
    }

    /// <summary>
    /// The five stash sizes this player's game must use, as <c>WxH</c> by template id: their
    /// own shapes, or the database's for a player who shares them. Sent back in the reply to
    /// the hello, and applied by the probe to the game's own copy of the templates.
    ///
    /// ## Why the game is told twice (1.1.4)
    ///
    /// <c>/client/items</c> already carries these sizes. On a Fika host in Forge issue #5 the
    /// server logged serving a player their own 19-wide stash, and their game measured the
    /// host's 10-wide one: something on that server replaced the response after this mod
    /// built it. The same path worked in a real game on a server without whatever that is.
    /// Telling the probe directly does not depend on the items response surviving the trip.
    /// </summary>
    public Dictionary<string, string> ShapesFor(MongoId sessionId)
    {
        var shapes = new Dictionary<string, string>();
        var own = entries.TryGetValue(sessionId.ToString(), out var entry) && Ready ? entry.Own : null;

        foreach (var (id, _) in StashLadder.Rungs)
        {
            if (own is not null && own.TryGetValue(id, out var mine))
            {
                shapes[id] = $"{mine.Columns}x{mine.Rows}";
                continue;
            }

            if (!templates.Items.TryGetValue(new MongoId(id), out var template)) continue;

            var grid = FirstGrid(template)?.Properties;

            if (grid?.CellsH is > 0 && grid.CellsV is > 0) shapes[id] = $"{grid.CellsH}x{grid.CellsV}";
        }

        return shapes;
    }

    /// <summary>
    /// SPT's own answer for a player's stash size, corrected to their own shape when
    /// they have one. The bonus rows SPT added stay added: only the template's part of
    /// the answer is swapped.
    /// </summary>
    public (int, int) StashSize(PmcData? pmc, (int Columns, int Rows) original)
    {
        if (!Ready || pmc is null) return original;

        var key = pmc.SessionId?.ToString() ?? pmc.Id?.ToString();

        if (key is null || !entries.TryGetValue(key, out var entry) || entry.Own is null) return original;

        var stashId = pmc.Inventory?.Stash;

        if (stashId is null || pmc.Inventory?.Items is null) return original;

        var stash = pmc.Inventory.Items.FirstOrDefault(i => i.Id == stashId.Value);

        if (stash is null || !entry.Own.TryGetValue(stash.Template.ToString(), out var shape)) return original;

        if (!templates.Items.TryGetValue(stash.Template, out var template)) return original;

        var db = FirstGrid(template)?.Properties;

        if (db is null) return original;

        var dbRows = db.CellsV is null or 0 ? 66 : db.CellsV.Value;
        var bonus = original.Rows - dbRows;

        return (shape.Columns, shape.Rows + bonus);
    }

    /// <summary>
    /// A game has started: record what its probe reports, and re-plan that player's
    /// stash if their width has changed. Returns the line the probe logs.
    /// </summary>
    public async Task<string> Hello(MongoId sessionId, HelloRequest hello, CancellationToken token)
    {
        var id = sessionId.ToString();

        if (!Ready) return "the server is still starting; the server-wide width applies";

        var record = RecordFor(id) ?? new PlayerRecord();

        record.Screen = hello.Screen;
        record.WidenStashPanel = hello.WidenStashPanel;
        record.GearPanelReserve = hello.GearPanelReserve > 0f ? hello.GearPanelReserve : ClientSettings.DefaultReserve;
        record.ProbeVersion = hello.ProbeVersion;
        record.SeenAtUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

        Save(id, record);

        var choice = ChoiceFor(record, out var reason);

        if (choice is null) return $"stash: {defaultColumns} columns, the server-wide width -- {reason}";

        var name = NameOf(sessionId);
        var columns = choice.Value.Columns;

        string line;
        var save = false;

        lock (gate)
        {
            var had = entries.TryGetValue(id, out var entry) ? entry : null;

            if (had is not null && had.Columns == columns)
            {
                line = $"stash: {columns} columns -- {reason}";
            }
            else
            {
                (line, save) = Replan(sessionId, name, had, columns, reason);
            }
        }

        logger.Info($"[UltrawideStash] {name}: {line}");

        if (choice.Value.Source == ColumnChoice.Origin.Measured && record.Measurement?.Diagnosis() is { } why)
        {
            logger.Warning($"[UltrawideStash] {name}: {why}");
        }

        if (save)
        {
            try
            {
                await saveServer.SaveProfileAsync(sessionId, token);
            }
            catch (Exception e)
            {
                logger.Warning(
                    $"[UltrawideStash] Could not save {name}'s profile straight away ({e.Message}). "
                    + "The relocation is in the running server and will be written at its next save.");
            }
        }

        return line;
    }

    /// <summary>The probe measured its stash panel: keep it for this player's next game start.</summary>
    public string Measured(MongoId sessionId, MeasuredRequest measured)
    {
        if (string.IsNullOrEmpty(recordFolder)) return "the server is still starting";

        var id = sessionId.ToString();
        var record = RecordFor(id) ?? new PlayerRecord { Screen = measured.Screen };

        record.Measurement = new Measurement
        {
            MaxColumns = measured.MaxColumns,
            Screen = measured.Screen,
            CanvasWidth = measured.CanvasWidth,
            PanelWidth = measured.PanelWidth,
            MeasuredAtUtc = measured.MeasuredAtUtc,
            ProbeVersion = measured.ProbeVersion,
            CanWiden = measured.CanWiden,
            WhyNot = measured.WhyNot,
        };

        Save(id, record);

        var now = entries.TryGetValue(id, out var entry) ? entry.Columns : defaultColumns;

        return measured.MaxColumns == now
            ? $"the server has the measurement: {measured.MaxColumns} columns, which is what this stash already is"
            : $"the server has the measurement: {measured.MaxColumns} columns; this stash is {now}, "
              + "and changes the next time the game starts";
    }

    /// <summary>
    /// Re-plan one player at a new width, repacking their loaded stash to fit. Runs under
    /// <see cref="gate"/>. On any refusal the player keeps what they had.
    /// </summary>
    private (string Line, bool Save) Replan(MongoId sessionId, string name, Entry? had, int columns, string reason)
    {
        var id = sessionId.ToString();
        var loaded = saveServer.ProfileExists(sessionId) && !saveServer.IsProfileInvalidOrUnloadable(sessionId)
            ? saveServer.GetProfile(sessionId)
            : null;

        var contents = loaded is null ? null : Contents(loaded, PathOf(id));

        // No stash yet (a new account), or not one of the five: nothing to repack, and
        // the shapes are planned from an empty stash.
        if (contents is null || StashLadder.PositionOf(contents.StashTemplateId) < 0)
        {
            if (columns == defaultColumns && contents is null) Share(id, columns);
            else Give(id, columns, StashShapes.For(null, vanilla, columns, settings.CompensateRows));

            return ($"stash: {columns} columns -- {reason}", false);
        }

        var shapes = StashShapes.For(contents, vanilla, columns, settings.CompensateRows);
        var shape = shapes[contents.StashTemplateId];
        var toSave = new List<MongoId>();
        var moved = 0;

        var edition = StashLadder.Rungs[StashLadder.PositionOf(contents.StashTemplateId)].Edition;

        if (!Relocate([contents], shape.Columns, shape.Rows, edition, toSave, ref moved))
        {
            return ($"stash left at {had?.Columns ?? defaultColumns} columns: it would not fit "
                    + $"{shape.Columns}x{shape.Rows} -- {reason}", false);
        }

        Give(id, columns, shapes);

        var was = had?.Columns ?? defaultColumns;

        return ($"stash: {columns} columns, was {was} ({shape.Columns}x{shape.Rows} {edition}"
                + (moved > 0 ? $", {moved} item(s) moved to fit" : string.Empty)
                + $") -- {reason}", toSave.Count > 0);
    }

    /// <summary>
    /// Relocate anything that does not fit, writing the profile back.
    ///
    /// Returns false when some item has nowhere to go -- the caller then leaves that
    /// stash alone entirely rather than applying a size that would hide something.
    /// </summary>
    public bool Relocate(
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
    /// The server's loaded copy of the profile stored in <paramref name="file"/>, or null
    /// when it has none -- a file SPT skipped, or one it marked invalid, which it will
    /// neither serve nor save. Profiles are named for their id.
    /// </summary>
    public SptProfile? LoadedProfile(string file, out MongoId id)
    {
        id = default;

        var name = System.IO.Path.GetFileNameWithoutExtension(file);

        if (!MongoId.IsValidMongoId(name)) return null;

        id = new MongoId(name);

        if (!saveServer.ProfileExists(id) || saveServer.IsProfileInvalidOrUnloadable(id)) return null;

        return saveServer.GetProfile(id);
    }

    /// <summary>
    /// A loaded profile's stash, read through SPT's own serializer: exactly the shape SPT
    /// writes to disk. Null when it has no stash to speak of.
    /// </summary>
    public ProfileStore.StashContents? Contents(SptProfile loaded, string path)
    {
        var json = jsonUtil.Serialize(loaded);

        if (json is null) return null;

        var items = loaded.CharacterData?.PmcData?.Inventory?.Items;

        if (items is null) return ProfileStore.Parse(json, path, SizeOf);

        // As SPT sizes them -- a weapon with its attachments on -- so the repack and
        // SPT's own bounds check agree. See ProfileStore.Parse.
        return ProfileStore.Parse(json, path, (itemId, templateId) =>
        {
            try
            {
                if (MongoId.IsValidMongoId(itemId) && MongoId.IsValidMongoId(templateId))
                {
                    var (width, height) = inventoryHelper.GetItemSize(new MongoId(templateId), new MongoId(itemId), items);

                    if (width > 0 && height > 0) return (width, height);
                }
            }
            catch
            {
                // The template's own size below.
            }

            return SizeOf(templateId);
        });
    }

    /// <summary>
    /// An item template's footprint, 1x1 for anything unrecognised -- the smallest an
    /// item can be, so this under-reports rather than inventing size.
    /// </summary>
    public (int Width, int Height) SizeOf(string templateId)
    {
        if (string.IsNullOrEmpty(templateId)) return (1, 1);

        if (!templates.Items.TryGetValue(new MongoId(templateId), out var template)) return (1, 1);

        var width = template.Properties?.Width ?? 1;
        var height = template.Properties?.Height ?? 1;

        return (width < 1 ? 1 : width, height < 1 ? 1 : height);
    }

    /// <summary>Where SPT keeps this profile, for naming it and backing it up.</summary>
    private string PathOf(string profileId)
        => System.IO.Path.Combine(StashWidener.ProfileDirectory(saveServer), profileId + ".json");

    /// <summary>The player's nickname for the log, else the profile id.</summary>
    private string NameOf(MongoId sessionId)
    {
        try
        {
            if (saveServer.ProfileExists(sessionId)
                && saveServer.GetProfile(sessionId)?.CharacterData?.PmcData?.Info?.Nickname is { Length: > 0 } nick)
            {
                return nick;
            }
        }
        catch
        {
            // Fall through to the id.
        }

        return sessionId.ToString();
    }

    private void Save(string id, PlayerRecord record)
    {
        try
        {
            PlayerRecord.Write(recordFolder, id, record);
        }
        catch (Exception e)
        {
            logger.Warning($"[UltrawideStash] Could not write the screen record for {id} ({e.Message}).");
        }
    }

    /// <summary>The stash's own grid; every player stash has exactly one.</summary>
    public static Grid? FirstGrid(TemplateItem? template)
    {
        var grids = template?.Properties?.Grids;

        if (grids is null) return null;

        foreach (var g in grids) return g;

        return null;
    }
}
