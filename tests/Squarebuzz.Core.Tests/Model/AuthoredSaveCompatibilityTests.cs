using Squarebuzz.Core.Content;
using Squarebuzz.Core.Generation;
using Squarebuzz.Core.Model;
using Xunit;

namespace Squarebuzz.Core.Tests.Model;

public class AuthoredSaveCompatibilityTests
{
    // Original shipped boards: keep these independent of the current content and its archives.
    [Theory]
    [InlineData("ghost", ".###./#####/#.#.#/#####/#.#.#")]
    [InlineData("balloon", ".###./#####/.###./.#.#./.###.")]
    [InlineData("bunny", ".#.#./.#.#./.###./#####/.###.")]
    [InlineData("apple", "..#../.###./#####/#####/.###.")]
    [InlineData("cat", "##......##/###....###/##########/##########/#.##..##.#/##########/####..####/##.####.##/.########./..######..")]
    [InlineData("planet", "...####.../..######../.###..###./.########./##########/##########/.########./.########./..######../...####...")]
    [InlineData("dog", ".##....##./###....###/##########/##########/##.#..#.##/##########/####..####/###....###/.########./..######..")]
    [InlineData("crab", "##..##..##/##..##..##/.########./##########/###.##.###/##########/.########./.##....##./##......##/..........")]
    [InlineData("mushroom", "...####.../.########./##########/##.####.##/##########/...####.../...####.../...####.../...####.../..######..")]
    [InlineData("tree", "...####.../..######../.########./##########/##########/.########./....##..../....##..../....##..../...####...")]
    [InlineData("plane", "....##..../...####.../....##..../##########/##########/##########/....##..../....##..../..######../..######..")]
    [InlineData("guitar", "....##..../....##..../....##..../....##..../..######../.########./.###..###./.########./.########./..######..")]
    [InlineData("alien", "..#....#../..#....#../.########./##########/##..##..##/##..##..##/##########/####..####/.########./..######..")]
    [InlineData("icecream", "...####.../..######../.########./.########./##########/.########./..######../...####.../....##..../....##....")]
    public void LegacySave_PreservesOriginalBoardThroughResumeRestartAndResave(string id, string originalRows)
    {
        var repository = new EmbeddedPuzzleRepository();
        var factory = new GameSessionFactory(repository, new BlobPuzzleGenerator());
        var current = repository.FindById(id)!;
        var original = Puzzle.FromRows(id, current.Pack, current.ColorHex, originalRows.Split('/'));
        var marks = Enumerable.Range(0, original.CellCount).Select(original.ExpectedState).ToArray();
        marks[Array.FindLastIndex(marks, cell => cell == CellState.Filled)] = CellState.Empty;
        var save = new SavedGame
        {
            Id = Guid.NewGuid(),
            PuzzleId = id,
            Size = original.Width,
            Difficulty = 2,
            PackId = original.Pack,
            Seed = 42,
            Challenge = ChallengeLevel.Relaxed,
            Cells = marks,
            Elapsed = TimeSpan.FromSeconds(60),
            HintsRemaining = 2,
            HintsUsed = 1,
            Mistakes = 0,
            SavedAt = DateTimeOffset.UtcNow,
        };

        Assert.True(original.Solution.SequenceEqual(factory.ResolvePuzzle(save).Solution));
        var restored = factory.Restore(save, HelperSettings.Default);
        Assert.Equal(marks, restored.Cells.ToArray());
        Assert.True(original.Solution.SequenceEqual(restored.Puzzle.Solution));
        Assert.Equal(save.Elapsed, restored.Elapsed);
        Assert.Equal(save.Mistakes, restored.Mistakes);
        Assert.Equal(save.HintsRemaining, restored.HintsRemaining);
        Assert.Equal(save.HintsUsed, restored.HintsUsed);

        var restarted = factory.Create(restored.Origin!.Restart(HelperSettings.Default));
        Assert.True(original.Solution.SequenceEqual(restarted.Puzzle.Solution));
        Assert.All(restarted.Cells.ToArray(), cell => Assert.Equal(CellState.Empty, cell));

        var resaved = SavedGame.FromSession(restored, save.Id, save.SavedAt);
        Assert.Equal(1, resaved.PuzzleRevision);
        Assert.True(original.Solution.SequenceEqual(factory.Restore(resaved, HelperSettings.Default).Puzzle.Solution));

        var fresh = factory.Create(NewGameOptions.Default with { PuzzleId = id });
        Assert.Same(current, fresh.Puzzle);
        Assert.False(original.Solution.SequenceEqual(fresh.Puzzle.Solution));
        var freshSave = SavedGame.FromSession(fresh, Guid.NewGuid(), save.SavedAt);
        Assert.Equal(2, freshSave.PuzzleRevision);
        Assert.True(current.Solution.SequenceEqual(factory.Restore(freshSave, HelperSettings.Default).Puzzle.Solution));

        // Archives must never enter Gallery, campaign or random new-game selection.
        Assert.Same(current, Assert.Single(repository.Puzzles, p => p.Id == id));
        Assert.Same(current, Assert.Single(repository.Find(current.Pack, current.Width), p => p.Id == id));
        Assert.Null(restored.Origin.NextPuzzle(HelperSettings.Default).PuzzleRevision);
    }

    [Fact]
    public void UnknownRevision_IsRejectedInsteadOfSubstitutingTheCurrentBoard()
    {
        var repository = new EmbeddedPuzzleRepository();
        var factory = new GameSessionFactory(repository, new BlobPuzzleGenerator());
        var session = factory.Create(NewGameOptions.Default with { PuzzleId = "cat" });
        var unknown = SavedGame.FromSession(session, Guid.NewGuid(), DateTimeOffset.UtcNow) with { PuzzleRevision = 99 };

        Assert.Null(repository.FindById("cat", 99));
        Assert.Throws<InvalidOperationException>(() => factory.ResolvePuzzle(unknown));
        Assert.Throws<InvalidOperationException>(() => factory.Restore(unknown, HelperSettings.Default));
        Assert.Throws<InvalidOperationException>(() => factory.Create(
            session.Origin!.Restart(HelperSettings.Default) with { PuzzleRevision = 99 }));
    }
}
