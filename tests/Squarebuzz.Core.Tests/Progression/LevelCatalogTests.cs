using Squarebuzz.Core.Content;
using Squarebuzz.Core.Model;
using Squarebuzz.Core.Progression;
using Xunit;

namespace Squarebuzz.Core.Tests.Progression;

public class LevelCatalogTests
{
    private static IReadOnlyList<Puzzle> Authored() => new EmbeddedPuzzleRepository().Puzzles;

    /// <summary>A fake content list: <paramref name="tiny"/> 5x5 pictures then <paramref name="normal"/> 10x10 ones.</summary>
    private static List<Puzzle> Fake(int tiny, int normal)
    {
        var puzzles = new List<Puzzle>();

        for (var i = 0; i < tiny; i++)
        {
            puzzles.Add(Puzzle.FromRows($"tiny{i}", "test", "#FF8A3D", ["#####", "#...#", "#...#", "#...#", "#####"]));
        }

        for (var i = 0; i < normal; i++)
        {
            var edge = new string('#', 10);
            var middle = "#........#";
            puzzles.Add(Puzzle.FromRows(
                $"normal{i}",
                "test",
                "#FF8A3D",
                [edge, middle, middle, middle, middle, middle, middle, middle, middle, edge]));
        }

        return puzzles;
    }

    [Theory]
    [InlineData(1, GridSize.Tiny)]
    [InlineData(40, GridSize.Tiny)]
    [InlineData(41, GridSize.Normal)]
    [InlineData(200, GridSize.Normal)]
    [InlineData(201, GridSize.Big)]
    [InlineData(400, GridSize.Big)]
    [InlineData(401, GridSize.Huge)]
    [InlineData(600, GridSize.Huge)]
    public void TheBandsClimbThroughTheSizes(int level, int expectedSize)
    {
        Assert.Equal(expectedSize, LevelCatalog.SizeFor(level));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    [InlineData(601)]
    public void LevelsOutsideTheCampaignAreRejected(int level)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => LevelCatalog.SizeFor(level));
        Assert.Throws<ArgumentOutOfRangeException>(() => LevelCatalog.SeedFor(level));
        Assert.Throws<ArgumentOutOfRangeException>(() => LevelCatalog.Get(level, Authored()));
    }

    [Fact]
    public void DifficultyRampsFromOneToFiveInsideEachBand()
    {
        var bandEdges = new[] { (1, 40), (41, 200), (201, 400), (401, 600) };

        foreach (var (first, last) in bandEdges)
        {
            Assert.Equal(1, LevelCatalog.DifficultyFor(first));
            Assert.Equal(5, LevelCatalog.DifficultyFor(last));

            // Never easier as the band progresses - a dip would read as a broken ramp.
            for (var level = first + 1; level <= last; level++)
            {
                Assert.True(
                    LevelCatalog.DifficultyFor(level) >= LevelCatalog.DifficultyFor(level - 1),
                    $"Difficulty dips at level {level}.");
            }
        }
    }

    [Fact]
    public void TheSameLevelAlwaysPlaysTheSameGame()
    {
        var puzzles = Authored();

        foreach (var level in new[] { 1, 40, 41, 137, 600 })
        {
            Assert.Equal(LevelCatalog.Get(level, puzzles), LevelCatalog.Get(level, puzzles));
        }
    }

    [Fact]
    public void SeedsIgnoreTheContentList_SoNewArtNeverReshufflesGeneratedLevels()
    {
        // The seed is a pure function of the level number: computing it must not consult the
        // puzzle list at all.
        Assert.Equal(LevelCatalog.SeedFor(300), LevelCatalog.SeedFor(300));
        Assert.NotEqual(LevelCatalog.SeedFor(300), LevelCatalog.SeedFor(301));
    }

    [Fact]
    public void NeighbouringLevelsGetScatteredSeeds()
    {
        var seeds = new HashSet<int>();

        for (var level = 1; level <= LevelCatalog.LevelCount; level++)
        {
            Assert.True(seeds.Add(LevelCatalog.SeedFor(level)), $"Level {level} repeats an earlier seed.");
        }
    }

    [Fact]
    public void EveryAuthoredPictureAppearsExactlyOnce_InItsOwnSizeBand()
    {
        var puzzles = Fake(tiny: 6, normal: 6);
        var all = LevelCatalog.All(puzzles);

        Assert.Equal(LevelCatalog.LevelCount, all.Count);

        var milestones = all.Where(s => s.IsMilestone).ToList();

        Assert.Equal(puzzles.Count, milestones.Count);
        Assert.Equal(puzzles.Select(p => p.Id), milestones.Select(m => m.PuzzleId));

        foreach (var milestone in milestones)
        {
            var picture = puzzles.Single(p => p.Id == milestone.PuzzleId);
            Assert.Equal(picture.Width, milestone.Size);
        }
    }

    [Fact]
    public void MilestonesAreSpreadEvenlyThroughTheirBand()
    {
        // 6 pictures over 40 levels: stops every ~5-6 levels, never at level 1 and never
        // stacked together at the start or end.
        var all = LevelCatalog.All(Fake(tiny: 6, normal: 0));
        var stops = all.Where(s => s.IsMilestone).Select(s => s.Level).ToList();

        Assert.Equal(6, stops.Count);
        Assert.True(stops[0] > 1, "The campaign should open with a plain board, not a milestone.");

        for (var i = 1; i < stops.Count; i++)
        {
            var gap = stops[i] - stops[i - 1];
            Assert.InRange(gap, 4, 7);
        }
    }

    [Fact]
    public void NoAuthoredArt_MeansEveryLevelIsGenerated()
    {
        var all = LevelCatalog.All([]);

        Assert.Equal(LevelCatalog.LevelCount, all.Count);
        Assert.All(all, s => Assert.False(s.IsMilestone));
    }

    [Fact]
    public void MorePicturesThanLevelsInTheBand_StillPlacesEachLevelAtMostOnce()
    {
        // 60 tiny pictures into a 40-level band: only 40 can fit, and none may collide.
        var all = LevelCatalog.All(Fake(tiny: 60, normal: 0));
        var band = all.Where(s => s.Size == GridSize.Tiny).ToList();

        Assert.Equal(40, band.Count);
        Assert.Equal(40, band.Count(s => s.IsMilestone));
        Assert.Equal(40, band.Where(s => s.IsMilestone).Select(s => s.PuzzleId).Distinct().Count());
    }

    [Fact]
    public void TheLargeBandsNeverCarryMilestones()
    {
        // 15x15 and 20x20 have no authored art by definition (LargestAuthoredSize is 10), so a
        // picture id leaking into those bands would be a weaving bug.
        var all = LevelCatalog.All(Authored());

        Assert.All(all.Where(s => s.Size > GridSize.LargestAuthoredSize), s => Assert.False(s.IsMilestone));
    }

    [Fact]
    public void MilestoneOptions_PlayTheirPicture()
    {
        var puzzles = Authored();
        var milestone = LevelCatalog.All(puzzles).First(s => s.IsMilestone);
        var options = milestone.ToOptions(HelperSettings.Default);

        Assert.Equal(milestone.PuzzleId, options.PuzzleId);
        Assert.False(options.ForceGenerated);
        Assert.Equal(milestone.Level, options.Level);
        Assert.Equal(ChallengeLevel.Relaxed, options.Challenge);
    }

    [Fact]
    public void GeneratedLevelOptions_NeverBorrowAShippedPicture()
    {
        var puzzles = Authored();
        var all = LevelCatalog.All(puzzles);

        // Within the authored size range a generated level must force generation, or the
        // factory would quietly hand out a milestone's picture ahead of its stop.
        var generatedSmall = all.First(s => !s.IsMilestone && GridSize.IsAuthored(s.Size));
        Assert.True(generatedSmall.ToOptions(HelperSettings.Default).ForceGenerated);

        // Past the authored range the flag is irrelevant; what matters is the level and seed.
        var generatedLarge = all.First(s => s.Size > GridSize.LargestAuthoredSize);
        var options = generatedLarge.ToOptions(HelperSettings.Default);

        Assert.Equal(generatedLarge.Level, options.Level);
        Assert.Equal(generatedLarge.Seed, options.Seed);
        Assert.Equal(LevelCatalog.GeneratedPackId, options.PackId);
    }
}
