using System.Collections.ObjectModel;

namespace Squarebuzz.Core.Model;

/// <summary>One cell changing value, with enough information to replay or reverse it.</summary>
/// <param name="Index">Row-major cell index.</param>
/// <param name="From">Value before the change.</param>
/// <param name="To">Value after the change.</param>
public readonly record struct CellChange(int Index, CellState From, CellState To)
{
    public bool FromAutoCrossed { get; init; }
    public bool ToAutoCrossed { get; init; }
}

/// <summary>
/// An immutable snapshot of a single undoable action. A cell edit and any cells auto-crossed
/// as a consequence belong to one stroke. History groups the edits of a drag into one action.
/// </summary>
public sealed class Stroke
{
    private readonly ReadOnlyCollection<CellChange> _changes;
    private readonly int? _combinedAutoCrossedCount;

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

    private Stroke(IEnumerable<CellChange> changes, int directChangeCount, int autoCrossedCount)
        : this(changes)
    {
        DirectChangeCount = directChangeCount;
        _combinedAutoCrossedCount = autoCrossedCount;
    }

    internal static Stroke Combine(IEnumerable<Stroke> strokes)
    {
        var changes = new List<CellChange>();
        var direct = 0;
        var automatic = 0;
        foreach (var stroke in strokes)
        {
            // Keep chronological order: an automatic cross may be removed by a later edit.
            changes.AddRange(stroke.Changes);
            direct += stroke.DirectChangeCount;
            automatic += stroke.AutoCrossedCount;
        }
        return new Stroke(changes, direct, automatic);
    }

    /// <summary>Cells the player touched directly, as opposed to consequences like auto-crossing.</summary>
    public int DirectChangeCount { get; init; } = 1;

    /// <summary>Cells crossed automatically because a line became complete.</summary>
    public int AutoCrossedCount => _combinedAutoCrossedCount ?? _changes.Skip(DirectChangeCount)
        .Count(change => change.From != CellState.Crossed && change.To == CellState.Crossed);
}
