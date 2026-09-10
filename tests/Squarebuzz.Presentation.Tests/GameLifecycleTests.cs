using Squarebuzz.Core.Model;
using Squarebuzz.Presentation.Services;
using Xunit;

namespace Squarebuzz.Presentation.Tests;

public sealed class GameLifecycleTests : IDisposable
{
    private readonly GameViewModelHarness _h = new();
    private readonly GameLifecycle _lifecycle;

    public GameLifecycleTests() => _lifecycle = new GameLifecycle(_h.CompletionService);

    public void Dispose() => _h.Dispose();

    private async Task ShowGameAsync()
    {
        _lifecycle.Show(_h.Vm);
        await _h.Vm.InitialiseAsync();
    }

    private void Tick(TimeSpan elapsed)
    {
        _h.Clock.Advance(elapsed);
        _h.Timers.Latest!.RaiseTick();
    }

    [Fact]
    public async Task Backgrounding_SavesAndStopsBeforeTheWriteFinishes()
    {
        await ShowGameAsync();
        _h.Vm.Paint(0, CellState.Filled);
        Tick(TimeSpan.FromSeconds(2));
        var timer = _h.Timers.Latest!;
        var gate = _h.SaveGames.SaveGate = new(TaskCreationOptions.RunContinuationsAsynchronously);

        var stopping = _lifecycle.SuspendAsync();
        Assert.False(timer.IsRunning);
        Assert.False(stopping.IsCompleted);
        gate.SetResult();
        await stopping;

        var save = Assert.Single(_h.SaveGames.Saves).Value;
        Assert.Equal(CellState.Filled, save.Cells[0]);
        Assert.Equal(TimeSpan.FromSeconds(2), save.Elapsed);
    }

    [Fact]
    public async Task Resume_ExcludesBackgroundTimeAndRefreshesAccessibility()
    {
        await ShowGameAsync();
        Tick(TimeSpan.FromSeconds(2));
        await _lifecycle.SuspendAsync();
        _h.Clock.Advance(TimeSpan.FromMinutes(10));
        var refreshes = _h.Accessibility.RefreshCalls;

        _lifecycle.Resume();
        Assert.Equal(refreshes + 1, _h.Accessibility.RefreshCalls);
        Assert.True(_h.Timers.Latest!.IsRunning);
        Tick(TimeSpan.FromSeconds(1));
        Assert.Equal(TimeSpan.FromSeconds(3), _h.Vm.Session!.Elapsed);
        Assert.Equal(TimeSpan.FromSeconds(3), _h.ScreenTime.Played);
    }

    [Fact]
    public async Task Resume_DoesNotDismissManualPauseOrStartItsTimer()
    {
        await ShowGameAsync();
        await _h.Vm.PauseCommand.ExecuteAsync(null);
        await _lifecycle.SuspendAsync();
        _h.Clock.Advance(TimeSpan.FromMinutes(5));
        _lifecycle.Resume();

        Assert.True(_h.Vm.IsPaused);
        Assert.False(_h.Timers.Latest!.IsRunning);
        _h.Vm.ResumeCommand.Execute(null);
        Tick(TimeSpan.FromSeconds(1));
        Assert.Equal(TimeSpan.FromSeconds(1), _h.Vm.Session!.Elapsed);
    }

    [Fact]
    public async Task Resume_DoesNotRestartAGameCoveredByAnotherPage()
    {
        await ShowGameAsync();
        _h.Vm.Paint(0, CellState.Filled);
        await _lifecycle.HideAsync(_h.Vm);
        Assert.Single(_h.SaveGames.Saves);
        await _lifecycle.SuspendAsync();
        _h.Clock.Advance(TimeSpan.FromMinutes(5));
        _lifecycle.Resume();
        Assert.False(_h.Timers.Latest!.IsRunning);

        _lifecycle.Show(_h.Vm);
        Tick(TimeSpan.FromSeconds(1));
        Assert.Equal(TimeSpan.FromSeconds(1), _h.Vm.Session!.Elapsed);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task LateInitialization_DoesNotStartTheClockWhileHidden(bool backgrounded)
    {
        // Register the page, then hide it before its asynchronous initialization creates a
        // session. This is the ordering of a window/page event during a delayed settings read.
        _lifecycle.Show(_h.Vm);
        if (backgrounded)
        {
            await _lifecycle.SuspendAsync();
        }
        else
        {
            await _lifecycle.HideAsync(_h.Vm);
        }

        await _h.Vm.InitialiseAsync();
        Assert.NotNull(_h.Vm.Session);
        Assert.Empty(_h.Timers.Created);

        if (backgrounded)
        {
            _lifecycle.Resume();
        }
        else
        {
            _lifecycle.Show(_h.Vm);
        }
        Assert.True(_h.Timers.Latest!.IsRunning);
    }

    [Fact]
    public async Task PageAppearingWhileWindowIsStopped_RemainsSuspended()
    {
        await _lifecycle.SuspendAsync();
        await ShowGameAsync();
        Assert.Empty(_h.Timers.Created);

        _lifecycle.Resume();
        Assert.True(_h.Timers.Latest!.IsRunning);
    }

    [Fact]
    public async Task DuplicateResumeEvents_DoNotResetTheRunningClock()
    {
        await ShowGameAsync();
        await _lifecycle.SuspendAsync();
        _lifecycle.Resume();
        var timer = _h.Timers.Latest!;
        _h.Clock.Advance(TimeSpan.FromMilliseconds(500));

        _lifecycle.Resume();
        _lifecycle.Show(_h.Vm);
        Assert.Same(timer, _h.Timers.Latest);
        Tick(TimeSpan.FromMilliseconds(500));
        Assert.Equal(TimeSpan.FromSeconds(1), _h.Vm.Session!.Elapsed);
    }

    [Fact]
    public async Task OldPageDisappearing_DoesNotDetachTheNewGame()
    {
        await ShowGameAsync();
        using var next = new GameViewModelHarness();
        _lifecycle.Show(next.Vm);
        await next.Vm.InitialiseAsync();
        await _lifecycle.HideAsync(_h.Vm);
        await _lifecycle.SuspendAsync();
        Assert.False(next.Timers.Latest!.IsRunning);

        _lifecycle.Resume();
        Assert.True(next.Timers.Latest!.IsRunning);
        Assert.False(_h.Timers.Latest!.IsRunning);
    }

    [Fact]
    public async Task TimedGame_DoesNotExpireOrCreateASaveWhileBackgrounded()
    {
        _h.Vm.ApplyQueryAttributes(new Dictionary<string, object> { ["tier"] = "1" });
        await ShowGameAsync();
        _h.Vm.Paint(0, CellState.Filled);
        Tick(TimeSpan.FromSeconds(1));
        await _lifecycle.SuspendAsync();
        _h.Clock.Advance(TimeSpan.FromHours(1));
        _lifecycle.Resume();
        Tick(TimeSpan.FromSeconds(1));

        Assert.True(_h.Vm.Session!.IsTimed);
        Assert.False(_h.Vm.Session.IsTimeUp);
        Assert.Equal(TimeSpan.FromSeconds(2), _h.Vm.Session.Elapsed);
        Assert.Empty(_h.SaveGames.Saves);
    }

    [Fact]
    public async Task SolvedGame_DoesNotRestartOnResume()
    {
        await ShowGameAsync();
        _h.SolveCurrentPuzzle();
        await _lifecycle.SuspendAsync();
        _lifecycle.Resume();
        Assert.True(_h.Vm.IsSolved);
        Assert.False(_h.Timers.Latest!.IsRunning);
    }
}
