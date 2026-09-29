using Squarebuzz.Core.Model;

namespace Squarebuzz.Core.Abstractions;

/// <summary>Stores unfinished puzzles for the Continue screen.</summary>
public interface ISaveGameRepository
{
    /// <summary>All saves, most recently played first.</summary>
    Task<IReadOnlyList<SavedGame>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<SavedGame?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Inserts or replaces a save.</summary>
    Task SaveAsync(SavedGame game, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    Task<int> CountAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes saves whose picture can no longer be reproduced, returning how many went.
    /// </summary>
    /// <remarks>
    /// A generated save holds a seed, not the picture, so it stops meaning anything once the
    /// generator changes - see <see cref="Model.SavedGame.CanBeRebuilt"/>. Run once at startup
    /// rather than filtered at each call site, so that the count on the menu and the list on the
    /// Continue screen cannot disagree about how many games are waiting.
    /// </remarks>
    Task<int> PurgeUnrebuildableAsync(CancellationToken cancellationToken = default);
}
