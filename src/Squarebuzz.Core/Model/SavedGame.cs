namespace Squarebuzz.Core.Model;

/// <summary>
/// A puzzle the player left unfinished.
/// </summary>
/// <remarks>
/// The picture itself is never stored - only how to obtain it again. Authored puzzles are
/// referenced by <see cref="PuzzleId"/>; generated ones are rebuilt from
/// <see cref="Seed"/> and the request that produced them, which is exactly what
/// <see cref="Generation.DeterministicRandom"/> and the seeded generator make possible. Only
/// the player's own marks need persisting.
/// </remarks>
public sealed record SavedGame
{
    public required Guid Id { get; init; }

    /// <summary>Authored puzzle this game is playing, or null when the puzzle was generated.</summary>
    public string? PuzzleId { get; init; }

    public required int Size { get; init; }

    public required int Difficulty { get; init; }

    public required string PackId { get; init; }

    /// <summary>Seed that reproduces the puzzle. Required for generated puzzles.</summary>
    public required int Seed { get; init; }

    public required ChallengeLevel Challenge { get; init; }

    /// <summary>The player's marks, row-major.</summary>
    public required IReadOnlyList<CellState> Cells { get; init; }

    public required TimeSpan Elapsed { get; init; }

    public required int HintsRemaining { get; init; }

    /// <summary>
    /// Hints actually spent. Stored rather than derived from the allowance: the allowance can
    /// change between saving and resuming - the player only has to switch hints off in Options -
    /// and the stars must keep charging for help that was really taken. See
    /// <see cref="GameSession.HintsUsed"/>.
    /// </summary>
    public int HintsUsed { get; init; }

    public required int Mistakes { get; init; }

    public required DateTimeOffset SavedAt { get; init; }

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
    /// An authored puzzle is shipped content, so it always can. A generated one only can while the
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

        return new SavedGame
        {
            Id = id,
            PuzzleId = session.Puzzle.IsGenerated ? null : session.Puzzle.Id,
            Size = session.Puzzle.Width,
            Difficulty = origin.Difficulty,
            PackId = origin.PackId,
            Seed = session.Seed,
            Challenge = origin.Challenge,
            Cells = [.. session.Cells],
            Elapsed = session.Elapsed,
            HintsRemaining = session.HintsRemaining,
            HintsUsed = session.HintsUsed,
            Mistakes = session.Mistakes,
            SavedAt = savedAt,

            // Only a generated picture depends on the algorithm; an authored one is content.
            GeneratorVersion = session.Puzzle.IsGenerated
                ? Generation.GeneratorVersion.Current
                : Generation.GeneratorVersion.Unknown,
        };
    }
}
