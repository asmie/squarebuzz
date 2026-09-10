using System.Collections.ObjectModel;

namespace Squarebuzz.Core.Model;

/// <summary>One cell changing value, with enough information to replay or reverse it.</summary>
/// <param name="Index">Row-major cell index.</param>
/// <param name="From">Value before the change.</param>
/// <param name="To">Value after the change.</param>
public readonly record struct CellChange(int Index, CellState From, CellState To);

/// <summary>
/// An immutable snapshot of a single undoable action. A cell edit and any cells auto-crossed
/// as a consequence belong to one stroke, so undo reverses the whole effect. A drag that
/// applies several cell edits can produce several strokes.
/// </summary>
public sealed class Stroke
{
    private readonly ReadOnlyCollection<CellChange> _changes;

    public Stroke(IEnumerable<CellChange> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);

        _changes = Array.AsReadOnly(changes.ToArray());

        if (_changes.Count == 0)
        {
            throw new ArgumentException("A stroke must contain at least one change.", nameof(changes));
        }
    }

    public IReadOnlyList<CellChange> Changes => _changes;

    /// <summary>Cells the player touched directly, as opposed to consequences like auto-crossing.</summary>
    public int DirectChangeCount { get; init; } = 1;

    /// <summary>Cells crossed automatically because a line became complete.</summary>
    public int AutoCrossedCount => _changes.Count - DirectChangeCount;
}
