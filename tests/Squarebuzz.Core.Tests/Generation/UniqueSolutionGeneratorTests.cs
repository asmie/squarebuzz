using Squarebuzz.Core.Generation;
using Squarebuzz.Core.Model;
using Squarebuzz.Core.Solving;
using Xunit;
using Xunit.Abstractions;

namespace Squarebuzz.Core.Tests.Generation;

public class UniqueSolutionGeneratorTests
{
    private readonly ITestOutputHelper _output;

    public UniqueSolutionGeneratorTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// Measures how often a raw blob grid happens to be logically solvable. This is what
    /// justifies <see cref="UniqueSolutionGenerator.DefaultMaxAttempts"/> - if the rate ever
    /// collapses, the budget is no longer adequate and this test says so.
    /// </summary>
    [Theory]
    [InlineData(15)]
    [InlineData(20)]
    [InlineData(25)]
    public void RawBlobAcceptanceRate_IsHighEnoughForTheAttemptBudget(int size)
    {
        var generator = new BlobPuzzleGenerator();
        const int samples = 200;
        var solvable = 0;

        for (var seed = 1; seed <= samples; seed++)
        {
            var puzzle = generator.Generate(new PuzzleRequest(size, size, 3, "surprise", seed * 7919));

            if (PuzzleSolver.Analyse(puzzle).IsSolvable)
            {
                solvable++;
            }
        }

        var rate = solvable / (double)samples;
        _output.WriteLine($"{size}x{size}: {solvable}/{samples} raw blob grids solvable ({rate:P1})");

        // With rate r, the chance all 60 attempts fail is (1-r)^60. Requiring r >= 5%
        // keeps that below 5%, and the assertion documents the real dependency.
        Assert.True(rate >= 0.05, $"Only {rate:P1} of {size}x{size} blob grids are solvable; the attempt budget is no longer safe.");
    }

    [Theory]
    [InlineData(15)]
    [InlineData(20)]
    [InlineData(25)]
    public void EveryGeneratedPuzzle_IsSolvableByPureLogic(int size)
    {
        var generator = new UniqueSolutionGenerator(new BlobPuzzleGenerator());
        var attemptTotal = 0;

        for (var seed = 1; seed <= 25; seed++)
        {
            var puzzle = generator.Generate(new PuzzleRequest(size, size, 3, "surprise", seed));
            attemptTotal += generator.LastAttemptCount;

            var result = PuzzleSolver.Analyse(puzzle);

            Assert.Equal(PuzzleSolveOutcome.Solvable, result.Outcome);
            Assert.Equal(size, puzzle.Width);
            Assert.Equal(size, puzzle.Height);
            Assert.True(puzzle.IsGenerated);
        }

        _output.WriteLine($"{size}x{size}: {attemptTotal / 25.0:F1} candidates per accepted puzzle on average");
    }

    [Theory]
    [InlineData(PuzzleRequest.MinDifficulty)]
    [InlineData(3)]
    [InlineData(PuzzleRequest.MaxDifficulty)]
    public void EveryDifficulty_CanBeSatisfied(int difficulty)
    {
        var generator = new UniqueSolutionGenerator(new BlobPuzzleGenerator());

        var puzzle = generator.Generate(new PuzzleRequest(15, 15, difficulty, "surprise", 4242));

        Assert.True(PuzzleSolver.Analyse(puzzle).IsSolvable);
    }

    [Fact]
    public void SameSeed_AlwaysProducesTheSamePuzzle()
    {
        var generator = new UniqueSolutionGenerator(new BlobPuzzleGenerator());
        var request = new PuzzleRequest(20, 20, 3, "surprise", 987654);

        var first = generator.Generate(request);
        var second = generator.Generate(request);

        // Reproducibility is what lets a saved game or the daily puzzle be regenerated
        // rather than stored cell by cell.
        Assert.True(first.Solution.SequenceEqual(second.Solution));
    }

    [Fact]
    public void DifferentSeeds_ProduceDifferentPuzzles()
    {
        var generator = new UniqueSolutionGenerator(new BlobPuzzleGenerator());

        var first = generator.Generate(new PuzzleRequest(15, 15, 3, "surprise", 1));
        var second = generator.Generate(new PuzzleRequest(15, 15, 3, "surprise", 2));

        Assert.False(first.Solution.SequenceEqual(second.Solution));
    }

    [Fact]
    public void GeneratedPuzzle_HasNoEmptyRowOrColumn()
    {
        var generator = new UniqueSolutionGenerator(new BlobPuzzleGenerator());
        var puzzle = generator.Generate(new PuzzleRequest(15, 15, 3, "surprise", 555));

        for (var y = 0; y < puzzle.Height; y++)
        {
            Assert.False(puzzle.RowClues[y].IsBlank, $"Row {y} is empty.");
        }

        for (var x = 0; x < puzzle.Width; x++)
        {
            Assert.False(puzzle.ColumnClues[x].IsBlank, $"Column {x} is empty.");
        }
    }

    [Theory]
    [InlineData(10)]
    [InlineData(16)]
    [InlineData(25)]
    public void GeneratedPuzzle_IsMirroredButNotAMirrorImage(int size)
    {
        // The mirrored body is what makes the blobs read as creatures, so most of the picture
        // must still match its reflection. But an exact mirror hands the player half the answer
        // - solve one side, copy it - which made the generated levels too easy.
        var generator = new BlobPuzzleGenerator();

        for (var seed = 1; seed <= 40; seed++)
        {
            var puzzle = generator.Generate(new PuzzleRequest(size, size, 3, "surprise", seed));
            var differing = 0;

            for (var y = 0; y < puzzle.Height; y++)
            {
                for (var x = 0; x < puzzle.Width; x++)
                {
                    if (puzzle.IsFilled(x, y) != puzzle.IsFilled(puzzle.Width - 1 - x, y))
                    {
                        differing++;
                    }
                }
            }

            var share = differing / (double)puzzle.CellCount;

            Assert.True(share > 0, $"seed {seed}: {size}x{size} picture is an exact mirror image.");
            Assert.True(share <= 0.3, $"seed {seed}: {share:P0} of the picture differs from its reflection.");
        }
    }

    [Fact]
    public void ExhaustingTheBudget_ThrowsRatherThanShippingAnUnfairPuzzle()
    {
        // A generator that only ever emits an ambiguous grid must never be let through.
        var generator = new UniqueSolutionGenerator(new AlwaysAmbiguousGenerator(), maxAttempts: 3);

        var exception = Assert.Throws<PuzzleGenerationException>(
            () => generator.Generate(new PuzzleRequest(10, 10, 3, "surprise", 1)));

        Assert.Contains("3 attempts", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A 2x2 checkerboard is the classic ambiguous nonogram: clues of 1 on every row and
    /// column admit both diagonals, so line logic can never settle it.
    /// </summary>
    private sealed class AlwaysAmbiguousGenerator : IPuzzleGenerator
    {
        public Puzzle Generate(PuzzleRequest request) =>
            Puzzle.FromRows("ambiguous", request.Pack, "#000000", ["#.", ".#"]);
    }
}
