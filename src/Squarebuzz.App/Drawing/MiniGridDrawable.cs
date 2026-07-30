namespace Squarebuzz.App.Drawing;

/// <summary>
/// A small teaching diagram: a few cells with their row clues, used by How to Play.
/// </summary>
/// <remarks>
/// Drawn rather than assembled from views because each diagram is a fixed picture, not an
/// interactive board. Patterns use the prototype's own characters: <c>#</c> filled, <c>x</c>
/// crossed, <c>o</c> highlighted (the cells a deduction has just proved), <c>.</c> untouched.
/// </remarks>
public sealed class MiniGridDrawable : IDrawable
{
    private const float ClueColumnRatio = 1.6f;

    /// <summary>Rows of the diagram, one string per row.</summary>
    public IReadOnlyList<string> Rows { get; set; } = [];

    /// <summary>Clue text for each row, e.g. "2 1". Empty strings are allowed.</summary>
    public IReadOnlyList<string> RowClues { get; set; } = [];

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        ArgumentNullException.ThrowIfNull(canvas);

        if (Rows.Count == 0 || Rows[0].Length == 0)
        {
            return;
        }

        var columns = Rows.Max(r => r.Length);
        var palette = BoardPalette.FromResources();

        // The clue column is sized in whole cells so the grid stays square.
        var cell = Math.Min(
            dirtyRect.Width / (columns + ClueColumnRatio),
            dirtyRect.Height / Rows.Count);

        if (cell <= 0)
        {
            return;
        }

        var clueWidth = cell * ClueColumnRatio;
        var totalWidth = clueWidth + (columns * cell);
        var originX = dirtyRect.X + ((dirtyRect.Width - totalWidth) / 2);
        var originY = dirtyRect.Y + ((dirtyRect.Height - (Rows.Count * cell)) / 2);
        var radius = Math.Max(1f, cell * 0.16f);
        var fontSize = Math.Max(8f, cell * 0.5f);

        for (var row = 0; row < Rows.Count; row++)
        {
            var top = originY + (row * cell);

            DrawRowClue(canvas, palette, row, originX, top, clueWidth, cell, fontSize);

            for (var column = 0; column < Rows[row].Length; column++)
            {
                var left = originX + clueWidth + (column * cell);
                var symbol = Rows[row][column];

                canvas.FillColor = symbol switch
                {
                    '#' => palette.CellFill,
                    'o' => palette.PrimarySoft,
                    _ => palette.CellEmpty,
                };

                if (symbol == '#')
                {
                    canvas.FillRoundedRectangle(left, top, cell, cell, radius);
                }
                else
                {
                    canvas.FillRectangle(left, top, cell, cell);
                }

                canvas.StrokeColor = palette.CellLine;
                canvas.StrokeSize = 1f;
                canvas.DrawRectangle(left, top, cell, cell);

                if (symbol == 'x')
                {
                    DrawCross(canvas, palette, left, top, cell);
                }
            }
        }
    }

    private void DrawRowClue(
        ICanvas canvas,
        BoardPalette palette,
        int row,
        float originX,
        float top,
        float clueWidth,
        float cell,
        float fontSize)
    {
        if (row >= RowClues.Count || string.IsNullOrEmpty(RowClues[row]))
        {
            return;
        }

        canvas.FontSize = fontSize;
        canvas.FontColor = palette.GutterInk;

        // Right-aligned against the grid, matching the real board's gutter.
        canvas.DrawString(
            RowClues[row],
            originX,
            top,
            clueWidth - 4,
            cell,
            HorizontalAlignment.Right,
            VerticalAlignment.Center);
    }

    private static void DrawCross(ICanvas canvas, BoardPalette palette, float left, float top, float cell)
    {
        var centreX = left + (cell / 2f);
        var centreY = top + (cell / 2f);
        var reach = cell * 0.27f;

        canvas.StrokeColor = palette.Ink2;
        canvas.StrokeSize = Math.Max(1.5f, cell * 0.12f);
        canvas.StrokeLineCap = LineCap.Round;

        canvas.DrawLine(centreX - reach, centreY - reach, centreX + reach, centreY + reach);
        canvas.DrawLine(centreX + reach, centreY - reach, centreX - reach, centreY + reach);

        canvas.StrokeLineCap = LineCap.Butt;
    }
}
