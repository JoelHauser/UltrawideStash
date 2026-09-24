using UltrawideStash.Server;

namespace UltrawideStash.Server.Tests;

/// <summary>
/// Where profile backups go and how many are kept: out of the profiles folder, the
/// original plus the newest two.
///
/// Up to 1.0.2 every relocation left a full copy of the profile beside it and nothing
/// was ever removed -- 11 of them, 5.5 MB, on the live install within a day.
/// </summary>
public class BackupRetentionTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("uws-bak-").FullName;

    private string Profiles => Path.Combine(_root, "profiles");
    private string Store => Path.Combine(_root, "appdata");

    public BackupRetentionTests() => Directory.CreateDirectory(Profiles);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
        GC.SuppressFinalize(this);
    }

    private string Profile(string content = "v0")
    {
        var path = Path.Combine(Profiles, "p.json");
        File.WriteAllText(path, content);
        return path;
    }

    private static DateTime At(int minute) => new(2026, 9, 23, 12, minute, 0, DateTimeKind.Utc);

    private static string[] Baks(string dir) => !Directory.Exists(dir)
        ? []
        : Directory.GetFiles(dir, "*.bak")
            .Select(f => Path.GetFileName(f)!)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

    /// <summary>What 1.0.2 and earlier left beside the profile.</summary>
    private void Legacy(string path, params int[] minutes)
    {
        foreach (var m in minutes)
        {
            File.WriteAllText($"{path}.ultrawidestash-{At(m):yyyyMMdd-HHmmss}.bak", $"legacy {m}");
        }
    }

    [Fact]
    public void BackupsGoToTheStoreNotTheProfilesFolder()
    {
        var path = Profile("pristine");

        BackupStore.Backup(path, Store, At(0));

        Assert.Empty(Baks(Profiles));
        Assert.Equal(["p.json.ultrawidestash-original.bak"], Baks(Store));
        Assert.Equal("pristine", File.ReadAllText(BackupStore.OriginalPath(path, Store)));
    }

    /// <summary>A folder named for a hash is no help without saying whose it is.</summary>
    [Fact]
    public void TheStoreSaysWhichProfilesItHolds()
    {
        var path = Profile();

        BackupStore.Backup(path, Store, At(0));

        Assert.Contains(Profiles, File.ReadAllText(Path.Combine(Store, "where-from.txt")));
    }

    [Fact]
    public void ManyBackupsLeaveTheOriginalAndTheNewestTwo()
    {
        var path = Profile("pristine");

        for (var i = 0; i < 10; i++)
        {
            File.WriteAllText(path, $"v{i}");
            BackupStore.Backup(path, Store, At(i));
        }

        Assert.Equal(
        [
            "p.json.ultrawidestash-20260923-120800.bak",
            "p.json.ultrawidestash-20260923-120900.bak",
            "p.json.ultrawidestash-original.bak",
        ], Baks(Store));

        // The original is the state before the first change, never a later one.
        Assert.Equal("v0", File.ReadAllText(BackupStore.OriginalPath(path, Store)));
        Assert.Equal("v9", File.ReadAllText(Path.Combine(Store, "p.json.ultrawidestash-20260923-120900.bak")));
    }

    /// <summary>
    /// Updating is enough: the first start moves what older versions left beside the
    /// profile, the oldest becomes the original, and only the newest two others survive.
    /// </summary>
    [Fact]
    public void LegacyBackupsLeaveTheProfilesFolder_TheOldestBecomingTheOriginal()
    {
        var path = Profile();
        Legacy(path, 1, 2, 3, 4, 5, 6, 7);

        var tidied = BackupStore.Tidy(path, Store);

        Assert.Equal(7, tidied.Moved);
        Assert.Equal(4, tidied.Deleted);
        Assert.Empty(Baks(Profiles));
        Assert.Equal(
        [
            "p.json.ultrawidestash-20260923-120600.bak",
            "p.json.ultrawidestash-20260923-120700.bak",
            "p.json.ultrawidestash-original.bak",
        ], Baks(Store));

        Assert.Equal("legacy 1", File.ReadAllText(BackupStore.OriginalPath(path, Store)));
    }

    /// <summary>An original an earlier build wrote beside the profile moves as it is.</summary>
    [Fact]
    public void ALegacyOriginalMovesAndStaysTheOriginal()
    {
        var path = Profile();
        File.WriteAllText($"{path}.ultrawidestash-original.bak", "the first");
        Legacy(path, 5, 6, 7);

        BackupStore.Tidy(path, Store);

        Assert.Empty(Baks(Profiles));
        Assert.Equal("the first", File.ReadAllText(BackupStore.OriginalPath(path, Store)));
        Assert.Equal(3, Baks(Store).Length);
    }

    [Fact]
    public void AnExistingOriginalIsNeverReplaced()
    {
        var path = Profile();
        Directory.CreateDirectory(Store);
        File.WriteAllText(BackupStore.OriginalPath(path, Store), "the first");
        Legacy(path, 1, 2, 3);

        BackupStore.Tidy(path, Store);
        BackupStore.Backup(path, Store, At(9));

        Assert.Equal("the first", File.ReadAllText(BackupStore.OriginalPath(path, Store)));
        Assert.Equal(3, Baks(Store).Length);
    }

    /// <summary>repair-stash.ps1's copies are the player's own act; not ours to move.</summary>
    [Fact]
    public void RepairScriptBackupsAndOtherProfilesAreLeftAlone()
    {
        var path = Profile();
        Legacy(path, 1, 2, 3, 4);

        var repair = Path.Combine(Profiles, "p.json.ultrawidestash-repair-20260901-000000.bak");
        var other = Path.Combine(Profiles, "q.json.ultrawidestash-20260901-000000.bak");
        File.WriteAllText(repair, "repair");
        File.WriteAllText(other, "other");

        BackupStore.Tidy(path, Store);

        Assert.True(File.Exists(repair));
        Assert.True(File.Exists(other));
    }

    /// <summary>
    /// A deleted profile's backups: nothing would ever look for them by profile, so they
    /// would sit in the profiles folder for good.
    /// </summary>
    [Fact]
    public void ADeletedProfilesBackupsLeaveTooAndArePruned()
    {
        var live = Profile();
        Legacy(live, 1);

        var gone = Path.Combine(Profiles, "gone.json");
        Legacy(gone, 1, 2, 3, 4, 5);

        var tidied = BackupStore.TidyAll(Profiles, Store);

        Assert.Empty(Baks(Profiles));
        Assert.Equal(6, tidied.Moved);
        Assert.Equal(2, tidied.Deleted);
        Assert.Equal(
        [
            "gone.json.ultrawidestash-20260923-120400.bak",
            "gone.json.ultrawidestash-20260923-120500.bak",
            "gone.json.ultrawidestash-original.bak",
            "p.json.ultrawidestash-original.bak",
        ], Baks(Store));
    }

    /// <summary>The profiles themselves are never moved, renamed or deleted.</summary>
    [Fact]
    public void ProfilesAreNeverTouched()
    {
        var path = Profile("the profile");
        Legacy(path, 1, 2, 3, 4);

        BackupStore.TidyAll(Profiles, Store);

        Assert.Equal("the profile", File.ReadAllText(path));
        Assert.Equal(["p.json"], Directory.GetFiles(Profiles).Select(f => Path.GetFileName(f)!).ToArray());
    }

    [Fact]
    public void NothingToTidyCreatesNothing()
    {
        var path = Profile();

        Assert.Equal(new BackupStore.Tidied(0, 0), BackupStore.Tidy(path, Store));
        Assert.False(Directory.Exists(Store));
    }

    /// <summary>With no AppData to be had, backups stay beside the profiles, still pruned.</summary>
    [Fact]
    public void WithoutAppDataTheProfilesFolderIsTheStore()
    {
        Assert.Equal(Profiles, BackupStore.DirectoryFor(Profiles, appData: ""));

        var path = Profile();
        Legacy(path, 1, 2, 3, 4, 5);

        var tidied = BackupStore.Tidy(path, Profiles);

        Assert.Equal(0, tidied.Moved);
        Assert.Equal(2, tidied.Deleted);
        Assert.Equal(3, Baks(Profiles).Length);
    }

    /// <summary>
    /// Copied installs share profile ids, so each install needs its own folder -- or a
    /// copy's first backup would find the other install's original and never take one.
    /// </summary>
    [Fact]
    public void EachInstallGetsItsOwnReadableFolder()
    {
        var a = Path.Combine(_root, "SPT4.1.X", "SPT_Runtime", "user", "profiles");
        var b = Path.Combine(_root, "SPT4.1.X-copy", "SPT_Runtime", "user", "profiles");

        var keyA = BackupStore.InstallKey(a);

        Assert.StartsWith("SPT4.1.X-", keyA);
        Assert.NotEqual(keyA, BackupStore.InstallKey(b));
        Assert.Equal(keyA, BackupStore.InstallKey(a + Path.DirectorySeparatorChar));
        Assert.Equal(
            Path.Combine(Store, "UltrawideStash", "backups", keyA),
            BackupStore.DirectoryFor(a, appData: Store));
    }
}
