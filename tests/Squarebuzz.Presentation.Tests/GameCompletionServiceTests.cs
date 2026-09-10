using Squarebuzz.Presentation.Services;
using Xunit;

namespace Squarebuzz.Presentation.Tests;

public sealed class GameCompletionServiceTests
{
    [Fact]
    public async Task FailedWin_KeepsItsSaveAndRetriesAfterThePageIsDisposed()
    {
        using var h = new GameViewModelHarness();
        await h.Vm.StartAsync();
        h.Vm.Paint(0, Core.Model.CellState.Filled);
        await h.Vm.AutosaveAsync();
        h.Completions.Fails = true;
        h.SolveCurrentPuzzle();
        Assert.Single(h.SaveGames.Saves);
        Assert.Empty(h.Progress.Completions);
        var original = Assert.Single(h.Completions.Attempts);
        h.Vm.Dispose();

        h.Completions.Fails = false;
        await h.CompletionService.RetryAsync();
        Assert.Empty(h.SaveGames.Saves);
        Assert.Equal(original.Completion, Assert.Single(h.Progress.Completions));
        Assert.All(h.Completions.Attempts, attempt => Assert.Equal(original.Id, attempt.Id));
    }

    [Fact]
    public async Task ForegroundResume_RetriesEvenWithoutAVisibleGame()
    {
        using var h = new GameViewModelHarness();
        await h.Vm.StartAsync();
        h.Completions.Fails = true;
        h.SolveCurrentPuzzle();
        var lifecycle = new GameLifecycle(h.CompletionService);
        await lifecycle.SuspendAsync();
        h.Completions.Fails = false;
        lifecycle.Resume();
        Assert.Single(h.Progress.Completions);
    }

    [Fact]
    public async Task Reset_ClearsAWinWhoseJournalWriteFailed()
    {
        using var h = new GameViewModelHarness();
        await h.Vm.StartAsync();
        h.Completions.Fails = true;
        h.SolveCurrentPuzzle();
        await h.CompletionService.ResetAsync();
        h.Completions.Fails = false;
        await h.CompletionService.RetryAsync();
        Assert.Empty(h.Progress.Completions);
        Assert.Equal(1, h.Progress.ResetCalls);
    }

    [Fact]
    public async Task Reset_CannotOvertakeACompletionWaitingForAutosave()
    {
        using var h = new GameViewModelHarness();
        await h.Vm.StartAsync();
        h.Vm.Paint(0, Core.Model.CellState.Filled);
        var gate = h.SaveGames.SaveGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var saving = h.Vm.AutosaveAsync();
        h.SolveCurrentPuzzle();
        var resetting = h.CompletionService.ResetAsync();
        Assert.False(resetting.IsCompleted);
        gate.SetResult();
        await saving;
        await resetting;
        await h.CompletionService.RetryAsync();
        Assert.Empty(h.Progress.Completions);
        Assert.Equal(1, h.Progress.ResetCalls);
    }
}
