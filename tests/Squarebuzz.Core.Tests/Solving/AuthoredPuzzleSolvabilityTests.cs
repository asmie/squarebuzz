using Squarebuzz.Core.Content;
using Squarebuzz.Core.Model;
using Squarebuzz.Core.Solving;
using Xunit;
using Xunit.Abstractions;

namespace Squarebuzz.Core.Tests.Solving;

/// <summary>
/// Holds the shipped content to the promise the game makes to children: "every puzzle has
/// exactly one answer - never guess". These are assertions about real data, so they will
/// fail loudly if a picture is ever edited into something that needs guesswork.
/// </summary>
public class AuthoredPuzzleSolvabilityTests
{
    private readonly ITestOutputHelper _output;

    public AuthoredPuzzleSolvabilityTests(ITestOutputHelper output) => _output = output;

    public static TheoryData<string> AuthoredPuzzleIds()
    {
        var data = new TheoryData<string>();

        foreach (var puzzle in new EmbeddedPuzzleRepository().Puzzles)
        {
            data.Add(puzzle.Id);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AuthoredPuzzleIds))]
    public void AuthoredPuzzle_IsSolvableByPureLogic(string puzzleId)
    {
        var puzzle = new EmbeddedPuzzleRepository().FindById(puzzleId);
        Assert.NotNull(puzzle);

        var result = PuzzleSolver.Analyse(puzzle);

        _output.WriteLine($"{puzzleId,-8} {puzzle.Width}x{puzzle.Height}  outcome={result.Outcome}  passes={result.Passes}  undetermined={result.UndeterminedCells}");

        Assert.Equal(PuzzleSolveOutcome.Solvable, result.Outcome);
    }

    [Theory]
    [MemberData(nameof(AuthoredPuzzleIds))]
    public void SolvingAnAuthoredPuzzle_ReproducesItsPictureExactly(string puzzleId)
    {
        var puzzle = new EmbeddedPuzzleRepository().FindById(puzzleId);
        Assert.NotNull(puzzle);

        var board = new CellState[puzzle.CellCount];
        var result = PuzzleSolver.Solve(puzzle, board);

        Assert.Equal(PuzzleSolveOutcome.Solvable, result.Outcome);

        // The solver must not merely finish - it must arrive at the intended picture.
        for (var i = 0; i < board.Length; i++)
        {
            Assert.Equal(puzzle.ExpectedState(i), board[i]);
        }
    }

    [Fact]
    public void ContentFile_LoadsEveryPackAndPuzzle()
    {
        var repository = new EmbeddedPuzzleRepository();

        Assert.Equal(7, repository.Packs.Count);
        Assert.Equal(12, repository.Puzzles.Count);
    }

    [Fact]
    public void WildcardPack_DrawsFromUnlockedPacksOnly()
    {
        var repository = new EmbeddedPuzzleRepository();

        var surprise = repository.Find("surprise", 5);

        Assert.NotEmpty(surprise);

        // 'fairy' ships locked, so the ghost (a 5x5 fairy picture) must not be offered yet.
        Assert.DoesNotContain(surprise, p => p.Pack == "fairy");
        Assert.DoesNotContain(surprise, p => p.Id == "ghost");
    }

    [Fact]
    public void WildcardPack_IncludesAPackTheePlayerHasEarned()
    {
        // A locked pack is earned by finding its pictures on the Puzzle Path, after which New
        // Game and the Gallery both offer it. Surprise used to be the one place that never
        // caught up, because it judged by the shipped flag alone.
        var repository = new EmbeddedPuzzleRepository();

        var earned = new HashSet<string>(StringComparer.Ordinal) { "fairy" };

        var surprise = repository.Find("surprise", 5, earned);

        Assert.Contains(surprise, p => p.Id == "ghost");

        // Everything it offered before is still there.
        Assert.ProperSuperset(
            repository.Find("surprise", 5).Select(p => p.Id).ToHashSet(StringComparer.Ordinal),
            surprise.Select(p => p.Id).ToHashSet(StringComparer.Ordinal));
    }

    [Fact]
    public void Find_MatchesOnPackAndSize()
    {
        var repository = new EmbeddedPuzzleRepository();

        Assert.All(repository.Find("animals", 5), p => Assert.Equal("animals", p.Pack));
        Assert.All(repository.Find("animals", 5), p => Assert.Equal(5, p.Width));

        // 'animals' has a 10x10 cat but no 15x15 anything.
        Assert.NotEmpty(repository.Find("animals", 10));
        Assert.Empty(repository.Find("animals", 15));
    }

    [Fact]
    public void DerivedClues_MatchTheKnownCluesOfTheHeartPicture()
    {
        var heart = new EmbeddedPuzzleRepository().FindById("heart");
        Assert.NotNull(heart);

        // .#.#.  ->  1 1
        // #####  ->  5
        // #####  ->  5
        // .###.  ->  3
        // ..#..  ->  1
        Assert.Equal(new LineClues(1, 1), heart.RowClues[0]);
        Assert.Equal(new LineClues(5), heart.RowClues[1]);
        Assert.Equal(new LineClues(5), heart.RowClues[2]);
        Assert.Equal(new LineClues(3), heart.RowClues[3]);
        Assert.Equal(new LineClues(1), heart.RowClues[4]);

        // Column 0 reads . # # . .  ->  2
        Assert.Equal(new LineClues(2), heart.ColumnClues[0]);

        // Column 2 reads . # # # #  ->  4
        Assert.Equal(new LineClues(4), heart.ColumnClues[2]);
    }
}
