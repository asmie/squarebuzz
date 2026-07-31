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

    public GameSession(Puzzle puzzle, GameRules rules, NewGameOptions? origin = null, int seed = 0)
    {
        ArgumentNullException.ThrowIfNull(puzzle);
        ArgumentNullException.ThrowIfNull(rules);

        Puzzle = puzzle;
        Rules = rules;
        Origin = origin;
        Seed = seed;
        _cells = new CellState[puzzle.CellCount];
        HintsRemaining = rules.HintAllowance;
    }

    public Puzzle Puzzle { get; }

    public GameRules Rules { get; }

    /// <summary>
    /// The choices this session was created from. Carried so a save can record the player's
    /// actual difficulty and challenge rather than guessing them back out of the rules.
    /// </summary>
    public NewGameOptions? Origin { get; }

    /// <summary>Seed that reproduces this puzzle, so a save need not store the picture.</summary>
    public int Seed { get; }

    /// <summary>Which mark a plain tap produces. The UI's fill/cross toggle sets this.</summary>
    public PaintMode Mode { get; set; } = PaintMode.Fill;

    public ReadOnlySpan<CellState> Cells => _cells;

    public int HintsRemaining { get; private set; }

    public int HintsUsed => Rules.HintAllowance - HintsRemaining;

    public int Mistakes { get; private set; }

    public TimeSpan Elapsed { get; private set; }

    public bool IsSolved { get; private set; }

    /// <summary>The countdown for a timed trial, or null when the game is untimed.</summary>
    public TimeSpan? TimeLimit => Origin?.TimeLimit;

    /// <summary>Whether this session is racing a clock at all.</summary>
    public bool IsTimed => TimeLimit is not null;

    /// <summary>
    /// Time left on the clock, never negative. <see cref="TimeSpan.Zero"/> for an untimed game,
    /// which callers should not be showing in the first place - check <see cref="IsTimed"/>.
    /// </summary>
    public TimeSpan Remaining => TimeLimit is { } limit
        ? (limit > Elapsed ? limit - Elapsed : TimeSpan.Zero)
        : TimeSpan.Zero;

    /// <summary>
    /// The clock ran out before the picture was finished - the game's only way to lose.
    /// </summary>
    /// <remarks>
    /// Solving on the very last tick counts as a win: <see cref="IsSolved"/> is checked first, and
    /// <see cref="Advance"/> stops the clock the moment the puzzle is done.
    /// </remarks>
    public bool IsTimeUp => IsTimed && !IsSolved && Remaining == TimeSpan.Zero;

    /// <summary>True once the session can no longer be played, whether won or lost.</summary>
    public bool IsOver => IsSolved || IsTimeUp;

    public bool CanUndo => !IsOver && _history.CanUndo;

    public bool CanRedo => !IsOver && _history.CanRedo;

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

    /// <summary>
    /// Squares the player has filled in.
    /// </summary>
    /// <remarks>
    /// Crossed squares do not count. This is progress towards the picture, and the count is what a
    /// screen reader is told about the board - a canvas has nothing for it to read otherwise.
    /// </remarks>
    public int FilledCount
    {
        get
        {
            var count = 0;

            for (var i = 0; i < _cells.Length; i++)
            {
                if (_cells[i] == CellState.Filled)
                {
                    count++;
                }
            }

            return count;
        }
    }

    public CellState this[int index] => _cells[index];

    public CellState At(int x, int y) => _cells[Puzzle.IndexOf(x, y)];

    public void Advance(TimeSpan delta)
    {
        if (IsOver)
        {
            return;
        }

        Elapsed += delta;

        // Clamped so a timed game never reports having run longer than its own limit, which would
        // make the recorded time of a loss depend on how coarsely the caller happened to tick.
        if (TimeLimit is { } limit && Elapsed > limit)
        {
            Elapsed = limit;
        }
    }

    /// <summary>
    /// Rehydrates a session from a save.
    /// </summary>
    /// <remarks>
    /// Deliberately bypasses the move rules: these marks were already validated when the
    /// player made them, and re-checking would count old mistakes twice. Undo history starts
    /// empty, so a resumed game cannot be unwound past the point it was saved.
    /// </remarks>
    public void Restore(IReadOnlyList<CellState> cells, TimeSpan elapsed, int hintsRemaining, int mistakes)
    {
        ArgumentNullException.ThrowIfNull(cells);

        if (cells.Count != _cells.Length)
        {
            throw new ArgumentException(
                $"Saved board has {cells.Count} cells but the puzzle needs {_cells.Length}.",
                nameof(cells));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(mistakes);
        ArgumentOutOfRangeException.ThrowIfNegative(hintsRemaining);

        for (var i = 0; i < cells.Count; i++)
        {
            _cells[i] = cells[i];
        }

        Elapsed = elapsed;
        HintsRemaining = Math.Min(hintsRemaining, Rules.HintAllowance);
        Mistakes = mistakes;
        _history.Clear();

        EvaluateSolved();
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
        // IsOver, not IsSolved: a timed trial whose clock has run out is finished too, and must
        // not keep accepting marks while the view catches up with the fact.
        if (IsOver)
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
        if (IsOver)
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
        if (IsOver)
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
        if (IsOver || HintsRemaining <= 0)
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
    /// The puzzle is won when the filled cells are exactly the picture. Crossing off the blanks
    /// is a bookkeeping aid for the player, not a requirement - but *filling* a blank blocks the
    /// win until it is cleared.
    /// </summary>
    /// <remarks>
    /// The second half of the check only matters when <see cref="GameRules.WarnOnMistakes"/> is
    /// off: with it on, a wrong fill is refused at <see cref="Paint"/> and can never be on the
    /// board. Without this clause, warn-off would accept painting the whole grid as a win -
    /// three stars for defeating the point of the game.
    /// </remarks>
    private bool EvaluateSolved()
    {
        var solution = Puzzle.Solution;

        for (var i = 0; i < solution.Length; i++)
        {
            var filled = _cells[i] == CellState.Filled;

            if (solution[i] != filled)
            {
                return false;
            }
        }

        IsSolved = true;
        return true;
    }
}
