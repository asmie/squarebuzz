using Squarebuzz.Core.Model;
using Xunit;

namespace Squarebuzz.Core.Tests.Model;

public class ClueImmutabilityTests
{
    [Fact]
    public void Constructor_RetainedInputCannotChangeClues()
    {
        int[] runs = [2, 1];
        var clues = new LineClues(runs);
        var knownClues = new HashSet<LineClues> { clues };

        runs[0] = 5;

        Assert.Equal([2, 1], clues.DisplayRuns);
        Assert.Equal(3, clues.Sum);
        Assert.Equal(4, clues.MinimumLength);
        Assert.Contains(new LineClues(2, 1), knownClues);
    }

    [Fact]
    public void DisplayRuns_CannotChangeTheClueOrItsCachedTotals()
    {
        var clues = new LineClues(2, 1);
        var knownClues = new HashSet<LineClues> { clues };

        AssertCannotReplaceFirst(clues.DisplayRuns, 5);

        Assert.Equal([2, 1], clues.DisplayRuns);
        Assert.Equal(3, clues.Sum);
        Assert.Equal(4, clues.MinimumLength);
        Assert.Contains(new LineClues(2, 1), knownClues);
    }

    [Fact]
    public void BlankDisplayRuns_CannotChangeTheZeroSharedByBlankLines()
    {
        AssertCannotReplaceFirst(LineClues.Blank.DisplayRuns, 5);

        Assert.Empty(LineClues.Blank);
        Assert.Equal([0], LineClues.Blank.DisplayRuns);
        Assert.Equal([0], new LineClues().DisplayRuns);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PuzzleClues_CannotBeReplacedIndependentlyOfTheSolution(bool rows)
    {
        var puzzle = Puzzle.FromRows("immutable", "test", "#FFFFFF", ["##.", "..#"]);
        var clues = rows ? puzzle.RowClues : puzzle.ColumnClues;
        var original = clues[0];

        AssertCannotReplaceFirst(clues, new LineClues(9));

        Assert.Same(original, clues[0]);
        Assert.Equal(3, puzzle.PictureCellCount);
        Assert.Equal(1, puzzle.MaxRowClueCount);
        Assert.Equal(1, puzzle.MaxColumnClueCount);
        Assert.Equal(new LineClues(2), puzzle.RowClues[0]);
        Assert.Equal(new LineClues(1), puzzle.ColumnClues[0]);
        Assert.True(puzzle.IsFilled(0, 0));
    }

    private static void AssertCannotReplaceFirst<T>(IReadOnlyList<T> values, T replacement)
    {
        // A read-only API may omit IList entirely or expose an IList that rejects writes.
        if (values is IList<T> mutable)
        {
            var original = values[0];
            var error = Record.Exception(() => mutable[0] = replacement);

            // Keep a failing regression from corrupting a shared blank clue for other tests.
            if (error is null)
            {
                mutable[0] = original;
            }

            Assert.IsType<NotSupportedException>(error);
        }
    }
}
