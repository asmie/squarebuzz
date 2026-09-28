namespace Squarebuzz.Core.Model;

/// <summary>Snapshot of an unfinished puzzle.</summary>
/// <remarks>
/// Authored pictures are identified by ID and revision. Generated pictures are rebuilt
/// from their request, seed and generator version. Cells contain the player's marks.
/// </remarks>
public sealed record SavedGame
{
    public required Guid Id { get; init; }

    /// <summary>Authored puzzle this game is playing, or null when the puzzle was generated.</summary>
    public string? PuzzleId { get; init; }

    /// <summary>Authored-board revision. Saves written before versioning use the original revision 1.</summary>
    public int PuzzleRevision { get; init; } = 1;

    public required int Size { get; init; }

    public required int Difficulty { get; init; }

    public required string PackId { get; init; }

    /// <summary>Seed that reproduces the puzzle. Required for generated puzzles.</summary>
    public required int Seed { get; init; }

    public required ChallengeLevel Challenge { get; init; }

    /// <summary>The player's marks, row-major.</summary>
    public required IReadOnlyList<CellState> Cells { get; init; }

    /// <summary>Row-major automatic-mark flags. Empty for legacy saves with unknown provenance.</summary>
    public IReadOnlyList<bool> AutoCrossedCells { get; init; } = [];

    public required TimeSpan Elapsed { get; init; }

    public required int HintsRemaining { get; init; }

    /// <summary>Hints spent before saving. Independent of the current allowance because it affects scoring.</summary>
    public int HintsUsed { get; init; }

    /// <summary>Initial budget, including unlimited. Null identifies a legacy save.</summary>
    public HintBudget? HintBudget { get; init; }

    public required int Mistakes { get; init; }

    public required DateTimeOffset SavedAt { get; init; }

    /// <summary>Campaign level this game plays, or null for anything outside the Levels mode.</summary>
    public int? Level { get; init; }

    /// <summary>The original daily puzzle date, or null for ordinary games and legacy saves.</summary>
    public DateOnly? DailyDate { get; init; }

    /// <summary>
    /// Generation algorithm that produced this puzzle, or <see cref="GeneratorVersion.Unknown"/>
    /// for an authored one and for saves written before versioning existed.
    /// </summary>
    public int GeneratorVersion { get; init; } = Generation.GeneratorVersion.Unknown;

    public bool IsGenerated => PuzzleId is null;

    /// <summary>
    /// True when the picture this save refers to can still be reproduced.
    /// </summary>
    /// <remarks>
    /// Authored revisions are retained in shipped content; the repository must resolve the exact
    /// revision rather than substitute the latest board. A generated one only can while the
    /// generator still turns its seed into the same picture - see
    /// <see cref="Generation.GeneratorVersion"/>. Resuming a save that fails this test would put
    /// the player's marks on a board they never played.
    /// </remarks>
    public bool CanBeRebuilt => !IsGenerated || GeneratorVersion == Generation.GeneratorVersion.Current;

    /// <summary>Filled cells so far, for the "in progress" thumbnail on the Continue screen.</summary>
    public int FilledCount => Cells.Count(c => c == CellState.Filled);

    /// <summary>
    /// Captures a session so it can be resumed later. Requires the session to know what
    /// created it - see <see cref="GameSession.Origin"/> - so nothing has to be inferred.
    /// </summary>
    public static SavedGame FromSession(GameSession session, Guid id, DateTimeOffset savedAt)
    {
        ArgumentNullException.ThrowIfNull(session);

        var origin = session.Origin
                     ?? throw new ArgumentException(
                         "Session has no origin, so its difficulty and challenge cannot be recorded. " +
                         "Create sessions through GameSessionFactory rather than the bare constructor.",
                         nameof(session));

        // Timed games cannot be saved: the snapshot has no countdown or deadline.
        if (session.IsTimed)
        {
            throw new ArgumentException(
                "A timed session cannot be saved: SavedGame carries no time limit, so restoring it " +
                "would drop the countdown. Callers must skip the save for timed games, as " +
                "GameViewModel.AutosaveAsync does.",
                nameof(session));
        }

        return new SavedGame
        {
            Id = id,
            PuzzleId = origin.PuzzleId,
            PuzzleRevision = origin.PuzzleRevision,
            Size = session.Puzzle.Width,
            Difficulty = origin.Difficulty,
            PackId = origin.PackId,
            Seed = origin.Seed,
            Challenge = origin.Challenge,
            Cells = [.. session.Cells],
            AutoCrossedCells = [.. session.AutoCrossedCells],
            Elapsed = session.Elapsed,
            HintsRemaining = session.HintsRemaining,
            HintsUsed = session.HintsUsed,
            HintBudget = session.HintBudget,
            Mistakes = session.Mistakes,
            SavedAt = savedAt,
            Level = origin.Level,
            DailyDate = origin.DailyDate,

            // Only a generated picture depends on the algorithm; an authored one is content.
            GeneratorVersion = origin.GeneratorVersion,
        };
    }
}
