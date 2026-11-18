using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace UltrawideStash.Server;

/// <summary>
/// What one player's game told the server about their own screen.
///
/// ## Why the width is per player (1.1.3)
///
/// Up to 1.1.2 the width was one number for the whole server, chosen from the screen of
/// the machine the server runs on. That is right for single player and wrong for Fika:
/// everyone plays on the host's server, so a 16:9 friend got the host's 21:9 stash and
/// a sideways scrollbar (Forge issue #4). The friend's own game could not help -- it
/// wrote its measurement into the server folder on the friend's PC, which the host never
/// reads.
///
/// So each game now reports in over HTTP, to whichever server it is connected to, under
/// its own session: the screen it starts on and its two layout settings at game start
/// (<c>/ultrawidestash/hello</c>), and its measurement when it takes one
/// (<c>/ultrawidestash/measured</c>). One file per profile, in <c>players/</c> beside the
/// config, keyed by profile id.
///
/// A profile with no record -- one whose game has never run 1.1.3 or later -- keeps
/// the server-wide width, exactly as before, so updating changes nothing until each
/// player starts their game once.
/// </summary>
public sealed class PlayerRecord
{
    /// <summary>The folder the records live in, inside the server mod's folder.</summary>
    public const string FolderName = "players";

    /// <summary>The screen the game started on, as <c>WxH</c>.</summary>
    [JsonPropertyName("screen")]
    public string? Screen { get; set; }

    /// <summary>The game's <c>WidenStashPanel</c>.</summary>
    [JsonPropertyName("widenStashPanel")]
    public bool WidenStashPanel { get; set; } = true;

    /// <summary>The game's <c>GearPanelReserve</c>.</summary>
    [JsonPropertyName("gearPanelReserve")]
    public float GearPanelReserve { get; set; } = ClientSettings.DefaultReserve;

    /// <summary>The version of the game half that last reported in.</summary>
    [JsonPropertyName("probeVersion")]
    public string? ClientVersion { get; set; }

    /// <summary>When the game last started, for the record's own sake.</summary>
    [JsonPropertyName("seenAtUtc")]
    public string? SeenAtUtc { get; set; }

    /// <summary>The game's last measurement of this player's stash panel, or null.</summary>
    [JsonPropertyName("measurement")]
    public Measurement? Measurement { get; set; }

    /// <summary>The game's settings, in the shape <see cref="ColumnChoice"/> takes.</summary>
    [JsonIgnore]
    public ClientSettings Client => new(WidenStashPanel, GearPanelReserve, true);

    /// <summary>The reported screen as numbers, or false when there is not a usable one.</summary>
    public bool TryScreen(out int width, out int height)
    {
        width = 0;
        height = 0;

        if (string.IsNullOrWhiteSpace(Screen)) return false;

        var parts = Screen.Trim().Split('x', 'X');

        return parts.Length == 2
               && int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out width)
               && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out height)
               && width > 0
               && height > 0;
    }

    /// <summary>
    /// The measurement when it describes the screen this player is on now, else null,
    /// with <paramref name="note"/> saying which. Same rules as the server-wide file:
    /// another screen's panel, or one measured by a version before 1.0.5, is not trusted.
    /// </summary>
    public Measurement? UsableMeasurement(out string note)
    {
        var m = Measurement;

        if (m is null)
        {
            note = "not measured yet";
            return null;
        }

        if (m.MaxColumns < StashFit.MinColumns)
        {
            note = $"measurement of {m.MaxColumns} columns is not usable";
            return null;
        }

        if (m.PredatesTheStretch())
        {
            note = $"measurement from version {m.ClientVersion ?? "(unknown)"} ignored";
            return null;
        }

        if (!string.Equals(m.Screen, Screen, StringComparison.Ordinal))
        {
            note = $"measurement from {m.Screen ?? "an unknown screen"} ignored on {Screen}";
            return null;
        }

        note = $"measured {m.MaxColumns} columns";
        return m;
    }

    /// <summary>
    /// The width a player with this record gets, or null when the record says too little
    /// and the player keeps the server-wide width. The config's <c>columns</c> and
    /// <c>ignoreMeasurement</c> apply to every player; the ceiling is each player's own.
    /// </summary>
    public static ColumnChoice.Choice? ChoiceFor(PlayerRecord? record, StashSettings settings, out string reason)
    {
        if (record is null || !record.TryScreen(out var width, out var height))
        {
            reason = "no screen reported yet, so the server-wide width";
            return null;
        }

        var measurement = record.UsableMeasurement(out var note);

        var choice = ColumnChoice.For(
            settings.Columns, measurement, width, height, settings.IgnoreMeasurement, record.Client);

        reason = $"{choice.Reason} ({record.Screen}; {note})";

        return choice;
    }

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Where a profile's record goes.</summary>
    public static string PathFor(string folder, string profileId)
        => System.IO.Path.Combine(folder, profileId + ".json");

    /// <summary>
    /// The record for a profile, or null when there is none or it cannot be read. An
    /// unreadable record is treated as absent: the player keeps the server-wide width
    /// until their game reports in again and rewrites it.
    /// </summary>
    public static PlayerRecord? Read(string folder, string profileId)
    {
        try
        {
            var path = PathFor(folder, profileId);

            if (!File.Exists(path)) return null;

            return JsonSerializer.Deserialize<PlayerRecord>(File.ReadAllText(path), Options);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Write a profile's record. Throws; the caller logs.</summary>
    public static void Write(string folder, string profileId, PlayerRecord record)
    {
        Directory.CreateDirectory(folder);

        var path = PathFor(folder, profileId);
        var temp = path + ".tmp";

        File.WriteAllText(temp, JsonSerializer.Serialize(record, Options));
        File.Move(temp, path, overwrite: true);
    }
}
