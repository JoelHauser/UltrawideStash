using System;
using System.Collections.Generic;
using System.Text;
using BepInEx.Bootstrap;

namespace UltrawideStash.Probe
{
    /// <summary>
    /// Which other stash-touching plugins are loaded, named in the report.
    ///
    /// ## Why this is worth a file
    ///
    /// A widened stash is only interesting alongside the mods that sort it. When a
    /// report comes back saying sorting misbehaved, the first question is always
    /// "with what else installed, and at what version" -- and the answer belongs in
    /// the same block as the measurement rather than in a follow-up.
    ///
    /// It reads <c>Chainloader.PluginInfos</c>, which is BepInEx's own registry, so
    /// it takes no dependency on any of these mods and does not care whether they are
    /// present. Nothing here is a compatibility shim; it is a census.
    /// </summary>
    internal static class Companions
    {
        /// <summary>
        /// The plugins whose behaviour overlaps a stash width change, by BepInEx GUID.
        ///
        /// Both of the sorting mods were read at 0.2.0 and neither hard-codes a stash
        /// width -- Advanced Stash Sorting takes <c>grid.GridWidth</c> into its layout
        /// planner, and UI Fixes reads <c>grid.Properties.CellsH</c> from the template
        /// in its server half. They are listed because they are the ones worth knowing
        /// the version of, not because they are suspected.
        /// </summary>
        private static readonly KeyValuePair<string, string>[] Watched =
        [
            new("com.tyfon.uifixes", "UI Fixes"),
            new("com.slpf.advstashsorting", "Advanced Stash Sorting"),
            new("com.markosz.stashmanagementhelper", "Stash Management Helper"),
        ];

        /// <summary>
        /// One line naming every watched plugin that is loaded, with its version, plus
        /// a count of everything else. Returns an empty string if BepInEx's registry
        /// cannot be read, which costs the report a line and nothing more.
        /// </summary>
        internal static string Describe()
        {
            try
            {
                var infos = Chainloader.PluginInfos;

                if (infos == null) return string.Empty;

                var found = new List<string>();

                foreach (var watched in Watched)
                {
                    foreach (var entry in infos)
                    {
                        if (entry.Value?.Metadata == null) continue;

                        // GUIDs are compared case-insensitively because a mod may not
                        // spell its own the way its documentation does.
                        if (!string.Equals(entry.Key, watched.Key, StringComparison.OrdinalIgnoreCase)) continue;

                        found.Add($"{watched.Value} {entry.Value.Metadata.Version}");
                        break;
                    }
                }

                var sb = new StringBuilder();

                sb.Append("companion plugins: ");
                sb.Append(found.Count == 0 ? "none of the watched ones" : string.Join(", ", found.ToArray()));
                sb.Append(" (");
                sb.Append(infos.Count);
                sb.Append(" plugins loaded in total)");

                return sb.ToString();
            }
            catch (Exception e)
            {
                return "companion plugins: could not read BepInEx's registry -- " + e.Message;
            }
        }
    }
}
