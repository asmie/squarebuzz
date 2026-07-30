using Squarebuzz.Core.Generation;
using Squarebuzz.Core.Model;
using Xunit;
using Xunit.Abstractions;

namespace Squarebuzz.Core.Tests.Generation;

/// <summary>
/// Guards the <em>quality</em> of generated pictures, not just their legality.
/// </summary>
/// <remarks>
/// A blob grid can satisfy every structural rule - symmetrical, no empty line, uniquely solvable -
/// and still be a bad picture. The original generator placed a fixed handful of small blobs
/// regardless of grid size, so at 10x10 most rows came out empty and the empty-line repair filled
/// them with a pair of cells on the mirror axis. The result was a bar down the middle of the great
/// majority of generated 10x10 pictures, which is the daily puzzle's size. Nothing failed; the
/// pictures were just poor. These tests are what would have caught it.
/// </remarks>
public class BlobPuzzleGeneratorTests
{
    private const int Samples = 200;
    private const int MiddlingDifficulty = 3;

    private readonly ITestOutputHelper _output;
    private readonly BlobPuzzleGenerator _generator = new();

    public BlobPuzzleGeneratorTests(ITestOutputHelper output) => _output = output;

    [Theory]
    [InlineData(5)]
    [InlineData(10)]
    [InlineData(15)]
    [InlineData(20)]
    [InlineData(25)]
    public void PictureDensity_LandsInTheSameBandAtEverySize(int size)
    {
        // Density used to swing from 58% at 5x5 down to 35% at 10x10 and back up to 56% at 25x25,
        // because blob count and radius were tuned for a small grid and never scaled. Coverage is
        // now driven by a target, so the same difficulty means the same kind of picture at any size.
        var fill = Average(size, puzzle => FilledCount(puzzle) / (double)(size * size));

        _output.WriteLine($"{size}x{size}: mean fill {fill:P1}");

        Assert.InRange(fill, 0.42, 0.68);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(15)]
    [InlineData(20)]
    [InlineData(25)]
    public void Pictures_AreNotDominatedByTheMirrorAxis(int size)
    {
        // The specific regression. A row whose only filled cells sit on the mirror axis is one the
        // empty-line repair invented, and it reads as a mistake rather than as part of a shape.
        // The old generator averaged 4.35 such rows per 10x10 picture - of ten - and up to 8.
        var axisOnly = Average(size, puzzle => CountAxisOnlyRows(puzzle, size));

        _output.WriteLine($"{size}x{size}: mean axis-only rows {axisOnly:0.00} of {size}");

        Assert.True(
            axisOnly <= size * 0.12,
            $"{axisOnly:0.00} of {size} rows are pure mirror-axis artefacts; the picture is being " +
            "made by the empty-line repair rather than by the blobs.");
    }

    [Theory]
    [InlineData(10)]
    [InlineData(15)]
    [InlineData(20)]
    [InlineData(25)]
    public void MostColumnsCarryRealContent(int size)
    {
        // Columns are the axis the mirroring works along, so a sparse column means the shape did
        // not reach that far out - the picture is a thin spine rather than a body.
        var sparse = Average(size, puzzle => CountSparseColumns(puzzle, size));

        _output.WriteLine($"{size}x{size}: mean columns with 2 or fewer cells {sparse:0.00}");

        Assert.True(sparse <= size * 0.12, $"{sparse:0.00} of {size} columns are nearly empty.");
    }

    [Theory]
    [InlineData(10)]
    [InlineData(25)]
    public void CluesAreNotSpeckled(int size)
    {
        // Every isolated cell is a clue run of 1. A speckled picture is therefore also a tedious
        // set of clues, which is why blobs are nearly solid rather than thinned by a third.
        var runsPerLine = Average(
            size,
            puzzle =>
            {
                var runs = 0;

                for (var y = 0; y < size; y++)
                {
                    runs += puzzle.RowClues[y].Count;
                }

                return runs / (double)size;
            });

        _output.WriteLine($"{size}x{size}: mean clue runs per row {runsPerLine:0.00}");

        // Roughly a fifth of the line length. Beyond that the clues stop reading as a shape.
        Assert.True(runsPerLine <= size * 0.2, $"{runsPerLine:0.00} runs per row is speckle, not a picture.");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(5)]
    public void HigherDifficulty_GivesASparserPicture(int difficulty)
    {
        // The reason difficulty affects density at all: fewer filled cells means shorter clues and
        // so fewer immediate deductions. Asserting the ordering keeps that link real rather than
        // incidental.
        var fill = Average(15, puzzle => FilledCount(puzzle) / 225.0, difficulty);

        _output.WriteLine($"difficulty {difficulty}: mean fill {fill:P1}");

        Assert.InRange(fill, 0.30, 0.75);
    }

    [Fact]
    public void SparsestDifficulty_IsSparserThanTheEasiest()
    {
        var easiest = Average(15, puzzle => FilledCount(puzzle) / 225.0, difficulty: 1);
        var hardest = Average(15, puzzle => FilledCount(puzzle) / 225.0, difficulty: 5);

        _output.WriteLine($"difficulty 1: {easiest:P1}, difficulty 5: {hardest:P1}");

        Assert.True(hardest < easiest, "Difficulty no longer thins the picture.");
    }

    private double Average(int size, Func<Puzzle, double> measure, int difficulty = MiddlingDifficulty)
    {
        var total = 0.0;

        for (var seed = 1; seed <= Samples; seed++)
        {
            total += measure(_generator.Generate(new PuzzleRequest(size, size, difficulty, "surprise", seed)));
        }

        return total / Samples;
    }

    private static int FilledCount(Puzzle puzzle)
    {
        var count = 0;

        for (var i = 0; i < puzzle.CellCount; i++)
        {
            if (puzzle.Solution[i])
            {
                count++;
            }
        }

        return count;
    }

    private static int CountAxisOnlyRows(Puzzle puzzle, int size)
    {
        // The repair writes the centre column and its mirror, which on an odd width is one cell.
        var centre = size / 2;
        var mirror = size - 1 - centre;
        var rows = 0;

        for (var y = 0; y < size; y++)
        {
            int filled = 0, onAxis = 0;

            for (var x = 0; x < size; x++)
            {
                if (!puzzle.Solution[(y * size) + x])
                {
                    continue;
                }

                filled++;

                if (x == centre || x == mirror)
                {
                    onAxis++;
                }
            }

            if (filled > 0 && filled == onAxis)
            {
                rows++;
            }
        }

        return rows;
    }

    private static int CountSparseColumns(Puzzle puzzle, int size)
    {
        var columns = 0;

        for (var x = 0; x < size; x++)
        {
            var filled = 0;

            for (var y = 0; y < size; y++)
            {
                if (puzzle.Solution[(y * size) + x])
                {
                    filled++;
                }
            }

            if (filled <= 2)
            {
                columns++;
            }
        }

        return columns;
    }
}
