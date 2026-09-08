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
        // "##..." might yet grow: without a cross after it, the 2 is not settled.
        var struck = ClueStrikeCalculator.Compute(new LineClues(2, 1), Marks("##..."));

        Assert.Equal([false, false], struck);
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
