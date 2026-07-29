using Squarebuzz.Core.Model;

namespace Squarebuzz.Core.Abstractions;

/// <summary>Reads and writes the player's Options.</summary>
public interface ISettingsRepository
{
    /// <summary>Loads settings, returning <see cref="GameSettings.Default"/> on first run.</summary>
    Task<GameSettings> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(GameSettings settings, CancellationToken cancellationToken = default);
}
