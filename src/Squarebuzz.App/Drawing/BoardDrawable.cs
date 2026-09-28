using Squarebuzz.Core.Clues;
using Squarebuzz.Core.Layout;
using Squarebuzz.Core.Model;

namespace Squarebuzz.App.Drawing;

/// <summary>Draws cells, clues, crosses and separators on one canvas.</summary>
public sealed class BoardDrawable : IDrawable
{
    private const float CellInset = 0.5f;
    private const double CrossBarLengthRatio = 0.54;
    private const double CrossBarThicknessRatio = 0.13;
    private const double StrikeThicknessRatio = 0.14;
    private const float GroupSeparatorThickness = 2f;
    private const float CellBorderThickness = 1f;
    private const float StruckClueOpacity = 0.42f;

    /// <summary>
    /// The platform-resolved clue face, supplied by BoardView when its handler connects.
    /// Graphics canvases do not resolve the font aliases registered for MAUI controls.
    /// </summary>
    public IFont ClueFont { get; set; } = Microsoft.Maui.Graphics.Font.DefaultBold;

    /// <summary>The puzzle being drawn. Null before a game starts.</summary>
    public Puzzle? Puzzle { get; set; }

    /// <summary>Snapshot of the player's marks. Copied, not shared, so drawing never races play.</summary>
    public CellState[] Cells { get; set; } = [];

    public BoardLayout Layout { get; set; }

    public BoardPalette Palette { get; set; } = BoardPalette.FromResources();

    /// <summary>Row under the finger, highlighted to help the player track the line. -1 for none.</summary>
    public int HighlightRow { get; set; } = -1;

    public int HighlightColumn { get; set; } = -1;

    /// <summary>Cell being pulsed as a hint. -1 for none.</summary>
    public int HintIndex { get; set; } = -1;

    /// <summary>Ring width around the hinted cell. Animated by the view; 3 when at rest.</summary>
    public float HintRingWidth { get; set; } = 3f;

    /// <summary>Cell flashed red after a wrong fill. -1 for none.</summary>
    public int MistakeIndex { get; set; } = -1;

    /// <summary>Cell mid-pop after being filled. -1 for none.</summary>
    public int PopIndex { get; set; } = -1;

    /// <summary>Scale of the popping cell, driven by the view's animation.</summary>
    public float PopScale { get; set; } = 1f;

    /// <summary>How long a clue strike takes to wipe across the number.</summary>
    private const double StrikeWipeMilliseconds = 180;

    /// <summary>Start times for clue-strike animations. Strikes present on initial draw are already settled.</summary>
    private readonly Dictionary<long, long> _strikeBirths = [];

    private bool _seedStrikesSilently = true;

    private bool _sawUnfinishedWipe;

    /// <summary>Requests another frame when a clue-strike animation is still active.</summary>
    public event EventHandler? WipeInProgress;

    /// <summary>Forget every strike and treat the next draw as a fresh board.</summary>
    public void ResetStrikeAnimations()
    {
        _strikeBirths.Clear();
        _seedStrikesSilently = true;
    }

    private float StrikeProgress(bool isRow, int line, int run, bool isStruck)
    {
        var key = ((isRow ? 1L : 0L) << 40) | ((long)line << 20) | (uint)run;

        if (!isStruck)
        {
            _strikeBirths.Remove(key);
            return 0f;
        }

        if (!_strikeBirths.TryGetValue(key, out var birth))
        {
            // Ancient (0) when seeding a freshly shown board, newborn otherwise.
            birth = _seedStrikesSilently ? 0 : Environment.TickCount64;
            _strikeBirths[key] = birth;
        }

        if (birth == 0 || Services.MotionPreferences.ReduceMotion)
        {
            return 1f;
        }

        var progress = (float)Math.Min(1.0, (Environment.TickCount64 - birth) / StrikeWipeMilliseconds);

        if (progress < 1f)
        {
            _sawUnfinishedWipe = true;
        }

        return progress;
    }

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        ArgumentNullException.ThrowIfNull(canvas);

        if (Puzzle is not { } puzzle || Cells.Length != puzzle.CellCount)
        {
            return;
        }

        var layout = Layout;

        // Skip a frame when the puzzle and layout describe different boards during a session change.
        if (layout.Columns != puzzle.Width || layout.Rows != puzzle.Height)
        {
            return;
        }

        _sawUnfinishedWipe = false;

        DrawGutterBackgrounds(canvas, layout);

        // Draw fills, a shared grid, then crosses. Draw the hint ring last so separators cannot cover it.
        DrawCellFills(canvas, layout);
        DrawGridLines(canvas, layout);
        DrawCellMarks(canvas, layout);
        DrawGroupSeparators(canvas, layout);
        DrawHintRing(canvas, layout);

        // Set the clue font once per gutter; the canvas does not inherit XAML font styles.
        canvas.Font = ClueFont;
        canvas.FontSize = (float)layout.ClueFontSize();

        DrawColumnClues(canvas, puzzle, layout);
        DrawRowClues(canvas, puzzle, layout);

        // Everything struck during the first draw of a board has now been seeded as ancient;
        // from here on a new strike is genuinely new and earns its wipe.
        _seedStrikesSilently = false;

        if (_sawUnfinishedWipe)
        {
            WipeInProgress?.Invoke(this, EventArgs.Empty);
        }
    }

    private void DrawGutterBackgrounds(ICanvas canvas, BoardLayout layout)
    {
        // Corner block where the two gutters meet - sunk, so it reads as not-a-clue-area.
        canvas.FillColor = Palette.Sunk;
        canvas.FillRectangle(0, 0, (float)layout.RowGutterWidth, (float)layout.ColumnGutterHeight);

        canvas.FillColor = Palette.Gutter;
        canvas.FillRectangle((float)layout.RowGutterWidth, 0, (float)layout.GridWidth, (float)layout.ColumnGutterHeight);
        canvas.FillRectangle(0, (float)layout.ColumnGutterHeight, (float)layout.RowGutterWidth, (float)layout.GridHeight);

        // Highlight the touched row and column right through the gutters.
        canvas.FillColor = Palette.PrimarySoft;

        if (HighlightColumn >= 0 && HighlightColumn < layout.Columns)
        {
            var (x, _) = layout.CellOrigin(HighlightColumn, 0);
            canvas.FillRectangle((float)x, 0, (float)layout.CellSize, (float)layout.ColumnGutterHeight);
        }

        if (HighlightRow >= 0 && HighlightRow < layout.Rows)
        {
            var (_, y) = layout.CellOrigin(0, HighlightRow);
            canvas.FillRectangle(0, (float)y, (float)layout.RowGutterWidth, (float)layout.CellSize);
        }
    }

    private void DrawCellFills(ICanvas canvas, BoardLayout layout)
    {
        var cellSize = (float)layout.CellSize;
        var cornerRadius = Math.Max(1f, cellSize * 0.18f);

        for (var row = 0; row < layout.Rows; row++)
        {
            for (var column = 0; column < layout.Columns; column++)
            {
                var index = (row * layout.Columns) + column;
                var state = Cells[index];
                var (x, y) = layout.CellOrigin(column, row);
                var left = (float)x;
                var top = (float)y;

                canvas.FillColor = index == MistakeIndex
                    ? Palette.Warn
                    : state == CellState.Filled
                        ? Palette.CellFill
                        : column == HighlightColumn || row == HighlightRow
                            ? Palette.PrimarySoft
                            : Palette.CellEmpty;

                if (state == CellState.Filled || index == MistakeIndex)
                {
                    // Scale the cell around its centre during the fill animation.
                    var scale = index == PopIndex ? PopScale : 1f;
                    var size = (cellSize - (CellInset * 2)) * scale;
                    var offset = (cellSize - size) / 2f;

                    // Filled cells get rounded corners, which is what gives the finished
                    // picture its soft, blocky character.
                    canvas.FillRoundedRectangle(
                        left + offset,
                        top + offset,
                        size,
                        size,
                        cornerRadius * scale);
                }
                else
                {
                    canvas.FillRectangle(left, top, cellSize, cellSize);
                }
            }
        }
    }

    /// <summary>Draws one line per grid boundary, including group separators.</summary>
    private void DrawGridLines(ICanvas canvas, BoardLayout layout)
    {
        canvas.StrokeColor = Palette.CellLine;
        canvas.StrokeSize = CellBorderThickness;

        var left = (float)layout.RowGutterWidth;
        var top = (float)layout.ColumnGutterHeight;
        var right = (float)layout.TotalWidth;
        var bottom = (float)layout.TotalHeight;

        for (var column = 0; column <= layout.Columns; column++)
        {
            var x = left + (float)(column * layout.CellSize);
            canvas.DrawLine(x, top, x, bottom);
        }

        for (var row = 0; row <= layout.Rows; row++)
        {
            var y = top + (float)(row * layout.CellSize);
            canvas.DrawLine(left, y, right, y);
        }
    }

    /// <summary>Crosses - the few cells that carry something over the grid.</summary>
    private void DrawCellMarks(ICanvas canvas, BoardLayout layout)
    {
        var cellSize = (float)layout.CellSize;

        // Rounded ends make a cross look drawn rather than stamped. Set once: a nearly-finished
        // 25x25 carries several hundred of them.
        canvas.StrokeLineCap = LineCap.Round;

        for (var row = 0; row < layout.Rows; row++)
        {
            for (var column = 0; column < layout.Columns; column++)
            {
                var index = (row * layout.Columns) + column;

                if (Cells[index] != CellState.Crossed)
                {
                    continue;
                }

                var (x, y) = layout.CellOrigin(column, row);
                DrawCross(canvas, (float)x, (float)y, cellSize);
            }
        }

        canvas.StrokeLineCap = LineCap.Butt;
    }

    /// <summary>
    /// The gold ring around a hinted cell. Drawn after every other board layer: the ring sits
    /// on the cell boundary, so anything stroked there later - the group separators above all -
    /// would cut straight through it.
    /// </summary>
    private void DrawHintRing(ICanvas canvas, BoardLayout layout)
    {
        if (HintIndex < 0 || HintIndex >= Cells.Length || HintRingWidth <= 0.1f)
        {
            return;
        }

        var cellSize = (float)layout.CellSize;
        var cornerRadius = Math.Max(1f, cellSize * 0.18f);
        var column = HintIndex % layout.Columns;
        var row = HintIndex / layout.Columns;
        var (x, y) = layout.CellOrigin(column, row);

        // Width animated by the view: two quick pulses when granted, 3 at rest.
        var ring = HintRingWidth;
        canvas.StrokeColor = Palette.Gold;
        canvas.StrokeSize = ring;
        canvas.DrawRoundedRectangle(
            (float)x + (ring / 2f),
            (float)y + (ring / 2f),
            cellSize - ring,
            cellSize - ring,
            cornerRadius);
    }

    private void DrawCross(ICanvas canvas, float left, float top, float cellSize)
    {
        var centreX = left + (cellSize / 2f);
        var centreY = top + (cellSize / 2f);
        var reach = (float)(cellSize * CrossBarLengthRatio / 2);

        // Line cap is set once by DrawCellMarks, around the whole pass.
        canvas.StrokeColor = Palette.Ink2;
        canvas.StrokeSize = Math.Max(2f, (float)(cellSize * CrossBarThicknessRatio));

        canvas.DrawLine(centreX - reach, centreY - reach, centreX + reach, centreY + reach);
        canvas.DrawLine(centreX + reach, centreY - reach, centreX - reach, centreY + reach);
    }

    /// <summary>
    /// Heavier lines every five cells. Nonogram players count in fives, and without these a
    /// 25-wide row is genuinely hard to read.
    /// </summary>
    private void DrawGroupSeparators(ICanvas canvas, BoardLayout layout)
    {
        canvas.StrokeColor = Palette.GutterInk;
        canvas.StrokeSize = GroupSeparatorThickness;

        for (var column = 0; column < layout.Columns; column++)
        {
            if (!layout.IsGroupBoundaryAfterColumn(column))
            {
                continue;
            }

            var (x, _) = layout.CellOrigin(column, 0);
            var lineX = (float)(x + layout.CellSize);
            canvas.DrawLine(lineX, (float)layout.ColumnGutterHeight, lineX, (float)layout.TotalHeight);
        }

        for (var row = 0; row < layout.Rows; row++)
        {
            if (!layout.IsGroupBoundaryAfterRow(row))
            {
                continue;
            }

            var (_, y) = layout.CellOrigin(0, row);
            var lineY = (float)(y + layout.CellSize);
            canvas.DrawLine((float)layout.RowGutterWidth, lineY, (float)layout.TotalWidth, lineY);
        }
    }

    private void DrawColumnClues(ICanvas canvas, Puzzle puzzle, BoardLayout layout)
    {
        var fontSize = (float)layout.ClueFontSize();
        var slot = (float)layout.ClueSlot;

        // Size strike buffers from the current puzzle; equal-sized boards can have different clue counts.
        Span<bool> struck = stackalloc bool[Math.Max(4, puzzle.MaxColumnClueCount)];
        Span<CellState> column = stackalloc CellState[puzzle.Height];

        for (var x = 0; x < puzzle.Width; x++)
        {
            for (var y = 0; y < puzzle.Height; y++)
            {
                column[y] = Cells[(y * puzzle.Width) + x];
            }

            var clues = puzzle.ColumnClues[x];
            ClueStrikeCalculator.Compute(clues, column, struck);

            var runs = clues.DisplayRuns;
            var (cellX, _) = layout.CellOrigin(x, 0);

            // Bottom-aligned: clues sit against the grid so the eye travels straight down
            // from the last number into the column it describes.
            var firstSlotTop = layout.ColumnGutterHeight - (runs.Count * slot);

            for (var k = 0; k < runs.Count; k++)
            {
                DrawClueNumeral(
                    canvas,
                    runs[k],
                    struck[k],
                    StrikeProgress(isRow: false, x, k, struck[k]),
                    (float)cellX,
                    (float)(firstSlotTop + (k * slot)),
                    (float)layout.CellSize,
                    slot,
                    fontSize);
            }
        }
    }

    private void DrawRowClues(ICanvas canvas, Puzzle puzzle, BoardLayout layout)
    {
        var fontSize = (float)layout.ClueFontSize();
        var slot = (float)layout.ClueSlot;

        // From the puzzle for the same reason as the column pass.
        Span<bool> struck = stackalloc bool[Math.Max(4, puzzle.MaxRowClueCount)];

        for (var y = 0; y < puzzle.Height; y++)
        {
            var clues = puzzle.RowClues[y];
            ClueStrikeCalculator.Compute(clues, Cells.AsSpan(y * puzzle.Width, puzzle.Width), struck);

            var runs = clues.DisplayRuns;
            var (_, cellY) = layout.CellOrigin(0, y);

            // Right-aligned against the grid, for the same reason columns are bottom-aligned.
            var firstSlotLeft = layout.RowGutterWidth - (runs.Count * slot);

            for (var k = 0; k < runs.Count; k++)
            {
                DrawClueNumeral(
                    canvas,
                    runs[k],
                    struck[k],
                    StrikeProgress(isRow: true, y, k, struck[k]),
                    (float)(firstSlotLeft + (k * slot)),
                    (float)cellY,
                    slot,
                    (float)layout.CellSize,
                    fontSize);
            }
        }
    }

    private void DrawClueNumeral(
        ICanvas canvas,
        int value,
        bool isStruck,
        float strikeProgress,
        float left,
        float top,
        float width,
        float height,
        float fontSize)
    {
        // A blank line's clue is 0, which the prototype renders as nothing at all rather than
        // a literal zero - the empty gutter slot already says "no runs here".
        if (value == 0)
        {
            return;
        }

        // Font and size come from Draw, which sets them once for the whole gutter. Only the
        // colour and the fade differ from one numeral to the next.
        canvas.FontColor = isStruck ? Palette.Ink2 : Palette.GutterInk;
        canvas.Alpha = isStruck ? StruckClueOpacity : 1f;

        canvas.DrawString(
            value.ToString(System.Globalization.CultureInfo.CurrentCulture),
            left,
            top,
            width,
            height,
            HorizontalAlignment.Center,
            VerticalAlignment.Center);

        if (isStruck && strikeProgress > 0f)
        {
            // Use a line as well as opacity to mark completed clues. New strikes animate from the left.
            canvas.Alpha = 1f;
            canvas.StrokeColor = Palette.Warn;
            canvas.StrokeSize = Math.Max(1.5f, fontSize * (float)StrikeThicknessRatio);

            var inset = width * 0.14f;
            var fullLength = width - (inset * 2);
            var centreY = top + (height / 2f);
            canvas.DrawLine(left + inset, centreY, left + inset + (fullLength * strikeProgress), centreY);
        }

        canvas.Alpha = 1f;
    }
}
