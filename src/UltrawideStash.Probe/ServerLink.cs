using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using HarmonyLib;

namespace UltrawideStash.Probe
{
    /// <summary>
    /// Tells the server this player's own screen, so the stash width is per player.
    ///
    /// ## Why
    ///
    /// Up to 1.1.2 the measurement went into a file in the server mod's folder on this
    /// PC. Under Fika the server is on the host's PC, so a friend's probe wrote to a file
    /// nobody read, and every player got the width for the host's screen (Forge issue #4).
    /// The server now keeps one record per player, filled from two requests sent to
    /// whichever server this game is connected to:
    ///
    /// <list type="bullet">
    ///   <item><c>/ultrawidestash/hello</c>, from the plugin's Awake -- before the game
    ///   has asked for its items or its profile -- with the screen and the two layout
    ///   settings. The server replies with the width this player's stash is now.</item>
    ///   <item><c>/ultrawidestash/measured</c>, when the probe measures the stash panel,
    ///   used from the next game start.</item>
    /// </list>
    ///
    /// ## How
    ///
    /// SPT's own <c>SPT.Common.Http.RequestHandler</c>, found by name like every game type
    /// here (see <see cref="GameTypes"/>), so the session cookie and the server address
    /// are the ones the game itself uses. Nothing here throws: a server without these
    /// routes (an older Ultrawide Stash) or no SPT at all costs one log line, and the
    /// server-wide width applies as before.
    /// </summary>
    internal static class ServerLink
    {
        private static MethodInfo _post;

        private static MethodInfo _postAsync;

        private static bool _resolved;

        /// <summary>Report this game's screen and settings. Blocks for one request.</summary>
        internal static string Hello(int screenWidth, int screenHeight, bool widen, float reserve)
        {
            var json = new StringBuilder()
                .Append("{\"screen\":\"").Append(Screen(screenWidth, screenHeight)).Append("\",")
                .Append("\"widenStashPanel\":").Append(widen ? "true" : "false").Append(',')
                .Append("\"gearPanelReserve\":").Append(reserve.ToString("0.##", CultureInfo.InvariantCulture)).Append(',')
                .Append("\"probeVersion\":\"").Append(Escape(ProbePlugin.PluginVersion)).Append("\"}")
                .ToString();

            try
            {
                if (!Resolve()) return "SPT's RequestHandler not found, so the server-wide width applies";

                var body = _post.Invoke(null, new object[] { "/ultrawidestash/hello", json }) as string;

                // This player's stash sizes, applied to the game's own templates once it
                // has loaded them -- see ClientTemplates. A server before 1.1.4 sends none.
                ClientTemplates.Want(ShapesOf(body));

                return "server: " + (MessageOf(body) ?? "no answer (is the server half 1.1.3 or later?)");
            }
            catch (Exception e)
            {
                var inner = e is TargetInvocationException && e.InnerException != null ? e.InnerException : e;

                return "could not reach the server (" + inner.Message + "), so the server-wide width applies";
            }
        }

        /// <summary>Send a measurement, without waiting; the answer goes to <paramref name="log"/>.</summary>
        internal static void Measured(string json, Action<string> log)
        {
            try
            {
                if (!Resolve()) return;

                var task = _postAsync.Invoke(null, new object[] { "/ultrawidestash/measured", json }) as Task<string>;

                if (task == null) return;

                task.ContinueWith(t =>
                {
                    log(t.IsFaulted
                        ? "could not send the measurement to the server (" + t.Exception?.GetBaseException().Message + ")"
                        : "server: " + (MessageOf(t.Result) ?? "no answer (is the server half 1.1.3 or later?)"));
                });
            }
            catch (Exception e)
            {
                log("could not send the measurement to the server (" + e.Message + ")");
            }
        }

        /// <summary><c>WxH</c>, the form the measurement uses too.</summary>
        internal static string Screen(int width, int height)
            => width.ToString(CultureInfo.InvariantCulture) + "x" + height.ToString(CultureInfo.InvariantCulture);

        internal static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;

            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private static bool Resolve()
        {
            if (_resolved) return _post != null && _postAsync != null;

            _resolved = true;

            var handler = AccessTools.TypeByName("SPT.Common.Http.RequestHandler");

            if (handler == null) return false;

            _post = AccessTools.Method(handler, "PostJson", new[] { typeof(string), typeof(string) });
            _postAsync = AccessTools.Method(handler, "PostJsonAsync", new[] { typeof(string), typeof(string) });

            return _post != null && _postAsync != null;
        }

        /// <summary>
        /// The <c>shapes</c> of the hello reply -- <c>{"&lt;template id&gt;": "WxH", ...}</c> --
        /// as columns and rows by template id; empty when there are none. Read by hand like
        /// <see cref="MessageOf"/>: every 24-hex key with a <c>WxH</c> value after the key.
        /// </summary>
        internal static Dictionary<string, KeyValuePair<int, int>> ShapesOf(string body)
        {
            var shapes = new Dictionary<string, KeyValuePair<int, int>>(StringComparer.OrdinalIgnoreCase);

            if (string.IsNullOrEmpty(body)) return shapes;

            var key = body.IndexOf("\"shapes\"", StringComparison.OrdinalIgnoreCase);

            if (key < 0) return shapes;

            var open = body.IndexOf('{', key);
            var close = open < 0 ? -1 : body.IndexOf('}', open);

            if (open < 0 || close < 0) return shapes;

            foreach (Match m in Regex.Matches(
                         body.Substring(open, close - open),
                         "\"([0-9a-fA-F]{24})\"\\s*:\\s*\"(\\d+)x(\\d+)\""))
            {
                int columns, rows;

                if (int.TryParse(m.Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out columns)
                    && int.TryParse(m.Groups[3].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out rows)
                    && columns > 0 && rows > 0)
                {
                    shapes[m.Groups[1].Value] = new KeyValuePair<int, int>(columns, rows);
                }
            }

            return shapes;
        }

        /// <summary>
        /// The <c>message</c> of the server's one-field reply, or null. Read by hand, since
        /// the plugin has no JSON reference: the first string after the key, unescaped.
        /// </summary>
        internal static string MessageOf(string body)
        {
            if (string.IsNullOrEmpty(body)) return null;

            var key = body.IndexOf("\"message\"", StringComparison.OrdinalIgnoreCase);

            if (key < 0) return null;

            var open = body.IndexOf('"', body.IndexOf(':', key) + 1);

            if (open < 0) return null;

            var sb = new StringBuilder();

            for (var i = open + 1; i < body.Length; i++)
            {
                var c = body[i];

                if (c == '"') return sb.ToString();

                if (c == '\\' && i + 1 < body.Length)
                {
                    var n = body[++i];

                    switch (n)
                    {
                        case 'n': sb.Append(' '); break;
                        case 't': sb.Append(' '); break;
                        case 'u' when i + 4 < body.Length
                                      && int.TryParse(body.Substring(i + 1, 4), NumberStyles.HexNumber,
                                          CultureInfo.InvariantCulture, out var code):
                            sb.Append((char)code);
                            i += 4;
                            break;
                        default: sb.Append(n); break;
                    }

                    continue;
                }

                sb.Append(c);
            }

            return null;
        }
    }
}
