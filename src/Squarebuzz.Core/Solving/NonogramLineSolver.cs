using Squarebuzz.Core.Model;

namespace Squarebuzz.Core.Solving;

/// <summary>
/// Deduces the cells of a single line that are forced by its clue, given what is already
/// marked. This is the engine behind three separate features: proving a generated puzzle is
/// solvable without guessing, powering hints that are genuinely deducible, and cross-checking
/// the player's own marks.
/// </summary>
/// <remarks>
/// <para>
/// The method is exact, not heuristic. It walks every valid arrangement of the clue that is
/// consistent with the current marks and intersects them: a cell filled in every arrangement
/// must be filled, a cell blank in every arrangement must be crossed.
/// </para>
/// <para>
/// Both steps run over a dynamic-programming table of <c>(clue index, position)</c> states,
/// so cost is linear in line length times clue count rather than exponential in the number
/// of arrangements.
/// </para>
/// </remarks>
public static class NonogramLineSolver
{
    /// <summary>
    /// Applies every deduction the clue forces to <paramref name="line"/>, in place.
    /// Cells already marked by the player are left alone; only <see cref="CellState.Empty"/>
    /// cells can be filled in.
    /// </summary>
    public static LineSolveStatus Solve(LineClues clues, Span<CellState> line)
    {
        ArgumentNullException.ThrowIfNull(clues);

        var n = line.Length;
        if (n == 0)
        {
            return LineSolveStatus.Unchanged;
        }

        var runCount = clues.IsBlank ? 0 : clues.Count;

        // Quick reject: the clue cannot physically fit.
        if (clues.MinimumLength > n)
        {
            return LineSolveStatus.Contradiction;
        }

        // crossedBefore[i] = number of Crossed cells in [0, i), so "is [a,b) free of
        // crosses" becomes a subtraction instead of a scan.
        var crossedBefore = new int[n + 1];
        for (var i = 0; i < n; i++)
        {
            crossedBefore[i + 1] = crossedBefore[i] + (line[i] == CellState.Crossed ? 1 : 0);
        }

        // noFilledFrom[j] = no Filled cell anywhere in [j, n): i.e. the tail can be all blank.
        var noFilledFrom = new bool[n + 1];
        noFilledFrom[n] = true;
        for (var j = n - 1; j >= 0; j--)
        {
            noFilledFrom[j] = noFilledFrom[j + 1] && line[j] != CellState.Filled;
        }

        // feasible[k, j]: can runs k.. be laid out within cells j.. ?
        var feasible = new bool[runCount + 1, n + 1];
        for (var j = 0; j <= n; j++)
        {
            feasible[runCount, j] = noFilledFrom[j];
        }

        for (var k = runCount - 1; k >= 0; k--)
        {
            var length = clues[k];

            for (var j = n; j >= 0; j--)
            {
                // Option A: leave cell j blank and carry on.
                var canSkip = j < n
                              && line[j] != CellState.Filled
                              && feasible[k, j + 1];

                // Option B: start run k at j. It needs `length` cross-free cells, and the
                // cell immediately after must be blank to separate it from the next run.
                var canPlace = false;
                var end = j + length;

                if (end <= n && crossedBefore[end] - crossedBefore[j] == 0)
                {
                    if (end == n)
                    {
                        canPlace = feasible[k + 1, n];
                    }
                    else if (line[end] != CellState.Filled)
                    {
                        canPlace = feasible[k + 1, end + 1];
                    }
                }

                feasible[k, j] = canSkip || canPlace;
            }
        }

        if (!feasible[0, 0])
        {
            return LineSolveStatus.Contradiction;
        }

        // Second pass: walk only the states that lie on a complete valid arrangement, and
        // record which values each cell takes across all of them.
        var canBeFilled = new bool[n];
        var canBeBlank = new bool[n];
        var visited = new bool[runCount + 1, n + 1];
        var pending = new Stack<(int Run, int Position)>();

        pending.Push((0, 0));
        visited[0, 0] = true;

        while (pending.Count > 0)
        {
            var (k, j) = pending.Pop();

            if (k == runCount)
            {
                // Every remaining cell is blank in this arrangement.
                for (var p = j; p < n; p++)
                {
                    canBeBlank[p] = true;
                }

                continue;
            }

            if (j < n && line[j] != CellState.Filled && feasible[k, j + 1])
            {
                canBeBlank[j] = true;

                if (!visited[k, j + 1])
                {
                    visited[k, j + 1] = true;
                    pending.Push((k, j + 1));
                }
            }

            var length = clues[k];
            var end = j + length;

            if (end > n || crossedBefore[end] - crossedBefore[j] != 0)
            {
                continue;
            }

            int nextPosition;
            if (end == n)
            {
                if (!feasible[k + 1, n])
                {
                    continue;
                }

                nextPosition = n;
            }
            else
            {
                if (line[end] == CellState.Filled || !feasible[k + 1, end + 1])
                {
                    continue;
                }

                canBeBlank[end] = true;
                nextPosition = end + 1;
            }

            for (var p = j; p < end; p++)
            {
                canBeFilled[p] = true;
            }

            if (!visited[k + 1, nextPosition])
            {
                visited[k + 1, nextPosition] = true;
                pending.Push((k + 1, nextPosition));
            }
        }

        var progressed = false;

        for (var p = 0; p < n; p++)
        {
            // A cell that can be neither filled nor blank has no consistent arrangement.
            // feasible[0,0] rules this out, so treat it as a guard rather than an expectation.
            if (!canBeFilled[p] && !canBeBlank[p])
            {
                return LineSolveStatus.Contradiction;
            }

            if (canBeFilled[p] == canBeBlank[p])
            {
                continue; // Genuinely undetermined.
            }

            var forced = canBeFilled[p] ? CellState.Filled : CellState.Crossed;

            if (line[p] == CellState.Empty)
            {
                line[p] = forced;
                progressed = true;
            }
            else if (line[p] != forced)
            {
                // The player's own mark disagrees with what the clue forces.
                return LineSolveStatus.Contradiction;
            }
        }

        return progressed ? LineSolveStatus.Progressed : LineSolveStatus.Unchanged;
    }
}
