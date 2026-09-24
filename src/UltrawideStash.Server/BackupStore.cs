using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace UltrawideStash.Server;

/// <summary>
/// Where profile backups go, and how many are kept.
///
/// ## Out of the profiles folder (1.0.2, re-released)
///
/// Up to 1.0.2 every change left a full copy of the profile beside it in
/// <c>user/profiles</c>, 380-730 KB each, and nothing was ever removed. Now backups live
/// in the user's local AppData -- <c>%LOCALAPPDATA%\UltrawideStash\backups\&lt;install&gt;</c>
/// on Windows, <c>~/.local/share/UltrawideStash/backups/&lt;install&gt;</c> on Linux --
/// and each profile keeps only:
///
/// - <c>&lt;profile&gt;.json.ultrawidestash-original.bak</c>, the profile before this mod
///   first changed it. Never overwritten and never pruned: a fixed name taken once, so
///   no later run can replace the pristine copy.
/// - the <see cref="RecentKept"/> newest timestamped copies.
///
/// ## One folder per install
///
/// People copy whole SPT folders to try a mod list, and a copy carries the same profile
/// ids. Sharing one folder would let the copy's first backup find the original's
/// <c>-original.bak</c> and never take its own. So each install gets a folder keyed by
/// a hash of its profile path, prefixed with the install's folder name for a human, and
/// a <c>where-from.txt</c> naming the profiles it belongs to.
///
/// ## Migration
///
/// <see cref="Tidy"/> moves any backups older versions left beside the profiles into
/// this folder, then prunes. It runs on every start, so updating is enough to clear the
/// profiles folder. <c>repair-stash.ps1</c>'s <c>-repair-</c> copies are the player's
/// own act and are left where they are.
///
/// If there is no local AppData to be had, backups stay beside the profiles, pruned the
/// same way.
/// </summary>
public static class BackupStore
{
    /// <summary>How many timestamped backups are kept per profile, besides the original.</summary>
    public const int RecentKept = 2;

    private const string Marker = ".ultrawidestash-";

    private const string WhereFrom = "where-from.txt";

    /// <summary>What <see cref="Tidy"/> did.</summary>
    public readonly record struct Tidied(int Moved, int Deleted);

    /// <summary>
    /// The backup folder for the install whose profiles are in
    /// <paramref name="profileDirectory"/>. Pure: creates nothing.
    /// </summary>
    public static string DirectoryFor(string profileDirectory, string? appData = null)
    {
        appData ??= Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolderOption.DoNotVerify);

        if (string.IsNullOrWhiteSpace(appData)) return profileDirectory;

        return System.IO.Path.Combine(
            appData, "UltrawideStash", "backups", InstallKey(profileDirectory));
    }

    /// <summary>
    /// A readable, stable folder name for one install: the first folder above the
    /// profiles that is not SPT's own (<c>H:\SPT4.1.X\SPT_Runtime\user\profiles</c> gives
    /// <c>SPT4.1.X</c>), then a hash of the full path to keep two installs of the same
    /// name apart.
    /// </summary>
    public static string InstallKey(string profileDirectory)
    {
        var full = System.IO.Path.GetFullPath(profileDirectory)
            .TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);

        // Windows paths are case-insensitive; the same install must not hash two ways.
        var normalised = OperatingSystem.IsWindows() ? full.ToUpperInvariant() : full;
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalised)))
            .Substring(0, 10)
            .ToLowerInvariant();

        var name = "SPT";

        for (var dir = new DirectoryInfo(full); dir is not null; dir = dir.Parent)
        {
            if (dir.Name is "profiles" or "user" or "SPT_Runtime") continue;

            name = dir.Name;
            break;
        }

        var invalid = System.IO.Path.GetInvalidFileNameChars();
        var safe = new string(name.Select(c => invalid.Contains(c) || c == ':' ? '_' : c).ToArray());

        return $"{(safe.Length == 0 ? "SPT" : safe)}-{hash}";
    }

    /// <summary>The never-overwritten copy of the profile from before the first change.</summary>
    public static string OriginalPath(string profilePath, string backupDirectory) =>
        System.IO.Path.Combine(
            backupDirectory, System.IO.Path.GetFileName(profilePath) + Marker + "original.bak");

    /// <summary>
    /// Copies the profile aside before anything changes it. Throws if the copy fails,
    /// and callers treat that as "change nothing". A profile's very first backup becomes
    /// its original.
    /// </summary>
    public static void Backup(string profilePath, string backupDirectory, DateTime? now = null)
    {
        Ensure(backupDirectory, profilePath);
        Tidy(profilePath, backupDirectory);

        var original = OriginalPath(profilePath, backupDirectory);

        if (!File.Exists(original))
        {
            File.Copy(profilePath, original, overwrite: false);
            return;
        }

        var backup = System.IO.Path.Combine(
            backupDirectory,
            $"{System.IO.Path.GetFileName(profilePath)}{Marker}{now ?? DateTime.UtcNow:yyyyMMdd-HHmmss}.bak");

        File.Copy(profilePath, backup, overwrite: false);

        Tidy(profilePath, backupDirectory);
    }

    /// <summary>
    /// Move one profile's backups out of the profiles folder, then bring them down to
    /// the original plus the <see cref="RecentKept"/> newest.
    ///
    /// With no original yet, the oldest timestamped copy **becomes** the original: it is
    /// the earliest state this mod saw, which is what the original is for.
    ///
    /// Best effort past that rename: a file that cannot be moved or deleted is left for
    /// the next start.
    /// </summary>
    public static Tidied Tidy(string profilePath, string backupDirectory)
    {
        var moved = MoveOutOfProfiles(profilePath, backupDirectory);

        if (!Directory.Exists(backupDirectory)) return new Tidied(moved, 0);

        var prefix = System.IO.Path.GetFileName(profilePath) + Marker;

        // yyyyMMdd-HHmmss sorts as text in time order, oldest first.
        var stamped = Directory.GetFiles(backupDirectory, prefix + "*.bak")
            .Where(f => Timestamped.IsMatch(System.IO.Path.GetFileName(f).Substring(prefix.Length)))
            .OrderBy(f => System.IO.Path.GetFileName(f), StringComparer.Ordinal)
            .ToList();

        if (stamped.Count == 0) return new Tidied(moved, 0);

        var original = OriginalPath(profilePath, backupDirectory);

        if (!File.Exists(original))
        {
            File.Move(stamped[0], original);
            stamped.RemoveAt(0);
        }

        var deleted = 0;

        foreach (var old in stamped.Take(Math.Max(0, stamped.Count - RecentKept)))
        {
            try
            {
                File.Delete(old);
                deleted++;
            }
            catch
            {
                // Left for the next start. Never worth failing over.
            }
        }

        return new Tidied(moved, deleted);
    }

    /// <summary>
    /// <see cref="Tidy"/> for every profile that has backups anywhere -- including a
    /// profile the player has since **deleted**, whose old backups would otherwise sit in
    /// the profiles folder for good, since nothing else would ever look for them.
    /// </summary>
    public static Tidied TidyAll(string profileDirectory, string backupDirectory)
    {
        var names = new HashSet<string>(
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

        if (Directory.Exists(profileDirectory))
        {
            foreach (var f in Directory.GetFiles(profileDirectory, "*.json"))
            {
                names.Add(System.IO.Path.GetFileName(f));
            }
        }

        foreach (var dir in new[] { profileDirectory, backupDirectory })
        {
            if (!Directory.Exists(dir)) continue;

            foreach (var f in Directory.GetFiles(dir, "*.json" + Marker + "*.bak"))
            {
                var name = System.IO.Path.GetFileName(f);
                names.Add(name.Substring(0, name.IndexOf(Marker, StringComparison.Ordinal)));
            }
        }

        var moved = 0;
        var deleted = 0;

        foreach (var name in names)
        {
            var tidied = Tidy(System.IO.Path.Combine(profileDirectory, name), backupDirectory);

            moved += tidied.Moved;
            deleted += tidied.Deleted;
        }

        return new Tidied(moved, deleted);
    }

    /// <summary>
    /// Backups older versions wrote beside the profile, moved into the backup folder.
    /// One already there under the same name wins, and the old one is left alone.
    /// </summary>
    private static int MoveOutOfProfiles(string profilePath, string backupDirectory)
    {
        var profileDirectory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(profilePath));

        if (profileDirectory is null || !Directory.Exists(profileDirectory)) return 0;

        if (SamePath(profileDirectory, backupDirectory)) return 0;

        var prefix = System.IO.Path.GetFileName(profilePath) + Marker;

        var legacy = Directory.GetFiles(profileDirectory, prefix + "*.bak")
            .Where(f =>
            {
                var rest = System.IO.Path.GetFileName(f).Substring(prefix.Length);
                return rest == "original.bak" || Timestamped.IsMatch(rest);
            })
            .ToList();

        if (legacy.Count == 0) return 0;

        Ensure(backupDirectory, profilePath);

        var moved = 0;

        foreach (var file in legacy)
        {
            var target = System.IO.Path.Combine(backupDirectory, System.IO.Path.GetFileName(file));

            if (File.Exists(target)) continue;

            try
            {
                File.Move(file, target);
                moved++;
            }
            catch
            {
                // Left for the next start.
            }
        }

        return moved;
    }

    /// <summary>
    /// Create the folder, and say whose backups are in it -- a folder named for a hash
    /// is no help to someone looking for their stash.
    /// </summary>
    private static void Ensure(string backupDirectory, string profilePath)
    {
        Directory.CreateDirectory(backupDirectory);

        var note = System.IO.Path.Combine(backupDirectory, WhereFrom);

        if (File.Exists(note)) return;

        var profiles = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(profilePath));

        try
        {
            File.WriteAllText(note, string.Join(Environment.NewLine,
            [
                "Ultrawide Stash profile backups",
                "",
                $"For the SPT profiles in: {profiles}",
                "",
                "<profile>.json.ultrawidestash-original.bak is that profile before this mod",
                "first changed it. The timestamped ones are the two most recent changes.",
                "To restore one: stop the server, copy it over the profile of the same name",
                "and remove everything after .json from the copy's name.",
                "",
                "Safe to delete once you have removed the mod and are happy with your stash.",
                "",
            ]));
        }
        catch
        {
            // A missing note is not worth failing a backup over.
        }
    }

    private static bool SamePath(string a, string b) =>
        string.Equals(
            System.IO.Path.GetFullPath(a).TrimEnd('\\', '/'),
            System.IO.Path.GetFullPath(b).TrimEnd('\\', '/'),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static readonly Regex Timestamped =
        new(@"^\d{8}-\d{6}\.bak$", RegexOptions.CultureInvariant);
}
