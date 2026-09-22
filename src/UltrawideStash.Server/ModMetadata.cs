using SPTarkov.Server.Core.Models.Spt.Mod;

namespace UltrawideStash.Server;

/// <summary>
/// The mod's one piece of metadata. SPT's ModLoader runs SingleOrDefault over the
/// types implementing IModMetadata in a folder, so there is exactly one of these.
/// </summary>
public record ModMetadata : IModMetadata
{
    /// <summary>
    /// Shared with the client probe's [BepInPlugin] so the two halves are visibly
    /// one mod, following the prefix the other repos here are registered under.
    /// </summary>
    public string ModGuid { get; init; } = "com.mybutthasarash.ultrawidestash";

    public string Name { get; init; } = "Ultrawide Stash";

    public string Author { get; init; } = "JoelHauser";

    public List<string>? Contributors { get; init; }

    public SemanticVersioning.Version Version { get; init; } = new("0.9.1");

    /// <summary>
    /// A hard gate: a mod outside the range loads nothing and logs nothing, so
    /// silence at startup means this line rather than a bug. "~4.1.5" is
    /// &gt;=4.1.5 &lt;4.2.0.
    /// </summary>
    public SemanticVersioning.Range SptVersion { get; init; } = new("~4.1.5");

    public List<string>? Incompatibilities { get; init; }

    public Dictionary<string, SemanticVersioning.Range>? ModDependencies { get; init; }

    public string? Url { get; init; } = "https://github.com/JoelHauser/UltrawideStash";

    public string License { get; init; } = "MIT";

    public bool HasPrepatcher { get; init; }
}
