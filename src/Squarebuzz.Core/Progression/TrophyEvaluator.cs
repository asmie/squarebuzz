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

/// <summary>
/// Decides which trophies a completion has just earned.
/// </summary>
/// <remarks>
/// <para>
/// The prototype named nine trophies but never defined what wins them, so the thresholds below
/// are a first proposal, gathered here deliberately so they are easy to argue with and change.
/// Each is a named constant with the reasoning next to it.
/// </para>
/// <para>
/// Pure and static: it reads a snapshot and returns what was earned, leaving awarding and
/// persistence to the caller. Nothing here needs injecting, and the rules are cheap to test
/// precisely because they touch no state of their own.
/// </para>
/// </remarks>
public static class TrophyEvaluator
{
    /// <summary>A week of consecutive days. The trophy is called "Week Streak".</summary>
    public const int WeekStreakDays = 7;

    /// <summary>
    /// "Speedy" targets a 5x5. A minute is comfortable for an adult and a real push for a child,
    /// which is roughly where a trophy should sit.
    /// </summary>
    public const int SpeedySeconds = 60;
    public const int SpeedySize = GridSize.Tiny;

    /// <summary>"100 Blocks" counts filled cells across every puzzle ever finished.</summary>
    public const int HundredBlocks = 100;

    /// <summary>
    /// "Perfect Ten" reads as a perfect 10x10: three stars, and no mistakes either, so it means
    /// a genuinely clean solve rather than one that scraped three stars.
    /// </summary>
    public const int PerfectTenSize = GridSize.Normal;

    /// <summary>Night Owl: finishing late in the evening or very early morning.</summary>
    public const int NightOwlFromHour = 20;
    public const int NightOwlUntilHour = 6;

    /// <summary>The pack "Dino Fan" is about.</summary>
    public const string DinoPackId = "dinos";

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
