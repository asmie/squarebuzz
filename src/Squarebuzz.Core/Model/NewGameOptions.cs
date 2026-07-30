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
    /// save record. Ignored if no picture with that id ships.
    /// </remarks>
    public string? PuzzleId { get; init; }

    public static NewGameOptions Default { get; } = new(GridSize.Tiny, 2, "animals", ChallengeLevel.Relaxed);
}
