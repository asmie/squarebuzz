using Squarebuzz.Core.Content;
using Squarebuzz.Core.Generation;
using Squarebuzz.Core.Model;
using Xunit;

namespace Squarebuzz.Core.Tests.Model;

/// <summary>
/// Covers turning a save back into a picture - the mechanism the Continue screen and resume
/// both depend on.
/// </summary>
public class SavedGameResolutionTests
{
    private static readonly DateTimeOffset SavedAt = new(2026, 7, 30, 9, 0, 0, TimeSpan.Zero);

    private static GameSessionFactory NewFactory() =>
        new(new EmbeddedPuzzleRepository(), new UniqueSolutionGenerator(new BlobPuzzleGenerator()));

    private static SavedGame Save(string? puzzleId, int size, int seed) => new()
    {
        Id = Guid.NewGuid(),
        PuzzleId = puzzleId,
        Size = size,
        Difficulty = 3,
        PackId = "surprise",
        Seed = seed,
        Challenge = ChallengeLevel.Relaxed,
        Cells = new CellState[size * size],
        Elapsed = TimeSpan.FromSeconds(30),
        HintsRemaining = 3,
        Mistakes = 0,
        SavedAt = SavedAt,
    };

    [Fact]
    public void AnAuthoredSave_ResolvesToTheSameStoredPicture()
    {
        var factory = NewFactory();

        var puzzle = factory.ResolvePuzzle(Save("heart", 5, seed: 1));

        Assert.Equal("heart", puzzle.Id);
        Assert.False(puzzle.IsGenerated);
    }

    [Fact]
    public void AGeneratedSave_IsRebuiltFromItsSeedAlone()
    {
        var factory = NewFactory();
        var save = Save(puzzleId: null, GridSize.Big, seed: 987654);

        var first = factory.ResolvePuzzle(save);
        var second = factory.ResolvePuzzle(save);

        Assert.True(first.IsGenerated);
        Assert.True(first.Solution.SequenceEqual(second.Solution));
    }

    [Fact]
    public void ResolveAndRestore_AgreeOnThePicture()
    {
        // The list draws what ResolvePuzzle returns and the board plays what Restore builds, so
        // a mismatch would show the player one picture and hand them another.
        var factory = NewFactory();
        var save = Save(puzzleId: null, GridSize.Big, seed: 4242);

        var resolved = factory.ResolvePuzzle(save);
        var restored = factory.Restore(save, HelperSettings.Default);

        Assert.True(resolved.Solution.SequenceEqual(restored.Puzzle.Solution));
    }

    [Fact]
    public void ASaveNamingAMissingPicture_FailsWithTheIdInTheMessage()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => NewFactory().ResolvePuzzle(Save("no-such-picture", 5, seed: 1)));

        Assert.Contains("no-such-picture", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RoundTrippingASessionThroughASave_PreservesEveryMark()
    {
        var factory = NewFactory();
        var session = factory.Create(NewGameOptions.Default with { Size = GridSize.Normal, PackId = "animals", Seed = 31337 });

        // Play a mixture of fills and crosses so both mark types have to survive.
        session.Paint(0, CellState.Crossed);
        for (var i = 0; i < session.Puzzle.CellCount && session.MoveCount < 6; i++)
        {
            if (session.Puzzle.Solution[i])
            {
                session.Paint(i, CellState.Filled);
            }
        }

        session.Advance(TimeSpan.FromSeconds(77));
        session.UseHint();

        var save = SavedGame.FromSession(session, Guid.NewGuid(), SavedAt);
        var resumed = factory.Restore(save, HelperSettings.Default);

        Assert.Equal(session.Cells.ToArray(), resumed.Cells.ToArray());
        Assert.Equal(session.Elapsed, resumed.Elapsed);
        Assert.Equal(session.HintsRemaining, resumed.HintsRemaining);
        Assert.Equal(session.Mistakes, resumed.Mistakes);
        Assert.Equal(session.Puzzle.Id, resumed.Puzzle.Id);
    }

    [Fact]
    public void AResumedGameCannotBeUndoneBeyondThePointItWasSaved()
    {
        var factory = NewFactory();
        var session = factory.Create(NewGameOptions.Default with { Size = GridSize.Tiny, PackId = "animals", Seed = 5 });

        var firstFilled = FirstSolutionIndex(session.Puzzle);
        session.Paint(firstFilled, CellState.Filled);
        Assert.True(session.CanUndo);

        var resumed = factory.Restore(SavedGame.FromSession(session, Guid.NewGuid(), SavedAt), HelperSettings.Default);

        // History is deliberately not persisted: undoing into a state from a previous sitting
        // would be more confusing than helpful.
        Assert.False(resumed.CanUndo);
        Assert.Equal(CellState.Filled, resumed[firstFilled]);
    }

    [Fact]
    public void FilledCount_ReflectsProgressForTheContinueList()
    {
        var factory = NewFactory();
        var session = factory.Create(NewGameOptions.Default with { Size = GridSize.Tiny, PackId = "animals", Seed = 9 });

        var save = SavedGame.FromSession(session, Guid.NewGuid(), SavedAt);
        Assert.Equal(0, save.FilledCount);

        session.Paint(FirstSolutionIndex(session.Puzzle), CellState.Filled);

        var progressed = SavedGame.FromSession(session, Guid.NewGuid(), SavedAt);
        Assert.True(progressed.FilledCount >= 1);
    }

    private static int FirstSolutionIndex(Puzzle puzzle)
    {
        for (var i = 0; i < puzzle.CellCount; i++)
        {
            if (puzzle.Solution[i])
            {
                return i;
            }
        }

        throw new InvalidOperationException("Puzzle has no filled cells.");
    }
}
