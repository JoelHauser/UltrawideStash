using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace UltrawideStash.Probe
{
    /// <summary>
    /// Works out which screen a <c>SimpleStashPanel</c> is being drawn on, by walking
    /// up from the panel and asking <see cref="StashScreens"/> about the components it
    /// passes.
    ///
    /// ## Why by name, and why up rather than down
    ///
    /// By name for the same reason as everything else here: the assembly in
    /// <c>Managed</c> is not the one the game runs, so there is no type to compare
    /// against at compile time. See <see cref="GameTypes"/>.
    ///
    /// Upward because each of the six screens *holds* a <c>SimpleStashPanel</c> in a
    /// field, so the panel is somewhere below the screen in the hierarchy and the
    /// nearest screen component above it is the one drawing it. Searching downward
    /// from a root would find whichever screen happened to be first in the scene.
    ///
    /// Base types are walked as well, so a screen that subclasses one of the six is
    /// still recognised. The most derived name wins, which matters if any of them
    /// turn out to inherit from each other -- <c>TraderDealScreen</c> is widened and
    /// <c>TransferItemsScreen</c> is not, so picking the base over the derived type
    /// would silently disable this whole change.
    /// </summary>
    internal static class ScreenFinder
    {
        /// <summary>
        /// The stash panel's neighbour on the character screen. Used only as a
        /// fallback identity check -- see <see cref="Identify"/>.
        /// </summary>
        private const string LeftSideName = "LeftSide";

        /// <summary>How far up to look. The canvas is a handful of levels away.</summary>
        private const int MaxDepth = 24;

        /// <summary>
        /// Stop walking base types here. Everything on a UI object derives from these
        /// eventually and none of them says anything about which screen this is.
        /// </summary>
        private static readonly HashSet<string> Floor =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "MonoBehaviour", "Behaviour", "Component", "Object", "ScriptableObject"
            };

        /// <summary>
        /// Identify the screen holding <paramref name="panel"/>.
        ///
        /// <paramref name="detail"/> comes back with the component names seen on the
        /// way up when nothing was recognised. That is the one thing worth having from
        /// a player's log on a screen this does not know yet: it names the type to add
        /// to <see cref="StashScreens"/>, from a log rather than from another round of
        /// reading the assembly.
        /// </summary>
        internal static ScreenPolicy Identify(MonoBehaviour panel, out string detail)
        {
            detail = string.Empty;

            if (panel == null) return StashScreens.Unrecognised();

            var seen = new List<string>();

            try
            {
                var node = panel.transform;
                var depth = 0;

                while (node != null && depth < MaxDepth)
                {
                    foreach (var component in node.GetComponents(typeof(Component)))
                    {
                        if (component == null) continue;

                        var type = component.GetType();

                        while (type != null && !Floor.Contains(type.Name))
                        {
                            var policy = StashScreens.For(type.Name);

                            if (policy.Recognised) return policy;

                            if (seen.Count < 40) seen.Add(type.Name);

                            type = type.BaseType;
                        }
                    }

                    node = node.parent;
                    depth++;
                }
            }
            catch (Exception e)
            {
                // A screen we cannot identify is a screen we leave alone, which is
                // the same answer a throw should give.
                detail = "the walk threw -- " + e.Message;

                return StashScreens.Unrecognised();
            }

            // The one concession to the old behaviour.
            //
            // Until this change, widening was gated on there being a stretching
            // sibling called LeftSide, and that is the layout the character screen
            // has and the only arrangement ever verified in game. If the table above
            // fails to recognise the character screen -- a rename in a future EFT
            // build, most likely -- falling back on the shape that was actually
            // tested is better than turning the mod off.
            //
            // It cannot leak onto the other five: they are recognised, so the walk
            // returns before ever reaching here.
            if (HasLeftSideNeighbour(panel))
            {
                detail = "not recognised by type, but there is a stretching '"
                    + LeftSideName + "' beside the stash panel, so it is being treated "
                    + "as the character screen -- the 1.0.0 behaviour";

                return StashScreens.Character;
            }

            detail = Describe(seen);

            return StashScreens.Unrecognised();
        }

        /// <summary>
        /// Whether any ancestor of the panel has a stretching sibling called
        /// <c>LeftSide</c>. Deliberately the same test <see cref="StashWiden"/> used
        /// to make for itself, so the fallback reproduces the old gate exactly.
        /// </summary>
        private static bool HasLeftSideNeighbour(MonoBehaviour panel)
        {
            var node = panel.transform as RectTransform;
            var depth = 0;

            while (node != null && depth < MaxDepth)
            {
                var parent = node.parent as RectTransform;

                if (parent != null)
                {
                    for (var i = 0; i < parent.childCount; i++)
                    {
                        var child = parent.GetChild(i) as RectTransform;

                        if (child == null) continue;

                        if (!string.Equals(child.name, LeftSideName, StringComparison.Ordinal))
                        {
                            continue;
                        }

                        if (Mathf.Abs(child.anchorMax.x - child.anchorMin.x) > 0.001f)
                        {
                            return true;
                        }
                    }
                }

                node = parent;
                depth++;
            }

            return false;
        }

        /// <summary>
        /// The component names seen on the way up, trimmed to something a log line can
        /// carry. Duplicates removed, order kept, because order is the hierarchy.
        /// </summary>
        private static string Describe(List<string> seen)
        {
            if (seen.Count == 0) return "nothing above the stash panel carried a component.";

            var unique = new List<string>();
            var already = new HashSet<string>(StringComparer.Ordinal);

            foreach (var name in seen)
            {
                if (already.Add(name)) unique.Add(name);
            }

            var sb = new StringBuilder();

            sb.Append("components seen from the stash panel upward: ");
            sb.Append(string.Join(", ", unique.ToArray()));
            sb.Append(". One of those is the screen -- add it to StashScreens to widen it.");

            return sb.ToString();
        }
    }
}
