using Squarebuzz.Core.Model;

namespace Squarebuzz.Core.Clues;

/// <summary>
/// Works out which clue numbers the player has provably finished, so the UI can fade them
/// out. Ported from the prototype's <c>struckFlags</c>.
/// </summary>
/// <remarks>
/// Only runs that are pinned down are struck: those anchored to the start or end of the line
/// by crosses or the edge. A matching run floating in the middle of an unresolved line is
/// left alone, because it might yet turn out to belong to a different clue number - striking
/// it would actively mislead a child.
/// </remarks>
public static class ClueStrikeCalculator
{
    /// <summary>
    /// Writes one flag per entry of <see cref="LineClues.DisplayRuns"/> into
    /// <paramref name="struck"/>. Allocation-free, for use during rendering.
    /// </summary>
    public static void Compute(LineClues clues, ReadOnlySpan<CellState> line, Span<bool> struck)
    {
        ArgumentNullException.ThrowIfNull(clues);

        var displayCount = clues.DisplayRuns.Count;

        if (struck.Length < displayCount)
        {
            throw new ArgumentException($"Need room for {displayCount} flags but got {struck.Length}.", nameof(struck));
        }

        struck[..displayCount].Clear();

        // A blank line shows a single "0", struck once every cell is crossed off.
        if (clues.IsBlank)
        {
            var allCrossed = true;

            foreach (var cell in line)
            {
                if (cell != CellState.Crossed)
                {
                    allCrossed = false;
                    break;
                }
            }

            struck[0] = allCrossed;
            return;
        }

        // Whole line already correct: every number is done.
        if (ClueCalculator.FromMarks(line).Equals(clues))
        {
            struck[..displayCount].Fill(true);
            return;
        }

        StrikeFromStart(clues, line, struck);
        StrikeFromEnd(clues, line, struck);
    }

    /// <summary>Convenience overload for tests and non-hot paths.</summary>
    public static bool[] Compute(LineClues clues, ReadOnlySpan<CellState> line)
    {
        ArgumentNullException.ThrowIfNull(clues);

        var struck = new bool[clues.DisplayRuns.Count];
        Compute(clues, line, struck);
        return struck;
    }

    private static void StrikeFromStart(LineClues clues, ReadOnlySpan<CellState> line, Span<bool> struck)
    {
        var cell = 0;
        var clue = 0;

        while (cell < line.Length && clue < clues.Count)
        {
            if (line[cell] == CellState.Crossed)
            {
                cell++;
                continue;
            }

            if (line[cell] != CellState.Filled)
            {
                return; // Hit an unmarked cell: nothing beyond here is pinned down.
            }

            var runEnd = cell;
            while (runEnd < line.Length && line[runEnd] == CellState.Filled)
            {
                runEnd++;
            }

            var runLength = runEnd - cell;
            var terminated = runEnd == line.Length || line[runEnd] == CellState.Crossed;

            if (runLength != clues[clue] || !terminated)
            {
                return;
            }

            struck[clue] = true;
            clue++;
            cell = runEnd;
        }
    }

    private static void StrikeFromEnd(LineClues clues, ReadOnlySpan<CellState> line, Span<bool> struck)
    {
        var cell = line.Length - 1;
        var clue = clues.Count - 1;

        while (cell >= 0 && clue >= 0)
        {
            // Stop once we meet the region the forward pass already claimed.
            if (struck[clue])
            {
                return;
            }

            if (line[cell] == CellState.Crossed)
            {
                cell--;
                continue;
            }

            if (line[cell] != CellState.Filled)
            {
                return;
            }

            var runStart = cell;
            while (runStart >= 0 && line[runStart] == CellState.Filled)
            {
                runStart--;
            }

            var runLength = cell - runStart;
            var terminated = runStart < 0 || line[runStart] == CellState.Crossed;

            if (runLength != clues[clue] || !terminated)
            {
                return;
            }

            struck[clue] = true;
            clue--;
            cell = runStart;
        }
    }
}
