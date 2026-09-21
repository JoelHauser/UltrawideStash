namespace UltrawideStash.Server;

/// <summary>
/// How many columns a screen can actually show, as pure arithmetic over ints.
///
/// ## Why the server needs this at all
///
/// The width is a server-side template change, but only the client knows how much
/// room there is to draw it. Before 0.6.0 the cap was a constant 40, derived from one
/// developer's 3440x1440 monitor -- on a 1920-wide canvas that is 2521px of grid
/// against 1920px of screen, so the cap protected nobody who needed protecting.
///
/// The real limit comes from the probe, which measures the panel on the live
/// hierarchy (<c>StashMeasure</c>). This class is the fallback for when no
/// measurement exists yet, and the shared arithmetic both halves agree on.
///
/// ## The canvas rule
///
/// <c>UICanvasScalerController.RunResolutionObserver</c> sets the canvas scale factor
/// to <c>Math.Min(width / 1920f, height / 1080f)</c> and <c>Utils.SetCanvasRestriction</c>
/// applies it with <c>uiScaleMode = ConstantPixelSize</c>. So the height alone sets the
/// scale, and any width past 16:9 becomes extra logical canvas:
///
/// <list type="bullet">
///   <item>1920x1080, 2560x1440, 3840x2160 -- all 16:9, all a 1920-wide canvas</item>
///   <item>3440x1440 -- scale 1.333, canvas 2580 wide</item>
///   <item>2560x1080 -- scale 1.000, canvas 2560 wide</item>
///   <item>5120x1440 -- scale 1.333, canvas 3840 wide</item>
/// </list>
///
/// The consequence that matters: **resolution is irrelevant, only aspect ratio counts.**
/// A 4K 16:9 player has exactly the same width budget as a 1080p player, and it is zero
/// extra. Cells never shrink to fit -- <c>ConstantPixelSize</c> means a cell is always
/// 63 logical px -- so a grid that is too wide does not squash, it overflows the panel.
/// </summary>
public static class StashFit
{
    /// <summary>
    /// One cell plus its border. <c>EFT.UI.DragAndDrop.ItemViewFactory.GetCellPixelSize</c>
    /// is literally <c>X * 63 + 1, Y * 63 + 1</c>.
    /// </summary>
    public const int CellPixels = 63;

    /// <summary>The single extra pixel a grid's width carries.</summary>
    public const int GridBorderPixels = 1;

    /// <summary>The canvas width EFT scales to, and the width a 16:9 screen gets.</summary>
    public const int ReferenceWidth = 1920;

    /// <summary>The canvas height EFT scales to. Only the ratio to width matters.</summary>
    public const int ReferenceHeight = 1080;

    /// <summary>Vanilla stash width, and the floor this mod never goes below.</summary>
    public const int VanillaColumns = 10;

    /// <summary>
    /// A typo guard and nothing more. The meaningful limit is always
    /// <see cref="ConservativeColumns"/> or a probe measurement; this only exists so a
    /// config holding 99999 cannot reach the template. 100 columns is 6301px, wider
    /// than a triple-monitor canvas, so it constrains no real screen.
    /// </summary>
    public const int AbsoluteMaxColumns = 100;

    /// <summary>The narrowest thing still worth calling a stash.</summary>
    public const int MinColumns = 2;

    /// <summary>
    /// The logical canvas width for a screen, which is what UI positions are expressed
    /// in. Returns <see cref="ReferenceWidth"/> for anything 16:9 or narrower, because
    /// EFT scales by the smaller of the two ratios and a taller-than-16:9 screen gains
    /// height rather than width.
    /// </summary>
    public static int CanvasWidth(int screenWidth, int screenHeight)
    {
        if (screenWidth <= 0 || screenHeight <= 0) return ReferenceWidth;

        // scale = min(w/1920, h/1080), and canvas width = w / scale. When the height
        // term is the smaller one the width term cancels to 1080 * aspect; otherwise
        // the canvas is exactly the reference width. Done in doubles and rounded, so
        // 3440x1440 gives 2580 rather than 2579.
        var byWidth = screenWidth / (double)ReferenceWidth;
        var byHeight = screenHeight / (double)ReferenceHeight;
        var scale = byWidth < byHeight ? byWidth : byHeight;

        if (scale <= 0d) return ReferenceWidth;

        var canvas = (int)Math.Round(screenWidth / scale, MidpointRounding.AwayFromZero);

        return canvas < ReferenceWidth ? ReferenceWidth : canvas;
    }

    /// <summary>How wide a grid of this many columns is drawn, in canvas pixels.</summary>
    public static int WidthOfColumns(int columns)
    {
        return columns * CellPixels + GridBorderPixels;
    }

    /// <summary>
    /// How many whole columns fit in a span of canvas pixels. The mirror of
    /// <see cref="WidthOfColumns"/>, and the same arithmetic the probe uses.
    /// </summary>
    public static int ColumnsThatFit(double availableWidth)
    {
        var usable = availableWidth - GridBorderPixels;

        if (usable < CellPixels) return 0;

        return (int)Math.Floor(usable / CellPixels);
    }

    /// <summary>
    /// The widest stash a screen can be assumed to show **without a measurement**.
    ///
    /// The model is deliberately pessimistic, because the one thing the server cannot
    /// see is how much slack BSG left in the stash panel at 16:9. So this grants only
    /// the canvas width that exists *beyond* 16:9 -- room no 16:9 layout could have
    /// been authored against -- and assumes the baseline slack is zero:
    ///
    /// <code>
    /// vanilla 10 + floor((canvasWidth - 1920) / 63)
    /// </code>
    ///
    /// <list type="bullet">
    ///   <item>any 16:9 screen -- 10 columns, i.e. the mod does nothing until measured</item>
    ///   <item>3440x1440 (canvas 2580) -- 20 columns</item>
    ///   <item>2560x1080 (canvas 2560) -- 20 columns</item>
    ///   <item>5120x1440 (canvas 3840) -- 40 columns</item>
    /// </list>
    ///
    /// A probe measurement supersedes this in both directions: it can allow more when
    /// the panel turns out to have slack, and less when the panel is pinned.
    /// </summary>
    public static int ConservativeColumns(int canvasWidth)
    {
        var extra = canvasWidth - ReferenceWidth;

        if (extra < CellPixels) return VanillaColumns;

        var columns = VanillaColumns + (int)Math.Floor(extra / (double)CellPixels);

        return columns > AbsoluteMaxColumns ? AbsoluteMaxColumns : columns;
    }

    /// <summary>
    /// <see cref="ConservativeColumns"/> straight from a screen size.
    /// </summary>
    public static int ConservativeColumnsForScreen(int screenWidth, int screenHeight)
    {
        return ConservativeColumns(CanvasWidth(screenWidth, screenHeight));
    }
}
