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

    Task DeleteAllAsync(CancellationToken cancellationToken = default);

    Task<int> CountAsync(CancellationToken cancellationToken = default);
}
