using Squarebuzz.Core.Model;
using Squarebuzz.Core.Solving;
using Xunit;

namespace Squarebuzz.Core.Tests.Solving;

public class NonogramLineSolverTests
{
    /// <summary>Renders a line for readable assertions: '#' filled, 'x' crossed, '.' unknown.</summary>
    private static string Render(ReadOnlySpan<CellState> line)
    {
        var chars = new char[line.Length];
        for (var i = 0; i < line.Length; i++)
        {
            chars[i] = line[i] switch
            {
                CellState.Filled => '#',
                CellState.Crossed => 'x',
                _ => '.',
            };
        }

        return new string(chars);
    }

    private static CellState[] Parse(string line) =>
        [.. line.Select(c => c switch
        {
            '#' => CellState.Filled,
            'x' => CellState.Crossed,
            _ => CellState.Empty,
        })];

    [Fact]
    public void FullLine_IsCompletelyDetermined()
    {
        var line = Parse(".....");

        var status = NonogramLineSolver.Solve(new LineClues(5), line);

        Assert.Equal(LineSolveStatus.Progressed, status);
        Assert.Equal("#####", Render(line));
    }

    [Fact]
    public void BlankClue_CrossesEveryCell()
    {
        var line = Parse(".....");

        var status = NonogramLineSolver.Solve(LineClues.Blank, line);

        Assert.Equal(LineSolveStatus.Progressed, status);
        Assert.Equal("xxxxx", Render(line));
    }

    [Fact]
    public void Overlap_IsDeducedOnLongRuns()
    {
        // The prototype's own How-To teaches this: in a 10-wide row a clue of 8 must cover
        // the middle 6 cells no matter where the run starts.
        var line = Parse("..........");

        var status = NonogramLineSolver.Solve(new LineClues(8), line);

        Assert.Equal(LineSolveStatus.Progressed, status);
        Assert.Equal("..######..", Render(line));
    }

    [Fact]
    public void SeparatedRuns_FillExactlyWhenTheyOnlyJustFit()
    {
        // 3 + gap + 1 needs 5 cells, so in 5 cells there is only one arrangement.
        var line = Parse(".....");

        var status = NonogramLineSolver.Solve(new LineClues(3, 1), line);

        Assert.Equal(LineSolveStatus.Progressed, status);
        Assert.Equal("###x#", Render(line));
    }

    [Fact]
    public void ExistingCross_NarrowsThePlacement()
    {
        // With the middle blocked, a run of 2 can only sit in the last two cells.
        var line = Parse("x.x..");

        var status = NonogramLineSolver.Solve(new LineClues(2), line);

        Assert.Equal(LineSolveStatus.Progressed, status);
        Assert.Equal("xxx##", Render(line));
    }

    [Fact]
    public void ExistingFill_AnchorsTheRun()
    {
        var line = Parse("#....");

        var status = NonogramLineSolver.Solve(new LineClues(2), line);

        Assert.Equal(LineSolveStatus.Progressed, status);
        Assert.Equal("##xxx", Render(line));
    }

    [Fact]
    public void AmbiguousLine_IsLeftAlone()
    {
        // A single cell in five: nothing is forced anywhere.
        var line = Parse(".....");

        var status = NonogramLineSolver.Solve(new LineClues(1), line);

        Assert.Equal(LineSolveStatus.Unchanged, status);
        Assert.Equal(".....", Render(line));
    }

    [Fact]
    public void ClueTooLongForTheLine_IsAContradiction()
    {
        var line = Parse("....");

        Assert.Equal(LineSolveStatus.Contradiction, NonogramLineSolver.Solve(new LineClues(5), line));
    }

    [Fact]
    public void RunsNeedingMoreGapsThanFit_IsAContradiction()
    {
        // 2 + gap + 2 needs 5 cells but only 4 are available.
        var line = Parse("....");

        Assert.Equal(LineSolveStatus.Contradiction, NonogramLineSolver.Solve(new LineClues(2, 2), line));
    }

    [Fact]
    public void MarksThatContradictTheClue_AreReported()
    {
        // The clue says one run of 1, but two separated cells are already filled.
        var line = Parse("#.#");

        Assert.Equal(LineSolveStatus.Contradiction, NonogramLineSolver.Solve(new LineClues(1), line));
    }

    [Fact]
    public void BlankClueWithAFilledCell_IsAContradiction()
    {
        var line = Parse("..#..");

        Assert.Equal(LineSolveStatus.Contradiction, NonogramLineSolver.Solve(LineClues.Blank, line));
    }

    [Fact]
    public void AlreadySolvedLine_ReportsUnchanged()
    {
        var line = Parse("##x#x");

        Assert.Equal(LineSolveStatus.Unchanged, NonogramLineSolver.Solve(new LineClues(2, 1), line));
    }

    [Theory]
    // Deductions must never contradict the truth: solve each line and check every forced
    // cell against the arrangement the clue was derived from.
    [InlineData("##.#.")]
    [InlineData("#####")]
    [InlineData(".....")]
    [InlineData("#.#.#")]
    [InlineData(".###.")]
    [InlineData("#..##")]
    public void EveryDeduction_AgreesWithTheTruth(string truth)
    {
        var solution = truth.Select(c => c == '#').ToArray();
        var clues = Squarebuzz.Core.Clues.ClueCalculator.FromSolution(solution);
        var line = new CellState[truth.Length];

        var status = NonogramLineSolver.Solve(clues, line);

        Assert.NotEqual(LineSolveStatus.Contradiction, status);

        for (var i = 0; i < line.Length; i++)
        {
            if (line[i] == CellState.Empty)
            {
                continue; // Undetermined is always acceptable; being wrong is not.
            }

            var expected = solution[i] ? CellState.Filled : CellState.Crossed;
            Assert.Equal(expected, line[i]);
        }
    }
}
