using Squarebuzz.Core.Generation;
using Squarebuzz.Core.Model;
using Squarebuzz.Core.Progression;
using Xunit;

namespace Squarebuzz.Presentation.Tests;

/// <summary>How the game screen interprets its route parameters.</summary>
public sealed class GameViewModelQueryTests : IDisposable
{
    private readonly GameViewModelHarness _h = new();

    public void Dispose() => _h.Dispose();

    [Fact]
    public async Task Level_InRange_StartsThatCampaignLevel()
    {
        _h.Vm.ApplyQueryAttributes(new Dictionary<string, object> { ["level"] = "5" });
        await _h.Vm.InitialiseAsync();

        Assert.Equal(5, _h.Vm.Session!.Origin!.Level);
        Assert.Equal(LevelCatalog.SeedFor(5), _h.Vm.Session.Seed);
        Assert.Equal("levelN:5", _h.Vm.PuzzleName);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("601")]
    [InlineData("-3")]
    [InlineData("abc")]
    public async Task Level_OutOfRangeOrUnparseable_IsIgnored(string level)
    {
        _h.Vm.ApplyQueryAttributes(new Dictionary<string, object> { ["level"] = level });
        await _h.Vm.InitialiseAsync();

        Assert.NotNull(_h.Vm.Session);
        Assert.Null(_h.Vm.Session!.Origin!.Level);
    }

    [Fact]
    public async Task SaveId_ResumesTheNamedSave()
    {
        var id = Guid.NewGuid();

        _h.SaveGames.Saves[id] = new SavedGame
        {
            Id = id,
            PuzzleId = null,
            Size = 5,
            Difficulty = 2,
            PackId = "surprise",
            Seed = 42,
            Challenge = ChallengeLevel.Relaxed,
            Cells = [CellState.Filled, CellState.Empty, CellState.Empty, CellState.Empty],
            Elapsed = TimeSpan.FromSeconds(90),
            HintsRemaining = 3,
            Mistakes = 1,
            SavedAt = _h.Clock.Now,
            Level = 7,
            GeneratorVersion = GeneratorVersion.Current,
        };

        _h.Vm.ApplyQueryAttributes(new Dictionary<string, object> { ["saveId"] = id.ToString("D") });
        await _h.Vm.InitialiseAsync();

        Assert.Equal(42, _h.Vm.Session!.Seed);
        Assert.Equal(7, _h.Vm.Session.Origin!.Level);
        Assert.Equal(TimeSpan.FromSeconds(90), _h.Vm.Session.Elapsed);
        Assert.Equal(1, _h.Vm.Mistakes);
        Assert.Equal("levelN:7", _h.Vm.PuzzleName);
        Assert.Equal("1:30", _h.Vm.ElapsedText);
    }

    [Fact]
    public async Task SaveId_ThatNoLongerExists_FallsBackToAFreshGame()
    {
        _h.Vm.ApplyQueryAttributes(new Dictionary<string, object>
        {
            ["saveId"] = Guid.NewGuid().ToString("D"),
        });
        await _h.Vm.InitialiseAsync();

        Assert.NotNull(_h.Vm.Session);
        Assert.Equal(0, _h.Vm.Session!.MoveCount);
    }

    [Fact]
    public async Task Tier_StartsATimedTrial_WithThatTiersClock()
    {
        _h.Vm.ApplyQueryAttributes(new Dictionary<string, object> { ["tier"] = "2" });
        await _h.Vm.InitialiseAsync();

        Assert.True(_h.Vm.Session!.IsTimed);
        Assert.Equal(TimeSpan.FromMinutes(5), _h.Vm.Session.TimeLimit);
    }

    [Fact]
    public async Task Tier_OverridesTheHiddenTimerPreference()
    {
        _h.Settings.Settings = GameSettings.Default with
        {
            Helpers = HelperSettings.Default with { ShowTimer = false },
        };

        _h.Vm.ApplyQueryAttributes(new Dictionary<string, object> { ["tier"] = "1" });
        await _h.Vm.InitialiseAsync();

        // A player racing a countdown must see it, whatever they chose in Options.
        Assert.True(_h.Vm.ShowTimer);
    }

    [Fact]
    public async Task Tier_Unknown_IsIgnored()
    {
        _h.Vm.ApplyQueryAttributes(new Dictionary<string, object> { ["tier"] = "9" });
        await _h.Vm.InitialiseAsync();

        Assert.NotNull(_h.Vm.Session);
        Assert.False(_h.Vm.Session!.IsTimed);
    }

    [Fact]
    public async Task Daily_UsesTheDateSeed_SoEveryoneGetsTheSamePuzzle()
    {
        _h.Vm.ApplyQueryAttributes(new Dictionary<string, object> { ["daily"] = "1" });
        await _h.Vm.InitialiseAsync();

        Assert.Equal(DailyPuzzle.SeedFor(_h.Clock.Today), _h.Vm.Session!.Seed);
        Assert.True(_h.Vm.Session.Origin!.ForceGenerated);
    }

    [Fact]
    public async Task PuzzleId_PlaysThatExactPicture()
    {
        _h.PuzzleRepository.PuzzlesList.Add(
            Puzzle.FromRows("cat", "animals", "#4FA8F5", ["##", "#."]));

        _h.Vm.ApplyQueryAttributes(new Dictionary<string, object> { ["puzzleId"] = "cat" });
        await _h.Vm.InitialiseAsync();

        Assert.Equal("cat", _h.Vm.Session!.Puzzle.Id);
        Assert.Equal("Puzzle_cat", _h.Vm.PuzzleName);
    }

    [Fact]
    public async Task StartingAGame_StartsTheSecondTimer()
    {
        await _h.Vm.InitialiseAsync();

        Assert.NotNull(_h.Timers.Latest);
        Assert.True(_h.Timers.Latest!.IsRunning);
    }

    [Fact]
    public async Task TimerTick_AdvancesTheClockText()
    {
        await _h.Vm.InitialiseAsync();

        _h.Timers.Latest!.RaiseTick();

        Assert.Equal("0:01", _h.Vm.ElapsedText);
        Assert.Equal(TimeSpan.FromSeconds(1), _h.ScreenTime.Played);
    }

    [Fact]
    public async Task SuspendAndResumeClock_StopAndRestartTheTimer()
    {
        await _h.Vm.InitialiseAsync();
        var first = _h.Timers.Latest!;

        _h.Vm.SuspendClock();
        Assert.False(first.IsRunning);

        _h.Vm.ResumeClock();
        Assert.True(_h.Timers.Latest!.IsRunning);
    }

    [Fact]
    public async Task WrongFill_CountsAMistake_AndShowsTheToast()
    {
        await _h.Vm.InitialiseAsync();

        // Cell 1 is not part of the fake generator's picture.
        _h.Vm.Paint(1, CellState.Filled);

        Assert.Equal(1, _h.Vm.Mistakes);
        Assert.Equal("mistakeMsg", _h.Vm.Toast);
        Assert.True(_h.Vm.HasToast);
    }
}
