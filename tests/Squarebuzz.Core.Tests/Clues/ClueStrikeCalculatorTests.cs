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
