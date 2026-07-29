namespace Squarebuzz.Core.Solving;

/// <summary>Outcome of running the line solver over a single row or column.</summary>
public enum LineSolveStatus
{
    /// <summary>The line is consistent but nothing new could be deduced from it yet.</summary>
    Unchanged,

    /// <summary>At least one cell was forced to a definite value.</summary>
    Progressed,

    /// <summary>No arrangement of the clue fits what is already marked - the line is broken.</summary>
    Contradiction,
}
