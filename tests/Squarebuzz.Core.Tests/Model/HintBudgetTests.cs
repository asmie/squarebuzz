using Squarebuzz.Core.Content;
using Squarebuzz.Core.Generation;
using Squarebuzz.Core.Model;
using Xunit;

namespace Squarebuzz.Core.Tests.Model;

public sealed class HintBudgetTests
{
    private static GameSessionFactory Factory() =>
        new(new EmbeddedPuzzleRepository(), new UniqueSolutionGenerator(new BlobPuzzleGenerator()));

    private static GameSession Create(int? limit, ChallengeLevel challenge = ChallengeLevel.Relaxed) =>
        Factory().Create(NewGameOptions.Default with
        {
            Seed = 42,
            Challenge = challenge,
            Helpers = HelperSettings.Default with { HintBudget = new HintBudget(limit) },
        });

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void FiniteBudget_MustBePositive(int limit) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new HintBudget(limit));

    [Theory]
    [InlineData(ChallengeLevel.Relaxed, 1)]
    [InlineData(ChallengeLevel.Relaxed, 7)]
    [InlineData(ChallengeLevel.Relaxed, 25)]
    public void FiniteBudget_ChargesHintsEvenAfterUndo(ChallengeLevel challenge, int limit)
    {
        var session = Create(limit, challenge);
        for (var i = 0; i < limit; i++)
        {
            Assert.NotNull(session.UseHint());
            Assert.True(session.Undo());
        }

        Assert.Equal(limit, session.HintsUsed);
        Assert.Equal(0, session.HintsRemaining);
        Assert.False(session.CanUseHint);
        Assert.Null(session.UseHint());
    }

    [Theory]
    [InlineData(ChallengeLevel.Relaxed)]
    public void UnlimitedHints_StillChargeStarsAndRespectTheHelperSwitch(ChallengeLevel challenge)
    {
        var session = Create(null, challenge);
        for (var i = 0; i < 12; i++)
        {
            Assert.NotNull(session.UseHint());
            Assert.True(session.Undo());
        }

        Assert.Equal(12, session.HintsUsed);
        Assert.True(session.HasUnlimitedHints);
        Assert.True(session.CanUseHint);
        Assert.Equal(2, session.StarRating);
        session.ApplyHelpers(HelperSettings.Default with { AllowHints = false });
        Assert.False(session.CanUseHint);
        Assert.Null(session.UseHint());
        session.ApplyHelpers(HelperSettings.Default);
        Assert.True(session.HasUnlimitedHints);
        Assert.Equal(12, session.HintsUsed);

        for (var i = 0; i < session.Puzzle.CellCount; i++)
        {
            if (session.Puzzle.Solution[i]) session.Paint(i, CellState.Filled);
        }

        Assert.True(session.IsSolved);
        Assert.False(session.CanUseHint);
        Assert.Null(session.UseHint());
    }

    [Theory]
    [InlineData(7)]
    [InlineData(null)]
    public void Sharp_KeepsOneHintRegardlessOfThePreference(int? limit)
    {
        var session = Create(limit, ChallengeLevel.Sharp);
        Assert.Equal(1, session.HintBudget.Limit);
        Assert.NotNull(session.UseHint());
        Assert.False(session.CanUseHint);
        Assert.False(session.HasUnlimitedHints);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(null)]
    public void SaveRestoreAndRestart_KeepBudget_WhileNextUsesNewSettings(int? limit)
    {
        var factory = Factory();
        var session = Create(limit);
        Assert.NotNull(session.UseHint());
        var changed = HelperSettings.Default with { HintBudget = new HintBudget(9) };
        session.ApplyHelpers(changed);
        Assert.Equal(limit, session.HintBudget.Limit);
        Assert.Equal(limit is null, session.CanUseHint);

        var save = SavedGame.FromSession(session, Guid.NewGuid(), DateTimeOffset.UtcNow);
        var resumed = factory.Restore(save, changed);
        Assert.Equal(session.HintBudget, resumed.HintBudget);
        Assert.Equal(1, resumed.HintsUsed);
        Assert.Equal(session.CanUseHint, resumed.CanUseHint);
        var restarted = factory.Create(resumed.Origin!.Restart(changed));
        Assert.Equal(session.HintBudget, restarted.HintBudget);
        Assert.Equal(0, restarted.HintsUsed);
        Assert.True(restarted.CanUseHint);
        var next = factory.Create(resumed.Origin.NextPuzzle(changed));
        Assert.Equal(9, next.HintsRemaining);
        Assert.False(next.HasUnlimitedHints);
    }

    [Theory]
    [InlineData(ChallengeLevel.Relaxed, 3)]
    [InlineData(ChallengeLevel.Sharp, 1)]
    public void LegacySave_KeepsItsOriginalAllowance(ChallengeLevel challenge, int allowance)
    {
        var session = Create(allowance, challenge);
        Assert.NotNull(session.UseHint());
        var save = SavedGame.FromSession(session, Guid.NewGuid(), DateTimeOffset.UtcNow) with { HintBudget = null };
        var restored = Factory().Restore(save, HelperSettings.Default with { HintBudget = HintBudget.Unlimited });
        Assert.Equal(allowance, restored.HintBudget.Limit);
        Assert.Equal(allowance - 1, restored.HintsRemaining);
        Assert.Equal(1, restored.HintsUsed);
        restored.ApplyHelpers(HelperSettings.Default with { HintBudget = new HintBudget(99) });
        Assert.Equal(allowance - 1, restored.HintsRemaining);
    }
}
