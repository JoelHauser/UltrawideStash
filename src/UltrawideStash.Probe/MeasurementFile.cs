using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;

namespace UltrawideStash.Probe
{
    /// <summary>
    /// Writes what the probe measured to where the server half will read it.
    ///
    /// ## Why this exists
    ///
    /// The server owns the stash width and the client owns the room to draw it. Until
    /// 0.6.0 the probe worked out the right answer and then only logged it, so the
    /// number a player needed was in a file they had to find, read and copy by hand --
    /// and the server's fallback cap was a constant derived from one developer's
    /// ultrawide. Writing it down closes the loop.
    ///
    /// ## Where, and why that path
    ///
    /// A normal install puts the two halves at:
    ///
    /// <code>
    /// &lt;SPT&gt;\BepInEx\plugins\UltrawideStash.Probe.dll
    /// &lt;SPT&gt;\SPT_Runtime\user\mods\UltrawideStash\UltrawideStash.Server.dll
    /// </code>
    ///
    /// So from this assembly, up two directories is the SPT root and the server mod
    /// folder is a known path below it. Note <c>SPT_Runtime\user\mods</c>, not a
    /// root-level <c>user\</c> -- SPT 4.x has no such folder, and getting this wrong
    /// once already cost the sibling repo a release where the server mod was staged
    /// somewhere nothing reads.
    ///
    /// If that folder is not there the server half is not installed, and the file goes
    /// beside the plugin instead so it is at least discoverable.
    ///
    /// ## It never throws
    ///
    /// A probe that breaks the stash screen is worse than no probe, and this is a
    /// convenience rather than a feature. Every failure is one log line and nothing
    /// else; the server falls back to its conservative estimate exactly as it does
    /// when the probe is not installed at all.
    /// </summary>
    internal static class MeasurementFile
    {
        /// <summary>Must match <c>Measurement.FileName</c> on the server half.</summary>
        internal const string FileName = "ultrawidestash.measured.json";

        /// <summary>
        /// Write the measurement. Returns a sentence for the log saying what happened,
        /// which is never null.
        /// </summary>
        internal static string Write(
            int maxColumns,
            int screenWidth,
            int screenHeight,
            int canvasWidth,
            double panelWidth)
        {
            try
            {
                var folder = ServerModFolder();

                if (folder == null)
                {
                    return "could not find the server mod folder, so the measurement was not "
                           + "written. Set \"columns\" by hand, or check the server half is "
                           + "installed under SPT_Runtime\\user\\mods\\UltrawideStash.";
                }

                var path = Path.Combine(folder, FileName);

                File.WriteAllText(path, Json(
                    maxColumns, screenWidth, screenHeight, canvasWidth, panelWidth),
                    new UTF8Encoding(false));

                return "measurement written to " + path
                       + " -- restart the SPT server and \"columns\": \"auto\" will use it.";
            }
            catch (Exception e)
            {
                return "could not write the measurement (" + e.Message
                       + "). Nothing is broken; the server will keep using its own estimate.";
            }
        }

        /// <summary>
        /// Hand-rolled so the plugin needs no serializer reference. Five scalar fields,
        /// two of them strings this code produces itself, so there is nothing here that
        /// needs escaping -- but <see cref="Escape"/> runs anyway, because a path or a
        /// version string is exactly the kind of value that grows a quote later.
        ///
        /// <c>InvariantCulture</c> throughout: a machine with a comma decimal separator
        /// would otherwise write JSON the server cannot parse.
        /// </summary>
        private static string Json(
            int maxColumns,
            int screenWidth,
            int screenHeight,
            int canvasWidth,
            double panelWidth)
        {
            var sb = new StringBuilder();

            sb.AppendLine("{");
            sb.AppendLine("  \"_comment\": \"Written by the Ultrawide Stash probe. Delete this to "
                          + "re-measure, or set \\\"columns\\\" in ultrawidestash.config.json to "
                          + "override it.\",");
            sb.AppendLine("  \"maxColumns\": "
                          + maxColumns.ToString(CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("  \"screen\": \"" + Escape(
                screenWidth.ToString(CultureInfo.InvariantCulture) + "x"
                + screenHeight.ToString(CultureInfo.InvariantCulture)) + "\",");
            sb.AppendLine("  \"canvasWidth\": "
                          + canvasWidth.ToString(CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("  \"panelWidth\": "
                          + panelWidth.ToString("0.##", CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("  \"measuredAtUtc\": \"" + Escape(
                DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture))
                + "\",");
            sb.AppendLine("  \"probeVersion\": \"" + Escape(ProbePlugin.PluginVersion) + "\"");
            sb.Append("}");

            return sb.ToString();
        }

        private static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;

            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        /// <summary>
        /// The server mod's folder, or null if it cannot be found.
        ///
        /// Falls back to the plugin's own folder, which is not where the server reads
        /// from but does put the number somewhere a person can find it.
        /// </summary>
        private static string ServerModFolder()
        {
            var pluginFolder = PluginFolder();

            if (pluginFolder == null) return null;

            // <SPT>\BepInEx\plugins -> <SPT>
            var bepInEx = Directory.GetParent(pluginFolder);
            var root = bepInEx != null ? bepInEx.Parent : null;

            if (root != null)
            {
                var mod = Path.Combine(
                    root.FullName,
                    Path.Combine("SPT_Runtime", Path.Combine("user",
                        Path.Combine("mods", "UltrawideStash"))));

                if (Directory.Exists(mod)) return mod;
            }

            return pluginFolder;
        }

        private static string PluginFolder()
        {
            try
            {
                var location = Assembly.GetExecutingAssembly().Location;

                return string.IsNullOrEmpty(location) ? null : Path.GetDirectoryName(location);
            }
            catch
            {
                return null;
            }
        }
    }
}
