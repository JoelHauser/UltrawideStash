using System;
using System.Collections.Generic;

namespace UltrawideStash.Probe
{
    /// <summary>
    /// Which of the six screens that draw a <c>SimpleStashPanel</c> may be
    /// rearranged, and which one of them owns the measurement handed to the server.
    ///
    /// ## Why this exists at all
    ///
    /// The stash is one item and its width lives in the item template, so it is
    /// <c>columns</c> wide *everywhere it is drawn*. The panel fix is not: it is a
    /// change to two RectTransforms on one screen. Until now the postfix ran on all
    /// six screens and only the character screen was rearranged, and what stopped it
    /// touching the other five was <see cref="StashWiden"/> needing a stretching
    /// sibling called <c>LeftSide</c> -- **a guard by accident, not by design**. It
    /// held because the name is specific, but a screen that happened to have a
    /// <c>LeftSide</c> would have been rearranged without anyone deciding it should
    /// be.
    ///
    /// This is that decision, written down. A screen is rearranged because it is on
    /// the list, not because of what its children happen to be called.
    ///
    /// ## The list
    ///
    /// Read off the patched assembly by walking <c>MainModule.GetTypes()</c> for every
    /// <c>FieldDefinition</c> whose type is <c>SimpleStashPanel</c>:
    ///
    /// <list type="bullet">
    ///   <item><c>EFT.UI.ItemsPanel</c> -- the character screen. Widened since 0.7.0
    ///     and verified in game at 3440x1440.</item>
    ///   <item><c>EFT.UI.TraderDealScreen</c> -- widened as of this change.</item>
    ///   <item><c>EFT.UI.TransferItemsScreen</c></item>
    ///   <item><c>EFT.UI.ScavengerInventoryScreen</c></item>
    ///   <item><c>UI.Hideout.BaseHideoutAreaTransferItemsScreen&lt;,&gt;</c></item>
    ///   <item><c>EFT.UI.PrestigeTransferItemsState</c></item>
    /// </list>
    ///
    /// The last four are recognised and deliberately left alone: nobody has looked at
    /// what they do with a wide grid, and rearranging a screen sight unseen is worse
    /// than leaving a scrollbar on it. They are on the list so the log can say
    /// "recognised, left alone" rather than "unknown", which are different problems.
    /// </summary>
    internal struct ScreenPolicy
    {
        /// <summary>A short name for the log. Never empty.</summary>
        internal readonly string Label;

        /// <summary>
        /// True when this screen was identified as one of the six. False means the
        /// walk found nothing it knew, which is a reason to leave the screen alone
        /// and say so with enough detail to add it to the table later.
        /// </summary>
        internal readonly bool Recognised;

        /// <summary>True when the panel on this screen may be widened.</summary>
        internal readonly bool Widen;

        /// <summary>
        /// True for the one screen whose measurement is written to
        /// <c>ultrawidestash.measured.json</c> for the server to read.
        ///
        /// Exactly one screen may own it. The server sizes the grid from a single
        /// number, so a second screen writing a smaller count would narrow the stash
        /// everywhere -- including on the character screen, which had room for it.
        /// The character screen owns it because it is the screen the stash is for and
        /// the one no player can avoid.
        /// </summary>
        internal readonly bool OwnsMeasurement;

        internal ScreenPolicy(
            string label,
            bool recognised,
            bool widen,
            bool ownsMeasurement)
        {
            Label = string.IsNullOrEmpty(label) ? "unknown screen" : label;
            Recognised = recognised;
            Widen = widen;
            OwnsMeasurement = ownsMeasurement;
        }

        /// <summary>
        /// A key for the per-screen bookkeeping in <see cref="StashMeasure"/>: one
        /// report, one chrome reading and one settle counter per screen per
        /// resolution, rather than one of each for the whole session.
        /// </summary>
        internal string KeyFor(int screenWidth, int screenHeight)
        {
            return Label + "|" + screenWidth + "x" + screenHeight;
        }
    }

    internal static class StashScreens
    {
        /// <summary>
        /// <c>EFT.UI.ItemsPanel</c>, the character screen's right-hand half. This is
        /// the type that carries the <c>SimpleStashPanel</c> field, not
        /// <c>InventoryScreen</c> around it, so it is the nearer of the two to the
        /// panel and the one the walk finds first.
        /// </summary>
        internal const string CharacterScreenType = "ItemsPanel";

        /// <summary><c>EFT.UI.TraderDealScreen</c>.</summary>
        internal const string TraderScreenType = "TraderDealScreen";

        /// <summary>
        /// Hideout transfer screens are generic
        /// (<c>BaseHideoutAreaTransferItemsScreen&lt;,&gt;</c>) and the component on
        /// the object is some closed derived type, so this is matched as a prefix
        /// rather than by exact name.
        /// </summary>
        private const string HideoutTransferPrefix = "BaseHideoutAreaTransferItemsScreen";

        /// <summary>
        /// Whether the trader screen is in scope. Set from the plugin's config before
        /// the first stash opens.
        ///
        /// Switchable separately from the character screen because it is the new one
        /// and the unverified one. The character screen's widening has been run on a
        /// live 3440x1440 install; the trader screen's has been run nowhere, and its
        /// layout was worked out from the live hierarchy at runtime rather than read
        /// off a prefab anybody has seen. If it rearranges a trader badly, this is the
        /// switch that puts it back without giving up the stash.
        /// </summary>
        internal static bool WidenTraderScreen = true;

        private static readonly Dictionary<string, ScreenPolicy> Table =
            new Dictionary<string, ScreenPolicy>(StringComparer.Ordinal)
            {
                // Widened, and the only screen that writes the measurement.
                { CharacterScreenType, new ScreenPolicy("character screen", true, true, true) },

                // Registered as well as ItemsPanel so that an EFT build which moves
                // the field onto the outer screen still identifies as the character
                // screen rather than falling through to unknown.
                { "InventoryScreen", new ScreenPolicy("character screen", true, true, true) },

                // Widened as of this change. Does not own the measurement: the width
                // it can show is the trader screen's business and must not decide how
                // wide the server makes the grid.
                { TraderScreenType, new ScreenPolicy("trader screen", true, true, false) },

                // Recognised, deliberately untouched. What each of these does with a
                // wide grid has never been looked at.
                { "TransferItemsScreen", new ScreenPolicy("transfer screen", true, false, false) },
                { "ScavengerInventoryScreen", new ScreenPolicy("scav inventory screen", true, false, false) },
                { "PrestigeTransferItemsState", new ScreenPolicy("prestige transfer screen", true, false, false) },
            };

        /// <summary>
        /// The policy for a screen component's type name, or an unrecognised policy.
        ///
        /// <paramref name="typeName"/> is a short type name. Generic types arrive as
        /// <c>Name`2</c> from reflection, so the backtick suffix is trimmed before
        /// matching.
        /// </summary>
        internal static ScreenPolicy For(string typeName)
        {
            if (string.IsNullOrEmpty(typeName)) return Unrecognised();

            var name = typeName;
            var tick = name.IndexOf('`');

            if (tick > 0) name = name.Substring(0, tick);

            ScreenPolicy found;

            if (Table.TryGetValue(name, out found))
            {
                var turnedOff = !WidenTraderScreen
                    && string.Equals(name, TraderScreenType, StringComparison.Ordinal);

                return turnedOff
                    ? new ScreenPolicy(found.Label, true, false, false)
                    : found;
            }

            if (name.StartsWith(HideoutTransferPrefix, StringComparison.Ordinal))
            {
                return new ScreenPolicy("hideout transfer screen", true, false, false);
            }

            return Unrecognised();
        }

        /// <summary>
        /// The character screen's policy, for the one case that does not come from a
        /// type name: see <see cref="ScreenFinder"/> on the <c>LeftSide</c> fallback.
        /// </summary>
        internal static ScreenPolicy Character
        {
            get { return Table[CharacterScreenType]; }
        }

        /// <summary>
        /// A screen the table does not know. Never widened, never writes a
        /// measurement -- the two things that could do damage on a screen nobody has
        /// looked at.
        /// </summary>
        internal static ScreenPolicy Unrecognised()
        {
            return new ScreenPolicy("unknown screen", false, false, false);
        }
    }
}
