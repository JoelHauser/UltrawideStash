using System.Text.Json;
using System.Text.Json.Serialization;

namespace UltrawideStash.Server;

/// <summary>
/// What the probe measured on the live stash screen, read back on the server.
///
/// ## The handshake
///
/// The server owns the width and the client owns the room to draw it, so one has to
/// tell the other. The probe already computes the answer -- <c>StashMeasure</c> walks
/// from the stash's GridView up to the Canvas and reports the widest ancestor that
/// stretches -- it simply had nowhere to put it. It now writes this file into the
/// server mod's own folder, and <see cref="StashWidener"/> reads it at startup.
///
/// This is the same discipline as <c>ScreenFit</c> in the sibling LoadingRaid repo,
/// including its most important property: **a measurement only affects the next
/// start.** The probe writes it while the player is in the menu; the server reads it
/// the next time it loads. Nothing tries to change a grid that is already on screen.
///
/// ## Failing safe
///
/// Every path that cannot produce a trustworthy number returns null, and
/// <see cref="StashWidener"/> then falls back to <see cref="StashFit.ConservativeColumns"/>
/// from the declared screen size. A missing file is the ordinary first-run case and
/// is not an error. A corrupt one is reported once and otherwise treated as missing,
/// because refusing to load over an unreadable diagnostic would be worse than the
/// diagnostic being absent.
/// </summary>
public sealed class Measurement
{
    /// <summary>The file the probe writes and the server reads.</summary>
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

    /// <summary>The probe version that wrote it.</summary>
    [JsonPropertyName("probeVersion")]
    public string? ProbeVersion { get; set; }

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
                note = "no measurement yet -- start the game with the probe installed and "
                       + "open your stash once";
                return null;
            }

            var loaded = JsonSerializer.Deserialize<Measurement>(
                File.ReadAllText(path), ReadOptions);

            if (loaded is null)
            {
                note = $"{FileName} is empty";
                return null;
            }

            // A zero or negative column count means the probe could not find a
            // stretching ancestor. That is a real answer -- "the panel does not grow"
            // -- but it is not a width we can act on, so treat it as absent and let
            // the conservative estimate stand.
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
