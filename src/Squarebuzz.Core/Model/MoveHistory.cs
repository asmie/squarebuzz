namespace Squarebuzz.Core.Model;

/// <summary>
/// Undo/redo stack over <see cref="Stroke"/>s - the Memento pattern, storing the delta of
/// each move rather than a copy of the whole board.
/// </summary>
public sealed class MoveHistory
{
    private readonly List<Stroke> _strokes = [];

    /// <summary>How many strokes are currently applied. Everything past this is redoable.</summary>
    private int _applied;

    public bool CanUndo => _applied > 0;

    public bool CanRedo => _applied < _strokes.Count;

    /// <summary>Strokes applied so far - the player's move count.</summary>
    public int AppliedCount => _applied;

    /// <summary>
    /// Records a new stroke. Anything that had been undone is discarded first: once the
    /// player moves again, the branch they backed out of is gone.
    /// </summary>
    public void Push(Stroke stroke)
    {
        ArgumentNullException.ThrowIfNull(stroke);

        if (CanRedo)
        {
            _strokes.RemoveRange(_applied, _strokes.Count - _applied);
        }

        _strokes.Add(stroke);
        _applied = _strokes.Count;
    }

    /// <summary>The stroke to reverse, or <c>null</c> when there is nothing to undo.</summary>
    public Stroke? Undo() => CanUndo ? _strokes[--_applied] : null;

    /// <summary>The stroke to re-apply, or <c>null</c> when there is nothing to redo.</summary>
    public Stroke? Redo() => CanRedo ? _strokes[_applied++] : null;

    public void Clear()
    {
        _strokes.Clear();
        _applied = 0;
    }
}
