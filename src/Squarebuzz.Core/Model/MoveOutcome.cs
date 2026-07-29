namespace Squarebuzz.Core.Model;

/// <summary>What happened when the player tried to mark a cell.</summary>
public enum MoveResult
{
    /// <summary>Nothing to do - the cell already held the requested value, or the puzzle is finished.</summary>
    NoChange,

    /// <summary>The mark was made.</summary>
    Applied,

    /// <summary>
    /// The player tried to fill a cell that is not part of the picture, and warnings are on.
    /// The board is left untouched and the mistake counter goes up.
    /// </summary>
    Mistake,
}

/// <summary>
/// Result of a move, including the consequences the caller needs in order to give feedback -
/// haptics on a mistake, a sound when a line completes, the win animation.
/// </summary>
/// <param name="Result">Whether the mark landed.</param>
/// <param name="AutoCrossedCells">Cells crossed off automatically because a line completed.</param>
/// <param name="SolvedPuzzle">True when this move finished the picture.</param>
public readonly record struct MoveOutcome(MoveResult Result, int AutoCrossedCells, bool SolvedPuzzle)
{
    public static MoveOutcome NoChange { get; } = new(MoveResult.NoChange, 0, false);

    public static MoveOutcome Mistake { get; } = new(MoveResult.Mistake, 0, false);

    public bool CompletedALine => AutoCrossedCells > 0;
}

/// <summary>Which mark a plain tap produces.</summary>
public enum PaintMode
{
    Fill,
    Cross,
}
