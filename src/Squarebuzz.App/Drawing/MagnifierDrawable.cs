using Squarebuzz.Core.Model;

namespace Squarebuzz.App.Drawing;

/// <summary>
/// Draws a magnified 3x3 window onto the board, centred on the cell under the finger.
/// </summary>
/// <remarks>
/// At the 17-unit minimum cell size a 25x25 board puts cells right at the limit of what a child
/// can aim at, and the finger covers the one being touched. This shows what is actually under it.
/// Cells beyond the edge of the grid are drawn sunk, so the player can see they have reached a
/// border rather than wondering why nothing is filling.
/// </remarks>
public sealed class MagnifierDrawable : IDrawable
{
    private const int Window = 3;

    public Puzzle? Puzzle { get; set; }

    public CellState[] Cells { get; set; } = [];

    /// <summary>Row-major index of the centre cell, or -1 when nothing is being touched.</summary>
    public int CenterIndex { get; set; } = -1;

    public BoardPalette Palette { get; set; } = BoardPalette.FromResources();

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        ArgumentNullException.ThrowIfNull(canvas);

        if (Puzzle is not { } puzzle || CenterIndex < 0 || Cells.Length != puzzle.CellCount)
        {
            return;
        }

        var centerColumn = CenterIndex % puzzle.Width;
        var centerRow = CenterIndex / puzzle.Width;

        var cell = Math.Min(dirtyRect.Width, dirtyRect.Height) / Window;
        if (cell <= 0)
        {
            return;
        }

        var originX = dirtyRect.X + ((dirtyRect.Width - (cell * Window)) / 2);
        var originY = dirtyRect.Y + ((dirtyRect.Height - (cell * Window)) / 2);
        var radius = Math.Max(1f, cell * 0.16f);

        for (var dy = -1; dy <= 1; dy++)
        {
            for (var dx = -1; dx <= 1; dx++)
            {
                var column = centerColumn + dx;
                var row = centerRow + dy;
                var left = originX + ((dx + 1) * cell);
                var top = originY + ((dy + 1) * cell);

                var isInside = column >= 0 && column < puzzle.Width && row >= 0 && row < puzzle.Height;
                var state = isInside ? Cells[(row * puzzle.Width) + column] : CellState.Empty;

                canvas.FillColor = !isInside
                    ? Palette.Sunk
                    : state == CellState.Filled
                        ? Palette.CellFill
                        : Palette.CellEmpty;

                canvas.FillRectangle(left, top, cell, cell);

                canvas.StrokeColor = Palette.CellLine;
                canvas.StrokeSize = 1f;
                canvas.DrawRectangle(left, top, cell, cell);

                if (isInside && state == CellState.Crossed)
                {
                    DrawCross(canvas, left, top, cell);
                }

                // Ring the centre cell so it is unmistakable which one the finger is on.
                if (dx == 0 && dy == 0)
                {
                    canvas.StrokeColor = Palette.Primary;
                    canvas.StrokeSize = 3f;
                    canvas.DrawRoundedRectangle(left + 1.5f, top + 1.5f, cell - 3f, cell - 3f, radius);
                }
            }
        }
    }

    private void DrawCross(ICanvas canvas, float left, float top, float cell)
    {
        var centreX = left + (cell / 2f);
        var centreY = top + (cell / 2f);
        var reach = cell * 0.27f;

        canvas.StrokeColor = Palette.Ink2;
        canvas.StrokeSize = Math.Max(2f, cell * 0.13f);
        canvas.StrokeLineCap = LineCap.Round;

        canvas.DrawLine(centreX - reach, centreY - reach, centreX + reach, centreY + reach);
        canvas.DrawLine(centreX + reach, centreY - reach, centreX - reach, centreY + reach);

        canvas.StrokeLineCap = LineCap.Butt;
    }
}
