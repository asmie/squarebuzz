namespace Squarebuzz.Core.Model;

/// <summary>
/// What the player has marked in a single cell. The numeric values match the prototype's
/// 0/1/2 encoding so saved boards remain readable across the port.
/// </summary>
public enum CellState : byte
{
    /// <summary>Untouched. Says nothing about the solution.</summary>
    Empty = 0,

    /// <summary>Marked as part of the picture.</summary>
    Filled = 1,

    /// <summary>Marked with an X: the player has proved this cell stays blank.</summary>
    Crossed = 2,
}
