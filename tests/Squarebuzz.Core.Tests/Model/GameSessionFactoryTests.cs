using Squarebuzz.Core.Abstractions;
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
        Assert.Equal(1, sharp.HintsRemaining);

        // The hint budget is what the challenge decides; auto-crossing is the player's switch,
        // which is on by default at either level.
        Assert.True(relaxed.Rules.AutoCrossCompletedLines);
        Assert.True(sharp.Rules.AutoCrossCompletedLines);
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
    public void ExcludingThePreviousPicture_ServesADifferentOne()
    {
        var factory = NewFactory();
        var options = NewGameOptions.Default with { Size = GridSize.Tiny, PackId = "surprise" };

        var first = factory.Create(options with { Seed = 7 });

        // Whatever seed comes next, the picture just solved must not come straight back.
        for (var seed = 0; seed < 24; seed++)
        {
            var next = factory.Create(options with { Seed = seed, ExcludePuzzleId = first.Puzzle.Id });

            Assert.NotEqual(first.Puzzle.Id, next.Puzzle.Id);
        }
    }

    [Fact]
    public void ASoleCandidate_IsServedDespiteTheExclusion()
    {
        // A pack with exactly one picture at the requested size must repeat it rather than fail
        // when it is excluded. Every shipped pack carries several 10x10 pictures now, so the
        // scenario is pinned with a single-picture repository instead of shipped content.
        var factory = new GameSessionFactory(
            new SolePictureRepository(),
            new UniqueSolutionGenerator(new BlobPuzzleGenerator()));
        var options = NewGameOptions.Default with { Size = GridSize.Normal, PackId = "dinos", Seed = 3 };

        var first = factory.Create(options);
        var next = factory.Create(options with { ExcludePuzzleId = first.Puzzle.Id });

        Assert.Equal(first.Puzzle.Id, next.Puzzle.Id);
    }

    [Fact]
    public void ExclusionIsIrrelevantToGeneratedBoards()
    {
        var session = NewFactory().Create(NewGameOptions.Default with
        {
            Size = GridSize.Big,
            Seed = 11,
            ExcludePuzzleId = "heart",
        });

        Assert.True(session.Puzzle.IsGenerated);
    }

    [Fact]
    public void TheLevelNumber_SurvivesIntoTheSessionOrigin()
    {
        var session = NewFactory().Create(NewGameOptions.Default with { Seed = 1, Level = 42 });

        Assert.Equal(42, session.Origin?.Level);
    }

    /// <summary>
    /// Exactly one 10x10 picture, so the sole-candidate path stays testable no matter how much
    /// content the real packs grow.
    /// </summary>
    private sealed class SolePictureRepository : IPuzzleRepository
    {
        private readonly Puzzle _only = Puzzle.FromRows(
            "solo",
            "dinos",
            "#59C36A",
            new[]
            {
                "......###.",
                ".....#####",
                ".....##.##",
                ".....#####",
                ".....####.",
                "..#######.",
                ".#########",
                ".########.",
                "..##..##..",
                "..##..##..",
            });

        public IReadOnlyList<PackDefinition> Packs { get; } =
            new[] { new PackDefinition("dinos", "🦕", Locked: false, IsWildcard: false) };

        public IReadOnlyList<Puzzle> Puzzles => new[] { _only };

        public IReadOnlyList<Puzzle> Find(string packId, int size, IReadOnlySet<string>? unlockedPackIds = null) =>
            packId == _only.Pack && size == _only.Width ? new[] { _only } : Array.Empty<Puzzle>();

        public Puzzle? FindById(string puzzleId, int? revision = null) =>
            puzzleId == _only.Id && (revision is null || revision == _only.Revision) ? _only : null;
    }
}
