using Squarebuzz.Core.Content;
using Squarebuzz.Core.Model;
using Squarebuzz.Core.Progression;
using Xunit;

namespace Squarebuzz.Core.Tests.Progression;

/// <summary>Pins trophies 10-18: the timed trials, the big grids and the long campaign.</summary>
public class LaterTrophyTests
{
    private static readonly IReadOnlyList<Puzzle> AllPuzzles = new EmbeddedPuzzleRepository().Puzzles;

    private static readonly DateTimeOffset Afternoon = new(2026, 7, 30, 15, 0, 0, TimeSpan.Zero);

    private static PuzzleCompletion Completion(
        int size = GridSize.Tiny,
        int hints = 1,
        int mistakes = 1,
        int? tier = null,
        string? puzzleId = null) =>
        new(puzzleId, 2, TimeSpan.FromSeconds(200), 10, hints, Afternoon)
        {
            Size = size,
            PackId = "animals",
            Mistakes = mistakes,
            TimedTier = tier,
        };

    private static IReadOnlyList<TrophyId> Evaluate(
        PuzzleCompletion completion,
        PlayerProgress? progress = null,
        IEnumerable<string>? solvedIds = null) =>
        TrophyEvaluator.Evaluate(new TrophyContext(
            completion,
            progress ?? PlayerProgress.Empty with { Streak = 1, TotalBlocksFilled = 10, Stars = 2 },
            [.. (solvedIds ?? []).Select(id => new SolvedPuzzle(id, new DateOnly(2026, 7, 30), 2, TimeSpan.FromSeconds(200), 1))],
            AllPuzzles,
            new HashSet<TrophyId>()));

    [Fact]
    public void AnUntimedGame_EarnsNoClockTrophy()
    {
        var earned = Evaluate(Completion());

        Assert.DoesNotContain(TrophyId.BeatTheClock, earned);
        Assert.DoesNotContain(TrophyId.MarathonChamp, earned);
    }

    [Fact]
    public void WinningAnyTrial_BeatsTheClock()
    {
        var earned = Evaluate(Completion(tier: 1));

        Assert.Contains(TrophyId.BeatTheClock, earned);
        Assert.DoesNotContain(TrophyId.MarathonChamp, earned);
    }

    [Fact]
    public void WinningTheMarathon_EarnsBoth()
    {
        var earned = Evaluate(Completion(size: GridSize.Huge, tier: TimedTrial.MarathonTier));

        Assert.Contains(TrophyId.BeatTheClock, earned);
        Assert.Contains(TrophyId.MarathonChamp, earned);
    }

    [Theory]
    [InlineData(29, false)]
    [InlineData(30, true)]
    public void MonthStreak_NeedsThirtyDays(int streak, bool expected)
    {
        var earned = Evaluate(Completion(), PlayerProgress.Empty with { Streak = streak });

        Assert.Equal(expected, earned.Contains(TrophyId.MonthStreak));
    }

    [Theory]
    [InlineData(GridSize.Normal, false)]
    [InlineData(GridSize.Big, true)]
    [InlineData(GridSize.Giant, true)]
    public void BigPicture_StartsAtFifteen(int size, bool expected)
    {
        Assert.Equal(expected, Evaluate(Completion(size: size)).Contains(TrophyId.BigPicture));
    }

    [Theory]
    [InlineData(999, false)]
    [InlineData(1000, true)]
    public void ThousandBlocks_CountsEveryPuzzle(int blocks, bool expected)
    {
        var earned = Evaluate(Completion(), PlayerProgress.Empty with { TotalBlocksFilled = blocks });

        Assert.Equal(expected, earned.Contains(TrophyId.ThousandBlocks));
    }

    [Theory]
    [InlineData(99, false)]
    [InlineData(100, true)]
    public void StarGazer_NeedsAHundredStars(int stars, bool expected)
    {
        var earned = Evaluate(Completion(), PlayerProgress.Empty with { Stars = stars });

        Assert.Equal(expected, earned.Contains(TrophyId.StarGazer));
    }

    [Theory]
    [InlineData(49, false)]
    [InlineData(50, true)]
    public void Explorer_NeedsLevelFifty(int level, bool expected)
    {
        var earned = Evaluate(Completion(), PlayerProgress.Empty with { HighestLevelCompleted = level });

        Assert.Equal(expected, earned.Contains(TrophyId.Explorer));
    }

    [Fact]
    public void PackMaster_NeedsEveryPictureOfSomePack()
    {
        var ocean = AllPuzzles.Where(p => p.Pack == "ocean").Select(p => p.Id).ToList();

        Assert.DoesNotContain(TrophyId.PackMaster, Evaluate(Completion(), solvedIds: ocean.Skip(1)));
        Assert.Contains(TrophyId.PackMaster, Evaluate(Completion(), solvedIds: ocean));
    }

    [Theory]
    [InlineData(GridSize.Big, 0, 0, true)]
    [InlineData(GridSize.Big, 1, 0, false)]
    [InlineData(GridSize.Big, 0, 1, false)]
    [InlineData(GridSize.Normal, 0, 0, false)]
    public void Flawless_IsABigCleanSolve(int size, int hints, int mistakes, bool expected)
    {
        var earned = Evaluate(Completion(size: size, hints: hints, mistakes: mistakes));

        Assert.Equal(expected, earned.Contains(TrophyId.Flawless));
    }
}
