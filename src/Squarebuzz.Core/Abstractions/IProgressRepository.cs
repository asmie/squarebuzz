using Squarebuzz.Core.Model;

namespace Squarebuzz.Core.Abstractions;

/// <summary>Stars, streak, solved pictures and trophies.</summary>
public interface IProgressRepository
{
    Task<PlayerProgress> GetProgressAsync(CancellationToken cancellationToken = default);

    Task SaveProgressAsync(PlayerProgress progress, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SolvedPuzzle>> GetSolvedPuzzlesAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EarnedTrophy>> GetTrophiesAsync(CancellationToken cancellationToken = default);

    Task AwardTrophyAsync(TrophyId trophy, DateOnly earnedOn, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a finished puzzle: adds its stars, updates the streak, and merges it into the
    /// solved list keeping the best result. Returns the progress after the update.
    /// </summary>
    Task<PlayerProgress> RecordCompletionAsync(PuzzleCompletion completion, CancellationToken cancellationToken = default);

    /// <summary>
    /// Erases everything - the parent zone's "reset progress". Settings are left alone, so a
    /// wipe does not also undo accessibility choices.
    /// </summary>
    Task ResetAsync(CancellationToken cancellationToken = default);
}
