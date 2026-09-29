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

        // Rows first, then columns - the order the hints have always been offered in. A row is a
        // contiguous slice (step 1); a column is every width-th cell (step width).
        for (var y = 0; y < height; y++)
        {
            if (FromLine(puzzle, board, puzzle.RowClues[y], line[..width], start: y * width, step: 1, ref crossedFallback) is { } hint)
            {
                return hint;
            }
        }

        for (var x = 0; x < width; x++)
        {
            if (FromLine(puzzle, board, puzzle.ColumnClues[x], line[..height], start: x, step: width, ref crossedFallback) is { } hint)
            {
                return hint;
            }
        }

        return crossedFallback;
    }

    /// <summary>
    /// Solves one line on its own and offers the first square it settles, rows and columns alike.
    /// </summary>
    /// <param name="line">Scratch buffer exactly as long as the line.</param>
    /// <param name="start">Board index of the line's first square.</param>
    /// <param name="step">Distance between consecutive squares of the line on the board.</param>
    private static Hint? FromLine(
        Puzzle puzzle,
        ReadOnlySpan<CellState> board,
        LineClues clues,
        Span<CellState> line,
        int start,
        int step,
        ref Hint? crossedFallback)
    {
        for (var i = 0; i < line.Length; i++)
        {
            line[i] = board[start + (i * step)];
        }

        if (NonogramLineSolver.Solve(clues, line) != LineSolveStatus.Progressed)
        {
            return null;
        }

        for (var i = 0; i < line.Length; i++)
        {
            if (Offer(puzzle, board, start + (i * step), line[i], HintSource.ImmediateDeduction, ref crossedFallback) is { } hint)
            {
                return hint;
            }
        }

        return null;
    }

    private static Hint? FindChainedDeduction(Puzzle puzzle, ReadOnlySpan<CellState> board)
    {
        var working = board.ToArray();

        // The status is deliberately ignored: on a board the player has contradicted, the solver
        // bails part-way and leaves whatever it had already written. Those partial writes are
        // still worth mining for a hint - the check in Offer is what makes that safe.
        PuzzleSolver.Solve(puzzle, working);

        Hint? crossedFallback = null;

        for (var index = 0; index < working.Length; index++)
        {
            if (Offer(puzzle, board, index, working[index], HintSource.ChainedDeduction, ref crossedFallback) is { } hint)
            {
                return hint;
            }
        }

        return crossedFallback;
    }

    /// <summary>
    /// Turns a deduced value for one square into a hint, if it is worth offering.
    /// </summary>
    /// <returns>
    /// The hint when the deduction fills a square - the more satisfying and more informative
    /// reveal, offered at once. A deduced cross is only remembered in
    /// <paramref name="crossedFallback"/>, for when nothing can be filled.
    /// </returns>
    private static Hint? Offer(
        Puzzle puzzle,
        ReadOnlySpan<CellState> board,
        int index,
        CellState deduced,
        HintSource source,
        ref Hint? crossedFallback)
    {
        // Only squares still blank on the player's board, and only deductions the answer agrees
        // with: they rest on the player's marks, so a wrong mark can force a wrong one.
        if (board[index] != CellState.Empty || deduced == CellState.Empty || deduced != puzzle.ExpectedState(index))
        {
            return null;
        }

        var hint = new Hint(index, deduced, index % puzzle.Width, index / puzzle.Width, source);

        if (deduced == CellState.Filled)
        {
            return hint;
        }

        crossedFallback ??= hint;
        return null;
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
