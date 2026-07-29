using Squarebuzz.Core.Model;

namespace Squarebuzz.Core.Generation;

/// <summary>
/// Scatters overlapping circular blobs across the grid and mirrors the left half onto the
/// right, producing symmetrical creature-like shapes. Ported from the prototype's
/// <c>PuzzleGen.stub</c>, including its constants, so generated pictures keep the same
/// character as the design.
/// </summary>
/// <remarks>
/// This generator makes no claim about solvability - a raw blob grid is often ambiguous.
/// Wrap it in <see cref="UniqueSolutionGenerator"/> before handing anything to a player.
/// </remarks>
public sealed class BlobPuzzleGenerator : IPuzzleGenerator
{
    private const string GeneratedColorHex = "#8B5CF6";

    public Puzzle Generate(PuzzleRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(request.Width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(request.Height);

        var width = request.Width;
        var height = request.Height;
        var random = new DeterministicRandom(request.Seed);

        // Higher difficulty means a sparser picture, which leaves shorter clues and so
        // fewer easy deductions.
        var density = 0.62 - (request.Difficulty * 0.045);
        var cells = new bool[width * height];
        var blobCount = Math.Max(3, (int)Math.Round(width * height / 26.0));

        for (var blob = 0; blob < blobCount; blob++)
        {
            var centreX = random.Next(width);
            var centreY = random.Next(height);
            var radius = 1 + random.Next(Math.Max(2, width / 5));
            var radiusSquared = radius * radius;

            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var dx = x - centreX;
                    var dy = y - centreY;

                    if ((dx * dx) + (dy * dy) <= radiusSquared && random.NextDouble() < density + 0.2)
                    {
                        cells[(y * width) + x] = true;
                    }
                }
            }
        }

        MirrorLeftHalfOntoRight(cells, width, height);
        FillEmptyLines(cells, width, height);

        return Puzzle.FromSolution(
            "generated",
            request.Pack,
            GeneratedColorHex,
            width,
            height,
            cells);
    }

    /// <summary>Vertical symmetry is what makes the blobs read as creatures rather than noise.</summary>
    private static void MirrorLeftHalfOntoRight(bool[] cells, int width, int height)
    {
        var half = width / 2;

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < half; x++)
            {
                cells[(y * width) + (width - 1 - x)] = cells[(y * width) + x];
            }
        }
    }

    /// <summary>
    /// A completely blank row or column is legal nonogram-wise but reads as a mistake in the
    /// picture, so each one gets a cell in the middle.
    /// </summary>
    /// <remarks>
    /// Each repair is mirrored, because this runs after
    /// <see cref="MirrorLeftHalfOntoRight"/> and would otherwise undo the symmetry it just
    /// established. On odd widths the mirror of the centre column is itself, so a single cell
    /// is added; on even widths the two centre cells are added as a pair. (The prototype
    /// wrote only one cell and so produced subtly lop-sided pictures at 20x20.)
    /// </remarks>
    private static void FillEmptyLines(bool[] cells, int width, int height)
    {
        var centreX = width / 2;
        var mirrorX = width - 1 - centreX;

        for (var y = 0; y < height; y++)
        {
            if (!RowHasFilledCell(cells, width, y))
            {
                cells[(y * width) + centreX] = true;
                cells[(y * width) + mirrorX] = true;
            }
        }

        var centreY = height / 2;

        for (var x = 0; x < width; x++)
        {
            if (!ColumnHasFilledCell(cells, width, height, x))
            {
                cells[(centreY * width) + x] = true;
                cells[(centreY * width) + (width - 1 - x)] = true;
            }
        }
    }

    private static bool RowHasFilledCell(bool[] cells, int width, int y)
    {
        for (var x = 0; x < width; x++)
        {
            if (cells[(y * width) + x])
            {
                return true;
            }
        }

        return false;
    }

    private static bool ColumnHasFilledCell(bool[] cells, int width, int height, int x)
    {
        for (var y = 0; y < height; y++)
        {
            if (cells[(y * width) + x])
            {
                return true;
            }
        }

        return false;
    }
}
