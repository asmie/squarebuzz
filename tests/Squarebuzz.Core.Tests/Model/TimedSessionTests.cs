using Squarebuzz.Core.Model;
using Squarebuzz.Core.Progression;
using Xunit;

namespace Squarebuzz.Core.Tests.Model;

/// <summary>
/// The countdown, which is the only way to lose. Everything else in the game either progresses or
/// stays still, so these tests are as much about what must <em>not</em> happen after the clock
/// stops as about the clock itself.
/// </summary>
public class TimedSessionTests
{
    private static Puzzle Plus() =>
        Puzzle.FromRows("plus", "test", "#FF8A3D",
        [
            "..#..",
            "..#..",
            "#####",
            "..#..",
            "..#..",
        ]);

    private static GameSession Timed(TimeSpan limit) =>
        new(Plus(), GameRules.Relaxed, NewGameOptions.Default with { TimeLimit = limit });

    private static GameSession Untimed() => new(Plus(), GameRules.Relaxed, NewGameOptions.Default);

    private static void SolveIt(GameSession session)
    {
        for (var i = 0; i < session.Puzzle.CellCount; i++)
        {
            if (session.Puzzle.Solution[i])
            {
                session.Paint(i, CellState.Filled);
            }
        }
    }

    [Fact]
    public void AnUntimedSession_HasNoClockAndCannotRunOut()
    {
        var session = Untimed();

        Assert.False(session.IsTimed);
        Assert.Null(session.TimeLimit);

        session.Advance(TimeSpan.FromHours(3));

        Assert.False(session.IsTimeUp);
        Assert.False(session.IsOver);
        Assert.Equal(TimeSpan.FromHours(3), session.Elapsed);
    }

    [Fact]
    public void RemainingCountsDownAndStopsAtZero()
    {
        var session = Timed(TimeSpan.FromMinutes(1));

        Assert.Equal(TimeSpan.FromMinutes(1), session.Remaining);

        session.Advance(TimeSpan.FromSeconds(20));
        Assert.Equal(TimeSpan.FromSeconds(40), session.Remaining);

        // Overshooting the limit must not produce negative time left, nor an elapsed time longer
        // than the limit - otherwise a loss would be recorded as taking longer the coarser the tick.
        session.Advance(TimeSpan.FromMinutes(5));
        Assert.Equal(TimeSpan.Zero, session.Remaining);
        Assert.Equal(TimeSpan.FromMinutes(1), session.Elapsed);
    }

    [Fact]
    public void WhenTheClockRunsOut_TheGameIsOverAndLost()
    {
        var session = Timed(TimeSpan.FromSeconds(30));

        session.Advance(TimeSpan.FromSeconds(29));
        Assert.False(session.IsTimeUp);

        session.Advance(TimeSpan.FromSeconds(1));

        Assert.True(session.IsTimeUp);
        Assert.True(session.IsOver);
        Assert.False(session.IsSolved);
    }

    [Fact]
    public void AfterTimeIsUp_NothingCanBePlayed()
    {
        // The view finds out on the next tick, so the session has to refuse input in the meantime
        // rather than let a mark land on a game that is already lost.
        var session = Timed(TimeSpan.FromSeconds(5));
        var index = session.Puzzle.IndexOf(2, 0);

        session.Advance(TimeSpan.FromSeconds(5));

        Assert.Equal(MoveResult.NoChange, session.Paint(index, CellState.Filled).Result);
        Assert.Equal(CellState.Empty, session[index]);
        Assert.Null(session.UseHint());
        Assert.False(session.CanUndo);
        Assert.False(session.Undo());
        Assert.False(session.CanRedo);
    }

    [Fact]
    public void TheClockStopsOnceTheClockHasStopped()
    {
        var session = Timed(TimeSpan.FromSeconds(10));

        session.Advance(TimeSpan.FromSeconds(10));
        session.Advance(TimeSpan.FromSeconds(10));

        Assert.Equal(TimeSpan.FromSeconds(10), session.Elapsed);
    }

    [Fact]
    public void SolvingBeforeTheLimit_IsAWinAndStopsTheClock()
    {
        var session = Timed(TimeSpan.FromMinutes(2));

        session.Advance(TimeSpan.FromSeconds(30));
        SolveIt(session);

        Assert.True(session.IsSolved);
        Assert.False(session.IsTimeUp);
        Assert.True(session.IsOver);

        // Time keeps its value after the win rather than draining away behind the results screen.
        session.Advance(TimeSpan.FromMinutes(5));
        Assert.Equal(TimeSpan.FromSeconds(30), session.Elapsed);
    }

    [Fact]
    public void ZeroRemainingTime_RejectsASolve_AndLaterAdvancesCannotUndoAnEarlierWin()
    {
        var session = Timed(TimeSpan.FromSeconds(10));

        session.Advance(TimeSpan.FromSeconds(10));
        SolveIt(session);
        Assert.True(session.IsTimeUp);
        Assert.False(session.IsSolved);

        var fresh = Timed(TimeSpan.FromSeconds(10));
        SolveIt(fresh);
        fresh.Advance(TimeSpan.FromSeconds(10));

        Assert.True(fresh.IsSolved);
        Assert.False(fresh.IsTimeUp);
    }

    [Theory]
    [InlineData(1, 5, 3, "3:00")]
    [InlineData(2, 10, 5, "5:00")]
    [InlineData(3, 10, 2, "2:00")]
    public void TheLadderMatchesTheDesign(int tier, int size, int minutes, string clock)
    {
        var definition = TimedTrial.Find(tier);

        Assert.NotNull(definition);
        Assert.Equal(size, definition.Size);
        Assert.Equal(TimeSpan.FromMinutes(minutes), definition.Limit);
        Assert.Equal(clock, definition.ClockText);
    }

    [Fact]
    public void ATrialIsGeneratedSharpAndTimed()
    {
        var options = TimedTrial.Tiers[2].ToOptions(HelperSettings.Default);

        Assert.Equal(TimeSpan.FromMinutes(2), options.TimeLimit);
        Assert.Equal(ChallengeLevel.Sharp, options.Challenge);

        // Both trial sizes sit inside the authored range, so without this a trial would hand out
        // one of the twelve shipped pictures - which the player may already know by heart.
        Assert.True(options.ForceGenerated);
        Assert.Null(options.Seed);
    }

    [Fact]
    public void AnUnknownTier_IsNotFound()
    {
        Assert.Null(TimedTrial.Find(0));
        Assert.Null(TimedTrial.Find(4));
    }

    [Fact]
    // A save has no field for a time limit, so restoring one would silently drop the countdown.
    // The rule is enforced where a save is made, not left to every caller to remember.
    public void ATimedSession_CannotBeSaved()
    {
        var session = Timed(TimeSpan.FromMinutes(2));
        session.Tap(0);

        var error = Assert.Throws<ArgumentException>(
            () => SavedGame.FromSession(session, Guid.NewGuid(), DateTimeOffset.UnixEpoch));

        Assert.Equal("session", error.ParamName);
    }

    [Fact]
    public void AnUntimedSession_StillSaves()
    {
        var session = Untimed();
        session.Tap(0);

        var save = SavedGame.FromSession(session, Guid.NewGuid(), DateTimeOffset.UnixEpoch);

        Assert.Equal(session.Puzzle.Width, save.Size);
    }
}
