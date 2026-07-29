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

    /// <summary>Hides the picture behind placeholder blocks, for unsolved gallery entries.</summary>
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

                if (!puzzle.IsFilled(x, y) || index > revealCutoff)
                {
                    continue;
                }

                canvas.FillColor = IsInterior(puzzle, x, y) ? interiorColour : artColour;
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
