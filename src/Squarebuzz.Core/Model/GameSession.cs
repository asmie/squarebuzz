using Squarebuzz.Core.Clues;
using Squarebuzz.Core.Solving;

namespace Squarebuzz.Core.Model;

/// <summary>
/// One puzzle in progress: the board, the move history, and the counters that decide the
/// final star rating. This is the aggregate root for gameplay - all board mutation goes
/// through here so the rules cannot be sidestepped.
/// </summary>
public sealed class GameSession
{
    private const int MaxStars = 3;
    private const int MistakesBeforeLosingAStar = 2;
    private const int HintsBeforeLosingAStar = 1;

    private readonly CellState[] _cells;
    private readonly MoveHistory _history = new();

    public GameSession(Puzzle puzzle, GameRules rules)
    {
        ArgumentNullException.ThrowIfNull(puzzle);
        ArgumentNullException.ThrowIfNull(rules);

        Puzzle = puzzle;
        Rules = rules;
        _cells = new CellState[puzzle.CellCount];
        HintsRemaining = rules.HintAllowance;
    }

    public Puzzle Puzzle { get; }

    public GameRules Rules { get; }

    /// <summary>Which mark a plain tap produces. The UI's fill/cross toggle sets this.</summary>
    public PaintMode Mode { get; set; } = PaintMode.Fill;

    public ReadOnlySpan<CellState> Cells => _cells;

    public int HintsRemaining { get; private set; }

    public int HintsUsed => Rules.HintAllowance - HintsRemaining;

    public int Mistakes { get; private set; }

    public TimeSpan Elapsed { get; private set; }

    public bool IsSolved { get; private set; }

    public bool CanUndo => !IsSolved && _history.CanUndo;

    public bool CanRedo => !IsSolved && _history.CanRedo;

    public int MoveCount => _history.AppliedCount;

    /// <summary>
    /// Stars awarded on completion: three, less one for more than two mistakes and one for
    /// more than a single hint, never below one. Matches the prototype's <c>starCount</c>.
    /// </summary>
    public int StarRating
    {
        get
        {
            var stars = MaxStars;

            if (Mistakes > MistakesBeforeLosingAStar)
            {
                stars--;
            }

            if (HintsUsed > HintsBeforeLosingAStar)
            {
                stars--;
            }

            return Math.Max(1, stars);
        }
    }

    public CellState this[int index] => _cells[index];

    public CellState At(int x, int y) => _cells[Puzzle.IndexOf(x, y)];

    public void Advance(TimeSpan delta)
    {
        if (!IsSolved)
        {
            Elapsed += delta;
        }
    }

    /// <summary>
    /// Marks a cell according to <see cref="Mode"/>, toggling it off if it already holds
    /// that value.
    /// </summary>
    public MoveOutcome Tap(int index)
    {
        var current = _cells[index];
        var target = Mode == PaintMode.Fill
            ? current == CellState.Filled ? CellState.Empty : CellState.Filled
            : current == CellState.Crossed ? CellState.Empty : CellState.Crossed;

        return Paint(index, target);
    }

    /// <summary>
    /// Sets a specific cell to a specific value - what a drag gesture uses, having decided
    /// the value from the first cell it touched.
    /// </summary>
    public MoveOutcome Paint(int index, CellState target)
    {
        if (IsSolved)
        {
            return MoveOutcome.NoChange;
        }

        var current = _cells[index];

        if (current == target)
        {
            return MoveOutcome.NoChange;
        }

        // Filling a cell that is not part of the picture is refused rather than recorded, so
        // the board never holds a state the clues contradict.
        if (target == CellState.Filled && Rules.WarnOnMistakes && !Puzzle.Solution[index])
        {
            Mistakes++;
            return MoveOutcome.Mistake;
        }

        var changes = new List<CellChange> { new(index, current, target) };
        _cells[index] = target;

        var autoCrossed = Rules.AutoCrossCompletedLines ? AutoCrossCompletedLines(changes) : 0;

        _history.Push(new Stroke(changes) { DirectChangeCount = 1 });

        var solved = EvaluateSolved();

        return new MoveOutcome(MoveResult.Applied, autoCrossed, solved);
    }

    /// <summary>
    /// Reverses the last stroke, including any cells it auto-crossed.
    /// </summary>
    public bool Undo()
    {
        if (IsSolved)
        {
            return false;
        }

        var stroke = _history.Undo();

        if (stroke is null)
        {
            return false;
        }

        foreach (var change in stroke.Changes)
        {
            _cells[change.Index] = change.From;
        }

        return true;
    }

    public bool Redo()
    {
        if (IsSolved)
        {
            return false;
        }

        var stroke = _history.Redo();

        if (stroke is null)
        {
            return false;
        }

        foreach (var change in stroke.Changes)
        {
            _cells[change.Index] = change.To;
        }

        EvaluateSolved();
        return true;
    }

    /// <summary>
    /// Spends a hint and applies it. Returns <c>null</c> when no hints remain or there is
    /// nothing left to reveal.
    /// </summary>
    public Hint? UseHint()
    {
        if (IsSolved || HintsRemaining <= 0)
        {
            return null;
        }

        var hint = HintProvider.Find(Puzzle, _cells);

        if (hint is null)
        {
            return null;
        }

        HintsRemaining--;

        var changes = new List<CellChange> { new(hint.Index, _cells[hint.Index], hint.Value) };
        _cells[hint.Index] = hint.Value;

        if (Rules.AutoCrossCompletedLines)
        {
            AutoCrossCompletedLines(changes);
        }

        _history.Push(new Stroke(changes) { DirectChangeCount = 1 });
        EvaluateSolved();

        return hint;
    }

    /// <summary>Flags for fading out the numbers of a row whose runs are accounted for.</summary>
    public bool[] RowClueStrikes(int y)
    {
        return ClueStrikeCalculator.Compute(Puzzle.RowClues[y], _cells.AsSpan(y * Puzzle.Width, Puzzle.Width));
    }

    /// <summary>Flags for fading out the numbers of a column.</summary>
    public bool[] ColumnClueStrikes(int x)
    {
        Span<CellState> column = new CellState[Puzzle.Height];

        for (var y = 0; y < Puzzle.Height; y++)
        {
            column[y] = _cells[(y * Puzzle.Width) + x];
        }

        return ClueStrikeCalculator.Compute(Puzzle.ColumnClues[x], column);
    }

    /// <summary>
    /// Crosses off the remaining cells of any row or column whose filled cells already match
    /// its clue. The changes are appended to <paramref name="changes"/> rather than committed
    /// separately, so undoing the stroke takes them back too.
    /// </summary>
    /// <remarks>
    /// The prototype applied auto-crossing outside its history, so undo left the automatic
    /// crosses stranded on the board. Folding them into the same stroke fixes that.
    /// </remarks>
    private int AutoCrossCompletedLines(List<CellChange> changes)
    {
        var width = Puzzle.Width;
        var height = Puzzle.Height;
        var added = 0;

        for (var y = 0; y < height; y++)
        {
            var row = _cells.AsSpan(y * width, width);

            if (!ClueCalculator.FromMarks(row).Equals(Puzzle.RowClues[y]))
            {
                continue;
            }

            for (var x = 0; x < width; x++)
            {
                var index = (y * width) + x;

                if (_cells[index] != CellState.Empty)
                {
                    continue;
                }

                changes.Add(new CellChange(index, CellState.Empty, CellState.Crossed));
                _cells[index] = CellState.Crossed;
                added++;
            }
        }

        Span<CellState> column = new CellState[height];

        for (var x = 0; x < width; x++)
        {
            for (var y = 0; y < height; y++)
            {
                column[y] = _cells[(y * width) + x];
            }

            if (!ClueCalculator.FromMarks(column).Equals(Puzzle.ColumnClues[x]))
            {
                continue;
            }

            for (var y = 0; y < height; y++)
            {
                var index = (y * width) + x;

                if (_cells[index] != CellState.Empty)
                {
                    continue;
                }

                changes.Add(new CellChange(index, CellState.Empty, CellState.Crossed));
                _cells[index] = CellState.Crossed;
                added++;
            }
        }

        return added;
    }

    /// <summary>
    /// The puzzle is won when every cell of the picture is filled. Crossing off the blanks is
    /// a bookkeeping aid for the player, not a requirement.
    /// </summary>
    private bool EvaluateSolved()
    {
        var solution = Puzzle.Solution;

        for (var i = 0; i < solution.Length; i++)
        {
            if (solution[i] && _cells[i] != CellState.Filled)
            {
                return false;
            }
        }

        IsSolved = true;
        return true;
    }
}
