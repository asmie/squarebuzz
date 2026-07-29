using Squarebuzz.Core.Clues;
using Squarebuzz.Core.Model;
using Xunit;

namespace Squarebuzz.Core.Tests.Clues;

public class ClueCalculatorTests
{
    private static bool[] Solution(string pattern) => [.. pattern.Select(c => c == '#')];

    private static CellState[] Marks(string pattern) =>
        [.. pattern.Select(c => c switch
        {
            '#' => CellState.Filled,
            'x' => CellState.Crossed,
            _ => CellState.Empty,
        })];

    [Theory]
    [InlineData("#####", new[] { 5 })]
    [InlineData("##.#.", new[] { 2, 1 })]
    [InlineData("#.#.#", new[] { 1, 1, 1 })]
    [InlineData(".###.", new[] { 3 })]
    [InlineData("#...#", new[] { 1, 1 })]
    [InlineData("..###", new[] { 3 })]
    public void FromSolution_ReadsRunsLeftToRight(string pattern, int[] expected)
    {
        var clues = ClueCalculator.FromSolution(Solution(pattern));

        Assert.Equal(new LineClues(expected), clues);
    }

    [Fact]
    public void FromSolution_OnAnEmptyLine_IsBlank()
    {
        var clues = ClueCalculator.FromSolution(Solution("....."));

        Assert.True(clues.IsBlank);
        Assert.Empty(clues);

        // The player still sees a "0" for that line.
        Assert.Equal([0], clues.DisplayRuns);
        Assert.Equal("0", clues.ToString());
    }

    [Fact]
    public void FromMarks_TreatsCrossedAndUnmarkedAlikeAsBreaks()
    {
        // Both patterns have the same filled cells; the crosses must not change the reading.
        Assert.Equal(ClueCalculator.FromMarks(Marks("##.#.")), ClueCalculator.FromMarks(Marks("##x#x")));
        Assert.Equal(new LineClues(2, 1), ClueCalculator.FromMarks(Marks("##x#x")));
    }

    [Fact]
    public void FromMarks_OnAnUntouchedLine_IsBlank()
    {
        Assert.True(ClueCalculator.FromMarks(Marks(".....")).IsBlank);
    }

    [Theory]
    [InlineData(new[] { 5 }, 5)]
    [InlineData(new[] { 3, 1 }, 5)]
    [InlineData(new[] { 1, 1, 1 }, 5)]
    [InlineData(new[] { 2, 2 }, 5)]
    [InlineData(new[] { 8 }, 8)]
    public void MinimumLength_AccountsForTheGapsBetweenRuns(int[] runs, int expected)
    {
        Assert.Equal(expected, new LineClues(runs).MinimumLength);
    }

    [Fact]
    public void BlankClue_NeedsNoRoom()
    {
        Assert.Equal(0, LineClues.Blank.MinimumLength);
        Assert.Equal(0, LineClues.Blank.Sum);
    }

    [Fact]
    public void Sum_CountsEveryFilledCell()
    {
        Assert.Equal(4, new LineClues(2, 1, 1).Sum);
    }

    [Fact]
    public void Clues_CompareByValue()
    {
        Assert.Equal(new LineClues(2, 1), new LineClues(2, 1));
        Assert.NotEqual(new LineClues(2, 1), new LineClues(1, 2));
        Assert.Equal(new LineClues(2, 1).GetHashCode(), new LineClues(2, 1).GetHashCode());
    }

    [Fact]
    public void NonPositiveRuns_AreRejected()
    {
        // A zero run would be ambiguous with "blank line"; negatives are meaningless.
        Assert.Throws<ArgumentOutOfRangeException>(() => new LineClues(2, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new LineClues(-1));
    }

    [Fact]
    public void ToString_IsSpaceSeparated()
    {
        Assert.Equal("3 1", new LineClues(3, 1).ToString());
    }

    [Fact]
    public void EveryFiveCellLine_ProducesAConsistentClue()
    {
        // Exhaustive over all 32 five-cell lines. Three invariants must hold for each:
        // the two readers agree, the runs account for exactly the filled cells, and the
        // clue always fits in the line it was derived from.
        for (var bits = 0; bits < 32; bits++)
        {
            var pattern = string.Concat(Enumerable.Range(0, 5).Select(i => (bits & (1 << i)) != 0 ? "#" : "."));
            var filledCount = pattern.Count(c => c == '#');

            var fromSolution = ClueCalculator.FromSolution(Solution(pattern));
            var fromMarks = ClueCalculator.FromMarks(Marks(pattern));

            Assert.Equal(fromSolution, fromMarks);
            Assert.Equal(filledCount, fromSolution.Sum);
            Assert.True(fromSolution.MinimumLength <= 5, $"Clue for '{pattern}' claims to need {fromSolution.MinimumLength} cells.");
        }
    }

    [Fact]
    public void EveryFiveCellLine_IsRecoveredByTheSolverWhenItIsUnambiguous()
    {
        // Ties the clue reader to the solver: wherever a 5-wide clue admits only one
        // arrangement, solving from empty must reproduce the original line exactly.
        for (var bits = 0; bits < 32; bits++)
        {
            var pattern = string.Concat(Enumerable.Range(0, 5).Select(i => (bits & (1 << i)) != 0 ? "#" : "."));
            var clues = ClueCalculator.FromSolution(Solution(pattern));
            var line = new CellState[5];

            var status = Squarebuzz.Core.Solving.NonogramLineSolver.Solve(clues, line);

            Assert.NotEqual(Squarebuzz.Core.Solving.LineSolveStatus.Contradiction, status);

            for (var i = 0; i < 5; i++)
            {
                if (line[i] == CellState.Empty)
                {
                    continue;
                }

                var expected = pattern[i] == '#' ? CellState.Filled : CellState.Crossed;
                Assert.Equal(expected, line[i]);
            }
        }
    }
}
