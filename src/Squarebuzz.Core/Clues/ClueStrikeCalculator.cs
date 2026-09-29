using Squarebuzz.Core.Model;

namespace Squarebuzz.Core.Clues;

/// <summary>
/// Works out which clue numbers the player has provably finished, so the UI can fade them
/// out. Ported from the prototype's <c>struckFlags</c>.
/// </summary>
/// <remarks>
/// Only runs that are pinned down are struck: those anchored to the start or end of the line
/// by crosses or the edge, and those anywhere else that the marks prove can only be one clue
/// number at exactly its length. A matching run that might yet turn out to belong to a
/// different clue number is left alone - striking it would actively mislead a child.
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

        // The end passes cannot see past the first unresolved cell, so a run in the middle of
        // the line - the 3 of "2 3 2" - was never struck however clearly it was done.
        StrikeProvenRuns(clues, line, struck);

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
        // are simply wrong here, and the honest answer is to strike nothing. (The line is known
        // not to match - a matching line returned at the fast path - so it is not tested again.)
        if (AllStruck(struck, displayCount))
        {
            struck[..displayCount].Clear();
        }
    }

    /// <summary>
    /// Strikes every clue number that the marks prove is finished, wherever it sits in the line.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A filled run is claimed by a clue number only when that is the <em>only</em> number any
    /// arrangement consistent with the marks could place over it, and the number is exactly the
    /// run's length - so the run can neither grow nor turn out to be a different number. That
    /// is the same caution the end passes apply, stated as logic rather than as position: a
    /// lone 3 against "2 3 2" can only be the 3, while a 1 floating inside "1 1" could be
    /// either and stays unstruck.
    /// </para>
    /// <para>
    /// When no arrangement fits the marks at all the player has made a mistake, and every
    /// claim would rest on it. Nothing is added then; the end passes still report what they can.
    /// Allocation-free for any real board: the two tables live on the stack.
    /// </para>
    /// </remarks>
    private static void StrikeProvenRuns(LineClues clues, ReadOnlySpan<CellState> line, Span<bool> struck)
    {
        var n = line.Length;
        var m = clues.Count;
        var stride = n + 1;
        var size = (m + 1) * stride;

        Span<bool> prefix = size <= 1024 ? stackalloc bool[size] : new bool[size];
        Span<bool> suffix = size <= 1024 ? stackalloc bool[size] : new bool[size];
        prefix.Clear();
        suffix.Clear();

        // prefix[k, i]: the first k numbers fit in cells [0, i).
        prefix[0] = true;
        for (var i = 1; i <= n; i++)
        {
            for (var k = 0; k <= m; k++)
            {
                var fits = line[i - 1] != CellState.Filled && prefix[(k * stride) + i - 1];

                if (!fits && k > 0)
                {
                    var start = i - clues[k - 1];
                    fits = start >= 0
                           && IsFreeOfCrosses(line, start, i)
                           && (start == 0
                               ? k == 1
                               : line[start - 1] != CellState.Filled && prefix[((k - 1) * stride) + start - 1]);
                }

                prefix[(k * stride) + i] = fits;
            }
        }

        // The marks contradict the clue: any claim would be built on a mistake.
        if (!prefix[(m * stride) + n])
        {
            return;
        }

        // suffix[k, i]: numbers k onwards fit in cells [i, n).
        suffix[(m * stride) + n] = true;
        for (var i = n - 1; i >= 0; i--)
        {
            for (var k = m; k >= 0; k--)
            {
                var fits = line[i] != CellState.Filled && suffix[(k * stride) + i + 1];

                if (!fits && k < m)
                {
                    var end = i + clues[k];
                    fits = end <= n
                           && IsFreeOfCrosses(line, i, end)
                           && (end == n
                               ? k == m - 1
                               : line[end] != CellState.Filled && suffix[((k + 1) * stride) + end + 1]);
                }

                suffix[(k * stride) + i] = fits;
            }
        }

        var cell = 0;
        while (cell < n)
        {
            if (line[cell] != CellState.Filled)
            {
                cell++;
                continue;
            }

            var runStart = cell;
            while (cell < n && line[cell] == CellState.Filled)
            {
                cell++;
            }

            var runLength = cell - runStart;
            var owner = -1;
            var ambiguous = false;

            for (var k = 0; k < m && !ambiguous; k++)
            {
                var length = clues[k];
                if (length < runLength)
                {
                    continue;
                }

                // Every placement of number k that would cover the whole run.
                var from = Math.Max(0, cell - length);
                var to = Math.Min(runStart, n - length);

                for (var start = from; start <= to; start++)
                {
                    if (CanPlace(clues, line, prefix, suffix, stride, k, start))
                    {
                        ambiguous = owner >= 0 && owner != k;
                        owner = k;
                        break;
                    }
                }
            }

            if (!ambiguous && owner >= 0 && clues[owner] == runLength)
            {
                struck[owner] = true;
            }
        }
    }

    /// <summary>Whether number <paramref name="k"/> can start at <paramref name="start"/> in some complete arrangement.</summary>
    private static bool CanPlace(
        LineClues clues,
        ReadOnlySpan<CellState> line,
        ReadOnlySpan<bool> prefix,
        ReadOnlySpan<bool> suffix,
        int stride,
        int k,
        int start)
    {
        var n = line.Length;
        var m = clues.Count;
        var end = start + clues[k];

        if (!IsFreeOfCrosses(line, start, end))
        {
            return false;
        }

        var leftFits = start == 0
            ? k == 0
            : line[start - 1] != CellState.Filled && prefix[(k * stride) + start - 1];

        if (!leftFits)
        {
            return false;
        }

        return end == n
            ? k == m - 1
            : line[end] != CellState.Filled && suffix[((k + 1) * stride) + end + 1];
    }

    private static bool IsFreeOfCrosses(ReadOnlySpan<CellState> line, int from, int to) =>
        !line[from..to].Contains(CellState.Crossed);

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
