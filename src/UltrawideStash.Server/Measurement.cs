using System.Text.Json;
using System.Text.Json.Serialization;

namespace UltrawideStash.Server;

/// <summary>
/// What the game measured on the live stash screen, read back on the server.
///
/// ## The handshake
///
/// The server owns the width and the client owns the room to draw it, so one has to
/// tell the other. The server predicts it from the screen (<see cref="StashFit.WidenedColumns"/>);
/// the client measures the real panel (<c>StashMeasure</c>) after widening it, writes
/// this file into the server mod's own folder and sends the same to the server
/// (<see cref="PlayerRecord"/>). The measurement corrects the prediction where another
/// mod has laid the screen out differently.
///
/// This is the same discipline as <c>ScreenFit</c> in the sibling LoadingRaid repo,
/// including its most important property: **a measurement only affects the next
/// start.** The game writes it while the player is in the menu; the server reads it
/// the next time it loads. Nothing tries to change a grid that is already on screen.
///
/// ## Failing safe
///
/// Every path that cannot produce a trustworthy number returns null, and
/// <see cref="StashWidener"/> then falls back to the prediction from the screen
/// (<see cref="StashFit.WidenedColumns"/>). A missing file is the ordinary first-run case and
/// is not an error. A corrupt one is reported once and otherwise treated as missing,
/// because refusing to load over an unreadable diagnostic would be worse than the
/// diagnostic being absent.
/// </summary>
public sealed class Measurement
{
    /// <summary>The file the game writes and the server reads.</summary>
    public const string FileName = "ultrawidestash.measured.json";

    /// <summary>
    /// The widest stash, in columns, that fitted the panel's stretching ancestor when
    /// the player last opened their stash. This is the number that matters.
    /// </summary>
    [JsonPropertyName("maxColumns")]
    public int MaxColumns { get; set; }

    /// <summary>The screen it was measured on, as <c>WxH</c>, for the log line.</summary>
    [JsonPropertyName("screen")]
    public string? Screen { get; set; }

    /// <summary>Logical canvas width, for the log line and for sanity-checking.</summary>
    [JsonPropertyName("canvasWidth")]
    public int CanvasWidth { get; set; }

    /// <summary>
    /// The measured width in canvas pixels of the widest ancestor that stretches with
    /// the canvas -- the span a grid actually has to live in.
    /// </summary>
    [JsonPropertyName("panelWidth")]
    public double PanelWidth { get; set; }

    /// <summary>When it was taken, so a stale measurement is visible in the log.</summary>
    [JsonPropertyName("measuredAtUtc")]
    public string? MeasuredAtUtc { get; set; }

    /// <summary>
    /// The version of the mod's game half that wrote it. Named <c>probeVersion</c> in the
    /// file, from when that half only measured; every version reads and writes that name.
    /// </summary>
    [JsonPropertyName("probeVersion")]
    public string? ClientVersion { get; set; }

    /// <summary>
    /// Whether the character screen had room to widen the stash panel at all. Null in
    /// files written before 1.0.4, which did not say.
    /// </summary>
    [JsonPropertyName("canWiden")]
    public bool? CanWiden { get; set; }

    /// <summary>The game's reason when <see cref="CanWiden"/> is false.</summary>
    [JsonPropertyName("whyNot")]
    public string? WhyNot { get; set; }

    /// <summary>
    /// The first version whose measurement can be trusted on an install without
    /// UIScale.Reloaded.
    ///
    /// Up to 1.0.4 the game half could only widen a screen UIScale.Reloaded had already
    /// stretched. On EFT's own inventory screen -- a fixed 1920 px frame -- it found no
    /// room and wrote 10 columns, and the server shrank the stash to match, moving
    /// items (Forge issues #1 and #2). 1.0.5 stretches that frame itself. A measurement
    /// from before it says nothing true about the panel 1.0.5 will build.
    /// </summary>
    public static readonly Version FirstTrustedClient = new(1, 0, 5);

    /// <summary>
    /// True when this was written by a version older than <see cref="FirstTrustedClient"/>,
    /// or one that did not say which it was.
    /// </summary>
    public bool PredatesTheStretch()
    {
        if (string.IsNullOrWhiteSpace(ClientVersion)) return true;

        // "1.0.5", and anything a build stamp might add after it.
        var core = ClientVersion.Trim().Split('+', '-', ' ')[0];

        return !Version.TryParse(core, out var version) || version < FirstTrustedClient;
    }

    /// <summary>
    /// A sentence for the server log when the game could not widen its stash panel,
    /// or null when it could (or the file is too old to say).
    ///
    /// ## Why the server repeats what the client found
    ///
    /// A 10-column measurement is the one result that looks exactly like a broken mod,
    /// and the reason for it was only ever in the BepInEx log. The first report of it
    /// (Forge issue #1, a 5120x1440 monitor) came with neither log. The server log is
    /// the one people look at and send, so the reason goes there too -- with the
    /// likeliest cause spelled out when the canvas is 16:9: EFT's menus are 1920 wide
    /// at any 16:9 resolution, so a wide monitor running EFT at a 16:9 resolution
    /// gains nothing.
    /// </summary>
    public string? Diagnosis()
    {
        if (CanWiden != false) return null;

        var why = string.IsNullOrWhiteSpace(WhyNot)
            ? "the game did not record why"
            : WhyNot.Trim().TrimEnd('.');

        var where = CanvasWidth > 0
            ? $"{Screen ?? "an unknown screen"}, where EFT's menus are {CanvasWidth} px wide"
            : Screen ?? "an unknown screen";

        var text = $"The game could not widen its stash panel when it last measured it "
                   + $"({where}): {why}. The stash stays {MaxColumns} columns wide.";

        if (CanvasWidth > 0 && CanvasWidth <= StashFit.ReferenceWidth)
        {
            text += " That is a 16:9 (or narrower) layout, which has no spare width at any "
                    + "resolution. If your monitor is wider than 16:9, set EFT's resolution "
                    + "in Settings > Graphics to the monitor's own, then restart the game and "
                    + "this server.";
        }

        return text;
    }

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>
    /// Read the measurement, or null when there is not a usable one.
    ///
    /// <paramref name="note"/> always says what happened, in a form fit for the server
    /// log, because "the mod did nothing" needs to come with the reason.
    /// </summary>
    public static Measurement? Read(string path, out string note)
    {
        try
        {
            if (!File.Exists(path))
            {
                note = "nothing measured yet";
                return null;
            }

            var loaded = JsonSerializer.Deserialize<Measurement>(
                File.ReadAllText(path), ReadOptions);

            if (loaded is null)
            {
                note = $"{FileName} is empty";
                return null;
            }

            // A zero or negative column count means the game could not find a
            // stretching ancestor. That is a real answer -- "the panel does not grow"
            // -- but it is not a width we can act on, so treat it as absent and let
            // the prediction stand.
            if (loaded.MaxColumns < StashFit.MinColumns)
            {
                note = $"{FileName} reports {loaded.MaxColumns} columns, which is not usable";
                return null;
            }

            note = $"measured {loaded.MaxColumns} columns on {loaded.Screen ?? "an unknown screen"}";
            return loaded;
        }
        catch (Exception e)
        {
            note = $"{FileName} unreadable ({e.Message})";
            return null;
        }
    }
}
