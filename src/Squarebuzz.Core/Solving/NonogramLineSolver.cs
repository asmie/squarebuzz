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
    /// Largest (runs + 1) x (cells + 1) table kept on the stack. A 25-cell line holds at most 13
    /// runs, 364 states; this leaves room without letting a pathological line blow the stack.
    /// </summary>
    private const int MaxStackStates = 1024;

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

        // Every table lives on the stack for any line the game draws (25 cells at most). This
        // runs thousands of times per generated puzzle - once per line per solver pass per
        // candidate - and the heap arrays, 2-D tables and Stack it used to allocate each time
        // were most of what generation spent on garbage. Flat tables index [k, j] as
        // k * stride + j. Very long lines, which nothing ships, fall back to the heap.
        var stride = n + 1;
        var states = (runCount + 1) * stride;
        var small = states <= MaxStackStates;

        // crossedBefore[i] = number of Crossed cells in [0, i), so "is [a,b) free of
        // crosses" becomes a subtraction instead of a scan.
        Span<int> crossedBefore = small ? stackalloc int[stride] : new int[stride];
        crossedBefore[0] = 0;
        for (var i = 0; i < n; i++)
        {
            crossedBefore[i + 1] = crossedBefore[i] + (line[i] == CellState.Crossed ? 1 : 0);
        }

        // feasible[k, j]: can runs k.. be laid out within cells j.. ? The last row, k == runCount,
        // is "no Filled cell anywhere in [j, n)": the tail can be all blank.
        Span<bool> feasible = small ? stackalloc bool[states] : new bool[states];
        feasible.Clear();
        var last = runCount * stride;
        feasible[last + n] = true;
        for (var j = n - 1; j >= 0; j--)
        {
            feasible[last + j] = feasible[last + j + 1] && line[j] != CellState.Filled;
        }

        for (var k = runCount - 1; k >= 0; k--)
        {
            var length = clues[k];

            for (var j = n; j >= 0; j--)
            {
                // Option A: leave cell j blank and carry on.
                var canSkip = j < n
                              && line[j] != CellState.Filled
                              && feasible[(k * stride) + j + 1];

                // Option B: start run k at j. It needs `length` cross-free cells, and the
                // cell immediately after must be blank to separate it from the next run.
                var canPlace = false;
                var end = j + length;

                if (end <= n && crossedBefore[end] - crossedBefore[j] == 0)
                {
                    if (end == n)
                    {
                        canPlace = feasible[((k + 1) * stride) + n];
                    }
                    else if (line[end] != CellState.Filled)
                    {
                        canPlace = feasible[((k + 1) * stride) + end + 1];
                    }
                }

                feasible[(k * stride) + j] = canSkip || canPlace;
            }
        }

        if (!feasible[0])
        {
            return LineSolveStatus.Contradiction;
        }

        // Second pass: walk only the states that lie on a complete valid arrangement, and
        // record which values each cell takes across all of them. Each state is pushed at most
        // once (visited guards it), so a stack as large as the state table can never overflow.
        Span<bool> canBeFilled = small ? stackalloc bool[n] : new bool[n];
        Span<bool> canBeBlank = small ? stackalloc bool[n] : new bool[n];
        Span<bool> visited = small ? stackalloc bool[states] : new bool[states];
        Span<int> pending = small ? stackalloc int[states] : new int[states];
        canBeFilled.Clear();
        canBeBlank.Clear();
        visited.Clear();

        var top = 0;
        pending[top++] = 0;
        visited[0] = true;

        while (top > 0)
        {
            var state = pending[--top];
            var k = state / stride;
            var j = state % stride;

            if (k == runCount)
            {
                // Every remaining cell is blank in this arrangement.
                for (var p = j; p < n; p++)
                {
                    canBeBlank[p] = true;
                }

                continue;
            }

            if (j < n && line[j] != CellState.Filled && feasible[state + 1])
            {
                canBeBlank[j] = true;

                if (!visited[state + 1])
                {
                    visited[state + 1] = true;
                    pending[top++] = state + 1;
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
                if (!feasible[((k + 1) * stride) + n])
                {
                    continue;
                }

                nextPosition = n;
            }
            else
            {
                if (line[end] == CellState.Filled || !feasible[((k + 1) * stride) + end + 1])
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

            var next = ((k + 1) * stride) + nextPosition;

            if (!visited[next])
            {
                visited[next] = true;
                pending[top++] = next;
            }
        }

        var progressed = false;

        for (var p = 0; p < n; p++)
        {
            // A cell that can be neither filled nor blank has no consistent arrangement.
            // feasible[0, 0] rules this out, so treat it as a guard rather than an expectation.
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
