using Squarebuzz.Core.Model;
using Squarebuzz.Presentation.ViewModels;
using Xunit;

namespace Squarebuzz.Presentation.Tests;

public sealed class GameAutosaveTests : IDisposable
{
    private readonly GameViewModelHarness _h = new();

    public GameAutosaveTests() => _h.Settings.Settings = GameSettings.Default with
    {
        Helpers = HelperSettings.Default with { AutoCross = false },
    };

    public void Dispose() => _h.Dispose();

    private void TickAutosave()
    {
        _h.Clock.Advance(TimeSpan.FromSeconds(16));
        _h.Timers.Latest!.RaiseTick();
    }

    [Fact]
    public async Task UntouchedBoard_IsNotSaved()
    {
        await _h.Vm.StartAsync();
        TickAutosave();
        await _h.Vm.AutosaveAsync();
        Assert.Empty(_h.SaveGames.Saves);
    }

    [Fact]
    public async Task UndoToBlank_ReplacesTheOldSavedMarks()
    {
        await _h.Vm.StartAsync();
        _h.Vm.Paint(0, CellState.Filled);
        await _h.Vm.AutosaveAsync();
        _h.Vm.UndoCommand.Execute(null);
        await _h.Vm.AutosaveAsync();

        Assert.All(Assert.Single(_h.SaveGames.Saves).Value.Cells, cell => Assert.Equal(CellState.Empty, cell));
    }

    [Fact]
    public async Task DifferentBoardAtTheSameUndoDepth_IsSavedPeriodically()
    {
        await _h.Vm.StartAsync();
        _h.Vm.Paint(0, CellState.Filled);
        await _h.Vm.AutosaveAsync();
        _h.Vm.UndoCommand.Execute(null);
        _h.Vm.Paint(3, CellState.Filled);
        TickAutosave();

        var saved = Assert.Single(_h.SaveGames.Saves).Value;
        Assert.Equal(CellState.Empty, saved.Cells[0]);
        Assert.Equal(CellState.Filled, saved.Cells[3]);
    }

    [Fact]
    public async Task ResumedBoardWithNoNewUndoHistory_SavesTimeAndMistakes()
    {
        await _h.Vm.StartAsync();
        _h.Vm.Paint(0, CellState.Filled);
        await _h.Vm.AutosaveAsync();
        var id = Assert.Single(_h.SaveGames.Saves).Key;
        _h.Vm.ApplyQueryAttributes(new Dictionary<string, object> { [GameViewModel.SaveIdParameter] = id.ToString() });
        await _h.Vm.InitialiseAsync();
        Assert.Equal(0, _h.Vm.Session!.MoveCount);

        _h.Vm.Paint(1, CellState.Filled);
        TickAutosave();
        Assert.Equal(1, _h.SaveGames.Saves[id].Mistakes);

        _h.Clock.Advance(TimeSpan.FromSeconds(2));
        _h.Timers.Latest!.RaiseTick();
        await _h.Vm.AutosaveAsync();
        Assert.Equal(TimeSpan.FromSeconds(18), _h.SaveGames.Saves[id].Elapsed);
    }

    [Fact]
    public async Task UnchangedProgress_SkipsPeriodicWritesButExplicitSaveUpdatesTime()
    {
        await _h.Vm.StartAsync();
        _h.Vm.Paint(0, CellState.Filled);
        await _h.Vm.AutosaveAsync();
        TickAutosave();
        Assert.Single(_h.SaveGames.SaveAttempts);

        await _h.Vm.AutosaveAsync();
        Assert.Equal(2, _h.SaveGames.SaveAttempts.Count);
        Assert.Equal(TimeSpan.FromSeconds(16), Assert.Single(_h.SaveGames.Saves).Value.Elapsed);
    }

    [Fact]
    public async Task EditDuringSave_RemainsDirtyAfterThatSnapshotCompletes()
    {
        await _h.Vm.StartAsync();
        _h.Vm.Paint(0, CellState.Filled);
        var gate = _h.SaveGames.SaveGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var saving = _h.Vm.AutosaveAsync();
        _h.Vm.Paint(1, CellState.Crossed);
        gate.SetResult();
        await saving;
        TickAutosave();

        Assert.Equal(CellState.Crossed, Assert.Single(_h.SaveGames.Saves).Value.Cells[1]);
    }

    [Fact]
    public async Task UndoWhileFirstSaveIsPending_QueuesTheBlankSnapshotInOrder()
    {
        await _h.Vm.StartAsync();
        _h.Vm.Paint(0, CellState.Filled);
        var gate = _h.SaveGames.SaveGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = _h.Vm.AutosaveAsync();
        _h.Vm.UndoCommand.Execute(null);
        _h.SaveGames.SaveGate = null;
        var second = _h.Vm.AutosaveAsync();
        Assert.False(second.IsCompleted);
        Assert.Single(_h.SaveGames.SaveAttempts);
        gate.SetResult();
        await Task.WhenAll(first, second);

        Assert.Equal(2, _h.SaveGames.SaveAttempts.Count);
        Assert.All(Assert.Single(_h.SaveGames.Saves).Value.Cells, cell => Assert.Equal(CellState.Empty, cell));
    }

    [Fact]
    public async Task CompletionWaitsForPendingSave_ThenRemovesIt()
    {
        await _h.Vm.StartAsync();
        _h.Vm.Paint(0, CellState.Filled);
        var gate = _h.SaveGames.SaveGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var saving = _h.Vm.AutosaveAsync();
        _h.Vm.Paint(3, CellState.Filled);
        Assert.Empty(_h.SaveGames.Deleted);

        gate.SetResult();
        await saving;
        await GameViewModelHarness.WaitUntilAsync(() => _h.Progress.Completions.Count == 1);
        Assert.Empty(_h.SaveGames.Saves);
    }

    [Fact]
    public async Task OldSessionSave_DoesNotAcknowledgeTheNewSessionsProgress()
    {
        await _h.Vm.StartAsync();
        _h.Vm.Paint(0, CellState.Filled);
        var gate = _h.SaveGames.SaveGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var saving = _h.Vm.AutosaveAsync();
        await _h.Vm.RestartCommand.ExecuteAsync(null);
        _h.Vm.Paint(3, CellState.Filled);
        gate.SetResult();
        await saving;
        TickAutosave();

        Assert.Equal(2, _h.SaveGames.Saves.Count);
        Assert.Contains(_h.SaveGames.Saves.Values, save => save.Cells[3] == CellState.Filled);
    }

    [Fact]
    public async Task UndoingAHint_StillPersistsItsCost()
    {
        await _h.Vm.StartAsync();
        _h.Vm.Paint(0, CellState.Filled);
        await _h.Vm.AutosaveAsync();
        _h.Vm.UseHintCommand.Execute(null);
        Assert.Equal(1, _h.Vm.Session!.HintsUsed);
        _h.Vm.UndoCommand.Execute(null);
        TickAutosave();

        var saved = Assert.Single(_h.SaveGames.Saves).Value;
        Assert.Equal(1, saved.HintsUsed);
        Assert.Equal(2, saved.HintsRemaining);
        Assert.Equal(_h.Vm.Session.Cells.ToArray(), saved.Cells);
    }

    [Fact]
    public async Task ReturningToSavedProgress_StillReplacesAnIntermediatePendingWrite()
    {
        await _h.Vm.StartAsync();
        _h.Vm.Paint(0, CellState.Filled);
        await _h.Vm.AutosaveAsync();
        _h.Vm.Paint(1, CellState.Crossed);
        var gate = _h.SaveGames.SaveGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var saving = _h.Vm.AutosaveAsync();
        _h.Vm.UndoCommand.Execute(null);
        _h.SaveGames.SaveGate = null;
        TickAutosave();

        gate.SetResult();
        await saving;
        await GameViewModelHarness.WaitUntilAsync(() => _h.SaveGames.SaveAttempts.Count == 3);
        Assert.Equal(CellState.Empty, Assert.Single(_h.SaveGames.Saves).Value.Cells[1]);
    }

    [Fact]
    public async Task FailedSave_DoesNotPreventTheNextAttempt()
    {
        await _h.Vm.StartAsync();
        _h.Vm.Paint(0, CellState.Filled);
        var gate = _h.SaveGames.SaveGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var saving = _h.Vm.AutosaveAsync();
        gate.SetException(new IOException("Storage unavailable"));
        await saving;
        _h.SaveGames.SaveGate = null;
        TickAutosave();

        Assert.Equal(2, _h.SaveGames.SaveAttempts.Count);
        Assert.Single(_h.SaveGames.Saves);
    }
}
