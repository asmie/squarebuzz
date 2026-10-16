namespace Squarebuzz.Core.Model;

/// <summary>
/// Undo/redo stack over <see cref="Stroke"/>s - the Memento pattern, storing the delta of
/// each move rather than a copy of the whole board.
/// </summary>
public sealed class MoveHistory
{
    private readonly List<Stroke> _strokes = [];
    private readonly List<int> _groupEnds = [];

    /// <summary>How many action groups are applied. Everything past this is redoable.</summary>
    private int _applied;

    public bool CanUndo => _applied > 0;

    public bool CanRedo => _applied < _groupEnds.Count;

    /// <summary>Actions applied so far - each tap, drag or hint counts once.</summary>
    public int AppliedCount => _applied;

    /// <summary>
    /// Records a new stroke. Anything that had been undone is discarded first: once the
    /// player moves again, the branch they backed out of is gone. Continuing a drag appends
    /// the delta to its existing action without copying earlier edits.
    /// </summary>
    public void Push(Stroke stroke, bool continuesStroke = false)
    {
        ArgumentNullException.ThrowIfNull(stroke);

        if (CanRedo)
        {
            var retained = _applied == 0 ? 0 : _groupEnds[_applied - 1];
            _strokes.RemoveRange(retained, _strokes.Count - retained);
            _groupEnds.RemoveRange(_applied, _groupEnds.Count - _applied);
            // A branch starts a new action even if the caller was still dragging.
            continuesStroke = false;
        }

        _strokes.Add(stroke);
        if (continuesStroke && _applied > 0)
        {
            _groupEnds[^1] = _strokes.Count;
        }
        else
        {
            _groupEnds.Add(_strokes.Count);
            _applied++;
        }
    }

    /// <summary>The stroke to reverse, or <c>null</c> when there is nothing to undo.</summary>
    public Stroke? Undo() => CanUndo ? Snapshot(--_applied) : null;

    /// <summary>The stroke to re-apply, or <c>null</c> when there is nothing to redo.</summary>
    public Stroke? Redo() => CanRedo ? Snapshot(_applied++) : null;

    private Stroke Snapshot(int group)
    {
        var first = group == 0 ? 0 : _groupEnds[group - 1];
        var count = _groupEnds[group] - first;
        // Keep per-cell deltas while painting; flatten a drag only when undo/redo needs it.
        return count == 1 ? _strokes[first] : Stroke.Combine(_strokes.Skip(first).Take(count));
    }

    public void Clear()
    {
        _strokes.Clear();
        _groupEnds.Clear();
        _applied = 0;
    }
}
