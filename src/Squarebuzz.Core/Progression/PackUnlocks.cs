using Squarebuzz.Core.Model;

namespace Squarebuzz.Core.Progression;

/// <summary>
/// Decides which picture packs are open for the player to choose from.
/// </summary>
/// <remarks>
/// <para>
/// A pack shipped as locked is "meet these on the trail first": its pictures still appear at
/// their milestone levels in the campaign, which weaves in every shipped picture regardless of
/// locks, and once the player has found them all there the pack opens up for Quick game and for
/// Gallery replays.
/// </para>
/// <para>
/// This rule exists to make the app agree with itself. The content file's <c>locked</c> flag
/// used to mean forever: New Game and the Gallery refused the pack while the Path played its
/// pictures anyway, and the Collector trophy - which counts every shipped picture on purpose -
/// could only be won through that back door. Deriving the unlock from the solved table, the
/// same way the Path and the trophies already derive their state, closes the contradiction
/// without inventing any new persistence.
/// </para>
/// </remarks>
public static class PackUnlocks
{
    /// <summary>
    /// Whether <paramref name="pack"/> is open, given the pictures that have been solved.
    /// </summary>
    public static bool IsUnlocked(
        PackDefinition pack,
        IReadOnlyList<Puzzle> puzzles,
        IReadOnlyCollection<string> solvedPuzzleIds)
    {
        ArgumentNullException.ThrowIfNull(pack);
        ArgumentNullException.ThrowIfNull(puzzles);
        ArgumentNullException.ThrowIfNull(solvedPuzzleIds);

        if (!pack.Locked)
        {
            return true;
        }

        var anyInPack = false;

        foreach (var puzzle in puzzles)
        {
            if (!string.Equals(puzzle.Pack, pack.Id, StringComparison.Ordinal))
            {
                continue;
            }

            anyInPack = true;

            if (!solvedPuzzleIds.Contains(puzzle.Id))
            {
                return false;
            }
        }

        // A locked pack with no pictures cannot be earned, so it stays shut rather than
        // springing open by vacuous truth.
        return anyInPack;
    }

    /// <summary>Ids of every open pack, for callers deciding a whole screen at once.</summary>
    public static IReadOnlySet<string> UnlockedPackIds(
        IReadOnlyList<PackDefinition> packs,
        IReadOnlyList<Puzzle> puzzles,
        IReadOnlyCollection<string> solvedPuzzleIds)
    {
        ArgumentNullException.ThrowIfNull(packs);

        var unlocked = new HashSet<string>(StringComparer.Ordinal);

        foreach (var pack in packs)
        {
            if (IsUnlocked(pack, puzzles, solvedPuzzleIds))
            {
                unlocked.Add(pack.Id);
            }
        }

        return unlocked;
    }
}
