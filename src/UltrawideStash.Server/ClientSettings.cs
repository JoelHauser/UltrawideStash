using System.Globalization;

namespace UltrawideStash.Server;

/// <summary>
/// The probe's own settings, read from its BepInEx config so the server can predict
/// what the probe will do before the game has run.
///
/// ## Why the server reads a client file
///
/// The server's prediction (<see cref="StashFit.WidenedColumns"/>) assumed the gear side
/// keeps the default 620 px per panel. A player who had raised <c>GearPanelReserve</c>
/// -- a 5120x1440 tester had it at about 1000 -- got a 39-wide grid in a panel the probe
/// only widened to 27, so a sideways scrollbar; and when the probe's 27 reached the next
/// server start, the stash was narrowed and repacked. Reading the same number the probe
/// reads makes the first start agree with the probe.
///
/// The file is <c>&lt;SPT&gt;/BepInEx/config/com.mybutthasarash.ultrawidestash.cfg</c>, on
/// the same install as <c>SPT_Runtime/user/mods/UltrawideStash</c>. A dedicated server has
/// none, and gets the defaults -- which is also what the probe uses when it has no file.
/// </summary>
public readonly record struct ClientSettings(bool WidenStashPanel, float GearPanelReserve, bool Found)
{
    /// <summary>The probe's GUID, and so its config file's name.</summary>
    public const string ConfigFileName = "com.mybutthasarash.ultrawidestash.cfg";

    /// <summary>The probe's default, <c>StashWiden.DefaultReservePerPanel</c>.</summary>
    public const float DefaultReserve = 620f;

    /// <summary>What the probe does with no config file.</summary>
    public static readonly ClientSettings Defaults = new(true, DefaultReserve, false);

    /// <summary>True when the reserve is not the default, so the player should hear about it.</summary>
    public bool ReserveChanged => Math.Abs(GearPanelReserve - DefaultReserve) >= 0.5f;

    /// <summary>
    /// The config file for a server mod folder: <c>&lt;SPT&gt;/SPT_Runtime/user/mods/UltrawideStash</c>
    /// up four to <c>&lt;SPT&gt;</c>, then <c>BepInEx/config</c>. Null when there is no such root.
    /// </summary>
    public static string? PathFor(string modFolder)
    {
        var root = Directory.GetParent(modFolder)?.Parent?.Parent?.Parent;

        return root is null
            ? null
            : System.IO.Path.Combine(root.FullName, "BepInEx", "config", ConfigFileName);
    }

    /// <summary>Read the file, or the defaults when it is missing or unreadable.</summary>
    public static ClientSettings Read(string? path, out string note)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            note = "no probe config on this install, so its defaults";
            return Defaults;
        }

        try
        {
            return Parse(File.ReadAllLines(path), out note);
        }
        catch (Exception e)
        {
            note = $"probe config unreadable ({e.Message}), so its defaults";
            return Defaults;
        }
    }

    /// <summary>
    /// Parse BepInEx's ini: <c>[Section]</c> headers, <c>#</c> comments, <c>Key = Value</c>.
    /// Only <c>[Layout]</c> is read. Anything that does not parse keeps the default,
    /// exactly as BepInEx itself would fall back.
    /// </summary>
    public static ClientSettings Parse(IEnumerable<string> lines, out string note)
    {
        var widen = true;
        var reserve = DefaultReserve;
        var section = string.Empty;

        foreach (var raw in lines)
        {
            var line = raw.Trim();

            if (line.Length == 0 || line.StartsWith('#')) continue;

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                section = line[1..^1].Trim();
                continue;
            }

            if (!string.Equals(section, "Layout", StringComparison.OrdinalIgnoreCase)) continue;

            var eq = line.IndexOf('=');

            if (eq <= 0) continue;

            var key = line[..eq].Trim();
            var value = line[(eq + 1)..].Trim();

            if (string.Equals(key, "WidenStashPanel", StringComparison.OrdinalIgnoreCase)
                && bool.TryParse(value, out var w))
            {
                widen = w;
            }
            else if (string.Equals(key, "GearPanelReserve", StringComparison.OrdinalIgnoreCase)
                     && float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var r)
                     && r > 0f)
            {
                reserve = r;
            }
        }

        note = $"probe config: WidenStashPanel {widen}, GearPanelReserve {reserve.ToString("0.##", CultureInfo.InvariantCulture)}";

        return new ClientSettings(widen, reserve, true);
    }
}
