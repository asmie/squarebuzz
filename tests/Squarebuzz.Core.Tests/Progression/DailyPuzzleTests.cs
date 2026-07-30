using Squarebuzz.Core.Content;
using Squarebuzz.Core.Generation;
using Squarebuzz.Core.Model;
using Squarebuzz.Core.Progression;
using Squarebuzz.Core.Solving;
using Xunit;

namespace Squarebuzz.Core.Tests.Progression;

public class DailyPuzzleTests
{
    private static GameSessionFactory NewFactory() =>
        new(new EmbeddedPuzzleRepository(), new UniqueSolutionGenerator(new BlobPuzzleGenerator()));

    [Fact]
    public void TheSameDayAlwaysGivesTheSameSeed()
    {
        // What makes the daily shared between players and resumable after a restart.
        var date = new DateOnly(2026, 7, 30);

        Assert.Equal(DailyPuzzle.SeedFor(date), DailyPuzzle.SeedFor(date));
    }

    [Fact]
    public void ConsecutiveDaysGiveUnrelatedSeeds()
    {
        var seeds = Enumerable.Range(0, 30)
            .Select(offset => DailyPuzzle.SeedFor(new DateOnly(2026, 7, 1).AddDays(offset)))
            .ToList();

        // All distinct, and not a simple ascending run - neighbouring days should not produce
        // near-identical pictures.
        Assert.Equal(seeds.Count, seeds.Distinct().Count());
        Assert.False(seeds.Zip(seeds.Skip(1)).All(pair => pair.Second == pair.First + 1));
    }

    [Fact]
    public void SeedsAreAlwaysPositive()
    {
        // DeterministicRandom takes an int; a negative seed still works, but a positive one keeps
        // the value readable in logs and saved rows.
        for (var offset = -400; offset < 400; offset += 7)
        {
            var seed = DailyPuzzle.SeedFor(new DateOnly(2026, 7, 30).AddDays(offset));
            Assert.True(seed >= 0, $"Seed for offset {offset} was {seed}.");
        }
    }

    [Theory]
    [InlineData(2026, 7, 30)]
    [InlineData(2026, 12, 31)]
    [InlineData(2027, 1, 1)]
    public void TheDailyIsAlwaysASolvablePuzzle(int year, int month, int day)
    {
        var options = DailyPuzzle.OptionsFor(new DateOnly(year, month, day), HelperSettings.Default);

        var session = NewFactory().Create(options);

        Assert.True(session.Puzzle.IsGenerated);
        Assert.Equal(DailyPuzzle.Size, session.Puzzle.Width);
        Assert.True(PuzzleSolver.Analyse(session.Puzzle).IsSolvable);
    }

    [Fact]
    public void TheDailyIsReproducibleFromItsDateAlone()
    {
        var date = new DateOnly(2026, 8, 15);
        var factory = NewFactory();

        var first = factory.Create(DailyPuzzle.OptionsFor(date, HelperSettings.Default));
        var second = factory.Create(DailyPuzzle.OptionsFor(date, HelperSettings.Default));

        Assert.True(first.Puzzle.Solution.SequenceEqual(second.Puzzle.Solution));
    }

    [Fact]
    public void DifferentDaysGiveDifferentPictures()
    {
        var factory = NewFactory();

        var monday = factory.Create(DailyPuzzle.OptionsFor(new DateOnly(2026, 8, 3), HelperSettings.Default));
        var tuesday = factory.Create(DailyPuzzle.OptionsFor(new DateOnly(2026, 8, 4), HelperSettings.Default));

        Assert.False(monday.Puzzle.Solution.SequenceEqual(tuesday.Puzzle.Solution));
    }

    [Fact]
    public void AvailabilityTurnsOnTheDayItWasLastCompleted()
    {
        var today = new DateOnly(2026, 7, 30);

        Assert.True(DailyPuzzle.IsAvailable(PlayerProgress.Empty, today));
        Assert.True(DailyPuzzle.IsAvailable(
            PlayerProgress.Empty with { LastDailyCompletedOn = today.AddDays(-1) },
            today));
        Assert.False(DailyPuzzle.IsAvailable(
            PlayerProgress.Empty with { LastDailyCompletedOn = today },
            today));
    }

    [Fact]
    public void TheDailyUsesTheRelaxedRuleset()
    {
        // A once-a-day puzzle every player sees should not be the strict variant.
        var options = DailyPuzzle.OptionsFor(new DateOnly(2026, 7, 30), HelperSettings.Default);

        Assert.Equal(ChallengeLevel.Relaxed, options.Challenge);
    }
}
