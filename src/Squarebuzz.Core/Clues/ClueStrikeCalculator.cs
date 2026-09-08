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
        if (ClueCalculator.MatchesMarks(clues, line))
        {
            struck[..displayCount].Fill(true);
            return;
        }

        // The backward pass must not walk back over cells the forward pass already accounted
        // for, or a single run can be claimed twice - see StrikeFromEnd.
        var claimedUpTo = StrikeFromStart(clues, line, struck);
        StrikeFromEnd(clues, line, struck, claimedUpTo);

        // Every number struck is the visual language for "this line is finished", so it must
        // never appear on a line that is not. Each individual strike above is defensible - the
        // run is anchored and the right length - but the passes stop as soon as they run out of
        // clue numbers, and an over-filled line has runs left over that neither pass ever looks
        // at. A line reading `#x#` against the clue `1` struck its only number, telling a child
        // the row was done while it carried an extra run.
        //
        // "All struck" and "does not match" is a contradiction rather than a judgement call: if
        // every number were genuinely done the filled runs would spell out the clue, which is
        // what MatchesMarks tests and what the fast path above already returns on. So the flags
        // are simply wrong here, and the honest answer is to strike nothing.
        if (AllStruck(struck, displayCount) && !ClueCalculator.MatchesMarks(clues, line))
        {
            struck[..displayCount].Clear();
        }
    }

    private static bool AllStruck(ReadOnlySpan<bool> struck, int displayCount)
    {
        for (var i = 0; i < displayCount; i++)
        {
            if (!struck[i])
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Convenience overload for tests and non-hot paths.</summary>
    public static bool[] Compute(LineClues clues, ReadOnlySpan<CellState> line)
    {
        ArgumentNullException.ThrowIfNull(clues);

        var struck = new bool[clues.DisplayRuns.Count];
        Compute(clues, line, struck);
        return struck;
    }

    /// <summary>
    /// Strikes clues anchored to the start of the line, and reports the first cell it did not
    /// account for, so the backward pass knows where to stop.
    /// </summary>
    private static int StrikeFromStart(LineClues clues, ReadOnlySpan<CellState> line, Span<bool> struck)
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
                return cell; // Hit an unmarked cell: nothing beyond here is pinned down.
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
                return cell;
            }

            struck[clue] = true;
            clue++;
            cell = runEnd;
        }

        return cell;
    }

    /// <summary>
    /// Strikes clues anchored to the end of the line, stopping at <paramref name="claimedUpTo"/> -
    /// the first cell the forward pass left unaccounted for.
    /// </summary>
    /// <remarks>
    /// The stop condition has to be a cell index, not <c>struck[clue]</c>. On a line the player
    /// has over-crossed there are fewer runs left than clue numbers, so the forward pass can
    /// consume the whole line while leaving later numbers unstruck - and the backward pass would
    /// then walk back over the very same run and claim it for a second number. A line reading
    /// <c>x#x</c> against the clue <c>1 1</c> struck both numbers, telling a child the line was
    /// finished at the exact moment they had broken it.
    /// </remarks>
    private static void StrikeFromEnd(
        LineClues clues,
        ReadOnlySpan<CellState> line,
        Span<bool> struck,
        int claimedUpTo)
    {
        var cell = line.Length - 1;
        var clue = clues.Count - 1;

        while (cell >= claimedUpTo && clue >= 0)
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
