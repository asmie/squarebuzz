using System.Globalization;
using Squarebuzz.Core.Model;
using Squarebuzz.Core.Progression;
using Xunit;

namespace Squarebuzz.Presentation.Tests;

/// <summary>
/// The completion race: a win must be recorded for the game that was won, however quickly the
/// player taps "Next". These drive the ViewModel through the same entry points GamePage uses -
/// ApplyQueryAttributes for the route, Paint for cell taps.
/// </summary>
public sealed class CompletionAttributionTests : IDisposable
{
    private readonly GameViewModelHarness _h = new();

    public void Dispose() => _h.Dispose();

    private async Task StartLevelAsync(int level)
    {
        _h.Vm.ApplyQueryAttributes(new Dictionary<string, object>
        {
            ["level"] = level.ToString(CultureInfo.InvariantCulture),
        });
        await _h.Vm.InitialiseAsync();
    }

    [Fact]
    public async Task CompletingLevel3_ThenTappingNextBeforePersistenceFinishes_StillRecordsLevel3()
    {
        await StartLevelAsync(3);
        Assert.Equal(3, _h.Vm.Session!.Origin!.Level);

        // Stall the first persistence step of the completion pipeline, so "Next" lands while
        // the win is still being written - the exact interleaving of the original bug.
        _h.SaveGames.DeleteGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        _h.SolveCurrentPuzzle();

        Assert.True(_h.Vm.IsSolved);
        Assert.Empty(_h.Progress.Completions);

        // The quick tap on "Next": replaces the session, the save id and the level.
        await _h.Vm.NextPuzzleCommand.ExecuteAsync(null);
        Assert.Equal(4, _h.Vm.Session!.Origin!.Level);

        // Only now does the stalled completion write finish.
        _h.SaveGames.DeleteGate.SetResult();
        await GameViewModelHarness.WaitUntilAsync(() => _h.Progress.Completions.Count == 1);

        var completion = Assert.Single(_h.Progress.Completions);
        Assert.Equal(3, completion.Level);
        Assert.False(completion.IsDaily);
    }

    [Fact]
    public async Task CompletingLevel_RecordsStatsOfTheSolvedGame()
    {
        await StartLevelAsync(3);
        _h.SolveCurrentPuzzle();

        await GameViewModelHarness.WaitUntilAsync(() => _h.Progress.Completions.Count == 1);

        var completion = Assert.Single(_h.Progress.Completions);
        Assert.Equal(3, completion.Level);
        Assert.Equal(3, completion.Stars);
        Assert.Equal(0, completion.Mistakes);
        Assert.Equal(0, completion.HintsUsed);
        Assert.Equal(2, completion.BlocksFilled);
        Assert.Null(completion.PuzzleId);
        Assert.Equal(_h.Clock.Now, completion.CompletedAt);
    }

    [Fact]
    public async Task NextAfterLevelWin_StartsTheFollowingLevel()
    {
        await StartLevelAsync(3);
        _h.SolveCurrentPuzzle();
        await GameViewModelHarness.WaitUntilAsync(() => _h.Progress.Completions.Count == 1);

        await _h.Vm.NextPuzzleCommand.ExecuteAsync(null);

        Assert.False(_h.Vm.IsSolved);
        Assert.Equal(4, _h.Vm.Session!.Origin!.Level);
        Assert.Equal(LevelCatalog.SeedFor(4), _h.Vm.Session.Seed);
        Assert.Equal("levelN:4", _h.Vm.PuzzleName);
    }

    [Fact]
    public async Task RestartAfterLevelWin_ReplaysTheSameLevel()
    {
        await StartLevelAsync(3);
        _h.SolveCurrentPuzzle();
        await GameViewModelHarness.WaitUntilAsync(() => _h.Progress.Completions.Count == 1);

        await _h.Vm.RestartCommand.ExecuteAsync(null);

        Assert.False(_h.Vm.IsSolved);
        Assert.Equal(3, _h.Vm.Session!.Origin!.Level);

        // Deterministic is the point of a level: the replay is the exact same board.
        Assert.Equal(LevelCatalog.SeedFor(3), _h.Vm.Session.Seed);
    }

    [Fact]
    public async Task CompletingDaily_RecordsDailyFlag_AndNextLeavesItBehind()
    {
        _h.Vm.ApplyQueryAttributes(new Dictionary<string, object> { ["daily"] = "1" });
        await _h.Vm.InitialiseAsync();

        _h.SolveCurrentPuzzle();
        await GameViewModelHarness.WaitUntilAsync(() => _h.Progress.Completions.Count == 1);

        Assert.True(_h.Progress.Completions[0].IsDaily);
        Assert.Null(_h.Progress.Completions[0].Level);

        // Moving on from the daily is an ordinary game; it must not be recorded as a second daily.
        await _h.Vm.NextPuzzleCommand.ExecuteAsync(null);
        _h.SolveCurrentPuzzle();
        await GameViewModelHarness.WaitUntilAsync(() => _h.Progress.Completions.Count == 2);

        Assert.False(_h.Progress.Completions[1].IsDaily);
    }

    [Fact]
    public async Task CompletingPuzzle_DeletesItsSaveRow()
    {
        await StartLevelAsync(3);
        _h.SolveCurrentPuzzle();

        await GameViewModelHarness.WaitUntilAsync(() => _h.SaveGames.Deleted.Count == 1);
        Assert.Empty(_h.SaveGames.Saves);
    }

    [Fact]
    public async Task LastLevelWin_HidesNextButton()
    {
        await StartLevelAsync(LevelCatalog.LevelCount);
        _h.SolveCurrentPuzzle();
        await GameViewModelHarness.WaitUntilAsync(() => _h.Progress.Completions.Count == 1);

        Assert.True(_h.Vm.IsCampaignComplete);
        Assert.False(_h.Vm.ShowNextButton);

        // The command's backstop: even if invoked, there is no level 601 to start.
        await _h.Vm.NextPuzzleCommand.ExecuteAsync(null);
        Assert.Equal(LevelCatalog.LevelCount, _h.Vm.Session!.Origin!.Level);
    }
}
