using Squarebuzz.Core.Model;

namespace Squarebuzz.App.Drawing;

/// <summary>
/// Draws a finished picture in its own colour - the reward shown on completion, and the same
/// art the gallery uses.
/// </summary>
/// <remarks>
/// Interior cells are darkened slightly, exactly as the prototype did. Without it a solid shape
/// reads as one flat blob; the shading gives the picture an outline and makes it legible as a
/// fish or a dinosaur rather than a coloured rectangle.
/// </remarks>
public sealed class PuzzleArtDrawable : IDrawable
{
    private const float InteriorDarkening = 0.86f;

    public Puzzle? Puzzle { get; set; }

    /// <summary>
    /// When set, the player's own marks are drawn instead of the finished picture.
    /// </summary>
    /// <remarks>
    /// Used by the Continue list. Drawing the solution there would hand the child the answer -
    /// far more of a giveaway than the picture's name - whereas their own progress both
    /// identifies the game and gives away nothing.
    /// </remarks>
    public IReadOnlyList<CellState>? Marks { get; set; }

    /// <summary>
    /// Hides the picture completely, for gallery entries the player has not found yet.
    /// </summary>
    /// <remarks>
    /// Draws a full grid of blank tiles rather than the solution in a muted colour. Recolouring
    /// the real shape would leave the silhouette perfectly readable - a "???" caption over a
    /// recognisable heart tells the child both what the picture is and which cells to fill,
    /// which is the entire puzzle given away.
    /// </remarks>
    public bool IsMasked { get; set; }

    /// <summary>0 to 1. Cells appear progressively, giving the reveal its sweep.</summary>
    public double RevealProgress { get; set; } = 1;

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        ArgumentNullException.ThrowIfNull(canvas);

        if (Puzzle is not { } puzzle)
        {
            return;
        }

        var gap = 1f;
        var cell = Math.Min(
            (dirtyRect.Width - (gap * (puzzle.Width - 1))) / puzzle.Width,
            (dirtyRect.Height - (gap * (puzzle.Height - 1))) / puzzle.Height);

        if (cell <= 0)
        {
            return;
        }

        var step = cell + gap;
        var offsetX = dirtyRect.X + ((dirtyRect.Width - ((puzzle.Width * step) - gap)) / 2);
        var offsetY = dirtyRect.Y + ((dirtyRect.Height - ((puzzle.Height * step) - gap)) / 2);
        var radius = Math.Max(1f, cell * 0.14f);

        var artColour = IsMasked
            ? Resolve("Sunk", "#F6E7D0")
            : Color.FromArgb(puzzle.ColorHex);

        var interiorColour = Darken(artColour, InteriorDarkening);
        var revealCutoff = puzzle.CellCount * Math.Clamp(RevealProgress, 0, 1);

        for (var y = 0; y < puzzle.Height; y++)
        {
            for (var x = 0; x < puzzle.Width; x++)
            {
                var index = puzzle.IndexOf(x, y);

                // Masked cards fill every cell, so the grid carries no information about the
                // shape hiding behind it.
                var isDrawn = IsMasked
                              || (Marks is { } marks
                                  ? index < marks.Count && marks[index] == CellState.Filled
                                  : puzzle.IsFilled(x, y));

                if (!isDrawn || index > revealCutoff)
                {
                    continue;
                }

                // Interior shading needs the true shape, so it only applies when drawing the
                // finished picture. Partial progress and masked cards have no interior.
                canvas.FillColor = !IsMasked && Marks is null && IsInterior(puzzle, x, y)
                    ? interiorColour
                    : artColour;
                canvas.FillRoundedRectangle(offsetX + (x * step), offsetY + (y * step), cell, cell, radius);
            }
        }
    }

    /// <summary>A cell with filled neighbours on all four sides, so it is not part of the outline.</summary>
    private static bool IsInterior(Puzzle puzzle, int x, int y)
    {
        return y > 0 && puzzle.IsFilled(x, y - 1)
               && y < puzzle.Height - 1 && puzzle.IsFilled(x, y + 1)
               && x > 0 && puzzle.IsFilled(x - 1, y)
               && x < puzzle.Width - 1 && puzzle.IsFilled(x + 1, y);
    }

    private static Color Darken(Color color, float factor) =>
        new(color.Red * factor, color.Green * factor, color.Blue * factor, color.Alpha);

    private static Color Resolve(string key, string fallbackHex)
    {
        if (Application.Current?.Resources.TryGetValue(key, out var value) == true && value is Color color)
        {
            return color;
        }

        return Color.FromArgb(fallbackHex);
    }
}
