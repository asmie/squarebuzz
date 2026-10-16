using Squarebuzz.Core.Model;
using Xunit;

namespace Squarebuzz.Core.Tests.Model;

public sealed class DragUndoTests
{
    private static GameSession NewSession(bool autoCross = false) => new(
        Puzzle.FromRows("plus", "test", "#FF8A3D", ["..#..", "..#..", "#####", "..#..", "..#.."]),
        GameRules.Create(ChallengeLevel.Relaxed, HelperSettings.Default with { AutoCross = autoCross }),
        NewGameOptions.Default);

    [Fact]
    public void OneDrag_UndoesAndRedoesEveryCellAndAutomaticMark()
    {
        var session = NewSession(autoCross: true);
        session.Paint(0, CellState.Crossed);
        var before = session.Cells.ToArray();
        var beforeFlags = session.AutoCrossedCells.ToArray();

        session.Paint(2, CellState.Filled);
        session.Paint(7, CellState.Filled, continuesStroke: true);
        var painted = session.Cells.ToArray();
        var paintedFlags = session.AutoCrossedCells.ToArray();

        Assert.Equal(2, session.MoveCount);
        Assert.True(session.Undo());
        Assert.Equal(before, session.Cells.ToArray());
        Assert.Equal(beforeFlags, session.AutoCrossedCells.ToArray());
        Assert.True(session.Redo());
        Assert.Equal(painted, session.Cells.ToArray());
        Assert.Equal(paintedFlags, session.AutoCrossedCells.ToArray());
    }

    [Fact]
    public void DragStartingWithAMistake_DoesNotMergeWithThePreviousTap()
    {
        var session = NewSession();
        session.Paint(2, CellState.Filled);
        session.Paint(0, CellState.Filled);
        session.Paint(7, CellState.Filled, continuesStroke: true);

        Assert.Equal(2, session.MoveCount);
        Assert.True(session.Undo());
        Assert.Equal(CellState.Filled, session[2]);
        Assert.Equal(CellState.Empty, session[7]);
        Assert.Equal(1, session.Mistakes);
    }

    [Fact]
    public void EraseDrag_RestoresSharedAutomaticCrossesOnUndo()
    {
        var session = NewSession(autoCross: true);
        session.Paint(2, CellState.Filled);
        session.Paint(10, CellState.Filled);
        var before = session.Cells.ToArray();
        var flags = session.AutoCrossedCells.ToArray();

        session.Paint(2, CellState.Empty);
        session.Paint(10, CellState.Empty, continuesStroke: true);
        Assert.All(session.Cells.ToArray(), cell => Assert.Equal(CellState.Empty, cell));
        Assert.True(session.Undo());
        Assert.Equal(before, session.Cells.ToArray());
        Assert.Equal(flags, session.AutoCrossedCells.ToArray());
        Assert.True(session.Redo());
        Assert.All(session.Cells.ToArray(), cell => Assert.Equal(CellState.Empty, cell));
        Assert.DoesNotContain(true, session.AutoCrossedCells.ToArray());
    }

    [Fact]
    public void NewDragAfterUndo_DiscardsOnlyTheRedoGroup()
    {
        var session = NewSession();
        session.Paint(2, CellState.Filled);
        session.Paint(7, CellState.Filled, continuesStroke: true);
        session.Paint(12, CellState.Filled);
        session.Paint(17, CellState.Filled, continuesStroke: true);
        Assert.True(session.Undo());

        session.Paint(10, CellState.Filled);
        session.Paint(11, CellState.Filled, continuesStroke: true);
        Assert.False(session.CanRedo);
        Assert.Equal(2, session.MoveCount);
        Assert.True(session.Undo());
        Assert.Equal(2, session.FilledCount);
        Assert.True(session.Undo());
        Assert.Equal(0, session.FilledCount);
        Assert.True(session.Redo());
        Assert.Equal(2, session.FilledCount);
    }

    [Fact]
    public void HintBetweenDragEdits_RemainsAnIndependentUndoableAction()
    {
        var session = NewSession();
        session.Paint(2, CellState.Filled);
        Assert.NotNull(session.UseHint());
        var hinted = session.Cells.ToArray();
        var next = Enumerable.Range(0, session.Puzzle.CellCount)
            .First(i => session.Puzzle.Solution[i] && session[i] == CellState.Empty);
        session.Paint(next, CellState.Filled, continuesStroke: true);

        Assert.Equal(3, session.MoveCount);
        Assert.True(session.Undo());
        Assert.Equal(hinted, session.Cells.ToArray());
        Assert.True(session.Undo());
        Assert.Equal(1, session.FilledCount);
        Assert.Equal(1, session.HintsUsed);
    }
}
