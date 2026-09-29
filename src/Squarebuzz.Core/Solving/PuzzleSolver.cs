using Squarebuzz.Core.Model;

namespace Squarebuzz.Core.Solving;

/// <summary>
/// Solves a whole puzzle by repeatedly applying <see cref="NonogramLineSolver"/> to every
/// row and column until the board stops changing.
/// </summary>
public static class PuzzleSolver
{
    /// <summary>
    /// Safety valve. Each pass must deduce at least one cell to continue, so the real bound
    /// is the cell count; this only guards against a logic error causing a runaway loop.
    /// </summary>
    private const int MaxPasses = 512;

    /// <summary>
    /// Solves from an empty board and reports whether the puzzle is fair.
    /// Use this to validate generated candidates and authored content.
    /// </summary>
    public static PuzzleSolveResult Analyse(Puzzle puzzle)
    {
        ArgumentNullException.ThrowIfNull(puzzle);

        var board = new CellState[puzzle.CellCount];
        return Solve(puzzle, board);
    }

    /// <summary>
    /// Advances <paramref name="board"/> as far as line logic allows, starting from whatever
    /// is already marked on it.
    /// </summary>
    public static PuzzleSolveResult Solve(Puzzle puzzle, Span<CellState> board)
    {
        ArgumentNullException.ThrowIfNull(puzzle);

        return Solve(puzzle.Width, puzzle.Height, puzzle.RowClues, puzzle.ColumnClues, board);
    }

    /// <summary>
    /// Clue-only overload, so the solver can be pointed at candidate clues that have no
    /// <see cref="Puzzle"/> behind them yet.
    /// </summary>
    public static PuzzleSolveResult Solve(
        int width,
        int height,
        IReadOnlyList<LineClues> rowClues,
        IReadOnlyList<LineClues> columnClues,
        Span<CellState> board)
    {
        ArgumentNullException.ThrowIfNull(rowClues);
        ArgumentNullException.ThrowIfNull(columnClues);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        if (rowClues.Count != height)
        {
            throw new ArgumentException($"Expected {height} row clues but got {rowClues.Count}.", nameof(rowClues));
        }

        if (columnClues.Count != width)
        {
            throw new ArgumentException($"Expected {width} column clues but got {columnClues.Count}.", nameof(columnClues));
        }

        if (board.Length != width * height)
        {
            throw new ArgumentException($"Board has {board.Length} cells but {width}x{height} needs {width * height}.", nameof(board));
        }

        // Rows sit contiguously in the board, so they are solved in place. Columns are
        // strided, so they are gathered into this scratch buffer and scattered back.
        Span<CellState> column = height <= 64 ? stackalloc CellState[height] : new CellState[height];
        Span<CellState> rowBefore = width <= 64 ? stackalloc CellState[width] : new CellState[width];

        // Only lines that something has touched since they were last solved are solved again.
        // A row can only change through a column and a column only through a row, and re-solving
        // an untouched line always comes back Unchanged - the solver already applied everything
        // it forces. Skipping them is therefore exact: the same board, outcome and pass count
        // (which the content tooling reports as difficulty), for a fraction of the line solves.
        Span<bool> rowDirty = height <= 64 ? stackalloc bool[height] : new bool[height];
        Span<bool> columnDirty = width <= 64 ? stackalloc bool[width] : new bool[width];
        rowDirty.Fill(true);
        columnDirty.Fill(true);

        var passes = 0;

        while (passes < MaxPasses)
        {
            var changed = false;
            passes++;

            for (var y = 0; y < height; y++)
            {
                if (!rowDirty[y])
                {
                    continue;
                }

                rowDirty[y] = false;
                var row = board.Slice(y * width, width);
                row.CopyTo(rowBefore);

                switch (NonogramLineSolver.Solve(rowClues[y], row))
                {
                    case LineSolveStatus.Contradiction:
                        return new PuzzleSolveResult(PuzzleSolveOutcome.Contradiction, passes, CountUndetermined(board));
                    case LineSolveStatus.Progressed:
                        changed = true;

                        for (var x = 0; x < width; x++)
                        {
                            if (row[x] != rowBefore[x])
                            {
                                columnDirty[x] = true;
                            }
                        }

                        break;
                    default:
                        break;
                }
            }

            for (var x = 0; x < width; x++)
            {
                if (!columnDirty[x])
                {
                    continue;
                }

                columnDirty[x] = false;

                for (var y = 0; y < height; y++)
                {
                    column[y] = board[(y * width) + x];
                }

                var status = NonogramLineSolver.Solve(columnClues[x], column);

                if (status == LineSolveStatus.Contradiction)
                {
                    return new PuzzleSolveResult(PuzzleSolveOutcome.Contradiction, passes, CountUndetermined(board));
                }

                if (status == LineSolveStatus.Progressed)
                {
                    for (var y = 0; y < height; y++)
                    {
                        var index = (y * width) + x;

                        if (board[index] != column[y])
                        {
                            board[index] = column[y];
                            rowDirty[y] = true;
                        }
                    }

                    changed = true;
                }
            }

            if (!changed)
            {
                break;
            }
        }

        var undetermined = CountUndetermined(board);

        return new PuzzleSolveResult(
            undetermined == 0 ? PuzzleSolveOutcome.Solvable : PuzzleSolveOutcome.NeedsGuessing,
            passes,
            undetermined);
    }

    private static int CountUndetermined(ReadOnlySpan<CellState> board)
    {
        var count = 0;

        foreach (var cell in board)
        {
            if (cell == CellState.Empty)
            {
                count++;
            }
        }

        return count;
    }
}
