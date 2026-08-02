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

    /// <summary>
    /// Chance that a cell inside a blob is actually filled, which keeps blob edges organic
    /// rather than perfectly circular.
    /// </summary>
    /// <remarks>
    /// High on purpose. The prototype thinned blobs by roughly a third, which speckles the
    /// picture with isolated cells - and an isolated cell is a clue run of 1, so a speckled
    /// picture is also a noisy, tedious set of clues.
    /// </remarks>
    private const double BlobSolidity = 0.85;

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
        var targetFill = 0.62 - (request.Difficulty * 0.045);
        var cells = new bool[width * height];

        // Only the left half is generated; the right is mirrored from it. Measuring the target
        // against the half rather than the whole grid is what makes the density predictable,
        // since mirroring copies whatever the half ended up with.
        var halfWidth = (width + 1) / 2;
        var wanted = (int)Math.Round(halfWidth * height * targetFill);
        var maxRadius = Math.Max(2, width / 5);

        // Blob centres come from a shuffled list of every cell in the half, consumed in order.
        // A permutation cannot leave a band of rows uncovered, which uniformly random centres
        // regularly did: at 10x10 the old generator placed four small blobs and most rows came
        // out empty, so FillEmptyLines "repaired" them into a bar down the mirror axis. That bar
        // was the dominant feature of most generated 10x10 pictures - the daily puzzle's size.
        var centres = ShuffledHalfCells(halfWidth, height, random);

        // Blobs are added until the half is as full as the difficulty asks, so coverage scales
        // with the grid instead of being a fixed count that happened to suit 5x5.
        //
        // The last blob is the one that decides how close to the target we land, so its radius
        // is capped by what is still wanted. Without that cap the loop only checked the total
        // *before* stamping, and a full-size final blob could overshoot by its whole area -
        // enough that every difficulty came out 5-6 points too full and the sparse end never
        // arrived. Adjacent settings then produced the same picture for a third to a half of
        // all seeds, so the slider did nothing for those players.
        var filled = 0;

        for (var i = 0; i < centres.Length && filled < wanted; i++)
        {
            var (centreX, centreY) = centres[i];
            var radius = 1 + random.Next(RadiusCapFor(wanted - filled, maxRadius));

            filled += StampBlob(cells, width, height, halfWidth, centreX, centreY, radius, random);
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

    /// <summary>
    /// The largest radius whose blob should still fit inside <paramref name="remaining"/>.
    /// </summary>
    /// <remarks>
    /// A radius-r blob covers about pi*r^2 cells, of which <see cref="BlobSolidity"/> land. That
    /// is an estimate, not a promise - the shape is clipped at the edges and the solidity roll is
    /// random - so a small overshoot is still possible and fine. What it prevents is the large
    /// overshoot: a radius-4 blob dropped when only two cells were still wanted.
    /// </remarks>
    private static int RadiusCapFor(int remaining, int maxRadius)
    {
        var affordable = (int)Math.Floor(Math.Sqrt(remaining / (Math.PI * BlobSolidity)));

        return Math.Clamp(affordable, 1, maxRadius);
    }

    /// <summary>
    /// Stamps one blob and reports how many <em>new</em> left-half cells it filled.
    /// </summary>
    /// <remarks>
    /// Only the left half counts: the right is mirrored from it afterwards, so anything stamped
    /// beyond the axis is discarded. Returning the delta also lets the caller keep a running
    /// total instead of recounting the half after every blob, which was quadratic in the grid.
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
    /// picture, so each one is given a cell that continues the shape beside it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each repair is mirrored, because this runs after
    /// <see cref="MirrorLeftHalfOntoRight"/> and would otherwise undo the symmetry it just
    /// established. (The prototype wrote only one cell and so produced subtly lop-sided
    /// pictures at 20x20.)
    /// </para>
    /// <para>
    /// The cell is placed under a filled cell of the nearest occupied row rather than on the
    /// mirror axis. Putting every repair on the axis was what made generated pictures look like
    /// they had a bar down the middle: the repairs all lined up with each other instead of with
    /// the shape. Attaching them to their neighbour reads as the shape tapering off, which is
    /// what a row with one or two cells in it should look like.
    /// </para>
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
            foreach (var y in new[] { emptyY - distance, emptyY + distance })
            {
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
            foreach (var x in new[] { emptyX - distance, emptyX + distance })
            {
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
