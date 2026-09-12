using Squarebuzz.Core.Generation;
using Squarebuzz.Core.Model;
using Squarebuzz.Core.Progression;
using Xunit;

namespace Squarebuzz.Core.Tests.Model;

public sealed class SessionOriginTests
{
    private static Puzzle Picture(bool generated = false) =>
        Puzzle.FromRows("resolved", "animals", "#000000", ["#.", ".#"], generated);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Identity_UsesResolvedPictureAndSeed(bool generated)
    {
        var origin = new SessionOrigin(Picture(generated), NewGameOptions.Default with { PuzzleId = "missing" }, 42);
        Assert.Equal(SessionMode.QuickGame, origin.Mode);
        Assert.Equal(generated ? null : "resolved", origin.PuzzleId);
        Assert.Equal(generated ? GeneratorVersion.Current : GeneratorVersion.Unknown, origin.GeneratorVersion);
        Assert.Equal(42, origin.Seed);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void ConflictingChallengeModes_AreRejected(int combination)
    {
        var options = NewGameOptions.Default with
        {
            Level = combination == 0 ? null : 1,
            DailyDate = combination == 1 ? null : new DateOnly(2026, 9, 10),
            TimeLimit = combination == 2 ? null : TimeSpan.FromMinutes(2),
        };
        Assert.Throws<ArgumentException>(() => new SessionOrigin(Picture(), options, 42));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CampaignRestart_PinsTheResolvedPicture(bool generated)
    {
        var origin = new SessionOrigin(Picture(generated), NewGameOptions.Default with { Level = 7 }, 42);
        var replay = origin.Restart(HelperSettings.Default);
        Assert.Equal(SessionMode.Campaign, origin.Mode);
        Assert.Equal(7, replay.Level);
        Assert.Equal(42, replay.Seed);
        Assert.Equal(origin.PuzzleId, replay.PuzzleId);
        Assert.Equal(generated, replay.ForceGenerated);
    }

    [Fact]
    public void DailyRestart_PreservesDate_WhileNextLeavesDailyMode()
    {
        var date = new DateOnly(2026, 9, 10);
        var options = DailyPuzzle.OptionsFor(date, HelperSettings.Default);
        var origin = new SessionOrigin(Picture(true), options, options.Seed!.Value);
        Assert.Equal(SessionMode.Daily, origin.Mode);
        Assert.Equal(options, origin.Restart(HelperSettings.Default));
        var next = origin.NextPuzzle(HelperSettings.Default);
        Assert.Null(next.DailyDate);
        Assert.Null(next.Seed);
    }

    [Fact]
    public void NextTimedPuzzle_KeepsLimitAndDrawsAfresh()
    {
        var limit = TimeSpan.FromMinutes(2);
        var origin = new SessionOrigin(Picture(true), NewGameOptions.Default with { TimeLimit = limit, ForceGenerated = true }, 42);
        var replay = origin.NextPuzzle(HelperSettings.Default);
        Assert.Equal(SessionMode.TimedTrial, origin.Mode);
        Assert.Equal(limit, replay.TimeLimit);
        Assert.Null(replay.Seed);
        Assert.Null(replay.PuzzleId);
        Assert.True(replay.ForceGenerated);
    }

    [Fact]
    public void NextQuickPuzzle_AvoidsTheResolvedAuthoredPicture()
    {
        var origin = new SessionOrigin(Picture(), NewGameOptions.Default, 42);
        var replay = origin.NextPuzzle(HelperSettings.Default);
        Assert.Null(replay.Seed);
        Assert.Null(replay.PuzzleId);
        Assert.Equal("resolved", replay.ExcludePuzzleId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Restart_PinsTheResolvedPictureInsteadOfTheOriginalSelection(bool generated)
    {
        var origin = new SessionOrigin(Picture(generated), NewGameOptions.Default with
        {
            PuzzleId = "missing",
            ExcludePuzzleId = "previous",
        }, 42);
        var helpers = HelperSettings.Default with { AutoCross = false };

        var restart = origin.Restart(helpers);

        Assert.Equal(42, restart.Seed);
        Assert.Equal(generated ? null : "resolved", restart.PuzzleId);
        Assert.Equal(generated, restart.ForceGenerated);
        Assert.Null(restart.ExcludePuzzleId);
        Assert.Equal(helpers, restart.Helpers);
    }
}
