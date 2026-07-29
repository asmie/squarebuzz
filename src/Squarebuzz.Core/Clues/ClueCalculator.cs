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
    public static LineClues FromSolution(ReadOnlySpan<bool> line)
    {
        Span<int> runs = line.Length <= 64 ? stackalloc int[(line.Length + 1) / 2] : new int[(line.Length + 1) / 2];
        var count = 0;
        var current = 0;

        foreach (var filled in line)
        {
            if (filled)
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
    /// Clue implied by what the player has marked so far. Only <see cref="CellState.Filled"/>
    /// counts; <see cref="CellState.Empty"/> and <see cref="CellState.Crossed"/> both break a run.
    /// </summary>
    public static LineClues FromMarks(ReadOnlySpan<CellState> line)
    {
        Span<int> runs = line.Length <= 64 ? stackalloc int[(line.Length + 1) / 2] : new int[(line.Length + 1) / 2];
        var count = 0;
        var current = 0;

        foreach (var cell in line)
        {
            if (cell == CellState.Filled)
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
}
