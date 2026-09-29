using Squarebuzz.Core.Model;

namespace Squarebuzz.Core.Clues;

/// <summary>
/// Derives clues from a line of cells. Clues are always computed, never stored, so they
/// cannot drift out of step with the picture they describe.
/// </summary>
public static class ClueCalculator
{
    /// <summary>
    /// Clue for a line of the finished picture. Port of the prototype's <c>lineClues</c>.
    /// </summary>
    public static LineClues FromSolution(ReadOnlySpan<bool> line) => Build<bool, SolutionCell>(line);

    /// <summary>
    /// Clue implied by what the player has marked so far. Only <see cref="CellState.Filled"/>
    /// counts; <see cref="CellState.Empty"/> and <see cref="CellState.Crossed"/> both break a run.
    /// </summary>
    public static LineClues FromMarks(ReadOnlySpan<CellState> line) => Build<CellState, MarkedCell>(line);

    /// <summary>What counts as a filled square, for each kind of line a clue is read from.</summary>
    /// <remarks>
    /// A static abstract member rather than a delegate, so the one run-counting loop below serves
    /// both kinds of line with no per-cell call cost.
    /// </remarks>
    private interface IFilledTest<T>
    {
        static abstract bool IsFilled(T cell);
    }

    private readonly struct SolutionCell : IFilledTest<bool>
    {
        public static bool IsFilled(bool cell) => cell;
    }

    private readonly struct MarkedCell : IFilledTest<CellState>
    {
        public static bool IsFilled(CellState cell) => cell == CellState.Filled;
    }

    private static LineClues Build<T, TTest>(ReadOnlySpan<T> line)
        where TTest : IFilledTest<T>
    {
        Span<int> runs = line.Length <= 64 ? stackalloc int[(line.Length + 1) / 2] : new int[(line.Length + 1) / 2];
        var count = 0;
        var current = 0;

        foreach (var cell in line)
        {
            if (TTest.IsFilled(cell))
            {
                current++;
            }
            else if (current > 0)
            {
                runs[count++] = current;
                current = 0;
            }
        }

        if (current > 0)
        {
            runs[count++] = current;
        }

        return count == 0 ? LineClues.Blank : new LineClues(runs[..count].ToArray());
    }

    /// <summary>
    /// Whether the player's marks already spell out exactly <paramref name="clues"/> - the same
    /// answer as <c>FromMarks(line).Equals(clues)</c>, without building anything.
    /// </summary>
    /// <remarks>
    /// This is asked a great deal: twice per painted cell by the auto-cross rules, and once per
    /// row and column on every frame the board draws. Answering it by materialising a
    /// <see cref="LineClues"/> cost two arrays and an object each time - about six kilobytes of
    /// garbage per drawn frame, on a canvas that redraws while a finger is moving across it.
    /// Comparing run lengths against the clue as the line is walked needs no memory at all and
    /// can also stop at the first run that disagrees.
    /// </remarks>
    public static bool MatchesMarks(LineClues clues, ReadOnlySpan<CellState> line)
    {
        ArgumentNullException.ThrowIfNull(clues);

        var clue = 0;
        var run = 0;

        for (var i = 0; i < line.Length; i++)
        {
            if (line[i] == CellState.Filled)
            {
                run++;
                continue;
            }

            if (run == 0)
            {
                continue;
            }

            // A run that is longer than its clue, or one run too many, can never come good.
            if (clue == clues.Count || run != clues[clue])
            {
                return false;
            }

            clue++;
            run = 0;
        }

        if (run > 0)
        {
            if (clue == clues.Count || run != clues[clue])
            {
                return false;
            }

            clue++;
        }

        // Every clue accounted for, and no clue left over.
        return clue == clues.Count;
    }
}
