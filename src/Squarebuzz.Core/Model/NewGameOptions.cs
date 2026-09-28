namespace Squarebuzz.Core.Model;

/// <summary>
/// Everything the player chose on the New Game screen.
/// </summary>
/// <param name="Size">Grid width and height in cells. One of <see cref="GridSize.All"/>.</param>
/// <param name="Difficulty">Clue difficulty, 1-5. Only affects generated puzzles.</param>
/// <param name="PackId">Picture pack, or the wildcard pack for a surprise.</param>
/// <param name="Challenge">Whether helpers are on.</param>
public sealed record NewGameOptions(
    int Size,
    int Difficulty,
    string PackId,
    ChallengeLevel Challenge)
{
    /// <summary>The player's helper preferences from Options.</summary>
    public HelperSettings Helpers { get; init; } = HelperSettings.Default;

    /// <summary>Frozen budget for a restart; null lets a new game use the current preference.</summary>
    public HintBudget? HintBudget { get; init; }

    /// <summary>
    /// Fixes which picture is chosen or generated. Leave null for a fresh random puzzle;
    /// set it to reproduce a specific one, as the daily puzzle and saved games do.
    /// </summary>
    public int? Seed { get; init; }

    /// <summary>
    /// Play this exact authored picture rather than letting the size and pack choose one.
    /// </summary>
    /// <remarks>
    /// Set by the Gallery, where the player picks a specific picture. Takes precedence over
    /// <see cref="PackId"/> and <see cref="Size"/>, which are then only carried through for the
    /// save record. A missing id is ignored unless <see cref="PuzzleRevision"/> pins an exact board.
    /// </remarks>
    public string? PuzzleId { get; init; }

    /// <summary>Exact authored revision for a restart; null selects the current artwork.</summary>
    public int? PuzzleRevision { get; init; }

    /// <summary>Forces generation at sizes that also have authored artwork, as daily puzzles require.</summary>
    public bool ForceGenerated { get; init; }

    /// <summary>Trial countdown duration. Null means untimed; zero means an immediate deadline.</summary>
    public TimeSpan? TimeLimit { get; init; }

    /// <summary>
    /// Which rung of the timed-trial ladder this is, or null outside a trial. Carried through to
    /// the completion so trophies can tell one race from another.
    /// </summary>
    public int? TimedTier { get; init; }

    /// <summary>
    /// The campaign level this game plays, or null for anything outside the Levels mode.
    /// </summary>
    /// <remarks>
    /// Carried in the options so it flows into the session's origin and from there into a save
    /// without any extra plumbing - a resumed level game still knows which level it is.
    /// </remarks>
    public int? Level { get; init; }

    /// <summary>The daily puzzle's date, or null for a game outside daily mode.</summary>
    public DateOnly? DailyDate { get; init; }

    /// <summary>
    /// A picture the factory should avoid handing out, or null to allow any.
    /// </summary>
    /// <remarks>
    /// Set by the win screen's "Next": serving the picture the player just solved again feels
    /// broken. Best effort - when it is the only candidate, a repeat beats a failure.
    /// </remarks>
    public string? ExcludePuzzleId { get; init; }

    public static NewGameOptions Default { get; } = new(GridSize.Tiny, 2, "animals", ChallengeLevel.Relaxed);
}
