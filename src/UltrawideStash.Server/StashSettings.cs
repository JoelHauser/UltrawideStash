using System.Text.Json;
using System.Text.Json.Serialization;

namespace UltrawideStash.Server;

/// <summary>
/// What the player wants, read from <c>ultrawidestash.config.json</c> beside the DLL.
///
/// Deliberately small. The one number that matters is <see cref="Columns"/>, and the
/// probe plugin logs the number that fits this monitor so it can be set from evidence
/// rather than taste.
/// </summary>
public sealed class StashSettings
{
    /// <summary>
    /// How many cells across the stash should be. Vanilla is 10.
    ///
    /// 16 is the default because it is a real gain that stays well inside the extra
    /// room an ultrawide has (a 16-wide grid draws at 1009px against the 2580px
    /// canvas of a 3440x1440 screen), and because the stash panel almost certainly
    /// does not get the whole canvas. The probe says what actually fits.
    /// </summary>
    [JsonPropertyName("columns")]
    public int Columns { get; set; } = 16;

    /// <summary>
    /// True to shorten the stash as it widens so total capacity stays at vanilla --
    /// a wider, much shorter stash holding the same amount. False to keep every row,
    /// which simply adds space.
    ///
    /// On by default: the ask was to fill the empty width, not to be given more
    /// stash. Rows are never cut below the deepest row an item is standing on, so
    /// this is safe to turn on against a stash with things in it.
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
