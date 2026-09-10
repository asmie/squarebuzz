using Squarebuzz.Core.Model;
using Squarebuzz.Presentation.Services;
using Squarebuzz.Presentation.Tests.Fakes;
using Squarebuzz.Presentation.ViewModels;
using Xunit;

namespace Squarebuzz.Presentation.Tests;

public sealed class OrderedSettingsRepositoryTests
{
    [Fact]
    public async Task Diagnostics_ReportFailedOperations_ButNotCancellation()
    {
        var storage = new FakeSettingsRepository { SaveFails = true, LoadFails = true };
        var diagnostics = new FakePersistenceDiagnostics();
        var settings = new OrderedSettingsRepository(storage, diagnostics);
        await Assert.ThrowsAsync<InvalidOperationException>(() => settings.SaveAsync(GameSettings.Default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => settings.LoadAsync());
        Assert.Equal([PersistenceOperation.SaveSettings, PersistenceOperation.LoadSettings],
            diagnostics.Reports.Select(r => r.Operation));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => settings.LoadAsync(cancellation.Token));
        Assert.Equal(2, diagnostics.Reports.Count);
    }

    [Fact]
    public async Task ReturningFromOptions_WaitsForAllChangesEvenAfterThePageIsDisposed()
    {
        var storage = new FakeSettingsRepository();
        var settings = new OrderedSettingsRepository(storage);
        using var game = new GameViewModelHarness(settings: settings);
        await game.Vm.StartAsync();
        using var options = new OptionsViewModel(game.Strings, settings, game.CompletionService,
            game.Theme, game.Navigation, game.ScreenTime, game.Audio, game.Narration, game.UiThread);
        await options.OnAppearingAsync();

        var gate = storage.SaveGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        options.Haptics = false;
        options.BigNumbers = true;
        options.AutoCross = false;
        options.Dispose();

        var returning = game.Vm.RefreshSettingsAsync();
        Assert.False(returning.IsCompleted);
        gate.SetResult();
        await returning;

        Assert.False(game.Vm.HapticsEnabled);
        Assert.True(game.Vm.BigNumbers);
        Assert.False(game.Vm.Session!.Rules.AutoCrossCompletedLines);
        Assert.Equal(3, storage.Saved.Count);
    }

    [Fact]
    public async Task ARead_ObservesPrecedingWritesWithoutBeingOvertakenByALaterWrite()
    {
        var storage = new FakeSettingsRepository();
        var settings = new OrderedSettingsRepository(storage);
        var gate = storage.SaveGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = settings.SaveAsync(GameSettings.Default with { CellZoomPercent = 110 });
        var second = settings.SaveAsync(GameSettings.Default with { CellZoomPercent = 120 });
        var reading = settings.LoadAsync();
        var third = settings.SaveAsync(GameSettings.Default with { CellZoomPercent = 130 });
        Assert.False(reading.IsCompleted);
        Assert.Empty(storage.Saved);

        gate.SetResult();
        var read = await reading;
        await Task.WhenAll(first, second, third);

        Assert.Equal(120, read.CellZoomPercent);
        Assert.Equal([110, 120, 130], storage.Saved.Select(s => s.CellZoomPercent));
        Assert.Equal(130, (await settings.LoadAsync()).CellZoomPercent);
    }

    [Fact]
    public async Task FailedWrite_IsReportedButDoesNotPreventLaterReadsOrWrites()
    {
        var storage = new FakeSettingsRepository { SaveFails = true };
        var settings = new OrderedSettingsRepository(storage);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            settings.SaveAsync(GameSettings.Default with { BigNumbers = true }));
        Assert.False((await settings.LoadAsync()).BigNumbers);

        storage.SaveFails = false;
        await settings.SaveAsync(GameSettings.Default with { BigNumbers = true });
        Assert.True((await settings.LoadAsync()).BigNumbers);
    }

    [Fact]
    public async Task CancelledQueuedWrite_DoesNotLetAReadOvertakeTheRunningWrite()
    {
        var storage = new FakeSettingsRepository();
        var settings = new OrderedSettingsRepository(storage);
        var gate = storage.SaveGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = settings.SaveAsync(GameSettings.Default with { BigNumbers = true });
        using var cancellation = new CancellationTokenSource();
        var cancelled = settings.SaveAsync(GameSettings.Default, cancellation.Token);
        cancellation.Cancel();
        var reading = settings.LoadAsync();
        Assert.False(reading.IsCompleted);

        gate.SetResult();
        await first;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
        Assert.True((await reading).BigNumbers);
        Assert.Single(storage.Saved);
    }
}
