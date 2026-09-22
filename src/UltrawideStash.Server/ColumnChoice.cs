namespace UltrawideStash.Server;

/// <summary>
/// Turns "what the player asked for" plus "what the screen can show" into the one
/// number the templates get, and a sentence saying why.
///
/// ## Why this is its own type
///
/// It is the whole of the safety argument for the width, and it is pure: no SPT type,
/// no file system, no Unity. That makes it testable, and this is exactly the kind of
/// decision that was wrong before -- 0.5.0 capped at a constant 40 derived from one
/// 3440x1440 monitor, which on a 1920-wide canvas is 601px wider than the entire
/// screen. A cap that does not know the screen is not a cap.
///
/// ## The order of authority
///
/// <list type="number">
///   <item><b>A probe measurement</b>, when there is one. It is the only source that
///   has seen the real panel on the real screen.</item>
///   <item><b>The declared screen size</b>, through
///   <see cref="StashFit.ConservativeColumns"/>, which grants only the canvas width
///   that exists beyond 16:9 and assumes the panel has no slack of its own. On a 16:9
///   screen that is vanilla 10, so an unmeasured 16:9 install changes nothing.</item>
///   <item><b>The player overriding both</b>, with <c>ignoreMeasurement</c>. Their
///   machine, their call -- but they have to say so.</item>
/// </list>
/// </summary>
public static class ColumnChoice
{
    /// <summary>Where the final number came from, for the log and for tests.</summary>
    public enum Origin
    {
        /// <summary>Auto, from the probe's measurement.</summary>
        Measured,

        /// <summary>Auto, from the declared screen size.</summary>
        Estimated,

        /// <summary>The player's number, and it fitted.</summary>
        Requested,

        /// <summary>The player's number, reduced to what the screen can show.</summary>
        Clamped,

        /// <summary>The player's number, unchecked because they asked for that.</summary>
        Unchecked,
    }

    /// <summary>
    /// The decision. <see cref="Columns"/> is what the templates get;
    /// <see cref="Ceiling"/> is what the screen was judged able to show.
    /// </summary>
    public readonly record struct Choice(
        int Columns,
        int Ceiling,
        Origin Source,
        string Reason)
    {
        /// <summary>True when this will not change any template.</summary>
        public bool IsNoOp => Columns <= StashFit.VanillaColumns;

        /// <summary>The grid's drawn width in canvas pixels.</summary>
        public int PixelWidth => StashFit.WidthOfColumns(Columns);
    }

    /// <summary>
    /// Work out the width to use.
    /// </summary>
    /// <param name="requested">
    /// The config's <c>columns</c>, or null for <c>"auto"</c>.
    /// </param>
    /// <param name="measurement">The probe's last measurement, or null.</param>
    /// <param name="screenWidth">Declared screen width, used only without a measurement.</param>
    /// <param name="screenHeight">Declared screen height, likewise.</param>
    /// <param name="ignoreMeasurement">
    /// True to honour <paramref name="requested"/> with no ceiling at all.
    /// </param>
    public static Choice For(
        int? requested,
        Measurement? measurement,
        int screenWidth,
        int screenHeight,
        bool ignoreMeasurement)
    {
        var ceiling = Ceiling(measurement, screenWidth, screenHeight, out var ceilingFrom);

        // The override. Still bounded by the absolute typo guard, because a config
        // holding 99999 is a mistake in every reading of it.
        if (ignoreMeasurement)
        {
            if (requested is null)
            {
                return new Choice(ceiling, ceiling, ceilingFrom,
                    $"ignoreMeasurement is set but columns is auto, so {ceilingFrom.ToString().ToLowerInvariant()} "
                    + $"{ceiling} is still what gets used");
            }

            var forced = Clamp(requested.Value, StashFit.MinColumns, StashFit.AbsoluteMaxColumns);

            return new Choice(forced, ceiling, Origin.Unchecked,
                $"{forced} columns as configured, unchecked -- ignoreMeasurement is set, so this "
                + $"is not limited to the {ceiling} this screen was judged able to show");
        }

        // Auto.
        if (requested is null)
        {
            return new Choice(ceiling, ceiling, ceilingFrom,
                ceilingFrom == Origin.Measured
                    ? $"auto: {ceiling} columns, from the probe's measurement"
                    : $"auto: {ceiling} columns, estimated from a {screenWidth}x{screenHeight} screen "
                      + "(no probe measurement yet)");
        }

        var wanted = Clamp(requested.Value, StashFit.MinColumns, StashFit.AbsoluteMaxColumns);

        if (wanted <= ceiling)
        {
            return new Choice(wanted, ceiling, Origin.Requested,
                $"{wanted} columns as configured (this screen can show {ceiling})");
        }

        return new Choice(ceiling, ceiling, Origin.Clamped,
            $"{wanted} columns was asked for but this screen can only show {ceiling}, so {ceiling} "
            + "is being used. A wider grid would not shrink to fit -- it would be clipped, and the "
            + "columns past the edge would look like lost items. Set ignoreMeasurement to true to "
            + "override this");
    }

    /// <summary>
    /// The widest this screen is judged able to show, and where that judgement came
    /// from. Never below vanilla, so the ceiling can never itself force a narrowing.
    /// </summary>
    private static int Ceiling(
        Measurement? measurement,
        int screenWidth,
        int screenHeight,
        out Origin from)
    {
        if (measurement is not null)
        {
            from = Origin.Measured;
            return Clamp(measurement.MaxColumns, StashFit.VanillaColumns, StashFit.AbsoluteMaxColumns);
        }

        from = Origin.Estimated;

        // Predict what the client's widening will produce rather than assuming the
        // panel is pinned. ConservativeColumns was written when it was -- it grants
        // only canvas beyond 16:9 and answers 20 on a 2580 canvas, one more than the
        // widened panel can actually show, which would overflow into a scrollbar.
        return Clamp(
            StashFit.WidenedColumnsForScreen(screenWidth, screenHeight),
            StashFit.VanillaColumns,
            StashFit.AbsoluteMaxColumns);
    }

    private static int Clamp(int value, int low, int high)
    {
        if (value < low) return low;

        return value > high ? high : value;
    }
}
