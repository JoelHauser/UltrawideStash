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
/// So the server predicts it (<see cref="WidenedColumns"/>), from the screen size and
/// the arithmetic both halves agree on. A measurement the game takes of the real panel
/// (<c>StashMeasure</c>) corrects the prediction when there is one.
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
    /// <see cref="WidenedColumns"/> or the game's measurement; this only exists so a
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
    /// <see cref="WidthOfColumns"/>, and the same arithmetic the client uses.
    /// </summary>
    public static int ColumnsThatFit(double availableWidth)
    {
        var usable = availableWidth - GridBorderPixels;

        if (usable < CellPixels) return 0;

        return (int)Math.Floor(usable / CellPixels);
    }

    /// <summary>
    /// What the client's widening will leave room for, on a canvas this wide.
    ///
    /// ## Why the server does this arithmetic at all
    ///
    /// The width used to come only from the game measuring its own panel, which meant
    /// the first server start after an install had nothing to go on and produced a
    /// vanilla stash -- a first run that looks broken.
    ///
    /// The measurement is not actually needed to know the answer. The client's widening is
    /// deterministic: it narrows LeftSide to a fixed reserve and gives the difference
    /// to the stash panel. Given the canvas width, the result is arithmetic, and the
    /// canvas width follows from the screen resolution, which the server can read
    /// (see <see cref="SavedScreen"/>). So the server predicts what the client will do
    /// and sizes the grid to match it, on the very first start.
    ///
    /// ## The layout this mirrors
    ///
    /// Measured off a live 3440x1440 client under UIScale.Reloaded's stretch, which the
    /// client makes itself from 1.0.5 on. On a canvas of width C the inventory
    /// screen is laid out as:
    ///
    /// <code>
    /// 12 | LeftSide | 10 | Stash Panel 680 | 12        (12 + L + 10 + 680 + 12 = C)
    /// </code>
    ///
    /// so LeftSide is <c>C - 714</c> -- 1866 at 2580, 1206 at 1920, both confirmed in
    /// the logs. The client then narrows LeftSide to twice <see cref="DefaultReservePerPanel"/>,
    /// keeps <see cref="ExtraGapPixels"/> of the slack as clearance and gives the rest
    /// to the panel, out of which the grid never gets <see cref="PanelChromePixels"/>
    /// or the <see cref="ScrollSlackPixels"/> that keeps it off its scrollbar.
    ///
    /// **These constants mirror the client's and have to move together.** They cannot be
    /// shared -- the two halves are separate assemblies on different frameworks -- so a
    /// change there is a change here. A disagreement is self-correcting rather than
    /// fatal: the game measures the real panel and its measurement supersedes this.
    /// </summary>
    /// <param name="canvasWidth">The logical canvas width.</param>
    /// <param name="reservePerPanel">
    /// The client's <c>GearPanelReserve</c>, raised to its 520 floor as the client does.
    /// A raised reserve leaves the stash less room, and predicting with the default then
    /// sized the grid wider than the client would draw it.
    /// </param>
    public static int WidenedColumns(int canvasWidth, float reservePerPanel = DefaultReservePerPanel)
    {
        if (reservePerPanel < MinReservePerPanel) reservePerPanel = MinReservePerPanel;

        var leftSide = canvasWidth - ScreenFurniturePixels;
        var slack = leftSide - reservePerPanel * 2f;

        if (slack < CellPixels) return VanillaColumns;

        var usable = VanillaPanelPixels + slack - ExtraGapPixels;
        var forGrid = usable - PanelChromePixels - ScrollSlackPixels;

        var columns = ColumnsThatFit(forGrid);

        if (columns < VanillaColumns) return VanillaColumns;

        return columns > AbsoluteMaxColumns ? AbsoluteMaxColumns : columns;
    }

    /// <summary><see cref="WidenedColumns"/> straight from a screen size.</summary>
    public static int WidenedColumnsForScreen(
        int screenWidth,
        int screenHeight,
        float reservePerPanel = DefaultReservePerPanel)
    {
        return WidenedColumns(CanvasWidth(screenWidth, screenHeight), reservePerPanel);
    }

    /// <summary>StashWiden.DefaultReservePerPanel -- what each gear panel keeps.</summary>
    public const float DefaultReservePerPanel = 620f;

    /// <summary>StashPlan.MinReservePerPanel -- the client raises anything lower to this.</summary>
    public const float MinReservePerPanel = 520f;

    /// <summary>Canvas width the inventory screen spends on margins, gap and the stash panel.</summary>
    private const int ScreenFurniturePixels = 714;

    /// <summary>The stash panel before it is widened.</summary>
    private const int VanillaPanelPixels = 680;

    /// <summary>StashWiden.GapPixels.</summary>
    private const int ExtraGapPixels = 24;

    /// <summary>StashMeasure.DefaultChrome -- toolbar strip plus scrollbar.</summary>
    private const int PanelChromePixels = 48;

    /// <summary>StashWiden.ScrollSlackPixels.</summary>
    private const int ScrollSlackPixels = 4;
}
