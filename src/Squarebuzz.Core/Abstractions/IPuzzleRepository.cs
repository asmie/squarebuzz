using Squarebuzz.Core.Model;

namespace Squarebuzz.Core.Abstractions;

/// <summary>
/// Source of the authored pictures that ship with the game. Read-only: player data lives
/// behind separate interfaces so shipped content and saved progress never mix.
/// </summary>
public interface IPuzzleRepository
{
    IReadOnlyList<PackDefinition> Packs { get; }

    IReadOnlyList<Puzzle> Puzzles { get; }

    /// <summary>
    /// Pictures of exactly <paramref name="size"/> belonging to <paramref name="packId"/>.
    /// A wildcard pack matches every unlocked pack. Returns an empty list when nothing
    /// matches, so callers can fall back to generation.
    /// </summary>
    /// <param name="unlockedPackIds">
    /// Packs the player has earned, which the wildcard should also draw from. Null means "judge
    /// by the shipped flag alone". Content and progress are kept apart deliberately - this type
    /// only reads shipped content - so whether a locked pack has been earned is something the
    /// caller has to tell it. See <c>Progression.PackUnlocks</c> for the rule.
    /// </param>
    IReadOnlyList<Puzzle> Find(string packId, int size, IReadOnlySet<string>? unlockedPackIds = null);

    Puzzle? FindById(string puzzleId);
}
