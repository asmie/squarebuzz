using Squarebuzz.Core.Content;
using Squarebuzz.Core.Generation;
using Squarebuzz.Core.Model;
using Xunit;

namespace Squarebuzz.Core.Tests.Model;

/// <summary>
/// Covers <see cref="NewGameOptions.PuzzleId"/> - the path the Gallery uses to play one
/// specific picture rather than letting size and pack choose.
/// </summary>
public class ChosenPuzzleTests
{
    private static GameSessionFactory NewFactory() =>
        new(new EmbeddedPuzzleRepository(), new UniqueSolutionGenerator(new BlobPuzzleGenerator()));

    [Theory]
    [InlineData("heart")]
    [InlineData("dino")]
    [InlineData("crown")]
    public void AChosenPictureIsTheOnePlayed(string puzzleId)
    {
        var session = NewFactory().Create(NewGameOptions.Default with { PuzzleId = puzzleId });

        Assert.Equal(puzzleId, session.Puzzle.Id);
    }

    [Fact]
    public void AChosenPictureOverridesSizeAndPack()
    {
        // 'dino' is a 10x10 from the dinos pack; the request deliberately asks for neither.
        var session = NewFactory().Create(NewGameOptions.Default with
        {
            PuzzleId = "dino",
            Size = GridSize.Tiny,
            PackId = "space",
        });

        Assert.Equal("dino", session.Puzzle.Id);
        Assert.Equal(GridSize.Normal, session.Puzzle.Width);
    }

    [Fact]
    public void AChosenPictureFromALockedPackStillPlays()
    {
        // The Gallery blocks locked cards in the UI, but the domain has no business refusing a
        // direct request - locking is a presentation rule, not a game rule.
        var session = NewFactory().Create(NewGameOptions.Default with { PuzzleId = "ghost" });

        Assert.Equal("ghost", session.Puzzle.Id);
        Assert.Equal("fairy", session.Puzzle.Pack);
    }

    [Fact]
    public void AnUnknownPictureFallsBackInsteadOfFailing()
    {
        // Removing content between releases must not strand a player on an error.
        var session = NewFactory().Create(NewGameOptions.Default with
        {
            PuzzleId = "a-picture-we-removed",
            Size = GridSize.Tiny,
            PackId = "animals",
        });

        Assert.Equal(GridSize.Tiny, session.Puzzle.Width);
        Assert.False(session.Puzzle.IsGenerated);
    }

    [Fact]
    public void AChosenPictureSurvivesSaveAndResume()
    {
        var factory = NewFactory();
        var session = factory.Create(NewGameOptions.Default with { PuzzleId = "cupcake" });

        session.Paint(FirstFilled(session.Puzzle), CellState.Filled);

        var save = SavedGame.FromSession(session, Guid.NewGuid(), new DateTimeOffset(2026, 7, 30, 10, 0, 0, TimeSpan.Zero));
        var resumed = factory.Restore(save, HelperSettings.Default);

        Assert.Equal("cupcake", resumed.Puzzle.Id);
    }

    [Fact]
    public void ChoosingNothingLeavesTheNormalSelectionAlone()
    {
        var factory = NewFactory();
        var options = NewGameOptions.Default with { Size = GridSize.Tiny, PackId = "animals", Seed = 12345 };

        var first = factory.Create(options);
        var second = factory.Create(options);

        Assert.Null(options.PuzzleId);
        Assert.Equal(first.Puzzle.Id, second.Puzzle.Id);
        Assert.Equal("animals", first.Puzzle.Pack);
    }

    private static int FirstFilled(Puzzle puzzle)
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
