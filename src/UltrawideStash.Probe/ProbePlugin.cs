using System;
using System.Collections;
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
        public const string PluginVersion = "0.9.3";

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

            if (_widen.Value) StashMeasure.Widen = _reserve.Value;

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
            // Resize here, in Show, so the panel is already its full width on the
            // first frame the player sees. Deferring this to Update costs one frame
            // at the vanilla width and the stash visibly snaps wider on every open.
            StashMeasure.WidenNow(__instance, Say);

            _pending = __instance;
            _framesWaited = 0;
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

            if (StashMeasure.Report(_pending, Say))
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





