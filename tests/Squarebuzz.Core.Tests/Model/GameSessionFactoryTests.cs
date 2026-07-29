using Squarebuzz.Core.Content;
using Squarebuzz.Core.Generation;
using Squarebuzz.Core.Model;
using Squarebuzz.Core.Solving;
using Xunit;

namespace Squarebuzz.Core.Tests.Model;

public class GameSessionFactoryTests
{
    private static GameSessionFactory NewFactory() =>
        new(new EmbeddedPuzzleRepository(), new UniqueSolutionGenerator(new BlobPuzzleGenerator()));

    [Theory]
    [InlineData(GridSize.Tiny)]
    [InlineData(GridSize.Normal)]
    public void SmallSizes_UseAuthoredArtwork(int size)
    {
        var session = NewFactory().Create(NewGameOptions.Default with { Size = size, PackId = "surprise", Seed = 1 });

        Assert.False(session.Puzzle.IsGenerated);
        Assert.Equal(size, session.Puzzle.Width);
    }

    [Theory]
    [InlineData(GridSize.Big)]
    [InlineData(GridSize.Huge)]
    [InlineData(GridSize.Giant)]
    public void LargeSizes_AreGeneratedAndAlwaysFair(int size)
    {
        var session = NewFactory().Create(NewGameOptions.Default with { Size = size, PackId = "surprise", Seed = 99 });

        Assert.True(session.Puzzle.IsGenerated);
        Assert.Equal(size, session.Puzzle.Width);
        Assert.True(PuzzleSolver.Analyse(session.Puzzle).IsSolvable);
    }

    [Fact]
    public void APackWithNoArtworkAtThatSize_StillYieldsAGame()
    {
        // 'dinos' only has a 10x10 picture, so a 5x5 request has to fall back rather than fail.
        var session = NewFactory().Create(NewGameOptions.Default with { Size = GridSize.Tiny, PackId = "dinos", Seed = 5 });

        Assert.Equal(GridSize.Tiny, session.Puzzle.Width);
    }

    [Fact]
    public void SameSeed_SelectsTheSamePicture()
    {
        var options = NewGameOptions.Default with { Size = GridSize.Tiny, PackId = "surprise", Seed = 4242 };

        var first = NewFactory().Create(options);
        var second = NewFactory().Create(options);

        Assert.Equal(first.Puzzle.Id, second.Puzzle.Id);
    }

    [Fact]
    public void ChallengeLevel_FlowsIntoTheRules()
    {
        var factory = NewFactory();

        var relaxed = factory.Create(NewGameOptions.Default with { Challenge = ChallengeLevel.Relaxed, Seed = 1 });
        var sharp = factory.Create(NewGameOptions.Default with { Challenge = ChallengeLevel.Sharp, Seed = 1 });

        Assert.Equal(3, relaxed.HintsRemaining);
        Assert.True(relaxed.Rules.AutoCrossCompletedLines);

        Assert.Equal(1, sharp.HintsRemaining);
        Assert.False(sharp.Rules.AutoCrossCompletedLines);
    }

    [Fact]
    public void UnsupportedSize_IsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => NewFactory().Create(NewGameOptions.Default with { Size = 7 }));
    }

    [Fact]
    public void GridSize_GatesTheGiantBoardToLargeScreens()
    {
        Assert.False(GridSize.RequiresLargeScreen(GridSize.Huge));
        Assert.True(GridSize.RequiresLargeScreen(GridSize.Giant));

        Assert.True(GridSize.IsAuthored(GridSize.Normal));
        Assert.False(GridSize.IsAuthored(GridSize.Big));
    }

    [Fact]
    public void ResumingASpecificPicture_KeepsIt()
    {
        var repository = new EmbeddedPuzzleRepository();
        var dino = repository.FindById("dino");
        Assert.NotNull(dino);

        var session = NewFactory().CreateFor(dino, ChallengeLevel.Sharp, HelperSettings.Default);

        Assert.Same(dino, session.Puzzle);
        Assert.Equal(1, session.HintsRemaining);
    }
}
