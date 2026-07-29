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
    IReadOnlyList<Puzzle> Find(string packId, int size);

    Puzzle? FindById(string puzzleId);
}
