using Squarebuzz.Core.Model;
using Xunit;

namespace Squarebuzz.Core.Tests.Model;

public class MoveHistoryTests
{
    private static Stroke StrokeAt(int index) =>
        new([new CellChange(index, CellState.Empty, CellState.Filled)]);

    [Fact]
    public void NewHistory_HasNothingToUndoOrRedo()
    {
        var history = new MoveHistory();

        Assert.False(history.CanUndo);
        Assert.False(history.CanRedo);
        Assert.Equal(0, history.AppliedCount);
        Assert.Null(history.Undo());
        Assert.Null(history.Redo());
    }

    [Fact]
    public void UndoThenRedo_ReturnsTheSameStroke()
    {
        var history = new MoveHistory();
        var stroke = StrokeAt(7);
        history.Push(stroke);

        Assert.Same(stroke, history.Undo());
        Assert.False(history.CanUndo);
        Assert.True(history.CanRedo);
        Assert.Same(stroke, history.Redo());
        Assert.True(history.CanUndo);
    }

    [Fact]
    public void PushingAfterUndo_DiscardsTheRedoBranch()
    {
        var history = new MoveHistory();
        history.Push(StrokeAt(1));
        history.Push(StrokeAt(2));
        history.Undo();

        Assert.True(history.CanRedo);

        var replacement = StrokeAt(3);
        history.Push(replacement);

        // Once the player moves again, the branch they backed out of is gone for good.
        Assert.False(history.CanRedo);
        Assert.Equal(2, history.AppliedCount);
        Assert.Same(replacement, history.Undo());
    }

    [Fact]
    public void UndoAll_ThenRedoAll_WalksTheStackInOrder()
    {
        var history = new MoveHistory();
        var strokes = new[] { StrokeAt(1), StrokeAt(2), StrokeAt(3) };

        foreach (var stroke in strokes)
        {
            history.Push(stroke);
        }

        Assert.Same(strokes[2], history.Undo());
        Assert.Same(strokes[1], history.Undo());
        Assert.Same(strokes[0], history.Undo());
        Assert.False(history.CanUndo);

        Assert.Same(strokes[0], history.Redo());
        Assert.Same(strokes[1], history.Redo());
        Assert.Same(strokes[2], history.Redo());
        Assert.False(history.CanRedo);
    }

    [Fact]
    public void Clear_EmptiesEverything()
    {
        var history = new MoveHistory();
        history.Push(StrokeAt(1));
        history.Clear();

        Assert.False(history.CanUndo);
        Assert.False(history.CanRedo);
        Assert.Equal(0, history.AppliedCount);
    }

    [Fact]
    public void EmptyStroke_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => new Stroke([]));
    }

    [Fact]
    public void Stroke_RetainedInputCannotRewriteHistory()
    {
        var original = new CellChange(7, CellState.Empty, CellState.Filled);
        CellChange[] changes = [original];
        var history = new MoveHistory();
        history.Push(new Stroke(changes));

        changes[0] = new CellChange(99, CellState.Crossed, CellState.Empty);

        Assert.Equal(original, Assert.Single(Assert.IsType<Stroke>(history.Undo()).Changes));
        Assert.Equal(original, Assert.Single(Assert.IsType<Stroke>(history.Redo()).Changes));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Stroke_ExposedChangesCannotRewriteHistory(bool alreadyUndone)
    {
        var original = new CellChange(7, CellState.Empty, CellState.Filled);
        var history = new MoveHistory();
        var stroke = new Stroke([original]);
        history.Push(stroke);

        if (alreadyUndone)
        {
            stroke = Assert.IsType<Stroke>(history.Undo());
        }

        // Read-only collections may expose IList, but must reject replacement of stored moves.
        if (stroke.Changes is IList<CellChange> mutable)
        {
            Assert.Throws<NotSupportedException>(() =>
                mutable[0] = new CellChange(99, CellState.Crossed, CellState.Empty));
        }

        if (!alreadyUndone)
        {
            Assert.Equal(original, Assert.Single(Assert.IsType<Stroke>(history.Undo()).Changes));
        }

        Assert.Equal(original, Assert.Single(Assert.IsType<Stroke>(history.Redo()).Changes));
        Assert.Equal(1, stroke.DirectChangeCount);
        Assert.Equal(0, stroke.AutoCrossedCount);
    }

    [Fact]
    public void Stroke_SeparatesDirectChangesFromAutoCrossedOnes()
    {
        var stroke = new Stroke(
        [
            new CellChange(0, CellState.Empty, CellState.Filled),
            new CellChange(1, CellState.Empty, CellState.Crossed),
            new CellChange(2, CellState.Empty, CellState.Crossed),
        ])
        { DirectChangeCount = 1 };

        Assert.Equal(2, stroke.AutoCrossedCount);
    }

    [Fact]
    public void Stroke_DoesNotCountRemovedCrossesAsNewAutomaticMarks()
    {
        var stroke = new Stroke(
        [
            new CellChange(0, CellState.Filled, CellState.Empty),
            new CellChange(1, CellState.Crossed, CellState.Empty) { FromAutoCrossed = true },
            new CellChange(2, CellState.Empty, CellState.Crossed) { ToAutoCrossed = true },
        ]);

        Assert.Equal(1, stroke.AutoCrossedCount);
    }
}
