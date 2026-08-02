using Squarebuzz.Core.Model;

namespace Squarebuzz.Core.Solving;

/// <summary>
/// Chooses a hint the player could genuinely have worked out, rather than revealing a random
/// cell from the answer as the prototype did.
/// </summary>
/// <remarks>
/// <para>
/// Preference order matters teaching-wise. An immediate deduction - one that follows from a
/// single line, right now - shows the child the reasoning step they missed. Falling straight
/// to the answer teaches nothing, so it is the last resort and is flagged as such.
/// </para>
/// <para>
/// Every tier deduces from the board as the player has actually marked it, which is the whole
/// point - the hint has to follow from where they are, not from a clean grid. But it means a
/// deduction can rest on a false premise: a cell the player crossed by mistake is taken as
/// fact, and the "forced" answer that follows can contradict the real picture. Wrong crosses
/// are never refused (<see cref="GameSession.Paint"/> only rejects wrong fills, and only with
/// warnings on), so this is an ordinary state to be in, not an exotic one.
/// </para>
/// <para>
/// So every candidate is checked against the finished picture before it is offered. A hint
/// that disagrees with the answer would be worse than no hint at all: it is written straight
/// to the board, it costs one of a very small budget, and because winning requires an exact
/// match it would lock the child out of ever finishing. On a correctly played board the check
/// changes nothing - a true deduction always agrees with the unique solution.
/// </para>
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

    private static Hint? FindSolutionReveal(Puzzle puzzle, ReadOnlySpan<CellState> board)
    {
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
