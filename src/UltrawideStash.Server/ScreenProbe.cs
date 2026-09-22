using System.Runtime.Versioning;

namespace UltrawideStash.Server;

/// <summary>
/// The resolution the game will actually run at, read without the game running.
///
/// ## Why this exists
///
/// The stash width depends on how wide the canvas is, which depends on the screen.
/// That used to be knowable only by the client measuring itself, which meant the first
/// server start after an install had nothing to work with and produced a vanilla stash.
/// The player had to launch, quit, and restart the server before anything happened --
/// a first run indistinguishable from a broken mod.
///
/// But Unity has already written the answer down. EFT stores its resolution in the
/// per-user registry under the publisher key, as every Unity game does, and the server
/// normally runs on the same machine as the client. Reading it turns the first start
/// from a guess into a calculation.
///
/// ## What this is not
///
/// Not authoritative. It is the resolution Unity last saved, which is the one the game
/// will start at, but a player who changes resolution mid-session leaves it stale until
/// the game writes it again. It is a starting estimate; a probe measurement of the real
/// panel supersedes it.
///
/// Windows-only, by nature: there is no registry on Linux, and a dedicated server has
/// no client resolution to find. Both return null and fall back to the declared size in
/// the config, which is 1920x1080 and yields a vanilla stash -- the safe answer when we
/// genuinely do not know.
/// </summary>
public static class ScreenProbe
{
    /// <summary>
    /// Unity writes its player prefs under HKCU\Software\&lt;company&gt;\&lt;product&gt;,
    /// taken from the project settings rather than the folder the game is installed in.
    /// </summary>
    private const string RegistryPath = @"Software\Battlestate Games\EscapeFromTarkov";

    /// <summary>
    /// Unity mangles pref names as "&lt;name&gt;_h&lt;hash&gt;", so the exact value name
    /// cannot be written out in advance -- it is found by prefix instead. The hash is
    /// stable for a given name, but matching on the prefix survives Unity changing how
    /// it computes one.
    /// </summary>
    private const string WidthPrefix = "Screenmanager Resolution Width_h";

    private const string HeightPrefix = "Screenmanager Resolution Height_h";

    /// <summary>
    /// The screen EFT will open on, or null when it cannot be determined.
    /// </summary>
    /// <param name="note">Where the answer came from, for the log.</param>
    public static (int Width, int Height)? Detect(out string note)
    {
        if (!OperatingSystem.IsWindows())
        {
            note = "not Windows, so no registry to read the screen size from";
            return null;
        }

        return DetectOnWindows(out note);
    }

    [SupportedOSPlatform("windows")]
    private static (int Width, int Height)? DetectOnWindows(out string note)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RegistryPath);

            if (key is null)
            {
                note = "EFT has no registry key yet, so it has not been run on this account";
                return null;
            }

            var width = ReadPrefixed(key, WidthPrefix);
            var height = ReadPrefixed(key, HeightPrefix);

            if (width is null || height is null)
            {
                note = "EFT's registry key holds no saved resolution";
                return null;
            }

            // A resolution below the reference canvas cannot widen anything, and one
            // absurdly large is a misread rather than a monitor.
            if (width < 640 || height < 480 || width > 32000 || height > 32000)
            {
                note = $"ignoring an implausible saved resolution of {width}x{height}";
                return null;
            }

            note = $"screen {width}x{height}, read from EFT's saved settings";
            return (width.Value, height.Value);
        }
        catch (Exception e)
        {
            // Reading this is a convenience, never a requirement. A locked-down registry
            // falls back to the declared size exactly as a dedicated server does.
            note = "could not read EFT's saved resolution (" + e.Message + ")";
            return null;
        }
    }

    [SupportedOSPlatform("windows")]
    private static int? ReadPrefixed(Microsoft.Win32.RegistryKey key, string prefix)
    {
        foreach (var name in key.GetValueNames())
        {
            if (!name.StartsWith(prefix, StringComparison.Ordinal)) continue;

            // Unity writes these as DWORD.
            if (key.GetValue(name) is int value) return value;
        }

        return null;
    }
}
