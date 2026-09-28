using Squarebuzz.Core.Model;
using Squarebuzz.Core.Solving;
using Xunit;

namespace Squarebuzz.Core.Tests.Model;

/// <summary>
/// How mistakes and hints are charged: once per slip rather than once per square, and never
/// for squares the drag was not aimed at.
/// </summary>
public class StrokeCountingTests
{
    /// <summary>Top row is "#...#": three blanks between two filled squares.</summary>
    private static Puzzle Gapped() =>
        Puzzle.FromRows("gapped", "test", "#FF8A3D",
        [
            "#...#",
            "#####",
            "#...#",
            "#####",
            "#...#",
        ]);

    private static GameSession NewSession(GameRules? rules = null) =>
        new(Gapped(), rules ?? GameRules.Relaxed);

    [Fact]
    public void AFillDraggedAcrossCrossedGaps_SkipsThemWithoutAMistake()
    {
        var session = NewSession();
        session.Paint(1, CellState.Crossed);
        session.Paint(2, CellState.Crossed);
        session.Paint(3, CellState.Crossed);

        // One drag along the whole top row, starting on the empty first square.
        for (var x = 0; x < 5; x++)
        {
            session.Paint(x, CellState.Filled, continuesStroke: x > 0);
        }

        Assert.Equal(0, session.Mistakes);
        Assert.Equal(CellState.Filled, session[0]);
        Assert.Equal(CellState.Crossed, session[2]);
        Assert.Equal(CellState.Filled, session[4]);
    }

    [Fact]
    public void ADragOverSeveralWrongSquares_IsChargedOneMistake()
    {
        var session = NewSession();

        var outcomes = Enumerable.Range(0, 5)
            .Select(x => session.Paint(x, CellState.Filled, continuesStroke: x > 0).Result)
            .ToArray();

        Assert.Equal(1, session.Mistakes);

        // Every wrong square still reports the mistake, so the board shakes where it happened.
        Assert.Equal(MoveResult.Mistake, outcomes[1]);
        Assert.Equal(MoveResult.Mistake, outcomes[3]);
    }

    [Fact]
    public void SeparateTaps_AreEachCharged()
    {
        var session = NewSession();

        session.Paint(1, CellState.Filled);
        session.Paint(2, CellState.Filled);

        Assert.Equal(2, session.Mistakes);
    }

    [Fact]
    public void AnEraseDrag_LeavesCrossesAlone()
    {
        var session = NewSession();
        session.Paint(0, CellState.Filled);
        session.Paint(1, CellState.Crossed);
        session.Paint(4, CellState.Filled);

        // Starting on a filled square, the drag clears fills only.
        session.Paint(0, CellState.Empty);
        session.Paint(1, CellState.Empty, continuesStroke: true);
        session.Paint(4, CellState.Empty, continuesStroke: true);

        Assert.Equal(CellState.Empty, session[0]);
        Assert.Equal(CellState.Crossed, session[1]);
        Assert.Equal(CellState.Empty, session[4]);
    }

    [Fact]
    public void Restore_GivesBackHintsSwitchedOnSinceTheSave()
    {
        // Saved while hints were off, so the save recorded none remaining and one spent earlier.
        var session = NewSession();

        session.Restore(new CellState[25], TimeSpan.Zero, hintsRemaining: 0, hintsUsed: 1, mistakes: 0);

        Assert.Equal(HintBudget.Default.Limit - 1, session.HintsRemaining);
    }

    [Fact]
    public void Restore_NeverMintsHintsBeyondTheBudget()
    {
        var session = NewSession();

        session.Restore(new CellState[25], TimeSpan.Zero, hintsRemaining: 99, hintsUsed: 2, mistakes: 0);

        Assert.Equal(HintBudget.Default.Limit - 2, session.HintsRemaining);
    }

    [Fact]
    public void AHint_CorrectsAWrongCrossOnceNothingElseIsLeft()
    {
        var session = NewSession(GameRules.Create(ChallengeLevel.Relaxed, HelperSettings.Default, HintBudget.Unlimited));
        var cells = Gapped().Solution.ToArray()
            .Select(filled => filled ? CellState.Filled : CellState.Crossed)
            .ToArray();

        // Every square marked, one of them wrongly: the picture can never be finished as it is.
        cells[0] = CellState.Crossed;
        session.Restore(cells, TimeSpan.Zero, 0, 0, 0);

        var hint = session.UseHint();

        Assert.NotNull(hint);
        Assert.Equal(0, hint.Index);
        Assert.Equal(CellState.Filled, hint.Value);
        Assert.True(session.IsSolved);
    }
}
