using Squarebuzz.Core.Model;

namespace Squarebuzz.Core.Progression;

/// <summary>Everything the trophy rules are allowed to look at, as of just after a completion.</summary>
/// <param name="Completion">The puzzle that was just finished.</param>
/// <param name="Progress">Player standing, already updated with this completion.</param>
/// <param name="Solved">Pictures completed, already including this one.</param>
/// <param name="AllPuzzles">Every authored picture that ships, for the collection trophies.</param>
/// <param name="AlreadyEarned">Trophies held before this completion.</param>
public sealed record TrophyContext(
    PuzzleCompletion Completion,
    PlayerProgress Progress,
    IReadOnlyList<SolvedPuzzle> Solved,
    IReadOnlyList<Puzzle> AllPuzzles,
    IReadOnlySet<TrophyId> AlreadyEarned);

/// <summary>Evaluates newly earned trophies from a completion snapshot. Persistence is handled by the caller.</summary>
public static class TrophyEvaluator
{
    /// <summary>A week of consecutive days. The trophy is called "Week Streak".</summary>
    public const int WeekStreakDays = 7;

    /// <summary>Maximum solve duration for the Speedy 5x5 trophy.</summary>
    public const int SpeedySeconds = 60;
    public const int SpeedySize = GridSize.Tiny;

    /// <summary>"100 Blocks" counts filled cells across every puzzle ever finished.</summary>
    public const int HundredBlocks = 100;

    /// <summary>Perfect Ten requires a 10x10 completion with three stars and no mistakes.</summary>
    public const int PerfectTenSize = GridSize.Normal;

    /// <summary>Night Owl: finishing late in the evening or very early morning.</summary>
    public const int NightOwlFromHour = 20;
    public const int NightOwlUntilHour = 6;

    /// <summary>The pack "Dino Fan" is about.</summary>
    public const string DinoPackId = "dinos";

    /// <summary>A month of consecutive days, the long sibling of "Week Streak".</summary>
    public const int MonthStreakDays = 30;

    /// <summary>
    /// "Big Picture" and "Flawless" start at the first size with no authored pictures: a 15x15 is
    /// where a puzzle stops being a quick one.
    /// </summary>
    public const int BigPictureSize = GridSize.Big;

    /// <summary>"1000 Blocks", ten times "100 Blocks".</summary>
    public const int ThousandBlocks = 1000;

    /// <summary>Total stars required for Star Gazer.</summary>
    public const int StarGazerStars = 100;

    /// <summary>
    /// "Explorer": far enough into the campaign to have left the 5x5 band (levels 1-40) behind.
    /// </summary>
    public const int ExplorerLevel = 50;

    /// <summary>
    /// Evaluates every rule and returns only newly earned trophies, so the caller can award
    /// blindly without checking what is already held.
    /// </summary>
    public static IReadOnlyList<TrophyId> Evaluate(TrophyContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var earned = new List<TrophyId>();

        void Award(TrophyId trophy, bool condition)
        {
            if (condition && !context.AlreadyEarned.Contains(trophy) && !earned.Contains(trophy))
            {
                earned.Add(trophy);
            }
        }

        var completion = context.Completion;

        // Any finished puzzle at all - the first thing a new player should be given.
        Award(TrophyId.FirstPicture, true);

        Award(TrophyId.WeekStreak, context.Progress.Streak >= WeekStreakDays);

        // Unaided, not merely hint-light.
        Award(TrophyId.NoHints, completion.HintsUsed == 0);

        Award(
            TrophyId.Speedy,
            completion.Size == SpeedySize && completion.Elapsed.TotalSeconds <= SpeedySeconds);

        Award(TrophyId.HundredBlocks, context.Progress.TotalBlocksFilled >= HundredBlocks);

        Award(TrophyId.DinoFan, HasCompletedPack(context, DinoPackId));

        Award(TrophyId.NightOwl, IsLateNight(completion.CompletedAt));

        Award(
            TrophyId.PerfectTen,
            completion.Size == PerfectTenSize && completion.Stars == 3 && completion.Mistakes == 0);

        Award(TrophyId.Collector, HasCompletedEverything(context));

        // Any rung of the ladder, won before the clock ran out - a lost trial never completes.
        Award(TrophyId.BeatTheClock, completion.TimedTier is not null);

        Award(TrophyId.MarathonChamp, completion.TimedTier == TimedTrial.MarathonTier);

        Award(TrophyId.MonthStreak, context.Progress.Streak >= MonthStreakDays);

        Award(TrophyId.BigPicture, completion.Size >= BigPictureSize);

        Award(TrophyId.ThousandBlocks, context.Progress.TotalBlocksFilled >= ThousandBlocks);

        Award(TrophyId.StarGazer, context.Progress.Stars >= StarGazerStars);

        Award(TrophyId.Explorer, context.Progress.HighestLevelCompleted >= ExplorerLevel);

        Award(TrophyId.PackMaster, HasCompletedAnyPack(context));

        // Stricter than "Perfect Ten": no hint at all, not merely few enough to keep three stars.
        Award(
            TrophyId.Flawless,
            completion.Size >= BigPictureSize && completion.HintsUsed == 0 && completion.Mistakes == 0);

        return earned;
    }

    /// <summary>Every authored picture in a pack finished at least once.</summary>
    private static bool HasCompletedPack(TrophyContext context, string packId)
    {
        var inPack = context.AllPuzzles
            .Where(p => string.Equals(p.Pack, packId, StringComparison.Ordinal))
            .Select(p => p.Id)
            .ToList();

        if (inPack.Count == 0)
        {
            // No content in that pack, so the trophy is unobtainable rather than free.
            return false;
        }

        var solvedIds = context.Solved.Select(s => s.PuzzleId).ToHashSet(StringComparer.Ordinal);

        return inPack.All(solvedIds.Contains);
    }

    /// <summary>Every authored picture of at least one pack finished.</summary>
    private static bool HasCompletedAnyPack(TrophyContext context) =>
        context.AllPuzzles
            .Select(p => p.Pack)
            .Distinct(StringComparer.Ordinal)
            .Any(pack => HasCompletedPack(context, pack));

    /// <summary>
    /// Every shipped picture found. Locked packs are included on purpose: the collection is the
    /// whole set, and a trophy that ignored part of it would be misnamed.
    /// </summary>
    private static bool HasCompletedEverything(TrophyContext context)
    {
        if (context.AllPuzzles.Count == 0)
        {
            return false;
        }

        var solvedIds = context.Solved.Select(s => s.PuzzleId).ToHashSet(StringComparer.Ordinal);

        return context.AllPuzzles.All(p => solvedIds.Contains(p.Id));
    }

    /// <summary>
    /// Late evening through to early morning, using the player's local time - a trophy about
    /// bedtime has to mean their bedtime, not UTC.
    /// </summary>
    private static bool IsLateNight(DateTimeOffset completedAt)
    {
        var hour = completedAt.LocalDateTime.Hour;

        return hour >= NightOwlFromHour || hour < NightOwlUntilHour;
    }
}
