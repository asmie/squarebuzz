using Squarebuzz.Core.Model;
using Xunit;

namespace Squarebuzz.Core.Tests.Model;

public sealed class AutomaticCrossTests
{
    private static GameSession NewSession(bool autoCross = true) => new(
        Puzzle.FromRows("plus", "test", "#FF8A3D", ["..#..", "..#..", "#####", "..#..", "..#.."]),
        GameRules.Create(ChallengeLevel.Relaxed, HelperSettings.Default with { AutoCross = autoCross }),
        NewGameOptions.Default);

    [Theory]
    [InlineData(CellState.Empty)]
    [InlineData(CellState.Crossed)]
    public void BreakingARow_ClearsAutomaticCrosses_ButPreservesManualMarks(CellState replacement)
    {
        var session = NewSession();
        session.Paint(0, CellState.Crossed);
        session.Paint(2, CellState.Filled);
        Assert.True(session.AutoCrossedCells[1]);
        var completed = session.Cells.ToArray();
        var completedFlags = session.AutoCrossedCells.ToArray();

        session.Paint(2, replacement);

        Assert.Equal(CellState.Crossed, session[0]);
        Assert.Equal(replacement, session[2]);
        Assert.Equal(CellState.Empty, session[1]);
        Assert.Equal(CellState.Empty, session[3]);
        Assert.Equal(CellState.Empty, session[4]);
        Assert.DoesNotContain(true, session.AutoCrossedCells.ToArray());
        var broken = session.Cells.ToArray();
        Assert.True(session.Undo());
        Assert.Equal(completed, session.Cells.ToArray());
        Assert.Equal(completedFlags, session.AutoCrossedCells.ToArray());
        Assert.True(session.Redo());
        Assert.Equal(broken, session.Cells.ToArray());
        Assert.DoesNotContain(true, session.AutoCrossedCells.ToArray());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SharedCross_RemainsUntilBothLinesAreBroken(bool rowFirst)
    {
        var session = NewSession();
        // Cell (0,0) belongs to both completed lines; the rest of the puzzle is unfinished.
        session.Paint(rowFirst ? 2 : 10, CellState.Filled);
        session.Paint(rowFirst ? 10 : 2, CellState.Filled);
        Assert.True(session.AutoCrossedCells[0]);
        session.Paint(rowFirst ? 2 : 10, CellState.Empty);
        Assert.Equal(CellState.Crossed, session[0]);
        Assert.True(session.AutoCrossedCells[0]);
        session.Paint(rowFirst ? 10 : 2, CellState.Empty);
        Assert.All(session.Cells.ToArray(), cell => Assert.Equal(CellState.Empty, cell));
        Assert.DoesNotContain(true, session.AutoCrossedCells.ToArray());
        Assert.True(session.Undo());
        Assert.True(session.AutoCrossedCells[0]);
        Assert.True(session.Undo());
        Assert.True(session.Redo());
        Assert.True(session.Redo());
        Assert.All(session.Cells.ToArray(), cell => Assert.Equal(CellState.Empty, cell));
    }

    [Fact]
    public void ExplicitCross_ClaimsAnAutomaticMark_AndUndoRestoresItsOrigin()
    {
        var session = NewSession();
        session.Paint(2, CellState.Filled);
        session.Paint(0, CellState.Crossed);
        Assert.False(session.AutoCrossedCells[0]);
        Assert.True(session.Undo());
        Assert.True(session.AutoCrossedCells[0]);
        Assert.True(session.Redo());
        session.Paint(2, CellState.Empty);
        Assert.Equal(CellState.Crossed, session[0]);
        Assert.Equal(CellState.Empty, session[1]);
    }

    [Fact]
    public void UndoingAnErasedAndImmediatelyRecrossedCell_RestoresTheExactPreviousMark()
    {
        var session = NewSession();
        session.Paint(0, CellState.Crossed);
        session.Paint(2, CellState.Filled);
        session.Paint(0, CellState.Empty);
        Assert.True(session.AutoCrossedCells[0]);
        Assert.True(session.Undo());
        Assert.Equal(CellState.Crossed, session[0]);
        Assert.False(session.AutoCrossedCells[0]);
        Assert.True(session.Redo());
        Assert.True(session.AutoCrossedCells[0]);
    }

    [Fact]
    public void AddingAnExtraFill_ClearsAutomaticMarks_AndUndoRestoresThem()
    {
        var session = NewSession();
        session.ApplyHelpers(HelperSettings.Default with { WarnOnMistakes = false });
        session.Paint(2, CellState.Filled);
        session.Paint(0, CellState.Filled);
        Assert.Equal(CellState.Empty, session[1]);
        Assert.Equal(CellState.Empty, session[3]);
        Assert.True(session.Undo());
        Assert.True(session.AutoCrossedCells[0]);
        Assert.True(session.AutoCrossedCells[1]);
        Assert.Equal(1, session.FilledCount);
    }

    [Fact]
    public void EnablingThenDisablingTheHelper_KeepsExistingMarksRemovable()
    {
        var session = NewSession(autoCross: false);
        session.Paint(2, CellState.Filled);
        session.ApplyHelpers(HelperSettings.Default);
        Assert.True(session.AutoCrossedCells[0]);
        Assert.True(session.Undo());
        Assert.False(session.AutoCrossedCells[0]);
        Assert.True(session.Redo());
        session.ApplyHelpers(HelperSettings.Default with { AutoCross = false });
        session.Paint(2, CellState.Empty);
        Assert.Equal(CellState.Empty, session[0]);
        Assert.DoesNotContain(true, session.AutoCrossedCells.ToArray());
    }

    [Fact]
    public void HintThatCompletesALine_CreatesRemovableAutomaticCrosses()
    {
        var session = NewSession();
        var hint = session.UseHint();
        Assert.NotNull(hint);
        Assert.True(session.AutoCrossedCells.Contains(true));
        session.Paint(hint.Index, CellState.Empty);
        Assert.All(session.Cells.ToArray(), cell => Assert.Equal(CellState.Empty, cell));
        Assert.Equal(1, session.HintsUsed);
    }

    [Fact]
    public void Restore_KeepsAutomaticOrigins_AndCopiesTheInput()
    {
        var session = NewSession();
        session.Paint(0, CellState.Crossed);
        session.Paint(2, CellState.Filled);
        var flags = session.AutoCrossedCells.ToArray();
        var restored = NewSession();
        restored.Restore(session.Cells.ToArray(), TimeSpan.Zero, 3, 0, 0, flags);
        Array.Clear(flags);
        restored.Paint(2, CellState.Empty);
        Assert.Equal(CellState.Crossed, restored[0]);
        Assert.Equal(CellState.Empty, restored[1]);
    }

    [Fact]
    public void LegacyRestore_PreservesMarksWithUnknownOrigins()
    {
        var session = NewSession();
        session.Paint(2, CellState.Filled);
        var cells = session.Cells.ToArray();
        session.Restore(cells, TimeSpan.Zero, 3, 0, 0);
        Assert.DoesNotContain(true, session.AutoCrossedCells.ToArray());
        session.Paint(2, CellState.Empty);
        Assert.Equal(CellState.Crossed, session[0]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void InvalidRestore_IsAtomic(bool wrongLength)
    {
        var session = NewSession();
        session.Paint(2, CellState.Filled);
        var before = session.Cells.ToArray();
        var flags = session.AutoCrossedCells.ToArray();
        var invalid = new bool[wrongLength ? 1 : 25];
        invalid[0] = true;
        Assert.Throws<ArgumentException>(() => session.Restore(new CellState[25], TimeSpan.Zero, 3, 0, 0, invalid));
        Assert.Equal(before, session.Cells.ToArray());
        Assert.Equal(flags, session.AutoCrossedCells.ToArray());
        Assert.True(session.CanUndo);
    }
}
