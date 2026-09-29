using Squarebuzz.Core.Model;

namespace Squarebuzz.Core.Generation;

/// <summary>Builds a mirrored blob shape, then adds asymmetric details.</summary>
/// <remarks>
/// Raw candidates may be ambiguous. Use UniqueSolutionGenerator for playable puzzles.
/// </remarks>
public sealed class BlobPuzzleGenerator : IPuzzleGenerator
{
    private const string GeneratedColorHex = "#8B5CF6";

    /// <summary>Probability of filling a cell within a blob. High solidity limits isolated cells and short clues.</summary>
    private const double BlobSolidity = 0.85;

    public Puzzle Generate(PuzzleRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(request.Width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(request.Height);

        // Validate difficulty before using it to calculate density.
        ArgumentOutOfRangeException.ThrowIfLessThan(request.Difficulty, PuzzleRequest.MinDifficulty);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(request.Difficulty, PuzzleRequest.MaxDifficulty);

        var width = request.Width;
        var height = request.Height;
        var random = new DeterministicRandom(request.Seed);

        // Higher difficulty means a sparser picture, which leaves shorter clues and so
        // fewer easy deductions.
        var targetFill = 0.62 - (request.Difficulty * 0.045);
        var cells = new bool[width * height];

        // Only the left half is generated; the right is mirrored from it. Measuring the target
        // against the half rather than the whole grid is what makes the density predictable,
        // since mirroring copies whatever the half ended up with.
        var halfWidth = (width + 1) / 2;
        var wanted = (int)Math.Round(halfWidth * height * targetFill);
        var maxRadius = Math.Max(2, width / 5);

        // Visit shuffled left-half centres to spread blobs across the grid before repairing empty lines.
        var centres = ShuffledHalfCells(halfWidth, height, random);

        // Add blobs to the target density. Limit the last radius by the remaining cells to reduce overshoot.
        var filled = 0;

        for (var i = 0; i < centres.Length && filled < wanted; i++)
        {
            var (centreX, centreY) = centres[i];
            var radius = 1 + random.Next(RadiusCapFor(wanted - filled, maxRadius));

            filled += StampBlob(cells, width, height, halfWidth, centreX, centreY, radius, random);
        }

        MirrorLeftHalfOntoRight(cells, width, height);
        BreakSymmetry(cells, width, height, request.Difficulty, random);
        FillEmptyLines(cells, width, height);

        return Puzzle.FromSolution(
            "generated",
            request.Pack,
            GeneratedColorHex,
            width,
            height,
            cells);
    }

    /// <summary>Every cell of the left half, in a seed-determined random order.</summary>
    private static (int X, int Y)[] ShuffledHalfCells(int halfWidth, int height, DeterministicRandom random)
    {
        var cells = new (int X, int Y)[halfWidth * height];

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < halfWidth; x++)
            {
                cells[(y * halfWidth) + x] = (x, y);
            }
        }

        // Fisher-Yates, so the order is a genuine permutation rather than a biased shuffle.
        for (var i = cells.Length - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (cells[i], cells[j]) = (cells[j], cells[i]);
        }

        return cells;
    }

    /// <summary>Estimates the largest blob radius that fits the remaining cell budget.</summary>
    /// <remarks>
    /// The estimate uses pi*r² and BlobSolidity. Clipping and random fill allow small deviations.
    /// </remarks>
    private static int RadiusCapFor(int remaining, int maxRadius)
    {
        var affordable = (int)Math.Floor(Math.Sqrt(remaining / (Math.PI * BlobSolidity)));

        return Math.Clamp(affordable, 1, maxRadius);
    }

    /// <summary>Stamps a blob and returns the number of newly filled left-half cells.</summary>
    /// <remarks>
    /// The right half is replaced by mirroring, so it is excluded from the count.
    /// </remarks>
    private static int StampBlob(
        bool[] cells,
        int width,
        int height,
        int halfWidth,
        int centreX,
        int centreY,
        int radius,
        DeterministicRandom random)
    {
        var radiusSquared = radius * radius;
        var added = 0;

        // Only the rows and columns the blob can actually reach, rather than the whole grid.
        var fromY = Math.Max(0, centreY - radius);
        var toY = Math.Min(height - 1, centreY + radius);
        var fromX = Math.Max(0, centreX - radius);
        var toX = Math.Min(width - 1, centreX + radius);

        for (var y = fromY; y <= toY; y++)
        {
            for (var x = fromX; x <= toX; x++)
            {
                var dx = x - centreX;
                var dy = y - centreY;

                if ((dx * dx) + (dy * dy) > radiusSquared || random.NextDouble() >= BlobSolidity)
                {
                    continue;
                }

                var index = (y * width) + x;

                if (cells[index])
                {
                    continue;
                }

                cells[index] = true;

                if (x < halfWidth)
                {
                    added++;
                }
            }
        }

        return added;
    }


    /// <summary>
    /// Share of the grid rewritten asymmetrically after mirroring, at the easiest difficulty and
    /// per difficulty step above it.
    /// </summary>
    private const double AsymmetryBase = 0.04;
    private const double AsymmetryPerDifficulty = 0.02;

    /// <summary>Adds asymmetric details while balancing added and removed cells.</summary>
    /// <remarks>
    /// The detail budget increases with difficulty. Only cells whose value changes count towards it.
    /// </remarks>
    private static void BreakSymmetry(bool[] cells, int width, int height, int difficulty, DeterministicRandom random)
    {
        var wanted = (int)Math.Round(cells.Length * (AsymmetryBase + (AsymmetryPerDifficulty * (difficulty - 1))));
        var maxRadius = Math.Max(1, width / 8);
        var changed = 0;
        var net = 0;

        // Bounded so a pathological grid (all full or all empty) cannot spin forever.
        for (var attempt = 0; attempt < cells.Length && changed < wanted; attempt++)
        {
            var centreX = random.Next(width);
            var centreY = random.Next(height);
            var radius = 1 + random.Next(maxRadius);

            // Carve whenever more has been added than taken away, so density holds its target.
            var fill = net <= 0;

            // Start on the edge of the shape: an addition grows out of it and a carving bites
            // into it. Either one floating in open space would only add speckle.
            if (cells[(centreY * width) + centreX] == fill || !HasNeighbour(cells, width, height, centreX, centreY, fill))
            {
                continue;
            }

            var stamped = StampDetail(cells, width, height, centreX, centreY, radius, fill);
            changed += stamped;
            net += fill ? stamped : -stamped;
        }
    }

    /// <summary>Whether the cell is on the grid and filled.</summary>
    private static bool IsFilled(bool[] cells, int width, int height, int x, int y) =>
        x >= 0 && x < width && y >= 0 && y < height && cells[(y * width) + x];

    /// <summary>Whether any orthogonal neighbour of the cell holds <paramref name="value"/>.</summary>
    private static bool HasNeighbour(bool[] cells, int width, int height, int x, int y, bool value) =>
        (x > 0 && cells[(y * width) + x - 1] == value)
        || (x < width - 1 && cells[(y * width) + x + 1] == value)
        || (y > 0 && cells[((y - 1) * width) + x] == value)
        || (y < height - 1 && cells[((y + 1) * width) + x] == value);

    /// <summary>Writes <paramref name="value"/> over a small blob and returns the changed-cell count.</summary>
    /// <remarks>
    /// Each edit extends or shortens an existing run. Additions must touch the shape; carvings
    /// must not split a run along either axis.
    /// </remarks>
    private static int StampDetail(
        bool[] cells,
        int width,
        int height,
        int centreX,
        int centreY,
        int radius,
        bool value)
    {
        var radiusSquared = radius * radius;
        var changed = 0;

        for (var y = Math.Max(0, centreY - radius); y <= Math.Min(height - 1, centreY + radius); y++)
        {
            for (var x = Math.Max(0, centreX - radius); x <= Math.Min(width - 1, centreX + radius); x++)
            {
                var dx = x - centreX;
                var dy = y - centreY;

                if ((dx * dx) + (dy * dy) > radiusSquared)
                {
                    continue;
                }

                var index = (y * width) + x;

                if (cells[index] == value)
                {
                    continue;
                }

                var keepsRuns = value
                    ? HasNeighbour(cells, width, height, x, y, true)
                    : !(IsFilled(cells, width, height, x - 1, y) && IsFilled(cells, width, height, x + 1, y))
                      && !(IsFilled(cells, width, height, x, y - 1) && IsFilled(cells, width, height, x, y + 1));

                if (keepsRuns)
                {
                    cells[index] = value;
                    changed++;
                }
            }
        }

        return changed;
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

    /// <summary>Fills empty lines by extending nearby occupied rows or columns.</summary>
    /// <remarks>
    /// Repairs are mirrored at this stage. Prefer positions attached to the body to avoid
    /// creating a vertical stripe down the centre.
    /// </remarks>
    private static void FillEmptyLines(bool[] cells, int width, int height)
    {
        for (var y = 0; y < height; y++)
        {
            if (RowHasFilledCell(cells, width, y))
            {
                continue;
            }

            var x = ColumnToContinue(cells, width, height, y);

            cells[(y * width) + x] = true;
            cells[(y * width) + (width - 1 - x)] = true;
        }

        for (var x = 0; x < width; x++)
        {
            if (ColumnHasFilledCell(cells, width, height, x))
            {
                continue;
            }

            var y = RowToContinue(cells, width, height, x);

            cells[(y * width) + x] = true;
            cells[(y * width) + (width - 1 - x)] = true;
        }
    }

    /// <summary>
    /// A column that is filled in the row nearest <paramref name="emptyY"/>, preferring one close
    /// to the middle so the repair attaches to the body rather than to an outlying limb.
    /// </summary>
    private static int ColumnToContinue(bool[] cells, int width, int height, int emptyY)
    {
        var centreX = width / 2;

        for (var distance = 1; distance < height; distance++)
        {
            // Above first, then below - the order the candidates have always been tried in, which
            // decides the picture. A loop rather than a two-element array per step.
            for (var side = -1; side <= 1; side += 2)
            {
                var y = emptyY + (side * distance);

                if (y < 0 || y >= height || !RowHasFilledCell(cells, width, y))
                {
                    continue;
                }

                var best = -1;

                // Search the left half only: the right is its mirror, so a choice there would be
                // the same cell reflected and the tie-break would depend on iteration order.
                for (var x = 0; x <= centreX && x < width; x++)
                {
                    if (cells[(y * width) + x] && (best < 0 || Math.Abs(x - centreX) < Math.Abs(best - centreX)))
                    {
                        best = x;
                    }
                }

                if (best >= 0)
                {
                    return best;
                }
            }
        }

        // Nothing filled anywhere, which the fill loop makes impossible in practice.
        return centreX;
    }

    /// <summary>The row equivalent of <see cref="ColumnToContinue"/>, for an empty column.</summary>
    private static int RowToContinue(bool[] cells, int width, int height, int emptyX)
    {
        var centreY = height / 2;

        for (var distance = 1; distance < width; distance++)
        {
            // Left first, then right, as in ColumnToContinue.
            for (var side = -1; side <= 1; side += 2)
            {
                var x = emptyX + (side * distance);

                if (x < 0 || x >= width || !ColumnHasFilledCell(cells, width, height, x))
                {
                    continue;
                }

                var best = -1;

                for (var y = 0; y < height; y++)
                {
                    if (cells[(y * width) + x] && (best < 0 || Math.Abs(y - centreY) < Math.Abs(best - centreY)))
                    {
                        best = y;
                    }
                }

                if (best >= 0)
                {
                    return best;
                }
            }
        }

        return centreY;
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
