namespace Squarebuzz.Core.Solving;

/// <summary>How a puzzle responded to line-by-line logical solving.</summary>
public enum PuzzleSolveOutcome
{
    /// <summary>
    /// Every cell was determined by reasoning about one line at a time. This is the bar
    /// squarebuzz holds itself to: it is what makes "never guess" true for a child.
    /// </summary>
    Solvable,

    /// <summary>
    /// Line-by-line logic stalled with cells still undetermined. The puzzle may well have
    /// exactly one solution, but reaching it needs cross-line case analysis - so it is not
    /// fair to ship, and generated candidates in this state are rejected.
    /// </summary>
    NeedsGuessing,

    /// <summary>The clues contradict each other, or contradict marks already on the board.</summary>
    Contradiction,
}

/// <summary>
/// Result of a solve, including how much work it took - which doubles as an objective
/// difficulty signal for the puzzle.
/// </summary>
/// <param name="Outcome">Whether the puzzle yielded to line logic.</param>
/// <param name="Passes">
/// Row-and-column sweeps needed before the board stopped changing. A puzzle solved in one
/// or two passes is easy; one needing many passes demands sustained cross-referencing.
/// </param>
/// <param name="UndeterminedCells">Cells still unknown when solving stopped. Zero when <see cref="Outcome"/> is <see cref="PuzzleSolveOutcome.Solvable"/>.</param>
public sealed record PuzzleSolveResult(PuzzleSolveOutcome Outcome, int Passes, int UndeterminedCells)
{
    public bool IsSolvable => Outcome == PuzzleSolveOutcome.Solvable;
}
