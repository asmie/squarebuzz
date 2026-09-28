using Squarebuzz.Core.Clues;
using Squarebuzz.Core.Model;
using Xunit;

namespace Squarebuzz.Core.Tests.Clues;

public class ClueStrikeCalculatorTests
{
    [Theory]
    // One run cannot satisfy two clue numbers. Striking both told a child the line was
    // finished at the exact moment they had over-crossed and broken it.
    [InlineData("x#x", new[] { 1, 1 })]
    [InlineData("xx#xx", new[] { 1, 1 })]
    [InlineData("xx#x#xx", new[] { 1, 1, 1 })]
    public void AnOverCrossedLine_DoesNotStrikeEveryClue(string line, int[] runs)
    {
        var clues = new LineClues(runs);

        var struck = ClueStrikeCalculator.Compute(clues, Marks(line));

        Assert.Contains(false, struck);
    }

    [Theory]
    // The forward and backward passes must still each claim their own anchored run.
    [InlineData("x##xx", new[] { 2, 1 }, new[] { true, false })]
    [InlineData("x#xx##xx", new[] { 1, 2, 1 }, new[] { true, true, false })]
    public void ConsistentLines_AreUnaffected(string line, int[] runs, bool[] expected)
    {
        var struck = ClueStrikeCalculator.Compute(new LineClues(runs), Marks(line));

        Assert.Equal(expected, struck);
    }

    private static CellState[] Marks(string pattern) =>
        [.. pattern.Select(c => c switch
        {
            '#' => CellState.Filled,
            'x' => CellState.Crossed,
            _ => CellState.Empty,
        })];

    /// <summary>The inverse of <see cref="Marks"/>, so an exhaustive failure names its line.</summary>
    private static string Describe(IEnumerable<CellState> line) =>
        string.Concat(line.Select(c => c switch
        {
            CellState.Filled => '#',
            CellState.Crossed => 'x',
            _ => '.',
        }));

    [Fact]
    public void UntouchedLine_StrikesNothing()
    {
        var struck = ClueStrikeCalculator.Compute(new LineClues(2, 1), Marks("....."));

        Assert.Equal([false, false], struck);
    }

    [Fact]
    public void FullyCorrectLine_StrikesEveryNumber()
    {
        var struck = ClueStrikeCalculator.Compute(new LineClues(2, 1), Marks("##x#x"));

        Assert.Equal([true, true], struck);
    }

    [Fact]
    public void RunAnchoredAtTheStart_IsStruck()
    {
        // "##x.." pins the leading 2, but the 1 could still be anywhere after it.
        var struck = ClueStrikeCalculator.Compute(new LineClues(2, 1), Marks("##x.."));

        Assert.Equal([true, false], struck);
    }

    [Fact]
    public void RunAnchoredAtTheEnd_IsStruck()
    {
        // Reading from the right, "x#" pins the trailing 1.
        var struck = ClueStrikeCalculator.Compute(new LineClues(2, 1), Marks("...x#"));

        Assert.Equal([false, true], struck);
    }

    [Fact]
    public void UnterminatedRun_IsNotStruck()
    {
        // "##..." might yet grow into the 3: without a cross after it, nothing is settled.
        var struck = ClueStrikeCalculator.Compute(new LineClues(3, 1), Marks("##..."));

        Assert.Equal([false, false], struck);
    }

    [Fact]
    public void RunThatCannotGrow_IsStruckWithoutACross()
    {
        // No number is longer than 2, so "##..." is already the whole of the first 2.
        var struck = ClueStrikeCalculator.Compute(new LineClues(2, 1), Marks("##..."));

        Assert.Equal([true, false], struck);
    }

    [Theory]
    // The reported case: the 3 of "2 3 2" sits in the middle, unreachable from either end.
    [InlineData("...###....", new[] { 2, 3, 2 }, new[] { false, true, false })]
    [InlineData("..x###x...", new[] { 2, 3, 2 }, new[] { false, true, false })]
    [InlineData("....###...", new[] { 2, 3, 2 }, new[] { false, true, false })]
    // Equal numbers stay open while either could own the run...
    [InlineData("...##.....", new[] { 2, 2 }, new[] { false, false })]
    [InlineData(".x#x.", new[] { 1, 1 }, new[] { false, false })]
    // ...but position alone can settle it: only the second 2 can reach this far right.
    [InlineData(".....x##x.", new[] { 2, 2 }, new[] { false, true })]
    // The middle number is struck alongside anchored ones.
    [InlineData("#x.###..x#", new[] { 1, 3, 1 }, new[] { true, true, true })]
    [InlineData("##x..###.......", new[] { 2, 3, 4 }, new[] { true, true, false })]
    public void RunInTheMiddle_IsStruckWhenOnlyOneNumberFits(string line, int[] runs, bool[] expected)
    {
        var struck = ClueStrikeCalculator.Compute(new LineClues(runs), Marks(line));

        Assert.Equal(expected, struck);
    }

    [Fact]
    public void ContradictoryMarks_AddNoMiddleStrikes()
    {
        // The crosses leave no room for the 2s around the 3, so the marks are wrong somewhere
        // and a claim resting on them could be wrong too.
        var struck = ClueStrikeCalculator.Compute(new LineClues(2, 3, 2), Marks("xx.###.xxx"));

        Assert.Equal([false, false, false], struck);
    }

    [Fact]
    public void FloatingRun_IsNotStruck()
    {
        // A matching run in the middle of an unresolved line could belong to either number,
        // so striking one would actively mislead the player.
        var struck = ClueStrikeCalculator.Compute(new LineClues(1, 1), Marks(".x#x."));

        Assert.Equal([false, false], struck);
    }

    [Fact]
    public void RunOfTheWrongLength_IsNotStruck()
    {
        var struck = ClueStrikeCalculator.Compute(new LineClues(3, 1), Marks("##x.."));

        Assert.Equal([false, false], struck);
    }

    [Fact]
    public void BlankClue_IsStruckOnlyWhenEveryCellIsCrossed()
    {
        Assert.Equal([false], ClueStrikeCalculator.Compute(LineClues.Blank, Marks("xxxx.")));
        Assert.Equal([true], ClueStrikeCalculator.Compute(LineClues.Blank, Marks("xxxxx")));
    }

    [Fact]
    public void BothEndsCanBeStruckIndependently()
    {
        // "#x.x#" pins the first 1 from the left and the last 1 from the right; the middle
        // number stays open.
        var struck = ClueStrikeCalculator.Compute(new LineClues(1, 1, 1), Marks("#x.x#"));

        Assert.Equal([true, false, true], struck);
    }

    [Fact]
    public void SpanOverload_MatchesTheAllocatingOne()
    {
        var clues = new LineClues(2, 1);
        var line = Marks("##x#x");

        Span<bool> struck = stackalloc bool[clues.DisplayRuns.Count];
        ClueStrikeCalculator.Compute(clues, line, struck);

        Assert.Equal(ClueStrikeCalculator.Compute(clues, line), struck.ToArray());
    }

    [Theory]
    // The mirror of the over-crossed case: an *over-filled* line has runs left over that
    // neither pass looks at, because both stop as soon as they run out of clue numbers.
    // Striking every number still says "this row is finished" on a row that is not.
    [InlineData("#x#", new[] { 1 })]
    [InlineData("#x#.", new[] { 1 })]
    [InlineData("#x.#", new[] { 1 })]
    [InlineData("##x#", new[] { 2 })]
    [InlineData("#x##", new[] { 1 })]
    [InlineData("#x#x#", new[] { 1, 1 })]
    [InlineData("x##xxx#x", new[] { 2 })]
    public void AnOverFilledLine_DoesNotStrikeEveryClue(string line, int[] runs)
    {
        var clues = new LineClues(runs);

        var struck = ClueStrikeCalculator.Compute(clues, Marks(line));

        Assert.Contains(false, struck);
    }

    [Theory]
    // The guard must not cost a line that genuinely is finished its strikes.
    [InlineData("#x#", new[] { 1, 1 })]
    [InlineData("###", new[] { 3 })]
    [InlineData("##x#", new[] { 2, 1 })]
    [InlineData(".#x#.", new[] { 1, 1 })]
    [InlineData("xxx", new int[0])]
    public void AFinishedLine_StrikesEveryClue(string line, int[] runs)
    {
        var clues = new LineClues(runs);

        var struck = ClueStrikeCalculator.Compute(clues, Marks(line));

        Assert.DoesNotContain(false, struck);
    }

    [Fact]
    // The invariant behind both theories above, stated once: "every number struck" is how the
    // board says a line is done, so it may only occur when the line really does match its clue.
    public void EveryClueStruck_OnlyEverMeansTheLineMatches()
    {
        var states = new[] { CellState.Empty, CellState.Filled, CellState.Crossed };

        for (var length = 1; length <= 8; length++)
        {
            var combinations = (int)Math.Pow(3, length);

            for (var code = 0; code < combinations; code++)
            {
                var line = new CellState[length];
                var remaining = code;

                for (var i = 0; i < length; i++)
                {
                    line[i] = states[remaining % 3];
                    remaining /= 3;
                }

                // Every clue a line of this length could carry, taken from every picture it
                // could be drawn from.
                for (var picture = 0; picture < 1 << length; picture++)
                {
                    var solution = new bool[length];
                    for (var i = 0; i < length; i++)
                    {
                        solution[i] = (picture & (1 << i)) != 0;
                    }

                    var clues = ClueCalculator.FromSolution(solution);
                    var struck = ClueStrikeCalculator.Compute(clues, line);

                    if (Array.IndexOf(struck, false) < 0)
                    {
                        Assert.True(
                            ClueCalculator.MatchesMarks(clues, line),
                            $"every number struck for clue [{string.Join(',', clues.DisplayRuns)}] on line {Describe(line)}, which does not match it");
                    }
                }
            }
        }
    }

    [Fact]
    // A struck number is a promise: on a line marked without mistakes, that number's block in
    // the real picture is already filled in, whole, somewhere on the line.
    public void OnACorrectlyMarkedLine_EveryStruckNumberIsReallyThere()
    {
        for (var length = 1; length <= 8; length++)
        {
            for (var picture = 0; picture < 1 << length; picture++)
            {
                var solution = new bool[length];
                for (var i = 0; i < length; i++)
                {
                    solution[i] = (picture & (1 << i)) != 0;
                }

                var clues = ClueCalculator.FromSolution(solution);
                if (clues.IsBlank)
                {
                    continue;
                }

                var blocks = Blocks(solution);

                // Every correct partial marking: each cell either unmarked or marked truthfully.
                for (var revealed = 0; revealed < 1 << length; revealed++)
                {
                    var line = new CellState[length];
                    for (var i = 0; i < length; i++)
                    {
                        line[i] = (revealed & (1 << i)) == 0 ? CellState.Empty
                            : solution[i] ? CellState.Filled : CellState.Crossed;
                    }

                    var struck = ClueStrikeCalculator.Compute(clues, line);

                    for (var k = 0; k < clues.Count; k++)
                    {
                        if (!struck[k])
                        {
                            continue;
                        }

                        var (start, runLength) = blocks[k];
                        var filled = line.AsSpan(start, runLength).IndexOfAnyExcept(CellState.Filled) < 0;

                        Assert.True(
                            filled,
                            $"number {k} of [{string.Join(',', clues.DisplayRuns)}] struck on {Describe(line)} before its block was filled");
                    }
                }
            }
        }
    }

    private static List<(int Start, int Length)> Blocks(bool[] solution)
    {
        var blocks = new List<(int, int)>();
        for (var i = 0; i < solution.Length; i++)
        {
            if (solution[i] && (i == 0 || !solution[i - 1]))
            {
                var end = i;
                while (end < solution.Length && solution[end])
                {
                    end++;
                }

                blocks.Add((i, end - i));
            }
        }

        return blocks;
    }

    [Fact]
    public void TooSmallABuffer_IsRejected()
    {
        var clues = new LineClues(2, 1);
        var line = Marks(".....");

        Assert.Throws<ArgumentException>(() =>
        {
            var struck = new bool[1];
            ClueStrikeCalculator.Compute(clues, line, struck);
        });
    }
}
