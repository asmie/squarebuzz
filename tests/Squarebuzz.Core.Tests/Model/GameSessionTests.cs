using Squarebuzz.Core.Model;
using Squarebuzz.Core.Solving;
using Xunit;

namespace Squarebuzz.Core.Tests.Model;

public class GameSessionTests
{
    /// <summary>
    /// A plus sign. Small enough to reason about by hand, and its middle row and column are
    /// full, which makes auto-crossing easy to trigger deliberately.
    /// </summary>
    private static Puzzle Plus() =>
        Puzzle.FromRows("plus", "test", "#FF8A3D",
        [
            "..#..",
            "..#..",
            "#####",
            "..#..",
            "..#..",
        ]);

    private static GameSession NewSession(GameRules? rules = null) =>
        new(Plus(), rules ?? GameRules.Relaxed);

    private static void FillEntireSolution(GameSession session)
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
    public void NewSession_StartsEmptyWithFullHintAllowance()
    {
        var session = NewSession();

        Assert.All(session.Cells.ToArray(), c => Assert.Equal(CellState.Empty, c));
        Assert.Equal(3, session.HintsRemaining);
        Assert.Equal(0, session.HintsUsed);
        Assert.Equal(0, session.Mistakes);
        Assert.False(session.IsSolved);
        Assert.False(session.CanUndo);
    }

    [Fact]
    public void FilledCount_CountsOnlyFilledSquares()
    {
        // This is what a screen reader is told about the board, so it has to mean "progress
        // towards the picture" - crossing squares out is not progress, and counting them would
        // report a board as nearly done when nothing had been filled in at all.
        var session = NewSession();

        Assert.Equal(0, session.FilledCount);

        session.Paint(session.Puzzle.IndexOf(2, 0), CellState.Filled);
        session.Paint(session.Puzzle.IndexOf(2, 1), CellState.Filled);
        Assert.Equal(2, session.FilledCount);

        session.Paint(session.Puzzle.IndexOf(0, 0), CellState.Crossed);
        Assert.Equal(2, session.FilledCount);

        session.Paint(session.Puzzle.IndexOf(2, 1), CellState.Empty);
        Assert.Equal(1, session.FilledCount);
    }

    [Fact]
    public void FilledCount_OnASolvedBoard_EqualsThePictureSize()
    {
        var session = NewSession();
        FillEntireSolution(session);

        var pictureSize = 0;

        for (var i = 0; i < session.Puzzle.CellCount; i++)
        {
            if (session.Puzzle.Solution[i])
            {
                pictureSize++;
            }
        }

        Assert.True(session.IsSolved);
        Assert.Equal(pictureSize, session.FilledCount);
    }

    [Fact]
    public void Tap_InFillMode_TogglesFilled()
    {
        var session = NewSession();
        var index = session.Puzzle.IndexOf(2, 0); // Part of the picture.

        Assert.Equal(MoveResult.Applied, session.Tap(index).Result);
        Assert.Equal(CellState.Filled, session[index]);

        Assert.Equal(MoveResult.Applied, session.Tap(index).Result);
        Assert.Equal(CellState.Empty, session[index]);
    }

    [Fact]
    public void Tap_InCrossMode_TogglesCrossed()
    {
        var session = NewSession();
        session.Mode = PaintMode.Cross;
        var index = session.Puzzle.IndexOf(0, 0); // Not part of the picture.

        session.Tap(index);
        Assert.Equal(CellState.Crossed, session[index]);

        session.Tap(index);
        Assert.Equal(CellState.Empty, session[index]);
    }

    [Fact]
    public void FillingAWrongCell_IsRefusedAndCounted()
    {
        var session = NewSession();
        var wrong = session.Puzzle.IndexOf(0, 0); // Blank in the picture.

        var outcome = session.Paint(wrong, CellState.Filled);

        Assert.Equal(MoveResult.Mistake, outcome.Result);
        Assert.Equal(1, session.Mistakes);

        // The board must not be left holding something the clues contradict.
        Assert.Equal(CellState.Empty, session[wrong]);
        Assert.False(session.CanUndo);
    }

    [Fact]
    public void WithWarningsOff_AWrongFillIsAllowedToStand()
    {
        var rules = GameRules.Create(
            ChallengeLevel.Relaxed,
            HelperSettings.Default with { WarnOnMistakes = false, AutoCross = false });

        var session = new GameSession(Plus(), rules);
        var wrong = session.Puzzle.IndexOf(0, 0);

        var outcome = session.Paint(wrong, CellState.Filled);

        Assert.Equal(MoveResult.Applied, outcome.Result);
        Assert.Equal(CellState.Filled, session[wrong]);
        Assert.Equal(0, session.Mistakes);
    }

    [Fact]
    public void WithWarningsOff_AWrongFillBlocksTheWin_UntilItIsCleared()
    {
        var rules = GameRules.Create(
            ChallengeLevel.Relaxed,
            HelperSettings.Default with { WarnOnMistakes = false, AutoCross = false });

        var session = new GameSession(Plus(), rules);
        var wrong = session.Puzzle.IndexOf(0, 0);

        session.Paint(wrong, CellState.Filled);
        FillEntireSolution(session);

        // The picture is all there, but so is a square that is not part of it.
        Assert.False(session.IsSolved);

        var outcome = session.Paint(wrong, CellState.Empty);

        Assert.True(outcome.SolvedPuzzle);
        Assert.True(session.IsSolved);
    }

    [Fact]
    public void WithWarningsOff_PaintingTheWholeGrid_IsNotAWin()
    {
        // Without this, turning off "Warn on mistakes" and dragging over everything would earn
        // three stars - the win has to mean the picture, not a full board.
        var rules = GameRules.Create(
            ChallengeLevel.Relaxed,
            HelperSettings.Default with { WarnOnMistakes = false, AutoCross = false });

        var session = new GameSession(Plus(), rules);

        for (var i = 0; i < session.Puzzle.CellCount; i++)
        {
            session.Paint(i, CellState.Filled);
        }

        Assert.False(session.IsSolved);
        Assert.Equal(session.Puzzle.CellCount, session.FilledCount);
    }

    [Fact]
    public void CompletingALine_WithAutoCrossOff_StillReportsTheCompletion()
    {
        // A player can switch auto-crossing off. The "line done" chime must not go with it -
        // the line is no less done.
        var rules = GameRules.Create(
            ChallengeLevel.Relaxed,
            HelperSettings.Default with { AutoCross = false });

        var session = new GameSession(Plus(), rules);

        var outcome = session.Paint(session.Puzzle.IndexOf(2, 0), CellState.Filled);

        Assert.True(outcome.CompletedALine);
        Assert.Equal(0, outcome.AutoCrossedCells);
    }

    [Fact]
    public void CompletingALine_WhoseBlanksWereCrossedByHand_StillReportsTheCompletion()
    {
        // A tidy player who crosses the blanks as they go leaves auto-cross nothing to do.
        // That must not silence the completion.
        var session = NewSession();

        session.Paint(session.Puzzle.IndexOf(0, 0), CellState.Crossed);
        session.Paint(session.Puzzle.IndexOf(1, 0), CellState.Crossed);
        session.Paint(session.Puzzle.IndexOf(3, 0), CellState.Crossed);
        session.Paint(session.Puzzle.IndexOf(4, 0), CellState.Crossed);

        var outcome = session.Paint(session.Puzzle.IndexOf(2, 0), CellState.Filled);

        Assert.True(outcome.CompletedALine);
        Assert.Equal(0, outcome.AutoCrossedCells);
    }

    [Fact]
    public void MarkingInsideAnAlreadyCompleteLine_IsNotReportedAsACompletion()
    {
        var rules = GameRules.Create(
            ChallengeLevel.Relaxed,
            HelperSettings.Default with { AutoCross = false });

        var session = new GameSession(Plus(), rules);
        session.Paint(session.Puzzle.IndexOf(2, 0), CellState.Filled);

        // Row 0 is already accounted for; bookkeeping in it is not news.
        var outcome = session.Paint(session.Puzzle.IndexOf(0, 0), CellState.Crossed);

        Assert.False(outcome.CompletedALine);
    }

    [Fact]
    public void ErasingTheFillThatCompletedALine_IsNotReportedAsACompletion()
    {
        var rules = GameRules.Create(
            ChallengeLevel.Relaxed,
            HelperSettings.Default with { AutoCross = false });

        var session = new GameSession(Plus(), rules);
        var index = session.Puzzle.IndexOf(2, 0);
        session.Paint(index, CellState.Filled);

        var outcome = session.Paint(index, CellState.Empty);

        Assert.False(outcome.CompletedALine);
    }

    [Fact]
    public void ApplyHelpers_TurnsAutoCrossingOnForSubsequentMoves()
    {
        // The pause overlay links to Options, so a helper can change mid-game. The session's
        // rules have to follow, or the switch silently does nothing until the next puzzle.
        var off = GameRules.Create(
            ChallengeLevel.Relaxed,
            HelperSettings.Default with { AutoCross = false });

        var session = new GameSession(
            Plus(),
            off,
            new NewGameOptions(GridSize.Tiny, 2, "test", ChallengeLevel.Relaxed));

        var before = session.Paint(session.Puzzle.IndexOf(2, 0), CellState.Filled);
        Assert.Equal(0, before.AutoCrossedCells);

        session.ApplyHelpers(HelperSettings.Default);

        // Row 4 is "..#..": filling its single cell completes it and must now auto-cross.
        var after = session.Paint(session.Puzzle.IndexOf(2, 4), CellState.Filled);

        Assert.True(after.AutoCrossedCells > 0);
        Assert.Equal(CellState.Crossed, session.At(0, 4));
    }

    [Fact]
    public void ApplyHelpers_KeepsTheChallengeButFollowsTheHelper()
    {
        var rules = GameRules.Create(
            ChallengeLevel.Sharp,
            HelperSettings.Default with { AutoCross = false });

        var session = new GameSession(
            Plus(),
            rules,
            new NewGameOptions(GridSize.Tiny, 2, "test", ChallengeLevel.Sharp));

        session.ApplyHelpers(HelperSettings.Default with { AutoCross = true });

        // The switch is obeyed even here; the challenge keeps its single hint.
        Assert.True(session.Rules.AutoCrossCompletedLines);
        Assert.Equal(1, session.Rules.HintAllowance);
    }

    [Fact]
    public void ApplyHelpers_CannotMintFreshHints()
    {
        var session = new GameSession(
            Plus(),
            GameRules.Relaxed,
            new NewGameOptions(GridSize.Tiny, 2, "test", ChallengeLevel.Relaxed));

        Assert.NotNull(session.UseHint());
        Assert.Equal(1, session.HintsUsed);

        session.ApplyHelpers(HelperSettings.Default with { AllowHints = false });
        Assert.Equal(0, session.HintsRemaining);

        // Back on: the spent hint stays spent.
        session.ApplyHelpers(HelperSettings.Default);

        Assert.Equal(2, session.HintsRemaining);
        Assert.Equal(1, session.HintsUsed);
    }

    [Fact]
    public void Restore_KeepsHintsSpent_EvenWhenTheAllowanceHasChanged()
    {
        // A save records hints spent, not just hints left. Re-deriving them from the current
        // allowance hands spent hints back the moment the allowance differs from the one the
        // game was saved under - which turning hints off in Options is enough to do - restoring
        // a star and the "no hints" trophy with them.
        var origin = new NewGameOptions(GridSize.Tiny, 2, "test", ChallengeLevel.Relaxed);

        var noHints = GameRules.Create(
            ChallengeLevel.Relaxed,
            HelperSettings.Default with { AllowHints = false });

        var session = new GameSession(Plus(), noHints, origin);

        // Saved under the 3-hint allowance with 2 spent; resumed with hints switched off.
        session.Restore([.. new CellState[25]], TimeSpan.FromMinutes(1), hintsRemaining: 1, hintsUsed: 2, mistakes: 0);

        Assert.Equal(2, session.HintsUsed);
        Assert.Equal(0, session.HintsRemaining);

        // Two hints cost a star; the old derivation reported none used and returned all three.
        Assert.Equal(2, session.StarRating);
    }

    [Fact]
    public void CompletingARow_AutoCrossesTheRest()
    {
        var session = NewSession();

        // Row 0 is "..#..", clue 1. Filling the single cell completes it.
        var outcome = session.Paint(session.Puzzle.IndexOf(2, 0), CellState.Filled);

        Assert.True(outcome.CompletedALine);

        // The four blanks in row 0 must now be crossed.
        Assert.Equal(CellState.Crossed, session.At(0, 0));
        Assert.Equal(CellState.Crossed, session.At(1, 0));
        Assert.Equal(CellState.Crossed, session.At(3, 0));
        Assert.Equal(CellState.Crossed, session.At(4, 0));
    }

    [Fact]
    public void UndoingAnAutoCrossingMove_TakesBackEveryAutomaticCross()
    {
        var session = NewSession();
        var index = session.Puzzle.IndexOf(2, 0);

        var outcome = session.Paint(index, CellState.Filled);
        Assert.True(outcome.AutoCrossedCells > 0);

        Assert.True(session.Undo());

        // This is the prototype's bug: it applied auto-crosses outside its history, so undo
        // left them stranded. Every cell of row 0 must be back to empty.
        for (var x = 0; x < session.Puzzle.Width; x++)
        {
            Assert.Equal(CellState.Empty, session.At(x, 0));
        }

        Assert.False(session.CanUndo);
        Assert.True(session.CanRedo);
    }

    [Fact]
    public void RedoingAnAutoCrossingMove_RestoresIt()
    {
        var session = NewSession();
        var index = session.Puzzle.IndexOf(2, 0);

        session.Paint(index, CellState.Filled);
        session.Undo();
        Assert.True(session.Redo());

        Assert.Equal(CellState.Filled, session[index]);
        Assert.Equal(CellState.Crossed, session.At(0, 0));
    }

    [Fact]
    public void WithAutoCrossOff_CompletingARowChangesNothingElse()
    {
        var rules = GameRules.Create(
            ChallengeLevel.Relaxed,
            HelperSettings.Default with { AutoCross = false });

        var session = new GameSession(Plus(), rules);

        var outcome = session.Paint(session.Puzzle.IndexOf(2, 0), CellState.Filled);

        Assert.Equal(0, outcome.AutoCrossedCells);
        Assert.Equal(CellState.Empty, session.At(0, 0));
    }

    [Fact]
    public void SharpChallenge_AllowsOnlyOneHint()
    {
        var rules = GameRules.Sharp;

        Assert.Equal(1, rules.HintAllowance);
    }

    [Fact]
    public void SharpChallenge_StillObeysTheAutoCrossSwitch()
    {
        // Auto-crossing is bookkeeping, not help: it only marks blanks a finished clue has
        // already proved empty. Sharp is about counted mistakes and a single hint, so it has
        // no business overriding the player's switch in either direction.
        Assert.True(GameRules.Sharp.AutoCrossCompletedLines);

        var off = GameRules.Create(
            ChallengeLevel.Sharp,
            HelperSettings.Default with { AutoCross = false });

        Assert.False(off.AutoCrossCompletedLines);
    }

    [Fact]
    public void SharpChallenge_AutoCrossesACompletedLine()
    {
        var session = new GameSession(Plus(), GameRules.Sharp);

        // Row 0 is "..#..", clue 1: filling its single cell completes it.
        var outcome = session.Paint(session.Puzzle.IndexOf(2, 0), CellState.Filled);

        Assert.True(outcome.AutoCrossedCells > 0);
        Assert.Equal(CellState.Crossed, session.At(0, 0));
    }

    [Fact]
    public void DisablingHints_LeavesNoneAvailable()
    {
        var rules = GameRules.Create(ChallengeLevel.Relaxed, HelperSettings.Default with { AllowHints = false });
        var session = new GameSession(Plus(), rules);

        Assert.Equal(0, session.HintsRemaining);
        Assert.Null(session.UseHint());
    }

    [Fact]
    public void FillingEveryPictureCell_WinsWithoutNeedingCrosses()
    {
        var session = NewSession();

        FillEntireSolution(session);

        Assert.True(session.IsSolved);
    }

    [Fact]
    public void AfterSolving_TheBoardIsFrozen()
    {
        var session = NewSession();
        FillEntireSolution(session);

        Assert.Equal(MoveResult.NoChange, session.Tap(0).Result);
        Assert.False(session.Undo());
        Assert.False(session.CanUndo);
    }

    [Fact]
    public void PaintingTheValueACellAlreadyHolds_IsANoOp()
    {
        var session = NewSession();
        var index = session.Puzzle.IndexOf(2, 0);
        session.Paint(index, CellState.Filled);

        var outcome = session.Paint(index, CellState.Filled);

        Assert.Equal(MoveResult.NoChange, outcome.Result);
    }

    [Theory]
    [InlineData(0, 0, 3)] // Clean run.
    [InlineData(2, 0, 3)] // Two mistakes is still forgiven.
    [InlineData(3, 0, 2)] // More than two costs a star.
    [InlineData(0, 2, 2)] // More than one hint costs a star.
    [InlineData(5, 3, 1)] // Both penalties, floored at one.
    public void StarRating_FollowsTheMistakeAndHintPenalties(int mistakes, int hintsUsed, int expectedStars)
    {
        var session = NewSession();

        // Drive the counters through the public surface rather than reaching into state.
        var wrong = session.Puzzle.IndexOf(0, 0);
        for (var i = 0; i < mistakes; i++)
        {
            session.Paint(wrong, CellState.Filled);
        }

        for (var i = 0; i < hintsUsed; i++)
        {
            session.UseHint();
        }

        Assert.Equal(mistakes, session.Mistakes);
        Assert.Equal(hintsUsed, session.HintsUsed);
        Assert.Equal(expectedStars, session.StarRating);
    }

    [Fact]
    public void UsingAHint_SpendsItAndMarksTheCell()
    {
        var session = NewSession();

        var hint = session.UseHint();

        Assert.NotNull(hint);
        Assert.Equal(2, session.HintsRemaining);
        Assert.Equal(1, session.HintsUsed);
        Assert.Equal(hint.Value, session[hint.Index]);
    }

    [Fact]
    public void HintsRunOut()
    {
        var session = NewSession();

        Assert.NotNull(session.UseHint());
        Assert.NotNull(session.UseHint());
        Assert.NotNull(session.UseHint());

        Assert.Equal(0, session.HintsRemaining);
        Assert.Null(session.UseHint());
    }

    [Fact]
    public void AHintIsUndoable_LikeAnyOtherMove()
    {
        var session = NewSession();
        var hint = session.UseHint();
        Assert.NotNull(hint);

        Assert.True(session.Undo());
        Assert.Equal(CellState.Empty, session[hint.Index]);

        // The hint itself stays spent: undo rewinds the board, not the cost of asking.
        Assert.Equal(2, session.HintsRemaining);
    }

    [Fact]
    public void Timer_OnlyRunsUntilThePuzzleIsSolved()
    {
        var session = NewSession();

        session.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(TimeSpan.FromSeconds(5), session.Elapsed);

        FillEntireSolution(session);
        session.Advance(TimeSpan.FromSeconds(30));

        Assert.Equal(TimeSpan.FromSeconds(5), session.Elapsed);
    }

    [Fact]
    public void ClueStrikes_MarkARowOnceItsRunIsPinnedDown()
    {
        var session = NewSession();

        Assert.All(session.RowClueStrikes(0), struck => Assert.False(struck));

        // Filling row 0's single cell completes the row and auto-crosses the rest.
        session.Paint(session.Puzzle.IndexOf(2, 0), CellState.Filled);

        Assert.All(session.RowClueStrikes(0), Assert.True);
    }

    [Fact]
    public void EveryHintOnASolvablePuzzle_IsLogicallyDeducible()
    {
        // Never a bare SolutionReveal on content that solves by logic - that would mean the
        // game was teaching nothing at the moment the child asked for help.
        var session = NewSession();

        while (session.HintsRemaining > 0)
        {
            var hint = session.UseHint();
            Assert.NotNull(hint);
            Assert.NotEqual(HintSource.SolutionReveal, hint.Source);
            Assert.Equal(session.Puzzle.ExpectedState(hint.Index), hint.Value);
        }
    }
}
