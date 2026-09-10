using Squarebuzz.Core.Model;
using Squarebuzz.Presentation.Services;
using Squarebuzz.Presentation.ViewModels;
using Xunit;

namespace Squarebuzz.Presentation.Tests;

public sealed class PersistenceRecoveryTests
{
    [Fact]
    public async Task InFlightSaveFailsAfterWin_CompletionOwnsRecoveryState()
    {
        using var h = new GameViewModelHarness();
        await h.Vm.StartAsync();
        h.Vm.Paint(0, CellState.Filled);
        var gate = h.SaveGames.SaveGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        h.SaveGames.SaveError = new IOException("disk full");
        var saving = h.Vm.AutosaveAsync();
        h.SolveCurrentPuzzle();
        gate.SetResult();
        await saving;
        await h.Saves.Pending;
        Assert.Single(h.Progress.Completions);
        Assert.False(h.Vm.HasPersistenceFailure);
    }

    [Fact]
    public async Task SaveFailure_ShowsRetry_AndRecoveryPersistsTheBoard()
    {
        using var h = new GameViewModelHarness();
        await h.Vm.StartAsync();
        h.Vm.Paint(0, CellState.Filled);
        h.SaveGames.SaveError = new IOException("disk full");
        await h.Vm.AutosaveAsync();
        Assert.True(h.Vm.HasPersistenceFailure);
        var report = Assert.Single(h.Diagnostics.Reports);
        Assert.Equal(PersistenceOperation.SaveGame, report.Operation);
        Assert.Equal(h.Saves.Id, report.SessionId);

        h.SaveGames.SaveError = null;
        await h.Vm.RetryPersistenceCommand.ExecuteAsync(null);
        Assert.False(h.Vm.HasPersistenceFailure);
        Assert.Equal(CellState.Filled, Assert.Single(h.SaveGames.Saves).Value.Cells[0]);
    }

    [Fact]
    public async Task CanceledSave_DoesNotReportStorageFailure_AndCanBeRetried()
    {
        using var h = new GameViewModelHarness();
        await h.Vm.StartAsync();
        h.Vm.Paint(0, CellState.Filled);
        h.SaveGames.SaveError = new OperationCanceledException();
        await h.Vm.AutosaveAsync();
        Assert.False(h.Vm.HasPersistenceFailure);
        Assert.Empty(h.Diagnostics.Reports);
        h.SaveGames.SaveError = null;
        await h.Vm.AutosaveAsync();
        Assert.Single(h.SaveGames.Saves);
    }

    [Fact]
    public async Task CompletionAfterFailedSave_ClearsSaveFailure_AndRetriesCompletionOnce()
    {
        using var h = new GameViewModelHarness();
        await h.Vm.StartAsync();
        h.Vm.Paint(0, CellState.Filled);
        h.SaveGames.SaveError = new IOException("disk full");
        await h.Vm.AutosaveAsync();
        h.Completions.Fails = true;
        h.SolveCurrentPuzzle();
        await h.Saves.Pending;
        Assert.True(h.Vm.HasPersistenceFailure);
        Assert.False(h.Saves.HasFailure);
        Assert.Contains(h.Diagnostics.Reports, r => r.Operation == PersistenceOperation.JournalCompletion);
        h.Completions.Fails = false;
        await h.Vm.RetryPersistenceCommand.ExecuteAsync(null);
        await h.Vm.RetryPersistenceCommand.ExecuteAsync(null);
        Assert.False(h.Vm.HasPersistenceFailure);
        Assert.Single(h.Progress.Completions);
    }

    [Fact]
    public async Task FailedResume_DoesNotStartNewGame_RetryRestoresOriginalSave()
    {
        using var original = new GameViewModelHarness();
        await original.Vm.StartAsync();
        original.Vm.Paint(0, CellState.Filled);
        await original.Vm.AutosaveAsync();
        var save = Assert.Single(original.SaveGames.Saves).Value;
        using var h = new GameViewModelHarness();
        h.SaveGames.Saves[save.Id] = save;
        h.SaveGames.LoadError = new IOException("busy");
        h.Vm.ApplyQueryAttributes(new Dictionary<string, object> { [GameViewModel.SaveIdParameter] = save.Id.ToString() });
        await h.Vm.InitialiseAsync();
        Assert.Null(h.Vm.Session);
        Assert.True(h.Vm.HasPersistenceFailure);

        h.SaveGames.LoadError = null;
        await h.Vm.RetryPersistenceCommand.ExecuteAsync(null);
        Assert.False(h.Vm.HasPersistenceFailure);
        Assert.Equal(save.Id, h.Saves.Id);
        Assert.Equal(CellState.Filled, h.Vm.Session!.Cells[0]);
    }

    [Fact]
    public async Task FailedSettingsRefresh_KeepsKnownPreferences_UntilRetry()
    {
        using var h = new GameViewModelHarness();
        h.Settings.Settings = GameSettings.Default with { Helpers = HelperSettings.Default with { ShowTimer = false } };
        await h.Vm.StartAsync();
        h.Settings.LoadFails = true;
        await h.Vm.RefreshSettingsAsync();
        Assert.False(h.Vm.ShowTimer);
        Assert.True(h.Vm.HasPersistenceFailure);
        h.Settings.LoadFails = false;
        await h.Vm.RetryPersistenceCommand.ExecuteAsync(null);
        Assert.False(h.Vm.HasPersistenceFailure);
    }

    [Fact]
    public async Task ContinueReadFailure_IsNotAnEmptySaveList()
    {
        using var h = new GameViewModelHarness();
        using var vm = new ContinueViewModel(h.Strings, h.SaveGames, h.Sessions, h.Navigation, h.Clock, h.Diagnostics);
        h.SaveGames.LoadError = new IOException("busy");
        await vm.OnAppearingAsync();
        Assert.False(vm.IsEmpty);
        Assert.False(vm.HasSaves);
        Assert.True(vm.HasPersistenceFailure);
        Assert.Equal(PersistenceOperation.LoadGames, Assert.Single(h.Diagnostics.Reports).Operation);
        h.SaveGames.LoadError = null;
        await vm.RetryPersistenceCommand.ExecuteAsync(null);
        Assert.True(vm.IsEmpty);
        Assert.False(vm.HasPersistenceFailure);
    }
}
