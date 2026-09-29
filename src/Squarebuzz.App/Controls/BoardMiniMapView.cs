using Squarebuzz.Core.Model;

namespace Squarebuzz.App.Controls;

/// <summary>
/// A postage-stamp overview of a big board: every mark as a pixel, plus a rectangle showing
/// which part of the board the scroll window is looking at.
/// </summary>
/// <remarks>
/// Exists for the 15x15-and-up grids on a phone, where the board is bigger than its viewport
/// and the player scrolls - without this, "where am I?" costs a scroll to the edge and back.
/// The host shows it only in that situation; on a tablet the whole board fits and a map of it
/// would show nothing new. Decorative and untouchable: screen readers already have the board
/// summary, and a stray finger on the map must not paint.
/// </remarks>
public sealed class BoardMiniMapView : GraphicsView
{
    private readonly MiniMapDrawable _drawable = new();

    public BoardMiniMapView()
    {
        Drawable = _drawable;
        InputTransparent = true;
    }

    /// <summary>Re-reads the marks. Called by the host after every move.</summary>
    public void UpdateCells(GameSession? session)
    {
        _drawable.Columns = session?.Puzzle.Width ?? 0;
        _drawable.Rows = session?.Puzzle.Height ?? 0;
        // Copied into a reused buffer: this runs after every move.
        ReadOnlySpan<CellState> cells = session is null ? [] : session.Cells;
        if (_drawable.Cells.Length != cells.Length)
        {
            _drawable.Cells = new CellState[cells.Length];
        }

        cells.CopyTo(_drawable.Cells);
        Invalidate();
    }

    /// <summary>The visible window, as fractions of the whole board (0..1 each).</summary>
    public void UpdateViewport(double x, double y, double width, double height)
    {
        _drawable.ViewportX = (float)Math.Clamp(x, 0, 1);
        _drawable.ViewportY = (float)Math.Clamp(y, 0, 1);
        _drawable.ViewportWidth = (float)Math.Clamp(width, 0, 1);
        _drawable.ViewportHeight = (float)Math.Clamp(height, 0, 1);
        Invalidate();
    }

    private sealed class MiniMapDrawable : IDrawable
    {
        public int Columns { get; set; }

        public int Rows { get; set; }

        public CellState[] Cells { get; set; } = [];

        public float ViewportX { get; set; }

        public float ViewportY { get; set; }

        public float ViewportWidth { get; set; } = 1f;

        public float ViewportHeight { get; set; } = 1f;

        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            ArgumentNullException.ThrowIfNull(canvas);

            if (Columns == 0 || Rows == 0 || Cells.Length != Columns * Rows)
            {
                return;
            }

            canvas.FillColor = Resolve("Surface", "#FFFFFF").WithAlpha(0.92f);
            canvas.FillRoundedRectangle(dirtyRect, 8f);

            canvas.StrokeColor = Resolve("Line", "#F0E2CE");
            canvas.StrokeSize = 1.5f;
            canvas.DrawRoundedRectangle(dirtyRect, 8f);

            const float pad = 4f;
            var innerWidth = dirtyRect.Width - (pad * 2);
            var innerHeight = dirtyRect.Height - (pad * 2);
            var cell = Math.Min(innerWidth / Columns, innerHeight / Rows);

            var left = dirtyRect.X + ((dirtyRect.Width - (cell * Columns)) / 2f);
            var top = dirtyRect.Y + ((dirtyRect.Height - (cell * Rows)) / 2f);

            canvas.FillColor = Resolve("Primary", "#FF8A3D");

            for (var row = 0; row < Rows; row++)
            {
                for (var column = 0; column < Columns; column++)
                {
                    if (Cells[(row * Columns) + column] != CellState.Filled)
                    {
                        continue;
                    }

                    canvas.FillRectangle(
                        left + (column * cell),
                        top + (row * cell),
                        Math.Max(1f, cell - 0.5f),
                        Math.Max(1f, cell - 0.5f));
                }
            }

            // The scroll window, so the map answers "where am I", not just "how is it going".
            canvas.StrokeColor = Resolve("Ink", "#332E42");
            canvas.StrokeSize = 1.5f;
            canvas.DrawRectangle(
                left + (ViewportX * cell * Columns),
                top + (ViewportY * cell * Rows),
                ViewportWidth * cell * Columns,
                ViewportHeight * cell * Rows);
        }

        private static Color Resolve(string key, string fallbackHex)
        {
            if (Application.Current?.Resources.TryGetValue(key, out var value) == true && value is Color color)
            {
                return color;
            }

            return Color.FromArgb(fallbackHex);
        }
    }
}
