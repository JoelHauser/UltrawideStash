using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;

namespace UltrawideStash.Client
{
    /// <summary>
    /// Hands what the character screen measured to the server half: sent to the server
    /// this game plays on (<see cref="ServerLink"/>), and written into the server mod's
    /// folder on this PC.
    ///
    /// The server owns the stash width and the client owns the room to draw it. The
    /// server predicts that room from the screen; a measurement of the real panel
    /// corrects the prediction where another mod has laid the screen out differently.
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
    /// A plugin that breaks the stash screen is worse than none, and this is a
    /// correction rather than the feature. Every failure is one log line and nothing
    /// else; the server keeps predicting from the screen, as it does when nothing has
    /// been measured yet.
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
            double panelWidth,
            bool canWiden,
            string whyNot)
        {
            var json = Json(
                maxColumns, screenWidth, screenHeight, canvasWidth, panelWidth,
                canWiden, whyNot);

            // To the server this game plays on, for this player alone. The file below is
            // only read by a server on this PC, and only for players whose game has never
            // reported in. See ServerLink.
            ServerLink.Measured(json, ClientPlugin.Note);

            try
            {
                var folder = ServerModFolder();

                if (folder == null)
                {
                    return "measurement sent to the server; no folder on this PC to keep a copy in.";
                }

                var path = Path.Combine(folder, FileName);

                File.WriteAllText(path, json, new UTF8Encoding(false));

                return "measurement sent to the server and kept in " + path
                       + " -- the stash takes it from the next game start.";
            }
            catch (Exception e)
            {
                return "measurement sent to the server, but the copy on this PC could not be "
                       + "written (" + e.Message + ").";
            }
        }

        /// <summary>
        /// Hand-rolled so the plugin needs no serializer reference. Scalar fields, the
        /// strings among them produced by this plugin itself -- but <see cref="Escape"/>
        /// runs on every one, because <c>whyNot</c> quotes a panel name and a config
        /// path, which is exactly the kind of value that grows a quote later.
        ///
        /// <c>InvariantCulture</c> throughout: a machine with a comma decimal separator
        /// would otherwise write JSON the server cannot parse.
        /// </summary>
        private static string Json(
            int maxColumns,
            int screenWidth,
            int screenHeight,
            int canvasWidth,
            double panelWidth,
            bool canWiden,
            string whyNot)
        {
            var sb = new StringBuilder();

            sb.AppendLine("{");
            sb.AppendLine("  \"_comment\": \"Written by Ultrawide Stash in the game. Delete this to "
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
            sb.AppendLine("  \"canWiden\": " + (canWiden ? "true" : "false") + ",");

            // Only when it could not: the server repeats it in its own log, which is
            // the log a player reporting "it stays at 10 columns" is most likely to send.
            if (!canWiden && !string.IsNullOrEmpty(whyNot))
            {
                sb.AppendLine("  \"whyNot\": \"" + Escape(whyNot) + "\",");
            }
            sb.AppendLine("  \"measuredAtUtc\": \"" + Escape(
                DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture))
                + "\",");
            sb.AppendLine("  \"probeVersion\": \"" + Escape(ClientPlugin.PluginVersion) + "\"");
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
