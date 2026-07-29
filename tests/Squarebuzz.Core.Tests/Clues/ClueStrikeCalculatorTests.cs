using Squarebuzz.Core.Clues;
using Squarebuzz.Core.Model;
using Xunit;

namespace Squarebuzz.Core.Tests.Clues;

public class ClueStrikeCalculatorTests
{
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
