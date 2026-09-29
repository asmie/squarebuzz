using Squarebuzz.Core.Model;

namespace Squarebuzz.Core.Abstractions;

/// <summary>Stars, streak, solved pictures and trophies.</summary>
public interface IProgressRepository
{
    Task<PlayerProgress> GetProgressAsync(CancellationToken cancellationToken = default);

    Task SaveProgressAsync(PlayerProgress progress, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SolvedPuzzle>> GetSolvedPuzzlesAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EarnedTrophy>> GetTrophiesAsync(CancellationToken cancellationToken = default);

    /// <summary>Every day whose daily puzzle was finished, for the Trials calendar.</summary>
    Task<IReadOnlyList<DateOnly>> GetDailyCompletionsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Erases everything - the parent zone's "reset progress". Settings are left alone, so a
    /// wipe does not also undo accessibility choices.
    /// </summary>
    Task ResetAsync(CancellationToken cancellationToken = default);
}
