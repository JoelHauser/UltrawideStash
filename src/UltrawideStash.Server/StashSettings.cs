using System.Text.Json;
using System.Text.Json.Serialization;

namespace UltrawideStash.Server;

/// <summary>
/// Reads <c>columns</c>, which is either the string <c>"auto"</c> or a whole number.
///
/// <c>"auto"</c> is represented as null, meaning "use whatever the screen can show".
/// Anything unrecognised -- a float, a bool, a misspelling -- also reads as auto
/// rather than throwing, because a config typo should cost the player the default
/// behaviour and a log line, not a server that will not start.
/// </summary>
public sealed class ColumnsConverter : JsonConverter<int?>
{
    public override int? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Number:
                return reader.TryGetInt32(out var n) ? n : null;

            case JsonTokenType.String:
                var text = reader.GetString();

                if (string.IsNullOrWhiteSpace(text)) return null;

                // "16" in quotes is a common enough mistake to be worth accepting.
                return int.TryParse(text.Trim(), out var parsed) ? parsed : null;

            default:
                return null;
        }
    }

    public override void Write(Utf8JsonWriter writer, int? value, JsonSerializerOptions options)
    {
        if (value.HasValue) writer.WriteNumberValue(value.Value);
        else writer.WriteStringValue("auto");
    }
}

/// <summary>
/// What the player wants, read from <c>ultrawidestash.config.json</c> beside the DLL.
///
/// The one number that matters is <see cref="Columns"/>, and as of 0.6.0 it defaults
/// to <c>"auto"</c> rather than a fixed 16. The reason is in <see cref="StashFit"/>:
/// a 16:9 screen of any resolution has a 1920-wide canvas and therefore no spare
/// width at all, so a shipped default of 16 was a number that suited one ultrawide
/// and overflowed everyone else's panel.
/// </summary>
public sealed class StashSettings
{
    /// <summary>
    /// How many cells across the stash should be, or null for <c>"auto"</c>.
    ///
    /// Auto uses the probe's measurement when there is one, and a deliberately
    /// pessimistic estimate from <see cref="ScreenWidth"/>/<see cref="ScreenHeight"/>
    /// when there is not -- which on a 16:9 screen is vanilla 10, i.e. it changes
    /// nothing until the panel has actually been measured.
    ///
    /// An explicit number is honoured but still clamped to what the screen can show,
    /// unless <see cref="IgnoreMeasurement"/> is set. A grid wider than the panel does
    /// not shrink its cells to fit -- EFT's canvas is <c>ConstantPixelSize</c> -- it
    /// overflows and gets clipped, which looks exactly like losing items even though
    /// nothing is lost.
    /// </summary>
    [JsonPropertyName("columns")]
    [JsonConverter(typeof(ColumnsConverter))]
    public int? Columns { get; set; }

    /// <summary>
    /// The screen the game will run at, used only to work out a safe column ceiling
    /// before the probe has measured anything. Defaults to 1920x1080, whose ceiling is
    /// vanilla 10 -- so an install with no probe and no edits does nothing at all.
    ///
    /// Worth setting on a dedicated server, where the probe's measurement never
    /// reaches this machine. Resolution does not matter, only aspect ratio: a 4K 16:9
    /// screen has the same canvas width as a 1080p one.
    /// </summary>
    [JsonPropertyName("screenWidth")]
    public int ScreenWidth { get; set; } = StashFit.ReferenceWidth;

    /// <inheritdoc cref="ScreenWidth"/>
    [JsonPropertyName("screenHeight")]
    public int ScreenHeight { get; set; } = StashFit.ReferenceHeight;

    /// <summary>
    /// True to use <see cref="Columns"/> exactly as written, with no clamp to what the
    /// screen can show.
    ///
    /// For someone who knows their panel better than the probe does -- a UI mod that
    /// re-anchors the stash, say. Off by default, because the clamp is the whole
    /// protection against a grid nobody can see.
    /// </summary>
    [JsonPropertyName("ignoreMeasurement")]
    public bool IgnoreMeasurement { get; set; }

    /// <summary>
    /// True to shorten the stash as it widens so total capacity stays at vanilla --
    /// a wider, much shorter stash holding the same amount. False to keep every row,
    /// which simply adds space.
    ///
    /// On by default: the ask was to fill the empty width, not to be given more
    /// stash. Rows are never cut below the deepest row an item is standing on, and
    /// never below what a lower rung of the hideout stash ladder needs -- see
    /// <see cref="StashLayout"/> -- so this is safe against a stash with things in it.
    /// </summary>
    [JsonPropertyName("compensateRows")]
    public bool CompensateRows { get; set; } = true;

    /// <summary>
    /// True to print every stash's before and after at startup. Off leaves a single
    /// summary line.
    /// </summary>
    [JsonPropertyName("verbose")]
    public bool Verbose { get; set; } = false;

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
    };

    /// <summary>
    /// Read the config, writing a default one if there is not one yet. A file that
    /// cannot be parsed is left exactly where it is -- the defaults are used for this
    /// run and the reason is reported -- because silently overwriting a config the
    /// player has been editing is worse than ignoring it once.
    /// </summary>
    public static StashSettings Load(string path, out string note)
    {
        try
        {
            if (!File.Exists(path))
            {
                var fresh = new StashSettings();
                File.WriteAllText(path, JsonSerializer.Serialize(fresh, WriteOptions));
                note = "wrote a default config";
                return fresh;
            }

            var loaded = JsonSerializer.Deserialize<StashSettings>(
                File.ReadAllText(path), ReadOptions);

            if (loaded is null)
            {
                note = "config was empty; using defaults";
                return new StashSettings();
            }

            note = "config loaded";
            return loaded;
        }
        catch (Exception e)
        {
            note = $"config unreadable ({e.Message}); using defaults";
            return new StashSettings();
        }
    }
}
