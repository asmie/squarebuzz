namespace Squarebuzz.Core.Generation;

/// <summary>
/// Identifies the generation algorithm, so a saved game can tell whether the picture it was
/// playing still exists.
/// </summary>
/// <remarks>
/// <para>
/// A save of a generated puzzle stores a <em>seed</em> rather than the picture, and rebuilds the
/// picture on resume. That is the right trade - a 25x25 grid is 625 cells against one integer -
/// but it means the save is only meaningful while the generator keeps turning that seed into the
/// same picture. Change the algorithm and the marks come back attached to a different board:
/// nothing crashes, the player's work is simply wrong against clues they never saw.
/// </para>
/// <para>
/// <b>Bump <see cref="Current"/> whenever a change alters what any seed produces.</b> That
/// includes changes that look purely cosmetic - a different blob radius, a reordered random draw,
/// an extra call to the random source - because the seed's meaning is the whole algorithm. Saves
/// recorded under an older version are then recognised as unrebuildable and discarded instead of
/// silently misleading the player.
/// </para>
/// <para>
/// Authored puzzles are unaffected: they are shipped content looked up by id, so their saves
/// survive any generator change.
/// </para>
/// </remarks>
public static class GeneratorVersion
{
    /// <summary>
    /// Version 1 was the ported prototype algorithm: a fixed blob count that did not scale with
    /// the grid, uniformly random blob centres, and an empty-line repair that wrote to the mirror
    /// axis. Version 2 drives coverage from a target density, takes centres from a shuffled
    /// permutation of the half-grid, and attaches repairs to the neighbouring row.
    /// </summary>
    public const int Current = 2;

    /// <summary>
    /// Used for a save written before versioning existed. Such a save was produced by version 1,
    /// but it cannot be told apart from a corrupt row, so it is treated as unrebuildable either
    /// way.
    /// </summary>
    public const int Unknown = 0;
}
