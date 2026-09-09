using Squarebuzz.Core.Model;

namespace Squarebuzz.Core.Layout;

/// <summary>
/// Board geometry: how big a cell can be, how much room the clue gutters need, and which cell
/// a touch landed on.
/// </summary>
/// <remarks>
/// Lives in Core, with no MAUI types, purely so it can be unit-tested without a device. Ported
/// from the prototype's <c>metrics()</c>, including its constants.
/// </remarks>
public readonly record struct BoardLayout
{
    /// <summary>A clue slot is this fraction of a cell. From the prototype's <c>cw</c>.</summary>
    public const double ClueSlotRatio = 0.66;

    /// <summary>
    /// The slot fraction when big numbers are on. The bigger font needs a bigger box: 0.52 x 1.3
    /// is 0.676 of a cell, which overflows the normal 0.66 slot, so the gutter grows instead of
    /// the accessibility feature silently clipping.
    /// </summary>
    public const double BigClueSlotRatio = 0.82;

    /// <summary>Below this a cell is too small to hit reliably with a child's finger.</summary>
    public const double MinCellSize = 17;

    /// <summary>Above this the board stops looking like a grid and starts looking like blocks.</summary>
    public const double MaxCellSize = 64;

    /// <summary>Heavier separator every this many cells, to make counting easier.</summary>
    public const int GroupSize = 5;

    private const double MinClueFontSize = 9;
    private const double MaxClueFontSize = 22;
    private const double ClueFontRatio = 0.52;
    private const double BigNumbersMultiplier = 1.3;

    /// <summary>
    /// A digit in the UI fonts advances roughly 0.6 em, so a two-digit clue is about 1.2 x the
    /// font size. Capping the font at this fraction of the slot keeps "10" inside its box.
    /// </summary>
    private const double MaxFontPerSlotRatio = 0.83;

    public required int Columns { get; init; }

    public required int Rows { get; init; }

    public required double CellSize { get; init; }

    /// <summary>Width and height of one clue numeral's box.</summary>
    public required double ClueSlot { get; init; }

    /// <summary>
    /// Whether this layout was sized for the big-numbers accessibility setting. Lives on the
    /// layout so the slot and the font can never be computed from different answers.
    /// </summary>
    public required bool BigNumbers { get; init; }

    /// <summary>Most clue numbers any single row carries - the row gutter is sized for this.</summary>
    public required int MaxRowClues { get; init; }

    public required int MaxColumnClues { get; init; }

    public double RowGutterWidth => MaxRowClues * ClueSlot;

    public double ColumnGutterHeight => MaxColumnClues * ClueSlot;

    public double GridWidth => Columns * CellSize;

    public double GridHeight => Rows * CellSize;

    /// <summary>Full drawn size, gutters included.</summary>
    public double TotalWidth => RowGutterWidth + GridWidth;

    public double TotalHeight => ColumnGutterHeight + GridHeight;

    /// <summary>
    /// True when the board is bigger than the space offered, so the host needs to scroll it.
    /// </summary>
    public required bool RequiresScrolling { get; init; }

    /// <summary>
    /// Works out the largest cell size that fits, then clamps it to the playable range.
    /// </summary>
    /// <param name="puzzle">The puzzle being laid out.</param>
    /// <param name="availableWidth">Usable width in device-independent units.</param>
    /// <param name="availableHeight">Usable height.</param>
    /// <param name="zoomPercent">The player's cell-size preference, 70-160.</param>
    /// <param name="bigNumbers">Whether clue numerals use the larger accessibility font.</param>
    public static BoardLayout Calculate(Puzzle puzzle, double availableWidth, double availableHeight, int zoomPercent = 100, bool bigNumbers = false)
    {
        ArgumentNullException.ThrowIfNull(puzzle);

        // The puzzle owns these; re-deriving them here was a second source of the same truth.
        var maxRowClues = puzzle.MaxRowClueCount;
        var maxColumnClues = puzzle.MaxColumnClueCount;

        // Guard against a zero or negative viewport, which happens for one layout pass before
        // MAUI has measured the view.
        var usableWidth = Math.Max(1, availableWidth);
        var usableHeight = Math.Max(1, availableHeight);

        // Solve for the cell size that makes cells plus gutter exactly fill each axis, then
        // take whichever axis is tighter.
        var slotRatio = bigNumbers ? BigClueSlotRatio : ClueSlotRatio;
        var cellFromWidth = usableWidth / (puzzle.Width + (maxRowClues * slotRatio));
        var cellFromHeight = usableHeight / (puzzle.Height + (maxColumnClues * slotRatio));

        var zoom = Math.Clamp(zoomPercent, GameSettings.MinCellZoomPercent, GameSettings.MaxCellZoomPercent) / 100.0;
        var fitted = Math.Floor(Math.Min(cellFromWidth, cellFromHeight) * zoom);
        var cellSize = Math.Clamp(fitted, MinCellSize, MaxCellSize);

        var clueSlot = Math.Round(cellSize * slotRatio);

        var layout = new BoardLayout
        {
            Columns = puzzle.Width,
            Rows = puzzle.Height,
            CellSize = cellSize,
            ClueSlot = clueSlot,
            BigNumbers = bigNumbers,
            MaxRowClues = maxRowClues,
            MaxColumnClues = maxColumnClues,
            RequiresScrolling = false,
        };

        return layout with
        {
            RequiresScrolling = layout.TotalWidth > usableWidth || layout.TotalHeight > usableHeight,
        };
    }

    /// <summary>
    /// Font size for clue numerals at this cell size, capped so a two-digit clue always fits
    /// inside its slot.
    /// </summary>
    public double ClueFontSize()
    {
        var scaled = CellSize * ClueFontRatio * (BigNumbers ? BigNumbersMultiplier : 1);
        var fitted = Math.Round(Math.Min(scaled, ClueSlot * MaxFontPerSlotRatio));

        return Math.Clamp(fitted, MinClueFontSize, MaxClueFontSize);
    }

    /// <summary>
    /// Row-major index of the cell at a canvas point, or <c>null</c> when the point is in a
    /// gutter or past the edge of the grid.
    /// </summary>
    public int? HitTest(double x, double y)
    {
        var gridX = x - RowGutterWidth;
        var gridY = y - ColumnGutterHeight;

        if (gridX < 0 || gridY < 0)
        {
            return null;
        }

        var column = (int)(gridX / CellSize);
        var row = (int)(gridY / CellSize);

        if (column < 0 || row < 0 || column >= Columns || row >= Rows)
        {
            return null;
        }

        return (row * Columns) + column;
    }

    /// <summary>Top-left corner of a cell in canvas coordinates.</summary>
    public (double X, double Y) CellOrigin(int column, int row) =>
        (RowGutterWidth + (column * CellSize), ColumnGutterHeight + (row * CellSize));

    /// <summary>True when a heavier separator belongs after this column.</summary>
    public bool IsGroupBoundaryAfterColumn(int column) =>
        column % GroupSize == GroupSize - 1 && column < Columns - 1;

    public bool IsGroupBoundaryAfterRow(int row) =>
        row % GroupSize == GroupSize - 1 && row < Rows - 1;
}
