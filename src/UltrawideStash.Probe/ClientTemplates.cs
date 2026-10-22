using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using HarmonyLib;

namespace UltrawideStash.Probe
{
    /// <summary>
    /// Sizes the game's own copy of the five stash templates to what the server told this
    /// player, the moment the game has loaded them.
    ///
    /// ## Why (1.1.4)
    ///
    /// The server already sends each player their stash size inside <c>/client/items</c>.
    /// On a Fika host in Forge issue #5 it logged sending a player their own 19-wide stash,
    /// and that player's game measured the host's 10-wide one -- <c>stash grid 10x288</c>,
    /// the host's unmodified template -- so something on that server replaced the items
    /// response after the mod built it. The same path, tested in a real game on a server
    /// without whatever that is, gave the player their own stash. Rather than chase every
    /// mod that might rewrite that response, the sizes also come back in the reply to the
    /// probe's hello (see <see cref="ServerLink"/>) and are applied here.
    ///
    /// ## Where, and why there
    ///
    /// <c>EFT.ItemFactory</c>'s constructor is handed the item templates fully built
    /// (<c>ItemTemplates.Init</c> has run), and the game makes it before it loads the
    /// profile, whose stash is made from those templates: <c>new Stash(id, template)</c>
    /// copies <c>template.Grids[0]</c>'s <c>GridWidth</c> x <c>GridHeight</c> into a new grid.
    /// So setting the template's grid size in a postfix there is early enough, and nothing
    /// that already exists is resized. A later item factory (the game remakes it when it
    /// recreates its backend) gets the same sizes.
    ///
    /// Only the five player stash templates, only to sizes the server sent for this session,
    /// and never in raid -- the stash does not exist there. Everything by name, like
    /// <see cref="GameTypes"/>; anything missing costs one log line and the game keeps what
    /// the items response gave it, exactly as in 1.1.3.
    /// </summary>
    internal static class ClientTemplates
    {
        /// <summary>Template id to (columns, rows), from the server's reply to the hello.</summary>
        private static Dictionary<string, KeyValuePair<int, int>> _wanted =
            new Dictionary<string, KeyValuePair<int, int>>(StringComparer.OrdinalIgnoreCase);

        private static Action<string> _log;

        private static FieldInfo _templatesField;

        /// <summary>The sizes this game should use, from the hello.</summary>
        internal static void Want(Dictionary<string, KeyValuePair<int, int>> shapes)
        {
            _wanted = shapes ?? new Dictionary<string, KeyValuePair<int, int>>(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>Patch the item factory's constructor. Returns a line for the log.</summary>
        internal static string Install(Harmony harmony, Action<string> log)
        {
            _log = log;

            if (_wanted.Count == 0)
            {
                return "the server sent no stash sizes (a server half before 1.1.4), so the game uses the item list as it is";
            }

            try
            {
                var factory = AccessTools.TypeByName("EFT.ItemFactory");

                if (factory == null) return "EFT.ItemFactory not found; the game uses the item list as it is";

                _templatesField = AccessTools.Field(factory, "ItemTemplates");

                if (_templatesField == null) return "ItemFactory.ItemTemplates not found; the game uses the item list as it is";

                var postfix = new HarmonyMethod(AccessTools.Method(typeof(ClientTemplates), nameof(AfterFactory)));
                var patched = 0;

                foreach (var ctor in AccessTools.GetDeclaredConstructors(factory))
                {
                    if (ctor.IsStatic) continue;

                    harmony.Patch(ctor, postfix: postfix);
                    patched++;
                }

                return patched > 0
                    ? "stash sizes from the server will be applied to the game's templates ("
                      + Describe(_wanted) + ")"
                    : "no ItemFactory constructor to patch; the game uses the item list as it is";
            }
            catch (Exception e)
            {
                return "could not patch the item factory (" + e.Message + "); the game uses the item list as it is";
            }
        }

        private static void AfterFactory(object __instance)
        {
            try
            {
                var templates = _templatesField != null ? _templatesField.GetValue(__instance) as IDictionary : null;

                if (templates == null || _wanted.Count == 0) return;

                var changed = new List<string>();

                foreach (DictionaryEntry entry in templates)
                {
                    var id = entry.Key != null ? entry.Key.ToString() : null;
                    KeyValuePair<int, int> shape;

                    if (id == null || !_wanted.TryGetValue(id, out shape) || entry.Value == null) continue;

                    var grid = FirstGrid(entry.Value);

                    if (grid == null) continue;

                    var widthProperty = AccessTools.Property(grid.GetType(), "GridWidth");
                    var heightProperty = AccessTools.Property(grid.GetType(), "GridHeight");

                    if (widthProperty == null || heightProperty == null) continue;

                    var width = (int)widthProperty.GetValue(grid, null);
                    var height = (int)heightProperty.GetValue(grid, null);

                    if (width == shape.Key && height == shape.Value) continue;

                    widthProperty.SetValue(grid, shape.Key, null);
                    heightProperty.SetValue(grid, shape.Value, null);

                    changed.Add(id.Substring(0, 6) + " " + width + "x" + height + " -> " + shape.Key + "x" + shape.Value);
                }

                // The usual case is silence: the items response already said the same. A
                // difference means something replaced that response on the way here.
                if (changed.Count > 0 && _log != null)
                {
                    _log("the game's item list did not have this player's stash sizes, so the server's were applied: "
                         + string.Join(", ", changed.ToArray())
                         + ". Something on the server replaced the item list after Ultrawide Stash built it.");
                }
            }
            catch (Exception e)
            {
                if (_log != null) _log("could not apply the stash sizes to the game's templates (" + e.Message + ")");
            }
        }

        /// <summary>A template's first grid: <c>CompoundItemTemplate.Grids[0]</c>, or null.</summary>
        private static object FirstGrid(object template)
        {
            var field = AccessTools.Field(template.GetType(), "Grids");
            var grids = field != null ? field.GetValue(template) as Array : null;

            return grids != null && grids.Length > 0 ? grids.GetValue(0) : null;
        }

        private static string Describe(Dictionary<string, KeyValuePair<int, int>> shapes)
        {
            var sb = new StringBuilder();

            foreach (var pair in shapes)
            {
                if (sb.Length > 0) sb.Append(", ");

                sb.Append(pair.Value.Key).Append('x').Append(pair.Value.Value);
            }

            return sb.ToString();
        }
    }
}
