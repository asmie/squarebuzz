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
        Origin = origin is null ? null : new SessionOrigin(puzzle, origin, seed);
        Seed = seed;
        _cells = new CellState[puzzle.CellCount];
        HintsRemaining = rules.HintAllowance;
    }

    public Puzzle Puzzle { get; }

    public GameRules Rules { get; private set; }

    /// <summary>
    /// Resolved challenge identity and replay choices. Saves and completion use the same
    /// picture, seed and mode rather than guessing them back out of the rules or route.
    /// </summary>
    public SessionOrigin? Origin { get; }

    /// <summary>Seed that reproduces this puzzle, so a save need not store the picture.</summary>
    public int Seed { get; }

    /// <summary>Which mark a plain tap produces. The UI's fill/cross toggle sets this.</summary>
    public PaintMode Mode { get; set; } = PaintMode.Fill;

    public ReadOnlySpan<CellState> Cells => _cells;

    public int HintsRemaining { get; private set; }

    /// <summary>
    /// Hints actually spent. A counter of its own rather than allowance-minus-remaining,
    /// because <see cref="ApplyHelpers"/> can change the allowance mid-game and the stars
    /// must keep charging for the help that was really taken.
    /// </summary>
    public int HintsUsed { get; private set; }

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
    /// An unfinished puzzle expires at zero remaining time. A puzzle already solved stays won:
    /// <see cref="Advance"/> stops counting once the session is over.
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
    /// Kept as a running total rather than counted on demand: it is read on every move, from a
    /// binding that re-reads whenever the board changes, so scanning the grid for it made every
    /// painted cell walk all 625 squares twice over.
    /// </remarks>
    public int FilledCount { get; private set; }

    public CellState this[int index] => _cells[index];

    public CellState At(int x, int y) => _cells[Puzzle.IndexOf(x, y)];

    /// <summary>Adds nonnegative play time, capped at the deadline or the largest representable duration.</summary>
    public void Advance(TimeSpan delta)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(delta, TimeSpan.Zero);

        if (IsOver)
        {
            return;
        }

        // Compare before adding: a large delta can overflow even when the result would be
        // clamped afterward. Untimed games keep accepting moves when their clock saturates.
        var limit = TimeLimit ?? TimeSpan.MaxValue;
        Elapsed = delta >= limit - Elapsed ? limit : Elapsed + delta;
    }

    /// <summary>
    /// Rehydrates a session from a save.
    /// </summary>
    /// <remarks>
    /// Validates saved values before changing the session, but does not replay move rules:
    /// replaying would count old mistakes twice. Undo history starts empty after a successful
    /// restore, so a resumed game cannot be unwound past the point it was saved.
    /// </remarks>
    public void Restore(
        IReadOnlyList<CellState> cells,
        TimeSpan elapsed,
        int hintsRemaining,
        int hintsUsed,
        int mistakes)
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
        ArgumentOutOfRangeException.ThrowIfNegative(hintsUsed);
        ArgumentOutOfRangeException.ThrowIfLessThan(elapsed, TimeSpan.Zero);

        // Read and validate the complete board before replacing live state. A bad cell or a
        // failing source collection must not leave a partially restored board and old counters.
        var restoredCells = new CellState[_cells.Length];
        for (var i = 0; i < cells.Count; i++)
        {
            var state = cells[i];
            if (!Enum.IsDefined(state))
            {
                throw new ArgumentException($"Saved board cell {i} has invalid state {(byte)state}.", nameof(cells));
            }

            restoredCells[i] = state;
        }

        restoredCells.CopyTo(_cells, 0);
        RecountFilled();

        Elapsed = elapsed;
        HintsRemaining = Math.Min(hintsRemaining, Rules.HintAllowance);

        // Taken from the save, never re-derived from the allowance. The allowance can differ
        // from the one the game was saved under - switching hints off in Options is enough -
        // and deriving it would hand back hints the player had already spent, restoring a star
        // and the "no hints" trophy along with them.
        HintsUsed = hintsUsed;

        Mistakes = mistakes;
        _history.Clear();

        EvaluateSolved();
    }

    /// <summary>
    /// Re-resolves the rules after the player changed a helper in Options mid-game.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Without this, the pause overlay's Options entry would be a lie: rules are resolved when
    /// a session is created, so a helper flipped mid-game would change nothing until the next
    /// puzzle. The challenge the game was started with is kept, so Sharp keeps counting
    /// mistakes and keeps its single hint, and the hint budget is recomputed against hints
    /// already spent - toggling hints off and back on cannot mint fresh ones.
    /// </para>
    /// <para>
    /// Switching auto-crossing <em>on</em> also catches up the lines already finished, as one
    /// undoable stroke. Ordinary moves only ever examine the row and column they touched, which
    /// is all that can have changed - so without this sweep those older lines would keep their
    /// blanks for the rest of the game and the switch would look broken.
    /// </para>
    /// </remarks>
    public void ApplyHelpers(HelperSettings helpers)
    {
        ArgumentNullException.ThrowIfNull(helpers);

        var wasAutoCrossing = Rules.AutoCrossCompletedLines;

        // Origin is present on every session the app creates; the Relaxed fallback only
        // matters for bare test constructions.
        Rules = GameRules.Create(Origin?.Challenge ?? ChallengeLevel.Relaxed, helpers);
        HintsRemaining = Math.Max(0, Rules.HintAllowance - HintsUsed);

        if (IsOver || wasAutoCrossing || !Rules.AutoCrossCompletedLines)
        {
            return;
        }

        var changes = new List<CellChange>();

        if (CrossAllSatisfiedLines(changes) > 0)
        {
            // No direct changes: the player did not make this move, so undoing it should take
            // back every cross rather than leaving one behind.
            _history.Push(new Stroke(changes) { DirectChangeCount = 0 });
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

        // Sampled before and after the change so the outcome reports a *transition*: only the
        // move that makes a line newly match its clue is a completion, not every later mark in
        // an already-finished line.
        var x = index % Puzzle.Width;
        var y = index / Puzzle.Width;
        var rowWasSatisfied = IsRowSatisfied(y);
        var columnWasSatisfied = IsColumnSatisfied(x);

        Write(index, target);

        var completedALine = (!rowWasSatisfied && IsRowSatisfied(y))
                             || (!columnWasSatisfied && IsColumnSatisfied(x));

        // One entry is the whole story for the overwhelming majority of moves, so that is what
        // is reserved. Only a move that just completed a line has blanks left to cross, and only
        // then is the worst case - this cell plus every blank in its row and column - worth the
        // several hundred bytes it costs at 25x25.
        var changes = new List<CellChange>(completedALine ? 1 + Puzzle.Width + Puzzle.Height : 1)
        {
            new(index, current, target),
        };

        var autoCrossed = Rules.AutoCrossCompletedLines ? AutoCrossLinesThrough(changes, x, y) : 0;

        _history.Push(new Stroke(changes) { DirectChangeCount = 1 });

        var solved = EvaluateSolved();

        return new MoveOutcome(MoveResult.Applied, autoCrossed, solved, completedALine);
    }

    /// <summary>Sets a cell and keeps <see cref="FilledCount"/> in step.</summary>
    private void Write(int index, CellState value)
    {
        var previous = _cells[index];

        if (previous == value)
        {
            return;
        }

        if (previous == CellState.Filled)
        {
            FilledCount--;
        }

        if (value == CellState.Filled)
        {
            FilledCount++;
        }

        _cells[index] = value;
    }

    /// <summary>Whether a row's filled runs already match its clue, crossed or not.</summary>
    private bool IsRowSatisfied(int y) =>
        ClueCalculator.MatchesMarks(Puzzle.RowClues[y], _cells.AsSpan(y * Puzzle.Width, Puzzle.Width));

    /// <summary>Whether a column's filled runs already match its clue, crossed or not.</summary>
    private bool IsColumnSatisfied(int x)
    {
        // stackalloc, not a heap array: a Span over `new CellState[]` still allocates, and this
        // runs several times per painted cell. The largest supported grid is 25 rows.
        Span<CellState> column = stackalloc CellState[Puzzle.Height];

        CopyColumn(x, column);

        return ClueCalculator.MatchesMarks(Puzzle.ColumnClues[x], column);
    }

    private void RecountFilled()
    {
        var count = 0;

        for (var i = 0; i < _cells.Length; i++)
        {
            if (_cells[i] == CellState.Filled)
            {
                count++;
            }
        }

        FilledCount = count;
    }

    private void CopyColumn(int x, Span<CellState> destination)
    {
        var width = Puzzle.Width;

        for (var y = 0; y < Puzzle.Height; y++)
        {
            destination[y] = _cells[(y * width) + x];
        }
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
            Write(change.Index, change.From);
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
            Write(change.Index, change.To);
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
        HintsUsed++;

        var changes = new List<CellChange>(1 + Puzzle.Width + Puzzle.Height)
        {
            new(hint.Index, _cells[hint.Index], hint.Value),
        };

        Write(hint.Index, hint.Value);

        if (Rules.AutoCrossCompletedLines)
        {
            AutoCrossLinesThrough(changes, hint.Index % Puzzle.Width, hint.Index / Puzzle.Width);
        }

        _history.Push(new Stroke(changes) { DirectChangeCount = 1 });
        EvaluateSolved();

        return hint;
    }

    /// <summary>
    /// Crosses off the remaining cells of a row or column whose filled cells already match its
    /// clue. The changes are appended to <paramref name="changes"/> rather than committed
    /// separately, so undoing the stroke takes them back too.
    /// </summary>
    /// <remarks>
    /// The prototype applied auto-crossing outside its history, so undo left the automatic
    /// crosses stranded on the board. Folding them into the same stroke fixes that.
    /// </remarks>
    /// <summary>
    /// Crosses off the blanks of the row and column through one cell, if either now matches its
    /// clue.
    /// </summary>
    /// <remarks>
    /// Only those two lines are examined, because only those two can have changed. Crossing a
    /// cell never alters a line's <em>filled</em> runs, so an auto-cross cannot complete some
    /// other line as a knock-on - which means the old sweep over all fifty lines of a 25x25
    /// board re-derived forty-eight clue sets per move for nothing. Measured, that sweep was
    /// 87% of the time and 85% of the allocation of a painted cell.
    /// </remarks>
    private int AutoCrossLinesThrough(List<CellChange> changes, int x, int y)
    {
        var added = CrossRowIfSatisfied(changes, y);

        added += CrossColumnIfSatisfied(changes, x);

        return added;
    }

    /// <summary>
    /// Crosses off every blank of every satisfied line on the board.
    /// </summary>
    /// <remarks>
    /// The catch-up sweep, for the one moment the cheap two-line check cannot cover: auto-cross
    /// being switched on part-way through a game, when lines finished earlier are still carrying
    /// their blanks. See <see cref="ApplyHelpers"/>.
    /// </remarks>
    private int CrossAllSatisfiedLines(List<CellChange> changes)
    {
        var added = 0;

        for (var y = 0; y < Puzzle.Height; y++)
        {
            added += CrossRowIfSatisfied(changes, y);
        }

        for (var x = 0; x < Puzzle.Width; x++)
        {
            added += CrossColumnIfSatisfied(changes, x);
        }

        return added;
    }

    private int CrossRowIfSatisfied(List<CellChange> changes, int y)
    {
        if (!IsRowSatisfied(y))
        {
            return 0;
        }

        var width = Puzzle.Width;
        var added = 0;

        for (var x = 0; x < width; x++)
        {
            if (CrossIfEmpty(changes, (y * width) + x))
            {
                added++;
            }
        }

        return added;
    }

    private int CrossColumnIfSatisfied(List<CellChange> changes, int x)
    {
        if (!IsColumnSatisfied(x))
        {
            return 0;
        }

        var width = Puzzle.Width;
        var added = 0;

        for (var y = 0; y < Puzzle.Height; y++)
        {
            if (CrossIfEmpty(changes, (y * width) + x))
            {
                added++;
            }
        }

        return added;
    }

    private bool CrossIfEmpty(List<CellChange> changes, int index)
    {
        if (_cells[index] != CellState.Empty)
        {
            return false;
        }

        changes.Add(new CellChange(index, CellState.Empty, CellState.Crossed));

        // Empty to Crossed, so FilledCount cannot move - but go through Write anyway rather
        // than reaching past it, so there is exactly one place that touches the board.
        Write(index, CellState.Crossed);

        return true;
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
