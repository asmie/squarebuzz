using Squarebuzz.Core.Model;

namespace Squarebuzz.Core.Abstractions;

/// <summary>Reads and writes the player's Options.</summary>
public interface ISettingsRepository
{
    /// <summary>Loads settings, returning <see cref="GameSettings.Default"/> on first run.</summary>
    Task<GameSettings> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(GameSettings settings, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes only the settings that differ between <paramref name="baseline"/> and
    /// <paramref name="updated"/>, leaving every other stored value alone.
    /// </summary>
    /// <remarks>
    /// For screens that change one or two values of a snapshot they may not have been able to
    /// read. After a failed read the baseline is <see cref="GameSettings.Default"/>, and writing
    /// the whole snapshot back would silently reset the language, the helpers and the parent's
    /// screen-time limit. Writing only the difference also stops two screens that each hold an
    /// older snapshot from undoing each other's changes.
    /// </remarks>
    Task SaveChangesAsync(GameSettings baseline, GameSettings updated, CancellationToken cancellationToken = default);
}
