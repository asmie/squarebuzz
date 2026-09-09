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
    // The clock shows what is *left* in a trial, so describing it as elapsed told a screen-reader
    // player the opposite of what the number meant - and nothing visual gives that away, because
    // the sighted player can see it counting down.
    public async Task Tier_DescribesTheClockAsTimeLeft_NotTimeElapsed()
    {
        _h.Vm.ApplyQueryAttributes(new Dictionary<string, object> { ["tier"] = "3" });
        await _h.Vm.InitialiseAsync();

        _h.Clock.Advance(TimeSpan.FromSeconds(20));
        _h.Timers.Latest!.RaiseTick();

        // A 2:00 trial, 20 seconds in: the clock reads what remains.
        Assert.Equal("1:40", _h.Vm.ElapsedText);
        Assert.Equal($"a11yTimeLeft:{_h.Vm.ElapsedText}", _h.Vm.ElapsedDescription);
    }

    [Fact]
    public async Task AnUntimedGame_StillDescribesTheClockAsTimeElapsed()
    {
        await _h.Vm.InitialiseAsync();

        _h.Clock.Advance(TimeSpan.FromSeconds(20));
        _h.Timers.Latest!.RaiseTick();

        Assert.Equal("0:20", _h.Vm.ElapsedText);
        Assert.Equal($"a11yTime:{_h.Vm.ElapsedText}", _h.Vm.ElapsedDescription);
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

        _h.Clock.Advance(TimeSpan.FromSeconds(1));
        _h.Timers.Latest!.RaiseTick();

        Assert.Equal("0:01", _h.Vm.ElapsedText);
        Assert.Equal(TimeSpan.FromSeconds(1), _h.ScreenTime.Played);
    }

    [Fact]
    // The clock charges the time that really passed, not the timer's nominal second. Under load
    // the dispatcher delivers ticks late or drops them, and counting each as one second ran the
    // game clock slow by exactly the shortfall - free time in a timed trial.
    public async Task LateTick_ChargesTheWholeIntervalItCovers()
    {
        await _h.Vm.InitialiseAsync();

        _h.Clock.Advance(TimeSpan.FromSeconds(3));
        _h.Timers.Latest!.RaiseTick();

        Assert.Equal(TimeSpan.FromSeconds(3), _h.Vm.Session!.Elapsed);
        Assert.Equal("0:03", _h.Vm.ElapsedText);
        Assert.Equal(TimeSpan.FromSeconds(3), _h.ScreenTime.Played);
    }

    [Fact]
    public async Task TickWithNoTimePassed_ChargesNothing()
    {
        await _h.Vm.InitialiseAsync();

        _h.Timers.Latest!.RaiseTick();
        _h.Timers.Latest!.RaiseTick();

        Assert.Equal(TimeSpan.Zero, _h.Vm.Session!.Elapsed);
        Assert.Equal(TimeSpan.Zero, _h.ScreenTime.Played);
    }

    [Fact]
    // The timer keeps running behind the break reminder, so the reference has to move forward
    // while the overlay is up - or the first tick after "A little longer" would charge the whole
    // time the child spent reading it.
    public async Task TimeSpentBehindTheBreakReminder_IsNotChargedOnDismissal()
    {
        await _h.Vm.InitialiseAsync();
        var timer = _h.Timers.Latest!;

        _h.ScreenTime.RemindOnNextAdd = true;
        _h.Clock.Advance(TimeSpan.FromSeconds(1));
        timer.RaiseTick();
        Assert.True(_h.Vm.IsBreakReminderOpen);
        Assert.Equal(TimeSpan.FromSeconds(1), _h.Vm.Session!.Elapsed);

        // Two minutes pass with the reminder up; its ticks must not accumulate.
        _h.Clock.Advance(TimeSpan.FromMinutes(1));
        timer.RaiseTick();
        _h.Clock.Advance(TimeSpan.FromMinutes(1));
        timer.RaiseTick();
        Assert.Equal(TimeSpan.FromSeconds(1), _h.Vm.Session.Elapsed);

        _h.Vm.DismissBreakReminderCommand.Execute(null);

        _h.Clock.Advance(TimeSpan.FromSeconds(1));
        timer.RaiseTick();

        Assert.Equal(TimeSpan.FromSeconds(2), _h.Vm.Session.Elapsed);
    }

    [Fact]
    // The reminder is documented as stopping play. Painting already honoured that; the hint,
    // undo and redo buttons checked only for pause, so hints could still be spent behind it.
    public async Task BreakReminder_BlocksHintsUndoAndRedo()
    {
        await _h.Vm.InitialiseAsync();
        var session = _h.Vm.Session!;

        // A move to undo, then one cell undone so there is also something to redo.
        session.Mode = PaintMode.Fill;
        var target = FirstPictureCell(session);
        _h.Vm.Paint(target, CellState.Filled);
        var moves = session.MoveCount;

        _h.ScreenTime.RemindOnNextAdd = true;
        _h.Clock.Advance(TimeSpan.FromSeconds(1));
        _h.Timers.Latest!.RaiseTick();
        Assert.True(_h.Vm.IsBreakReminderOpen);

        var hintsBefore = session.HintsRemaining;

        _h.Vm.UseHintCommand.Execute(null);
        _h.Vm.UndoCommand.Execute(null);
        _h.Vm.RedoCommand.Execute(null);

        Assert.Equal(hintsBefore, session.HintsRemaining);
        Assert.Equal(moves, session.MoveCount);

        _h.Vm.DismissBreakReminderCommand.Execute(null);
        _h.Vm.UndoCommand.Execute(null);

        Assert.Equal(moves - 1, session.MoveCount);
    }

    private static int FirstPictureCell(GameSession session)
    {
        for (var i = 0; i < session.Puzzle.CellCount; i++)
        {
            if (session.Puzzle.Solution[i])
            {
                return i;
            }
        }

        throw new InvalidOperationException("The fake puzzle has no filled cell.");
    }

    [Fact]
    public async Task ResumingTheClock_DoesNotChargeTheTimeItWasSuspended()
    {
        await _h.Vm.InitialiseAsync();

        _h.Clock.Advance(TimeSpan.FromSeconds(1));
        _h.Timers.Latest!.RaiseTick();

        _h.Vm.SuspendClock();
        _h.Clock.Advance(TimeSpan.FromMinutes(10));
        _h.Vm.ResumeClock();

        _h.Clock.Advance(TimeSpan.FromSeconds(1));
        _h.Timers.Latest!.RaiseTick();

        Assert.Equal(TimeSpan.FromSeconds(2), _h.Vm.Session!.Elapsed);
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
