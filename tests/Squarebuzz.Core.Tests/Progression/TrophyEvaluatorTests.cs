using Squarebuzz.Core.Content;
using Squarebuzz.Core.Model;
using Squarebuzz.Core.Progression;
using Xunit;

namespace Squarebuzz.Core.Tests.Progression;

/// <summary>
/// Pins the nine trophy rules. These are product decisions rather than derived facts, so each
/// test states the rule it is holding in place.
/// </summary>
public class TrophyEvaluatorTests
{
    private static readonly IReadOnlyList<Puzzle> AllPuzzles = new EmbeddedPuzzleRepository().Puzzles;

    /// <summary>Mid-afternoon, so Night Owl does not fire unless a test asks for it.</summary>
    private static readonly DateTimeOffset Afternoon = new(2026, 7, 30, 15, 0, 0, TimeSpan.Zero);

    private static PuzzleCompletion Completion(
        string? puzzleId = "heart",
        int stars = 2,
        double seconds = 200,
        int blocks = 10,
        int hints = 1,
        int mistakes = 1,
        int size = GridSize.Tiny,
        string pack = "animals",
        DateTimeOffset? at = null) =>
        new(puzzleId, stars, TimeSpan.FromSeconds(seconds), blocks, hints, at ?? Afternoon)
        {
            Size = size,
            PackId = pack,
            Mistakes = mistakes,
        };

    private static TrophyContext Context(
        PuzzleCompletion completion,
        PlayerProgress? progress = null,
        IEnumerable<string>? solvedIds = null,
        IEnumerable<TrophyId>? alreadyEarned = null) =>
        new(
            completion,
            progress ?? PlayerProgress.Empty with { Streak = 1, TotalBlocksFilled = completion.BlocksFilled },
            [.. (solvedIds ?? [completion.PuzzleId ?? "heart"]).Select(id =>
                new SolvedPuzzle(id, new DateOnly(2026, 7, 30), 2, TimeSpan.FromSeconds(200), 1))],
            AllPuzzles,
            (alreadyEarned ?? []).ToHashSet());

    [Fact]
    public void FinishingAnythingEarnsFirstPicture()
    {
        var earned = TrophyEvaluator.Evaluate(Context(Completion()));

        Assert.Contains(TrophyId.FirstPicture, earned);
    }

    [Fact]
    public void TrophiesAlreadyHeldAreNotAwardedAgain()
    {
        var earned = TrophyEvaluator.Evaluate(
            Context(Completion(), alreadyEarned: [TrophyId.FirstPicture]));

        Assert.DoesNotContain(TrophyId.FirstPicture, earned);
    }

    [Theory]
    [InlineData(6, false)]
    [InlineData(7, true)]
    [InlineData(30, true)]
    public void WeekStreakNeedsSevenConsecutiveDays(int streak, bool expected)
    {
        var earned = TrophyEvaluator.Evaluate(Context(
            Completion(),
            progress: PlayerProgress.Empty with { Streak = streak }));

        Assert.Equal(expected, earned.Contains(TrophyId.WeekStreak));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    public void NoHintsMeansNoneAtAll(int hintsUsed, bool expected)
    {
        var earned = TrophyEvaluator.Evaluate(Context(Completion(hints: hintsUsed)));

        Assert.Equal(expected, earned.Contains(TrophyId.NoHints));
    }

    [Theory]
    [InlineData(GridSize.Tiny, 45, true)]
    [InlineData(GridSize.Tiny, 60, true)]      // The threshold itself counts.
    [InlineData(GridSize.Tiny, 61, false)]
    [InlineData(GridSize.Normal, 20, false)]   // A fast 10x10 is not what Speedy is about.
    public void SpeedyIsAFastFiveByFive(int size, double seconds, bool expected)
    {
        var earned = TrophyEvaluator.Evaluate(Context(Completion(size: size, seconds: seconds)));

        Assert.Equal(expected, earned.Contains(TrophyId.Speedy));
    }

    [Theory]
    [InlineData(99, false)]
    [InlineData(100, true)]
    public void HundredBlocksCountsTheRunningTotal(int total, bool expected)
    {
        var earned = TrophyEvaluator.Evaluate(Context(
            Completion(),
            progress: PlayerProgress.Empty with { TotalBlocksFilled = total }));

        Assert.Equal(expected, earned.Contains(TrophyId.HundredBlocks));
    }

    [Fact]
    public void DinoFanNeedsEveryPictureInTheDinosPack()
    {
        var dinoIds = AllPuzzles.Where(p => p.Pack == "dinos").Select(p => p.Id).ToList();
        Assert.NotEmpty(dinoIds);

        var without = TrophyEvaluator.Evaluate(Context(Completion(), solvedIds: ["heart"]));
        Assert.DoesNotContain(TrophyId.DinoFan, without);

        var with = TrophyEvaluator.Evaluate(Context(Completion(), solvedIds: dinoIds));
        Assert.Contains(TrophyId.DinoFan, with);
    }

    [Theory]
    [InlineData(19, false)]
    [InlineData(20, true)]
    [InlineData(23, true)]
    [InlineData(5, true)]
    [InlineData(6, false)]
    [InlineData(12, false)]
    public void NightOwlSpansTheEveningIntoTheEarlyMorning(int hour, bool expected)
    {
        // Constructed as local time, because the rule is about the player's own clock.
        var at = new DateTimeOffset(new DateTime(2026, 7, 30, hour, 0, 0, DateTimeKind.Local));

        var earned = TrophyEvaluator.Evaluate(Context(Completion(at: at)));

        Assert.Equal(expected, earned.Contains(TrophyId.NightOwl));
    }

    [Theory]
    [InlineData(GridSize.Normal, 3, 0, true)]
    [InlineData(GridSize.Normal, 3, 1, false)]   // Three stars but a mistake is not "perfect".
    [InlineData(GridSize.Normal, 2, 0, false)]
    [InlineData(GridSize.Tiny, 3, 0, false)]     // Must be the 10x10.
    public void PerfectTenIsAFlawlessTenByTen(int size, int stars, int mistakes, bool expected)
    {
        var earned = TrophyEvaluator.Evaluate(Context(
            Completion(size: size, stars: stars, mistakes: mistakes)));

        Assert.Equal(expected, earned.Contains(TrophyId.PerfectTen));
    }

    [Fact]
    public void CollectorNeedsEveryShippedPicture()
    {
        var partial = TrophyEvaluator.Evaluate(Context(Completion(), solvedIds: ["heart", "fish"]));
        Assert.DoesNotContain(TrophyId.Collector, partial);

        var everything = TrophyEvaluator.Evaluate(Context(
            Completion(),
            solvedIds: AllPuzzles.Select(p => p.Id)));

        Assert.Contains(TrophyId.Collector, everything);
    }

    [Fact]
    public void CollectorIncludesLockedPacks()
    {
        // Everything except the locked fairy pack must not be enough - the collection is the
        // whole set, so a trophy that ignored part of it would be misnamed.
        var withoutFairy = AllPuzzles.Where(p => p.Pack != "fairy").Select(p => p.Id);

        var earned = TrophyEvaluator.Evaluate(Context(Completion(), solvedIds: withoutFairy));

        Assert.DoesNotContain(TrophyId.Collector, earned);
    }

    [Fact]
    public void AFlawlessRunCanEarnSeveralAtOnce()
    {
        // A clean, fast, complete-the-set finish late at night should award everything it meets,
        // not just the first rule that matches.
        var at = new DateTimeOffset(new DateTime(2026, 7, 30, 22, 0, 0, DateTimeKind.Local));

        var earned = TrophyEvaluator.Evaluate(Context(
            Completion(size: GridSize.Tiny, stars: 3, seconds: 30, hints: 0, mistakes: 0, at: at),
            progress: PlayerProgress.Empty with { Streak = 7, TotalBlocksFilled = 150 },
            solvedIds: AllPuzzles.Select(p => p.Id)));

        Assert.Contains(TrophyId.FirstPicture, earned);
        Assert.Contains(TrophyId.WeekStreak, earned);
        Assert.Contains(TrophyId.NoHints, earned);
        Assert.Contains(TrophyId.Speedy, earned);
        Assert.Contains(TrophyId.HundredBlocks, earned);
        Assert.Contains(TrophyId.NightOwl, earned);
        Assert.Contains(TrophyId.Collector, earned);
        Assert.Contains(TrophyId.DinoFan, earned);

        // Not this one: it was a 5x5, not a 10x10.
        Assert.DoesNotContain(TrophyId.PerfectTen, earned);
    }

    [Fact]
    public void EveryTrophyIsReachable()
    {
        // Guards against a rule written so tightly that nothing can ever satisfy it.
        var reachable = new HashSet<TrophyId>();
        var lateNight = new DateTimeOffset(new DateTime(2026, 7, 30, 22, 0, 0, DateTimeKind.Local));

        foreach (var trophy in TrophyEvaluator.Evaluate(Context(
            Completion(size: GridSize.Tiny, stars: 3, seconds: 30, hints: 0, mistakes: 0, at: lateNight),
            progress: PlayerProgress.Empty with { Streak = 7, TotalBlocksFilled = 150 },
            solvedIds: AllPuzzles.Select(p => p.Id))))
        {
            reachable.Add(trophy);
        }

        foreach (var trophy in TrophyEvaluator.Evaluate(Context(
            Completion(size: GridSize.Normal, stars: 3, mistakes: 0))))
        {
            reachable.Add(trophy);
        }

        Assert.Equal(Enum.GetValues<TrophyId>().Length, reachable.Count);
    }
}
