using Squarebuzz.Core.Model;
using Squarebuzz.Core.Progression;
using Xunit;

namespace Squarebuzz.Presentation.Tests;

public sealed class DailyGameTests : IDisposable
{
    private readonly GameViewModelHarness _h = new();
    private readonly DateOnly _dailyDate = new(2026, 12, 31);

    public void Dispose() => _h.Dispose();

    private async Task StartDailyAsync()
    {
        _h.Clock.Today = _dailyDate;
        _h.Clock.Now = new DateTimeOffset(2026, 12, 31, 23, 59, 0, TimeSpan.FromHours(2));
        _h.Vm.ApplyQueryAttributes(new Dictionary<string, object> { ["daily"] = "1" });
        await _h.Vm.InitialiseAsync();
        _h.Vm.Paint(0, CellState.Filled);
    }

    private async Task ResumeIntoAsync(GameViewModelHarness resumed)
    {
        await _h.Vm.AutosaveAsync();
        var save = Assert.Single(_h.SaveGames.Saves).Value;
        Assert.Equal(_dailyDate, save.DailyDate);
        resumed.SaveGames.Saves[save.Id] = save;
        resumed.Vm.ApplyQueryAttributes(new Dictionary<string, object> { ["saveId"] = save.Id.ToString("D") });
        await resumed.Vm.InitialiseAsync();
        Assert.Equal(CellState.Filled, resumed.Vm.Session![0]);
    }

    [Fact]
    public async Task ResumedDaily_FinishedOnAnotherDate_KeepsOriginalAttribution()
    {
        await StartDailyAsync();
        using var resumed = new GameViewModelHarness();
        resumed.Clock.Today = _dailyDate.AddDays(2);
        resumed.Clock.Now = _h.Clock.Now.AddDays(2);
        await ResumeIntoAsync(resumed);

        resumed.Vm.Paint(3, CellState.Filled);
        await GameViewModelHarness.WaitUntilAsync(() => resumed.Progress.Completions.Count == 1);

        var completion = Assert.Single(resumed.Progress.Completions);
        Assert.True(completion.IsDaily);
        Assert.Equal(_dailyDate, completion.DailyDate);
        Assert.Equal(resumed.Clock.Now, completion.CompletedAt);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RestartAfterMidnight_KeepsTheOriginalDailyPuzzle(bool resumeFirst)
    {
        await StartDailyAsync();
        using var resumed = new GameViewModelHarness();
        var playing = _h;
        if (resumeFirst)
        {
            await ResumeIntoAsync(resumed);
            playing = resumed;
        }

        playing.Clock.Today = _dailyDate.AddDays(1);
        playing.Clock.Now = _h.Clock.Now.AddDays(1);
        await playing.Vm.RestartCommand.ExecuteAsync(null);

        Assert.Equal(DailyPuzzle.SeedFor(_dailyDate), playing.Vm.Session!.Seed);
        Assert.Equal(_dailyDate, playing.Vm.Session.Origin!.DailyDate);
        Assert.True(playing.Vm.Session.Origin.ForceGenerated);
        Assert.All(playing.Vm.Session.Cells.ToArray(), cell => Assert.Equal(CellState.Empty, cell));
    }

    [Fact]
    public async Task NextWhileDailyCompletionIsPending_PreservesItsDateAndLeavesDailyMode()
    {
        await StartDailyAsync();
        _h.Clock.Today = _dailyDate.AddDays(1);
        _h.Clock.Now = _h.Clock.Now.AddMinutes(2);
        _h.Completions.CompleteGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _h.Vm.Paint(3, CellState.Filled);

        await _h.Vm.NextPuzzleCommand.ExecuteAsync(null);
        Assert.Null(_h.Vm.Session!.Origin!.DailyDate);
        _h.Completions.CompleteGate.SetResult();
        await GameViewModelHarness.WaitUntilAsync(() => _h.Progress.Completions.Count == 1);
        Assert.Equal(_dailyDate, _h.Progress.Completions[0].DailyDate);

        _h.SolveCurrentPuzzle();
        await GameViewModelHarness.WaitUntilAsync(() => _h.Progress.Completions.Count == 2);
        Assert.False(_h.Progress.Completions[1].IsDaily);
        Assert.Null(_h.Progress.Completions[1].DailyDate);
    }
}
