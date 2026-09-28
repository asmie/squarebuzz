using Squarebuzz.Core.Clues;
using Squarebuzz.Core.Solving;

namespace Squarebuzz.Core.Model;

/// <summary>Owns the board, undo history, timing and scoring for one puzzle. All board mutations pass through this class.</summary>
public sealed class GameSession
{
    private const int MaxStars = 3;
    private const int MistakesBeforeLosingAStar = 2;
    private const int HintsBeforeLosingAStar = 1;

    private readonly CellState[] _cells;
    private readonly bool[] _autoCrossed;
    private readonly MoveHistory _history = new();

    // What the first square of the current drag held, and whether the drag has already been
    // charged a mistake. See Paint.
    private CellState _strokeSource;
    private bool _strokeChargedAMistake;

    public GameSession(Puzzle puzzle, GameRules rules, NewGameOptions? origin = null, int seed = 0)
    {
        ArgumentNullException.ThrowIfNull(puzzle);
        ArgumentNullException.ThrowIfNull(rules);

        Puzzle = puzzle;
        Rules = rules;
        HintBudget = rules.HintBudget;
        Origin = origin is null ? null : new SessionOrigin(puzzle, origin with { HintBudget = HintBudget }, seed);
        Seed = seed;
        _cells = new CellState[puzzle.CellCount];
        _autoCrossed = new bool[puzzle.CellCount];
        HintsRemaining = rules.HintAllowance;
    }

    public Puzzle Puzzle { get; }

    public GameRules Rules { get; private set; }

    public HintBudget HintBudget { get; }

    public bool HasUnlimitedHints => Rules.HasUnlimitedHints;

    public bool CanUseHint => !IsOver && (HasUnlimitedHints || HintsRemaining > 0);

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

    /// <summary>Marks created by completed lines, rather than by the player or a hint.</summary>
    public ReadOnlySpan<bool> AutoCrossedCells => _autoCrossed;

    /// <summary>Remaining finite hints. Zero for unlimited games; use CanUseHint to check availability.</summary>
    public int HintsRemaining { get; private set; }

    /// <summary>Hints spent, retained independently of the current allowance for scoring and trophies.</summary>
    public int HintsUsed { get; private set; }

    public int Mistakes { get; private set; }

    public TimeSpan Elapsed { get; private set; }

    public bool IsSolved { get; private set; }

    /// <summary>The countdown for a timed trial, or null when the game is untimed.</summary>
    public TimeSpan? TimeLimit => Origin?.TimeLimit;

    /// <summary>Whether this session is racing a clock at all.</summary>
    public bool IsTimed => TimeLimit is not null;

    /// <summary>Nonnegative time remaining. Check IsTimed before displaying it for an ordinary game.</summary>
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

    /// <summary>Number of filled cells, maintained on each write for the progress display and accessibility summary.</summary>
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
        int mistakes,
        IReadOnlyList<bool>? autoCrossedCells = null)
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

        var restoredAutoCrossed = new bool[_cells.Length];
        if (autoCrossedCells is { Count: > 0 })
        {
            if (autoCrossedCells.Count != restoredCells.Length)
            {
                throw new ArgumentException("Automatic marks must match the saved board size.", nameof(autoCrossedCells));
            }

            for (var i = 0; i < restoredAutoCrossed.Length; i++)
            {
                var automatic = autoCrossedCells[i];
                if (automatic && restoredCells[i] != CellState.Crossed)
                {
                    throw new ArgumentException("Only crossed cells can be automatic.", nameof(autoCrossedCells));
                }

                restoredAutoCrossed[i] = automatic;
            }
        }

        // Preserve legacy crosses whose automatic/manual origin is unknown.
        restoredCells.CopyTo(_cells, 0);
        restoredAutoCrossed.CopyTo(_autoCrossed, 0);
        RecountFilled();

        Elapsed = elapsed;
        // Recompute remaining hints from the original budget and recorded usage.
        HintsRemaining = Math.Max(0, Rules.HintAllowance - hintsUsed);

        // Restore hint usage directly; a changed allowance must not alter past scoring.
        HintsUsed = hintsUsed;

        Mistakes = mistakes;
        _history.Clear();

        EvaluateSolved();
    }

    /// <summary>Applies helper preferences to the active session.</summary>
    /// <remarks>
    /// The original hint budget and usage remain unchanged. Enabling automatic crosses also
    /// updates previously completed lines as one undoable stroke.
    /// </remarks>
    public void ApplyHelpers(HelperSettings helpers)
    {
        ArgumentNullException.ThrowIfNull(helpers);

        var wasAutoCrossing = Rules.AutoCrossCompletedLines;

        // Origin is present on every session the app creates; the Relaxed fallback only
        // matters for bare test constructions.
        Rules = GameRules.Create(Origin?.Challenge ?? ChallengeLevel.Relaxed, helpers, HintBudget);
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

    /// <summary>Sets a cell to the value selected at the start of a drag.</summary>
    /// <param name="index">Cell index.</param>
    /// <param name="target">Requested mark.</param>
    /// <param name="continuesStroke">
    /// True after the first cell of a drag. Only cells matching the first cell's original state
    /// are painted, and the entire drag can charge at most one mistake.
    /// </param>
    public MoveOutcome Paint(int index, CellState target, bool continuesStroke = false)
    {
        // Expired trials reject input as soon as the deadline is reached.
        if (IsOver)
        {
            return MoveOutcome.NoChange;
        }

        var current = _cells[index];

        if (!continuesStroke)
        {
            _strokeSource = current;
            _strokeChargedAMistake = false;
        }
        else if (current != _strokeSource)
        {
            return MoveOutcome.NoChange;
        }

        if (current == target && !_autoCrossed[index])
        {
            return MoveOutcome.NoChange;
        }

        // With mistake warnings enabled, reject incorrect fills before changing the board.
        if (target == CellState.Filled && Rules.WarnOnMistakes && !Puzzle.Solution[index])
        {
            if (!_strokeChargedAMistake)
            {
                Mistakes++;
                _strokeChargedAMistake = true;
            }

            return MoveOutcome.Mistake;
        }

        // Report line completion only on the transition from incomplete to complete.
        var x = index % Puzzle.Width;
        var y = index / Puzzle.Width;
        var rowWasSatisfied = IsRowSatisfied(y);
        var columnWasSatisfied = IsColumnSatisfied(x);

        var wasAutoCrossed = _autoCrossed[index];
        Write(index, target);

        var completedALine = (!rowWasSatisfied && IsRowSatisfied(y))
                             || (!columnWasSatisfied && IsColumnSatisfied(x));

        // Reserve for line-wide consequences only when a line completes or is broken.
        var changes = new List<CellChange>(completedALine || rowWasSatisfied || columnWasSatisfied
            ? 1 + Puzzle.Width + Puzzle.Height : 1)
        {
            new(index, current, target) { FromAutoCrossed = wasAutoCrossed },
        };

        var autoCrossed = UpdateAutoCrossesThrough(changes, x, y);

        _history.Push(new Stroke(changes) { DirectChangeCount = 1 });

        var solved = EvaluateSolved();

        return new MoveOutcome(MoveResult.Applied, autoCrossed, solved, completedALine);
    }

    /// <summary>Sets a cell and keeps <see cref="FilledCount"/> in step.</summary>
    private void Write(int index, CellState value, bool automatic = false)
    {
        var previous = _cells[index];
        _autoCrossed[index] = automatic;

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
        // Use stack storage for the column buffer; this runs several times per move.
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

        // A cell can be cleared directly and then re-crossed by its still-complete line.
        // Reverse deltas in order as well as value so repeated indices restore correctly.
        for (var i = stroke.Changes.Count - 1; i >= 0; i--)
        {
            var change = stroke.Changes[i];
            Write(change.Index, change.From, change.FromAutoCrossed);
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
            Write(change.Index, change.To, change.ToAutoCrossed);
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
        if (!CanUseHint)
        {
            return null;
        }

        var hint = HintProvider.Find(Puzzle, _cells);

        if (hint is null)
        {
            return null;
        }

        if (!HasUnlimitedHints)
        {
            HintsRemaining--;
        }

        if (HintsUsed < int.MaxValue)
        {
            HintsUsed++;
        }

        var changes = new List<CellChange>(1 + Puzzle.Width + Puzzle.Height)
        {
            new(hint.Index, _cells[hint.Index], hint.Value) { FromAutoCrossed = _autoCrossed[hint.Index] },
        };

        Write(hint.Index, hint.Value);

        UpdateAutoCrossesThrough(changes, hint.Index % Puzzle.Width, hint.Index / Puzzle.Width);

        _history.Push(new Stroke(changes) { DirectChangeCount = 1 });
        EvaluateSolved();

        return hint;
    }

    /// <summary>
    /// Adds crosses for completed lines and removes automatic crosses no longer supported
    /// by either intersecting line. Every consequence belongs to the same undoable stroke.
    /// </summary>
    /// <remarks>
    /// Only marks in the touched row and column can lose support. Before removing one,
    /// check its perpendicular line too. Manual and hint crosses are never removed here.
    /// Existing automatic marks still lose support when the helper is switched off.
    /// </remarks>
    private int UpdateAutoCrossesThrough(List<CellChange> changes, int x, int y)
    {
        if (!IsRowSatisfied(y))
        {
            for (var column = 0; column < Puzzle.Width; column++)
            {
                var index = (y * Puzzle.Width) + column;
                if (_autoCrossed[index] && !IsColumnSatisfied(column))
                {
                    ClearAutomaticCross(changes, index);
                }
            }
        }

        if (!IsColumnSatisfied(x))
        {
            for (var row = 0; row < Puzzle.Height; row++)
            {
                var index = (row * Puzzle.Width) + x;
                if (_autoCrossed[index] && !IsRowSatisfied(row))
                {
                    ClearAutomaticCross(changes, index);
                }
            }
        }

        return Rules.AutoCrossCompletedLines
            ? CrossRowIfSatisfied(changes, y) + CrossColumnIfSatisfied(changes, x)
            : 0;
    }

    private void ClearAutomaticCross(List<CellChange> changes, int index)
    {
        changes.Add(new CellChange(index, CellState.Crossed, CellState.Empty) { FromAutoCrossed = true });
        Write(index, CellState.Empty);
    }

    /// <summary>Adds automatic crosses to all completed lines when the helper is enabled mid-game.</summary>
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

        changes.Add(new CellChange(index, CellState.Empty, CellState.Crossed) { ToAutoCrossed = true });

        // Use Write to keep all cell changes on the same mutation path.
        Write(index, CellState.Crossed, automatic: true);

        return true;
    }

    /// <summary>A win requires filled cells to match the solution exactly. Crosses are optional.</summary>
    /// <remarks>
    /// When mistake warnings are disabled, the board may contain extra fills; those must also
    /// be checked before declaring a win.
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
