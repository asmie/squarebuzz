using Squarebuzz.Core.Model;
using Xunit;

namespace Squarebuzz.Presentation.Tests;

public sealed class GameTimingTests : IDisposable
{
    private readonly GameViewModelHarness _h = new();

    public GameTimingTests() => _h.Settings.Settings = GameSettings.Default with
    {
        Helpers = HelperSettings.Default with { AutoCross = false },
    };

    public void Dispose() => _h.Dispose();

    private async Task StartAsync(bool timed = false)
    {
        await _h.Vm.StartAsync(NewGameOptions.Default with
        {
            TimeLimit = timed ? TimeSpan.FromSeconds(1) : null,
        });
        _h.Vm.Paint(0, CellState.Filled);
    }

    [Theory]
    [InlineData("paint", 999, true)]
    [InlineData("tap", 999, true)]
    [InlineData("hint", 999, true)]
    [InlineData("paint", 1000, false)]
    [InlineData("tap", 1000, false)]
    [InlineData("hint", 1000, false)]
    [InlineData("paint", 2000, false)]
    [InlineData("tap", 2000, false)]
    [InlineData("hint", 2000, false)]
    public async Task FinishingWithoutATimerTick_UsesTheActualDeadline(string action, int milliseconds, bool wins)
    {
        await StartAsync(timed: true);
        if (action == "hint")
        {
            // Leave only the final filled square undecided, so the hint must finish it
            // instead of choosing one of the empty squares to cross.
            _h.Vm.Paint(1, CellState.Crossed);
            _h.Vm.Paint(2, CellState.Crossed);
        }
        _h.Clock.Advance(TimeSpan.FromMilliseconds(milliseconds));

        switch (action)
        {
            case "paint": _h.Vm.Paint(3, CellState.Filled); break;
            case "tap": _h.Vm.TapCell(3); break;
            case "hint": _h.Vm.UseHintCommand.Execute(null); break;
        }

        Assert.Equal(wins, _h.Vm.IsSolved);
        Assert.Equal(!wins, _h.Vm.IsTimeUp);
        Assert.False(_h.Timers.Latest!.IsRunning);
        var elapsed = TimeSpan.FromMilliseconds(Math.Min(milliseconds, 1000));
        Assert.Equal(elapsed, _h.Vm.Session!.Elapsed);
        Assert.Equal(elapsed, _h.ScreenTime.Played);
        if (wins)
        {
            await GameViewModelHarness.WaitUntilAsync(() => _h.Progress.Completions.Count == 1);
            Assert.Equal(elapsed, Assert.Single(_h.Progress.Completions).Elapsed);
        }
        else
        {
            Assert.Empty(_h.Progress.Completions);
            Assert.Equal(CellState.Empty, _h.Vm.Session[3]);
            Assert.Equal(0, _h.Vm.HintsUsed);
            Assert.False(_h.Vm.CanUseHint);
            Assert.False(_h.Vm.CanUndo);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HistoryActions_RespectDeadlineWithoutATick(bool redo)
    {
        await StartAsync(timed: true);
        if (redo)
        {
            _h.Vm.UndoCommand.Execute(null);
        }
        var before = _h.Vm.Session!.Cells.ToArray();
        _h.Clock.Advance(TimeSpan.FromSeconds(2));

        if (redo)
        {
            _h.Vm.RedoCommand.Execute(null);
        }
        else
        {
            _h.Vm.UndoCommand.Execute(null);
        }

        Assert.True(_h.Vm.IsTimeUp);
        Assert.Equal(before, _h.Vm.Session.Cells.ToArray());
    }

    [Theory]
    [InlineData("pause")]
    [InlineData("suspend")]
    [InlineData("quit")]
    [InlineData("save")]
    public async Task SavePoints_CaptureTheFractionSinceTheLastTick(string action)
    {
        await StartAsync();
        _h.Clock.Advance(TimeSpan.FromSeconds(1));
        _h.Timers.Latest!.RaiseTick();
        _h.Clock.Advance(TimeSpan.FromMilliseconds(375));

        switch (action)
        {
            case "pause": await _h.Vm.PauseCommand.ExecuteAsync(null); break;
            case "quit": await _h.Vm.QuitCommand.ExecuteAsync(null); break;
            case "suspend":
                _h.Vm.SuspendClock();
                await _h.Vm.AutosaveAsync();
                break;
            case "save": await _h.Vm.AutosaveAsync(); break;
        }

        Assert.Equal(TimeSpan.FromMilliseconds(1375), Assert.Single(_h.SaveGames.Saves).Value.Elapsed);
        Assert.Equal(TimeSpan.FromMilliseconds(1375), _h.ScreenTime.Played);
    }

    [Fact]
    public async Task RepeatedShortPauses_CannotDiscardPlayTime()
    {
        await StartAsync();
        for (var i = 0; i < 4; i++)
        {
            _h.Clock.Advance(TimeSpan.FromMilliseconds(250));
            await _h.Vm.PauseCommand.ExecuteAsync(null);
            _h.Clock.Advance(TimeSpan.FromMinutes(1));
            _h.Vm.ResumeCommand.Execute(null);
        }

        Assert.Equal(TimeSpan.FromSeconds(1), _h.Vm.Session!.Elapsed);
        Assert.Equal(TimeSpan.FromSeconds(1), _h.ScreenTime.Played);
        Assert.Equal(TimeSpan.FromSeconds(1), Assert.Single(_h.SaveGames.Saves).Value.Elapsed);
    }

    [Fact]
    public async Task MovesAndTicks_ChargeEachIntervalOnce_AndCompletionKeepsTheFinalFraction()
    {
        await StartAsync();
        _h.Clock.Advance(TimeSpan.FromMilliseconds(250));
        _h.Vm.TapCell(0);
        _h.Clock.Advance(TimeSpan.FromMilliseconds(750));
        _h.Timers.Latest!.RaiseTick();
        _h.Clock.Advance(TimeSpan.FromMilliseconds(125));
        _h.Vm.Paint(0, CellState.Filled);
        _h.Vm.Paint(3, CellState.Filled);
        await GameViewModelHarness.WaitUntilAsync(() => _h.Progress.Completions.Count == 1);

        Assert.Equal(TimeSpan.FromMilliseconds(1125), Assert.Single(_h.Progress.Completions).Elapsed);
        Assert.Equal(TimeSpan.FromMilliseconds(1125), _h.ScreenTime.Played);
    }

    [Fact]
    public async Task DismissingBreakWithoutInterveningTicks_ExcludesTheEntireBreak()
    {
        await StartAsync();
        _h.ScreenTime.RemindOnNextAdd = true;
        _h.Clock.Advance(TimeSpan.FromSeconds(1));
        _h.Timers.Latest!.RaiseTick();
        Assert.True(_h.Vm.IsBreakReminderOpen);
        _h.Clock.Advance(TimeSpan.FromMinutes(2));
        _h.Vm.DismissBreakReminderCommand.Execute(null);
        _h.Clock.Advance(TimeSpan.FromMilliseconds(250));
        await _h.Vm.AutosaveAsync();

        Assert.Equal(TimeSpan.FromMilliseconds(1250), _h.Vm.Session!.Elapsed);
        Assert.Equal(TimeSpan.FromMilliseconds(1250), _h.ScreenTime.Played);
    }

    [Fact]
    public async Task Restart_AccountsForTheOutgoingAttempt_AndStartsAFreshClock()
    {
        await StartAsync();
        var previous = _h.Vm.Session!;
        _h.Clock.Advance(TimeSpan.FromMilliseconds(250));
        await _h.Vm.RestartCommand.ExecuteAsync(null);
        Assert.Equal(TimeSpan.FromMilliseconds(250), previous.Elapsed);
        Assert.Equal(TimeSpan.Zero, _h.Vm.Session!.Elapsed);
        _h.Clock.Advance(TimeSpan.FromMilliseconds(125));
        _h.Vm.TapCell(0);

        Assert.Equal(TimeSpan.FromMilliseconds(125), _h.Vm.Session.Elapsed);
        Assert.Equal(TimeSpan.FromMilliseconds(375), _h.ScreenTime.Played);
    }

    [Fact]
    public async Task InputAtTheBreakLimit_OpensTheReminderBeforeApplyingTheMove()
    {
        await StartAsync();
        _h.ScreenTime.RemindOnNextAdd = true;
        _h.Clock.Advance(TimeSpan.FromMilliseconds(500));
        _h.Vm.Paint(3, CellState.Filled);

        Assert.True(_h.Vm.IsBreakReminderOpen);
        Assert.False(_h.Vm.IsSolved);
        Assert.Equal(CellState.Empty, _h.Vm.Session![3]);
        Assert.Equal(TimeSpan.FromMilliseconds(500), Assert.Single(_h.SaveGames.Saves).Value.Elapsed);
    }

    [Fact]
    public async Task InputAccounting_DoesNotPostponeThePeriodicSave()
    {
        await StartAsync();
        _h.Clock.Advance(TimeSpan.FromMilliseconds(14500));
        _h.Vm.Paint(1, CellState.Crossed);
        _h.Clock.Advance(TimeSpan.FromMilliseconds(500));
        _h.Timers.Latest!.RaiseTick();

        Assert.Equal(TimeSpan.FromSeconds(15), Assert.Single(_h.SaveGames.Saves).Value.Elapsed);
        Assert.Equal(TimeSpan.FromSeconds(15), _h.ScreenTime.Played);
    }

    [Fact]
    public async Task PausingAfterDeadline_ShowsTimeUpInsteadOfPause_OnlyOnce()
    {
        await StartAsync(timed: true);
        _h.Clock.Advance(TimeSpan.FromSeconds(2));
        await _h.Vm.PauseCommand.ExecuteAsync(null);
        Assert.True(_h.Vm.IsTimeUp);
        Assert.False(_h.Vm.IsPaused);
        var announcements = _h.ScreenReader.Announcements.Count;

        await _h.Vm.PauseCommand.ExecuteAsync(null);
        _h.Vm.TapCell(3);
        _h.Vm.SuspendClock();
        Assert.Equal(announcements, _h.ScreenReader.Announcements.Count);
        Assert.Empty(_h.SaveGames.Saves);
    }

    [Fact]
    public async Task SuspendedGame_IgnoresLateInputAndTicks()
    {
        await StartAsync();
        var timer = _h.Timers.Latest!;
        _h.Clock.Advance(TimeSpan.FromMilliseconds(250));
        _h.Vm.SuspendClock();
        _h.Clock.Advance(TimeSpan.FromMinutes(2));
        timer.RaiseTick();
        _h.Vm.TapCell(3);

        Assert.False(_h.Vm.IsSolved);
        Assert.Equal(TimeSpan.FromMilliseconds(250), _h.Vm.Session!.Elapsed);
        _h.Vm.ResumeClock();
        _h.Clock.Advance(TimeSpan.FromMilliseconds(250));
        await _h.Vm.AutosaveAsync();
        Assert.Equal(TimeSpan.FromMilliseconds(500), Assert.Single(_h.SaveGames.Saves).Value.Elapsed);
    }
}
