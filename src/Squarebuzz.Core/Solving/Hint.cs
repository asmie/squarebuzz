using Squarebuzz.Core.Model;

namespace Squarebuzz.Core.Solving;

/// <summary>Where a hint's certainty came from.</summary>
public enum HintSource
{
    /// <summary>
    /// The cell follows from a single row or column clue given what is already on the board -
    /// the player could have found it themselves right now.
    /// </summary>
    ImmediateDeduction,

    /// <summary>
    /// Deducible, but only after chaining several rows and columns together.
    /// </summary>
    ChainedDeduction,

    /// <summary>
    /// Taken straight from the answer because logic had stalled. Only reachable on puzzles
    /// that need guessing, which the generator is meant to prevent.
    /// </summary>
    SolutionReveal,
}

/// <summary>A single cell offered to the player, with the reason it is certain.</summary>
/// <param name="Index">Row-major cell index.</param>
/// <param name="Value">What the cell must be.</param>
/// <param name="Column">Zero-based column, for pointing the camera at it.</param>
/// <param name="Row">Zero-based row.</param>
/// <param name="Source">How firmly this was established.</param>
public sealed record Hint(int Index, CellState Value, int Column, int Row, HintSource Source);
