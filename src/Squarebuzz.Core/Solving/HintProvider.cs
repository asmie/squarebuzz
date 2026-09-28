using Squarebuzz.Core.Model;

namespace Squarebuzz.Core.Solving;

/// <summary>Chooses a deduction from the current marks, with a solution-based fallback.</summary>
/// <remarks>
/// Prefer single-line deductions. Validate candidates against the solution because incorrect
/// player marks can create false premises. The fallback corrects a wrong mark before revealing
/// an empty cell.
/// </remarks>
public static class HintProvider
{
    public static Hint? Find(Puzzle puzzle, ReadOnlySpan<CellState> board)
    {
        ArgumentNullException.ThrowIfNull(puzzle);

        if (board.Length != puzzle.CellCount)
        {
            throw new ArgumentException(
                $"Board has {board.Length} cells but the puzzle needs {puzzle.CellCount}.",
                nameof(board));
        }

        // Best: something a single line settles from the current position.
        var immediate = FindSingleLineDeduction(puzzle, board);
        if (immediate is not null)
        {
            return immediate;
        }

        // Next best: still logic, but it took chaining lines together to get there.
        var chained = FindChainedDeduction(puzzle, board);
        if (chained is not null)
        {
            return chained;
        }

        return FindSolutionReveal(puzzle, board);
    }

    private static Hint? FindSingleLineDeduction(Puzzle puzzle, ReadOnlySpan<CellState> board)
    {
        var width = puzzle.Width;
        var height = puzzle.Height;

        Span<CellState> line = stackalloc CellState[Math.Max(width, height)];
        Hint? crossedFallback = null;

        for (var y = 0; y < height; y++)
        {
            var row = line[..width];
            board.Slice(y * width, width).CopyTo(row);

            if (NonogramLineSolver.Solve(puzzle.RowClues[y], row) != LineSolveStatus.Progressed)
            {
                continue;
            }

            for (var x = 0; x < width; x++)
            {
                var index = (y * width) + x;

                if (board[index] != CellState.Empty || row[x] == CellState.Empty)
                {
                    continue;
                }

                // Deduced from the player's marks, so a wrong mark can force a wrong answer.
                if (row[x] != puzzle.ExpectedState(index))
                {
                    continue;
                }

                var hint = new Hint(index, row[x], x, y, HintSource.ImmediateDeduction);

                // Filling a cell is the more satisfying and more informative reveal, so a
                // cross is only offered when nothing can be filled.
                if (row[x] == CellState.Filled)
                {
                    return hint;
                }

                crossedFallback ??= hint;
            }
        }

        for (var x = 0; x < width; x++)
        {
            var column = line[..height];

            for (var y = 0; y < height; y++)
            {
                column[y] = board[(y * width) + x];
            }

            if (NonogramLineSolver.Solve(puzzle.ColumnClues[x], column) != LineSolveStatus.Progressed)
            {
                continue;
            }

            for (var y = 0; y < height; y++)
            {
                var index = (y * width) + x;

                if (board[index] != CellState.Empty || column[y] == CellState.Empty)
                {
                    continue;
                }

                if (column[y] != puzzle.ExpectedState(index))
                {
                    continue;
                }

                var hint = new Hint(index, column[y], x, y, HintSource.ImmediateDeduction);

                if (column[y] == CellState.Filled)
                {
                    return hint;
                }

                crossedFallback ??= hint;
            }
        }

        return crossedFallback;
    }

    private static Hint? FindChainedDeduction(Puzzle puzzle, ReadOnlySpan<CellState> board)
    {
        var working = board.ToArray();

        // The status is deliberately ignored: on a board the player has contradicted, the solver
        // bails part-way and leaves whatever it had already written. Those partial writes are
        // still worth mining for a hint - the check below is what makes that safe.
        PuzzleSolver.Solve(puzzle, working);

        Hint? crossedFallback = null;

        for (var index = 0; index < working.Length; index++)
        {
            if (board[index] != CellState.Empty || working[index] == CellState.Empty)
            {
                continue;
            }

            if (working[index] != puzzle.ExpectedState(index))
            {
                continue;
            }

            var hint = new Hint(
                index,
                working[index],
                index % puzzle.Width,
                index / puzzle.Width,
                HintSource.ChainedDeduction);

            if (working[index] == CellState.Filled)
            {
                return hint;
            }

            crossedFallback ??= hint;
        }

        return crossedFallback;
    }

    /// <remarks>
    /// A wrong mark is corrected before anything is revealed. When the player has crossed a
    /// square that belongs to the picture, no amount of revealing blank squares will ever let
    /// them finish - and once the blanks ran out the hint button used to do nothing at all, with
    /// no word as to why the picture would not complete.
    /// </remarks>
    private static Hint? FindSolutionReveal(Puzzle puzzle, ReadOnlySpan<CellState> board)
    {
        for (var index = 0; index < board.Length; index++)
        {
            var expected = puzzle.ExpectedState(index);

            if (board[index] != CellState.Empty && board[index] != expected)
            {
                return new Hint(index, expected, index % puzzle.Width, index / puzzle.Width, HintSource.SolutionReveal);
            }
        }

        for (var index = 0; index < board.Length; index++)
        {
            var expected = puzzle.ExpectedState(index);

            if (board[index] == CellState.Empty && expected == CellState.Filled)
            {
                return new Hint(index, expected, index % puzzle.Width, index / puzzle.Width, HintSource.SolutionReveal);
            }
        }

        for (var index = 0; index < board.Length; index++)
        {
            var expected = puzzle.ExpectedState(index);

            if (board[index] == CellState.Empty)
            {
                return new Hint(index, expected, index % puzzle.Width, index / puzzle.Width, HintSource.SolutionReveal);
            }
        }

        return null; // Nothing left to reveal.
    }
}
