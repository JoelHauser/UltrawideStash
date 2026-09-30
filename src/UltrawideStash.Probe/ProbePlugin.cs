using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace UltrawideStash.Probe
{
    /// <summary>
    /// A read-only measuring stick for the stash screen.
    ///
    /// ## What this is and is not
    ///
    /// It changes nothing. It opens no window, moves no RectTransform and patches
    /// exactly one method with a postfix that only reads. Its whole job is to answer,
    /// from the live hierarchy, the one question static analysis of the game assembly
    /// cannot: when the stash grid is made wider than 10 columns, does the panel have
    /// room to draw it, or does an ancestor clip it?
    ///
    /// The width itself comes from the server half, which edits the stash item
    /// template. The two are independent -- this is useful with or without it, and on
    /// a vanilla 10-wide stash it still reports how much room there is.
    ///
    /// ## Why nothing here references Assembly-CSharp
    ///
    /// See <see cref="GameTypes"/>. The short version: the assembly in Managed is not
    /// the one the game runs, so every game member is resolved by its patched name at
    /// runtime and the plugin builds against any install.
    /// </summary>
    [BepInPlugin(PluginGuid, "Ultrawide Stash Probe", PluginVersion)]
    public class ProbePlugin : BaseUnityPlugin
    {
        /// <summary>Shared with the server half so the two read as one mod.</summary>
        public const string PluginGuid = "com.mybutthasarash.ultrawidestash";

        /// <summary>Must match the csproj's Version.</summary>
        public const string PluginVersion = "1.1.0";

        /// <summary>
        /// Every line this plugin writes is prefixed, so one grep finds the whole
        /// report in a log full of other mods.
        /// </summary>
        private const string Tag = "[UltrawideStash] ";

        private static ManualLogSource _log;

        /// <summary>
        /// The panel whose grid we are still waiting on, and how many frames we have
        /// waited. <c>SimpleStashPanel.Show</c> returns before the GridViews exist,
        /// so the postfix cannot measure directly.
        /// </summary>
        private static MonoBehaviour _pending;

        /// <summary>
        /// Which screen <see cref="_pending"/> is being drawn on. Worked out once in
        /// the postfix rather than on every polling frame: the walk is cheap but it is
        /// not free, and the answer cannot change between Show and the grid appearing.
        /// </summary>
        private static ScreenPolicy _pendingScreen;

        /// <summary>
        /// Screens already remarked on, so each says its piece once. Keyed by what
        /// would be said, because an unrecognised screen is identified by its detail
        /// line rather than by its label.
        /// </summary>
        private static readonly HashSet<string> ScreensSaid =
            new HashSet<string>(StringComparer.Ordinal);

        /// <summary>
        /// Whether to take the slack out of the gear side and give it to the stash.
        ///
        /// On by default, which reverses the original decision. Off was defensible in
        /// the abstract -- rearranging someone's inventory screen uninvited is rude --
        /// but the mod is called Ultrawide Stash and nobody installs it hoping it does
        /// nothing. Worse, off is not merely inert: the panel stays vanilla, so the
        /// probe measures a vanilla panel, writes 10 columns, and every later server
        /// start reads that back. The mod then looks broken rather than disabled.
        ///
        /// Setting it false puts the screen back on the next open.
        /// </summary>
        private ConfigEntry<bool> _widen;

        /// <summary>
        /// What each of the two gear panels keeps, in canvas px. See
        /// <see cref="StashWiden.DefaultReservePerPanel"/> for why 620.
        /// </summary>
        private ConfigEntry<float> _reserve;

        /// <summary>
        /// Whether the trader screen gets the same treatment as the character screen.
        ///
        /// On by default, because the stash grid is the width the server made it on
        /// every screen that draws it: leaving the trader's panel at its stock width
        /// means a wide grid overflowing it, and the player's own stash is half of what
        /// a trader screen is for. Off leaves it stock, scrollbar and all.
        /// </summary>
        private ConfigEntry<bool> _widenTrader;

        private void Awake()
        {
            _log = Logger;

            // No apostrophes in these keys. BepInEx writes them into an ini and the
            // parser does not survive one.
            _widen = Config.Bind(
                "Layout",
                "WidenStashPanel",
                true,
                "Narrow the gear side of the inventory screen and give the width to the "
                + "stash panel. This is what the mod is for; set it false to leave the "
                + "screen alone.");

            _reserve = Config.Bind(
                "Layout",
                "GearPanelReserve",
                StashWiden.DefaultReservePerPanel,
                "Canvas px each gear panel keeps when the stash is widened. The game's "
                + "own 16:9 layout gives them about 600. Below 520 the character doll "
                + "starts to clip.");

            _widenTrader = Config.Bind(
                "Layout",
                "WidenTraderScreen",
                true,
                "Widen the stash panel on the trader screen too. The grid is the width the "
                + "server made it everywhere it is drawn, so without this a wide stash "
                + "overflows the trader screen's panel into a horizontal scrollbar. Has no "
                + "effect unless WidenStashPanel is also true.");

            if (_widen.Value) StashMeasure.Widen = _reserve.Value;

            StashScreens.WidenTraderScreen = _widenTrader.Value;

            GameTypes.Resolve();

            if (!GameTypes.Ready)
            {
                Say("probe off -- " + GameTypes.Failure
                    + ". The game's UI names have moved; nothing else is affected.");
                return;
            }

            try
            {
                new Harmony(PluginGuid).Patch(
                    GameTypes.SimpleStashPanelShow,
                    postfix: new HarmonyMethod(
                        AccessTools.Method(typeof(ProbePlugin), nameof(AfterStashShow))));

                Say(_widen.Value
                    ? "armed. Open your stash: it gets one measurement block, and the panel "
                      + "gets widened."
                    : "armed, but WidenStashPanel is false, so the panel will be left alone "
                      + "and measured at its vanilla width. The measurement handed to the "
                      + "server will say 10 columns, and the server will keep reading that "
                      + "back on every start. Set WidenStashPanel to true if you wanted a "
                      + "wider stash.");
            }
            catch (Exception e)
            {
                Say("probe could not patch SimpleStashPanel.Show -- " + e.Message);
            }
        }

        /// <summary>
        /// Runs when the stash panel is shown.
        ///
        /// <c>__instance</c> is typed as <c>MonoBehaviour</c> rather than the panel's
        /// own type deliberately: it is a genuine supertype (SimpleStashPanel ->
        /// UIInputNode -> InputNode -> InputNodeAbstract -> SerializedMonoBehaviour ->
        /// MonoBehaviour), which Harmony accepts, and it keeps Assembly-CSharp out of
        /// the compile.
        ///
        /// The measurement is deferred because <c>Show</c> has not built the grids
        /// yet when it returns.
        /// </summary>
        private static void AfterStashShow(MonoBehaviour __instance)
        {
            // Which of the six screens this is. The postfix has no screen filter --
            // it is on SimpleStashPanel.Show, which all six call -- so this is where
            // the filtering happens, and it is a decision rather than a side effect of
            // what the children are called. See StashScreens.
            string detail;
            var screen = ScreenFinder.Identify(__instance, out detail);

            Explain(screen, detail);

            _pendingScreen = screen;

            // Resize here, in Show, so the panel is already its full width on the
            // first frame the player sees. Deferring this to Update costs one frame
            // at the vanilla width and the stash visibly snaps wider on every open.
            StashMeasure.WidenNow(__instance, screen, Say);

            _pending = __instance;
            _framesWaited = 0;
        }

        /// <summary>
        /// Say once, per screen, what is going to happen on it.
        ///
        /// Worth the lines. The stash grid is <c>columns</c> wide on every screen that
        /// draws it and the panel fix is per screen, so "this screen was left alone" is
        /// the difference between a scrollbar somebody has to report and a scrollbar
        /// already accounted for. An unrecognised screen also carries the component
        /// names seen on the way up, which is the one thing needed to add it.
        /// </summary>
        private static void Explain(ScreenPolicy screen, string detail)
        {
            string message;

            if (!screen.Recognised)
            {
                message = "the stash is being drawn on a screen this mod does not "
                    + "recognise, so its panel is left alone and the grid may show a "
                    + "horizontal scrollbar. " + detail;
            }
            else if (!screen.Widen)
            {
                message = "the stash is being drawn on the " + screen.Label
                    + ", which is deliberately left alone -- nobody has looked at what a "
                    + "wide grid does to it, so it may show a horizontal scrollbar.";
            }
            else if (detail.Length > 0)
            {
                message = screen.Label + ": " + detail;
            }
            else
            {
                return;
            }

            if (ScreensSaid.Add(message)) Say(message);
        }

        /// <summary>
        /// Poll for the grid appearing. Cheap: a null check on every frame the stash
        /// is not opening, and at most a short burst of hierarchy walks after it is.
        /// </summary>
        private void Update()
        {
            if (_pending == null) return;

            // Gone before we measured -- the player closed the screen immediately.
            if (!_pending)
            {
                _pending = null;
                return;
            }

            if (StashMeasure.Report(_pending, _pendingScreen, Say))
            {
                _pending = null;
                return;
            }

            _framesWaited++;

            // The grid is built over a handful of frames. Two seconds at 60fps is
            // generous; past that something is wrong and polling forever is not the
            // answer.
            if (_framesWaited > 120)
            {
                Say("gave up waiting for the stash grid to appear after 120 frames.");
                _pending = null;
            }
        }

        private static int _framesWaited;

        /// <summary>
        /// Write one multi-line block, prefixing every line so it survives being
        /// read in a log alongside everything else.
        /// </summary>
        private static void Say(string message)
        {
            if (_log == null) return;

            foreach (var line in message.Split('\n'))
            {
                _log.LogInfo(Tag + line.TrimEnd('\r'));
            }
        }
    }
}





