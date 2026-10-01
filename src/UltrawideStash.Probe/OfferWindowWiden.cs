using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace UltrawideStash.Probe
{
    /// <summary>
    /// Widens the flea market's add-offer window so the stash in it is not cut off.
    ///
    /// ## Why this is not <see cref="ScreenWiden"/>
    ///
    /// The window draws the stash in a plain <c>GridView</c> of its own, with no stash
    /// panel, so nothing hooked on <c>SimpleStashPanel.Show</c> ever saw it. And it is
    /// not laid out by hand: read off the live window (2026-09-30, 3440x1440), it is a
    /// centred, draggable 1200 px box ('Inner') whose contents are a horizontal layout
    /// group of two parts -- the stash ('StashPart', 676 px, sized by its
    /// LayoutElement) and the price side (524 px). The grid scrolls inside a 632 px
    /// mask, 44 px narrower than the part. Moving rects there would be undone by the
    /// layout, so this asks the layout instead: a larger preferred width for the stash
    /// part, and a window that much wider. The layout does the rest.
    ///
    /// Done in <c>Show</c>'s postfix, before the window's first frame, and from the
    /// vanilla numbers every time, so opening it twice is the same as once. Never in
    /// raid: the flea market is a menu screen.
    /// </summary>
    internal static class OfferWindowWiden
    {
        /// <summary>Clear canvas kept either side of the widened window.</summary>
        private const float EdgeMargin = 12f;

        /// <summary>Same four pixels as everywhere else, so rounding never scrolls.</summary>
        private const float ScrollSlack = 4f;

        private sealed class Vanilla
        {
            internal RectTransform Inner;
            internal RectTransform Part;
            internal RectTransform Scroll;
            internal Component Element;
            internal float InnerWidth;
            internal float PartWidth;
            internal float Chrome;
            internal float Preferred;
            internal float Min;
        }

        /// <summary>Per window, by instance id. The game keeps one and reuses it.</summary>
        private static readonly Dictionary<int, Vanilla> Saved = new Dictionary<int, Vanilla>();

        private static PropertyInfo _preferred;

        private static PropertyInfo _min;

        /// <summary>
        /// Size the window for a grid this many columns wide. Returns one line for the
        /// log, or null when there was nothing to do.
        /// </summary>
        internal static string Apply(MonoBehaviour window, int columns)
        {
            if (window == null || columns <= 0) return null;

            var v = VanillaOf(window);

            if (v == null) return "flea add offer window: cannot widen -- its layout is not the one this was written against.";

            Restore(v);

            var need = columns * ScreenLayout.CellPixels + 1f + ScrollSlack + v.Chrome;
            var grow = need - v.PartWidth;

            if (!ScreenWiden.Enabled || grow <= 0f)
            {
                CorrectPosition(window);
                return null;
            }

            var canvas = window.GetComponentInParent<Canvas>();
            var canvasWidth = canvas != null ? ((RectTransform)canvas.rootCanvas.transform).rect.width : 0f;
            var room = canvasWidth - 2f * EdgeMargin - v.InnerWidth;
            var capped = canvasWidth > 0f && grow > room;

            if (capped) grow = Math.Max(0f, room);

            if (grow < ScreenLayout.CellPixels)
            {
                CorrectPosition(window);
                return string.Format(
                    "flea add offer window: no room to widen ({0:0} px canvas, window {1:0} px).", canvasWidth, v.InnerWidth);
            }

            if (v.Preferred > 0f) _preferred.SetValue(v.Element, v.Preferred + grow, null);
            else _preferred.SetValue(v.Element, v.PartWidth + grow, null);

            if (v.Min > 0f) _min.SetValue(v.Element, v.Min + grow, null);

            // The window's own rect takes the new width at once -- nothing lays it out --
            // so the game's keep-it-on-screen sees it without forcing a layout pass,
            // which rebuilt the whole UI and was felt as a hitch on every open. The
            // inside follows in the normal layout pass, before the frame is drawn.
            SetWidth(v.Inner, v.InnerWidth + grow);
            CorrectPosition(window);

            return string.Format(
                "flea add offer window: {0:0} -> {1:0} px, stash side {2:0} -> {3:0} px ({4} columns{5}).",
                v.InnerWidth, v.InnerWidth + grow, v.PartWidth, v.PartWidth + grow,
                ScreenLayout.ColumnsIn(v.PartWidth + grow, v.Chrome + ScrollSlack),
                capped ? string.Format(", capped by the {0:0} px canvas", canvasWidth) : string.Empty);
        }

        /// <summary>
        /// The outcome, measured: the grid against its mask, and the window against the
        /// canvas. Null when the window is gone.
        /// </summary>
        internal static string Check(MonoBehaviour window, Component gridView)
        {
            if (window == null || !window.gameObject.activeInHierarchy) return null;

            if (!Saved.TryGetValue(window.GetInstanceID(), out var v) || !v.Scroll || !v.Inner) return null;

            var grid = gridView != null ? gridView.transform as RectTransform : null;
            var drawn = grid != null ? grid.rect.width : 0f;
            var viewport = v.Scroll.rect.width;
            var spare = viewport - drawn;

            var canvas = window.GetComponentInParent<Canvas>();
            var onScreen = string.Empty;

            if (canvas != null)
            {
                var c = (RectTransform)canvas.rootCanvas.transform;
                var corners = new Vector3[4];

                v.Inner.GetWorldCorners(corners);

                var a = c.InverseTransformPoint(corners[0]);
                var b = c.InverseTransformPoint(corners[2]);
                var half = c.rect.width / 2f;

                onScreen = a.x >= -half - 1f && b.x <= half + 1f
                    ? string.Format("; window x {0:0}..{1:0}, on screen", a.x, b.x)
                    : string.Format("; window x {0:0}..{1:0}, PAST THE CANVAS EDGE (+/-{2:0})", a.x, b.x, half);
            }

            return string.Format(
                "flea add offer window: CHECK: viewport {0:0.0} px vs grid {1:0.0} px -- {2}{3}",
                viewport, drawn,
                spare < 0f ? string.Format("OVERFLOW by {0:0.0} px", -spare) : string.Format("fits, {0:0.0} px spare", spare),
                onScreen);
        }

        /// <summary>
        /// Find the window's pieces from the grid up, by structure rather than name:
        /// the mask the grid scrolls in, then the first ancestor above it with a
        /// LayoutElement (the stash part), and the window's own transform. Recorded the
        /// first time, while the window is vanilla.
        /// </summary>
        private static Vanilla VanillaOf(MonoBehaviour window)
        {
            var id = window.GetInstanceID();

            if (Saved.TryGetValue(id, out var known) && known.Inner && known.Part && known.Element) return known;

            var inner = AccessTools.Property(window.GetType(), "WindowTransform")?.GetValue(window, null) as RectTransform;
            var gridView = AccessTools.Field(window.GetType(), "_gridView")?.GetValue(window) as Component;

            if (inner == null || gridView == null) return null;

            RectTransform scroll = null;
            RectTransform part = null;
            Component element = null;

            for (var n = gridView.transform.parent as RectTransform; n != null && n != inner; n = n.parent as RectTransform)
            {
                if (scroll == null)
                {
                    if (HasBehaviour(n, "Mask") != null || HasBehaviour(n, "RectMask2D") != null) scroll = n;
                    continue;
                }

                element = HasBehaviour(n, "LayoutElement");

                if (element != null)
                {
                    part = n;
                    break;
                }
            }

            if (scroll == null || part == null) return null;

            if (_preferred == null)
            {
                _preferred = element.GetType().GetProperty("preferredWidth");
                _min = element.GetType().GetProperty("minWidth");
            }

            if (_preferred == null || _min == null) return null;

            var chrome = part.rect.width - scroll.rect.width;

            // Sanity: the window as read off the game, give or take.
            if (chrome < 0f || chrome > 200f || part.rect.width < 300f || inner.rect.width < part.rect.width) return null;

            var v = new Vanilla
            {
                Inner = inner,
                Part = part,
                Scroll = scroll,
                Element = element,
                InnerWidth = inner.rect.width,
                PartWidth = part.rect.width,
                Chrome = chrome,
                Preferred = (float)_preferred.GetValue(element, null),
                Min = (float)_min.GetValue(element, null),
            };

            Saved[id] = v;

            return v;
        }

        private static void Restore(Vanilla v)
        {
            _preferred.SetValue(v.Element, v.Preferred, null);
            _min.SetValue(v.Element, v.Min, null);
            SetWidth(v.Inner, v.InnerWidth);
        }

        /// <summary>Set a rect's width about its current centre, whatever its anchors.</summary>
        private static void SetWidth(RectTransform rt, float width)
        {
            var grow = width - rt.rect.width;

            if (Math.Abs(grow) < 0.01f) return;

            var left = grow * rt.pivot.x;
            var right = grow - left;

            rt.offsetMin = new Vector2(rt.offsetMin.x - left, rt.offsetMin.y);
            rt.offsetMax = new Vector2(rt.offsetMax.x + right, rt.offsetMax.y);
        }

        private static MethodInfo _correct;

        /// <summary>The game's own keep-the-window-on-screen, which Show ran at the old width.</summary>
        private static void CorrectPosition(MonoBehaviour window)
        {
            if (_correct == null) _correct = AccessTools.Method(window.GetType(), "CorrectPosition");

            if (_correct == null) return;

            var parameters = _correct.GetParameters();
            var args = new object[parameters.Length];

            for (var i = 0; i < parameters.Length; i++)
            {
                var t = parameters[i].ParameterType;
                args[i] = t.IsValueType ? Activator.CreateInstance(t) : null;
            }

            _correct.Invoke(window, args);
        }

        private static Component HasBehaviour(Component node, string typeName)
        {
            foreach (var b in node.GetComponents<Behaviour>())
            {
                if (b != null && b.enabled && b.GetType().Name == typeName) return b;
            }

            return null;
        }
    }
}
