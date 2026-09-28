namespace Squarebuzz.Core.Model;

/// <summary>A picture the player has finished, as the Gallery and trophies need it.</summary>
/// <param name="PuzzleId">Authored puzzle identifier.</param>
/// <param name="FirstSolvedOn">When it was first completed - shown on the gallery card.</param>
/// <param name="BestStars">Best star rating achieved.</param>
/// <param name="BestTime">Fastest completion.</param>
/// <param name="TimesSolved">How often it has been completed.</param>
public sealed record SolvedPuzzle(
    string PuzzleId,
    DateOnly FirstSolvedOn,
    int BestStars,
    TimeSpan BestTime,
    int TimesSolved);

/// <summary>The trophies the game can award. Ids are persisted, so do not renumber them.</summary>
public enum TrophyId
{
    FirstPicture = 1,
    WeekStreak = 2,
    NoHints = 3,
    Speedy = 4,
    HundredBlocks = 5,
    DinoFan = 6,
    NightOwl = 7,
    PerfectTen = 8,
    Collector = 9,
    BeatTheClock = 10,
    MarathonChamp = 11,
    MonthStreak = 12,
    BigPicture = 13,
    ThousandBlocks = 14,
    StarGazer = 15,
    Explorer = 16,
    PackMaster = 17,
    Flawless = 18,
}

/// <summary>A trophy the player has earned.</summary>
public sealed record EarnedTrophy(TrophyId Trophy, DateOnly EarnedOn);

/// <summary>
/// The player's standing: their name, stars, streak, and what they have finished.
/// </summary>
public sealed record PlayerProgress
{
    public string PlayerName { get; init; } = string.Empty;

    /// <summary>Total stars collected across all completions.</summary>
    public int Stars { get; init; }

    /// <summary>Consecutive days played, as of <see cref="LastPlayedOn"/>.</summary>
    public int Streak { get; init; }

    public DateOnly? LastPlayedOn { get; init; }

    /// <summary>Total filled cells ever, which the "100 blocks" trophy counts.</summary>
    public int TotalBlocksFilled { get; init; }

    /// <summary>
    /// Latest daily puzzle date completed, so Trials knows whether today's is still open.
    /// </summary>
    public DateOnly? LastDailyCompletedOn { get; init; }

    /// <summary>
    /// Highest campaign level ever completed, or 0 before the first. Unlocking is strictly
    /// linear, so this single number is the whole of the Levels progress.
    /// </summary>
    public int HighestLevelCompleted { get; init; }

    public static PlayerProgress Empty { get; } = new();
}

/// <summary>
/// The result of finishing a puzzle, journaled before updating earned progress.
/// </summary>
/// <param name="PuzzleId">Authored puzzle id, or null for a generated one (not gallery-tracked).</param>
/// <param name="Stars">Stars awarded, 1-3.</param>
/// <param name="Elapsed">How long it took.</param>
/// <param name="BlocksFilled">Cells filled in the finished picture.</param>
/// <param name="HintsUsed">Hints spent.</param>
/// <param name="CompletedAt">When it was finished, for streaks and the night-owl trophy.</param>
public sealed record PuzzleCompletion(
    string? PuzzleId,
    int Stars,
    TimeSpan Elapsed,
    int BlocksFilled,
    int HintsUsed,
    DateTimeOffset CompletedAt)
{
    /// <summary>Grid width, which several trophies condition on.</summary>
    public int Size { get; init; }

    /// <summary>Pack the picture came from, for pack-completion trophies.</summary>
    public string PackId { get; init; } = string.Empty;

    /// <summary>True when this was the daily puzzle rather than a freely chosen one.</summary>
    public bool IsDaily { get; init; }

    /// <summary>
    /// The daily puzzle date to credit, even when finished later. Null for ordinary games and
    /// older journal entries, whose daily credit falls back to the completion date.
    /// </summary>
    public DateOnly? DailyDate { get; init; }

    /// <summary>Mistakes made. Zero is what "perfect" means for the trophies.</summary>
    public int Mistakes { get; init; }

    /// <summary>Campaign level this completion finished, or null outside the Levels mode.</summary>
    public int? Level { get; init; }

    /// <summary>Timed-trial rung this completion won, or null for an untimed game.</summary>
    public int? TimedTier { get; init; }
}
