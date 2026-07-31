using Squarebuzz.Core.Clues;
using Squarebuzz.Core.Layout;
using Squarebuzz.Core.Model;

namespace Squarebuzz.App.Drawing;

/// <summary>
/// Draws the whole nonogram - clue gutters, cells, crosses and group separators - onto a single
/// canvas.
/// </summary>
/// <remarks>
/// One view for the entire board, rather than a view per cell. At 25x25 the per-cell approach
/// would mean 625 live views with 625 bindings; here it is one <c>Invalidate()</c> and a few
/// hundred fill calls, which is what keeps drag-painting smooth on a mid-range phone.
/// </remarks>
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
    /// The clue face. "BodyBold" is the alias registered in <c>MauiProgram</c>; a canvas resolves
    /// it through the same font registry the XAML styles use, so there is one source of truth.
    /// </summary>
    private static readonly Microsoft.Maui.Graphics.Font ClueFont = new("BodyBold");

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

    public bool BigNumbers { get; set; }

    /// <summary>How long a clue strike takes to wipe across the number.</summary>
    private const double StrikeWipeMilliseconds = 180;

    /// <summary>
    /// When each clue strike first appeared, keyed by line and run, so a newly satisfied clue
    /// wipes its line in over <see cref="StrikeWipeMilliseconds"/> instead of snapping. Strikes
    /// present when a board is first drawn - a resumed save - are seeded as ancient, because a
    /// restore is not news.
    /// </summary>
    private readonly Dictionary<long, long> _strikeBirths = [];

    private bool _seedStrikesSilently = true;

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

        return (float)Math.Min(1.0, (Environment.TickCount64 - birth) / StrikeWipeMilliseconds);
    }

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        ArgumentNullException.ThrowIfNull(canvas);

        if (Puzzle is not { } puzzle || Cells.Length != puzzle.CellCount)
        {
            return;
        }

        var layout = Layout;

        DrawGutterBackgrounds(canvas, layout);
        DrawCells(canvas, puzzle, layout);
        DrawGroupSeparators(canvas, layout);
        DrawColumnClues(canvas, puzzle, layout);
        DrawRowClues(canvas, puzzle, layout);

        // Everything struck during the first draw of a board has now been seeded as ancient;
        // from here on a new strike is genuinely new and earns its wipe.
        _seedStrikesSilently = false;
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

    private void DrawCells(ICanvas canvas, Puzzle puzzle, BoardLayout layout)
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
                    // A cell mid-pop is drawn scaled around its own centre - small, overshoot,
                    // settle - which is what makes a mark feel placed rather than switched on.
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

                canvas.StrokeColor = Palette.CellLine;
                canvas.StrokeSize = CellBorderThickness;
                canvas.DrawRectangle(left, top, cellSize, cellSize);

                if (state == CellState.Crossed)
                {
                    DrawCross(canvas, left, top, cellSize);
                }

                if (index == HintIndex && HintRingWidth > 0.1f)
                {
                    // Width animated by the view: two quick pulses when granted, 3 at rest.
                    var ring = HintRingWidth;
                    canvas.StrokeColor = Palette.Gold;
                    canvas.StrokeSize = ring;
                    canvas.DrawRoundedRectangle(
                        left + (ring / 2f),
                        top + (ring / 2f),
                        cellSize - ring,
                        cellSize - ring,
                        cornerRadius);
                }
            }
        }
    }

    private void DrawCross(ICanvas canvas, float left, float top, float cellSize)
    {
        var centreX = left + (cellSize / 2f);
        var centreY = top + (cellSize / 2f);
        var reach = (float)(cellSize * CrossBarLengthRatio / 2);

        canvas.StrokeColor = Palette.Ink2;
        canvas.StrokeSize = Math.Max(2f, (float)(cellSize * CrossBarThicknessRatio));
        canvas.StrokeLineCap = LineCap.Round;

        canvas.DrawLine(centreX - reach, centreY - reach, centreX + reach, centreY + reach);
        canvas.DrawLine(centreX + reach, centreY - reach, centreX - reach, centreY + reach);

        canvas.StrokeLineCap = LineCap.Butt;
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
        var fontSize = (float)layout.ClueFontSize(BigNumbers);
        var slot = (float)layout.ClueSlot;
        Span<bool> struck = stackalloc bool[Math.Max(4, layout.MaxColumnClues)];
        Span<CellState> column = stackalloc CellState[layout.Rows];

        for (var x = 0; x < layout.Columns; x++)
        {
            for (var y = 0; y < layout.Rows; y++)
            {
                column[y] = Cells[(y * layout.Columns) + x];
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
        var fontSize = (float)layout.ClueFontSize(BigNumbers);
        var slot = (float)layout.ClueSlot;
        Span<bool> struck = stackalloc bool[Math.Max(4, layout.MaxRowClues)];

        for (var y = 0; y < layout.Rows; y++)
        {
            var clues = puzzle.RowClues[y];
            ClueStrikeCalculator.Compute(clues, Cells.AsSpan(y * layout.Columns, layout.Columns), struck);

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

        // Clue numbers are the one place text is drawn rather than laid out, so the family has to
        // be set here too - a canvas does not inherit the XAML styles. Bold, because these are the
        // most-read characters in the game and they sit on a tinted gutter.
        canvas.Font = ClueFont;
        canvas.FontSize = fontSize;
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
            // A line through the number, so "done" is not conveyed by opacity alone - it has
            // to survive the colour-blind palette and a washed-out screen in sunlight. A new
            // strike wipes in from the left; progress is 1 for anything already settled.
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
