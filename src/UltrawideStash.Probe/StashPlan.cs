#nullable disable

using System;

namespace UltrawideStash.Probe
{
    /// <summary>
    /// How far the character screen's stash panel is widened -- pure arithmetic, no
    /// engine type, so it is also compiled into the test project (like
    /// <see cref="ScreenLayout"/>). <see cref="StashWiden"/> reads the live
    /// RectTransforms, asks this, and applies the answer.
    ///
    /// ## Two numbers, not one
    ///
    /// <see cref="Potential"/> is how many columns the panel can show when it takes
    /// every column's worth of slack the gear side can spare. That is what the server
    /// needs to size the grid, so it is what the measurement file carries.
    ///
    /// <see cref="Columns"/> is how many it is actually widened to: the grid's own
    /// width, when the grid is narrower. Until 1.0.3 the panel always took the full
    /// potential, so after a resolution change (or a <c>columns</c> set below what fits)
    /// a 19-wide grid sat in a 21-wide panel with two columns of empty panel beside it,
    /// width taken off the gear side and spent on nothing. The server catches up at its
    /// next start, because the measurement still says 21.
    ///
    /// ## Always from vanilla
    ///
    /// The inputs are the panel's and LeftSide's **untouched** widths, whatever an
    /// earlier open left on screen. Planning from the current state made a second open
    /// see no slack and a resolution change widen on top of a widening; planning from
    /// vanilla makes every open give the same answer for the same screen.
    ///
    /// ## The constants mirror the server's
    ///
    /// <c>StashFit.WidenedColumns</c> predicts <see cref="Potential"/> from a screen
    /// size, before the client has ever run, with its own copies of these numbers.
    /// <c>StashPlanTests</c> holds the two to the same answer across screen shapes.
    /// </summary>
    internal struct StashPlan
    {
        /// <summary>One cell, from <c>ItemViewFactory.GetCellPixelSize</c>.</summary>
        internal const int CellPixels = 63;

        /// <summary>The one extra pixel a grid's width carries.</summary>
        internal const int GridBorderPixels = 1;

        /// <summary>
        /// Clear air kept between the gear side and the widened panel, on top of the
        /// 10 px the screen ships with. See <see cref="StashWiden.GapPixels"/>.
        /// </summary>
        internal const float GapPixels = 24f;

        /// <summary>
        /// Slack between the grid and the viewport of a grown panel, so rounding cannot
        /// tip the ScrollRect into scrolling. See <see cref="StashWiden.ScrollSlackPixels"/>.
        /// </summary>
        internal const float ScrollSlackPixels = 4f;

        /// <summary>The narrowest reserve per gear panel that still clears Gear Panel's 494 px.</summary>
        internal const float MinReservePerPanel = 520f;

        /// <summary>
        /// LeftSide's left margin once the inventory screen is stretched to the canvas.
        /// UIScale.Reloaded's value, which every live measurement before 1.0.5 was taken
        /// under; vanilla's own frame puts it at 11.6.
        /// </summary>
        internal const float StretchedLeftMargin = 12f;

        /// <summary>The seam between LeftSide and the unwidened stash panel, stretched.</summary>
        internal const float StretchedSeam = 10f;

        /// <summary>
        /// LeftSide's right offset once stretched: clear of the stash panel and the
        /// seam. 680 px panel, 12 px right margin -> -702, UIScale.Reloaded's number,
        /// and 12 + 702 = 714 is the furniture the server's prediction subtracts.
        /// </summary>
        internal static float StretchedLeftSideRightOffset(float panelWidth, float panelRightMargin)
        {
            return -(panelWidth + panelRightMargin + StretchedSeam);
        }

        /// <summary>Columns the panel shows at its vanilla width.</summary>
        internal int Shown;

        /// <summary>Columns the panel can show when widened as far as it will go.</summary>
        internal int Potential;

        /// <summary>Columns the panel is widened to: <see cref="Potential"/>, or the grid if narrower.</summary>
        internal int Columns;

        /// <summary>Canvas px added to the panel. Zero leaves it vanilla.</summary>
        internal float Grow;

        /// <summary>Canvas px taken off LeftSide: <see cref="Grow"/> plus the gap.</summary>
        internal float Shrink;

        /// <summary>
        /// Why the panel cannot grow at all, in words fit for a player reading the
        /// server log. Null whenever <see cref="CanWiden"/> is true.
        /// </summary>
        internal string WhyNot;

        /// <summary>True when this plan changes the screen.</summary>
        internal bool Widens => Grow > 0f;

        /// <summary>True when the screen has room for more columns than vanilla.</summary>
        internal bool CanWiden => Potential > Shown;

        /// <summary>Whole columns in a span of canvas px.</summary>
        internal static int ColumnsThatFit(float availableWidth)
        {
            var usable = availableWidth - GridBorderPixels;

            if (usable < CellPixels) return 0;

            return (int)Math.Floor(usable / CellPixels);
        }

        /// <summary>How wide a grid of this many columns is drawn.</summary>
        internal static int WidthOfColumns(int columns)
        {
            return columns * CellPixels + GridBorderPixels;
        }

        /// <summary>A panel that cannot be widened for a structural reason.</summary>
        internal static StashPlan Refused(int shown, string why)
        {
            return new StashPlan
            {
                Shown = shown,
                Potential = shown,
                Columns = shown,
                WhyNot = why,
            };
        }

        /// <summary>
        /// Plan the widening.
        /// </summary>
        /// <param name="leftSideWidth">LeftSide's untouched width, canvas px.</param>
        /// <param name="panelWidth">The stash panel's untouched width, canvas px.</param>
        /// <param name="chrome">Panel width the grid never gets: toolbar strip and scrollbar.</param>
        /// <param name="reservePerPanel">What each of the two gear panels keeps.</param>
        /// <param name="gridColumns">The stash grid's width, or 0 when it is not known.</param>
        internal static StashPlan For(
            float leftSideWidth,
            float panelWidth,
            float chrome,
            float reservePerPanel,
            int gridColumns)
        {
            if (reservePerPanel < MinReservePerPanel) reservePerPanel = MinReservePerPanel;

            var reserve = reservePerPanel * 2f;
            var slack = leftSideWidth - reserve;
            var shown = ColumnsThatFit(panelWidth - chrome);

            if (slack < CellPixels)
            {
                return Refused(shown, string.Format(
                    "the gear side of the inventory screen is {0:0} px wide and keeps {1:0} px "
                    + "for itself, which leaves no room for another column",
                    leftSideWidth, reserve));
            }

            // The gap comes out of the slack, not on top of the panel: the two are
            // neighbours, so growing the panel by exactly what LeftSide gives up would
            // leave the seam between them where it was.
            var usable = panelWidth + slack - GapPixels;
            var potential = ColumnsThatFit(usable - chrome - ScrollSlackPixels);

            if (potential <= shown)
            {
                return Refused(shown, string.Format(
                    "the {0:0} px the gear side can spare is less than one more column once "
                    + "the {1:0} px gap is kept",
                    slack, GapPixels));
            }

            // No wider than the grid. A grid wider than the potential still gets the
            // whole potential -- it will scroll sideways, which the CHECK line says.
            var columns = potential;

            if (gridColumns > 0 && gridColumns < columns) columns = Math.Max(gridColumns, shown);

            var plan = new StashPlan
            {
                Shown = shown,
                Potential = potential,
                Columns = shown,
            };

            if (columns <= shown) return plan;

            // Only what turns into columns: a panel sized to the last pixel ends in a
            // strip too narrow to hold one.
            var wanted = WidthOfColumns(columns) + chrome + ScrollSlackPixels;
            var grow = wanted - panelWidth;

            if (grow <= 0f) return plan;

            plan.Columns = columns;
            plan.Grow = grow;
            plan.Shrink = grow + GapPixels;

            return plan;
        }
    }
}
