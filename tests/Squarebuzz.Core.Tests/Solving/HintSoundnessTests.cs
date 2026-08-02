using Squarebuzz.Core.Generation;
using Squarebuzz.Core.Model;
using Squarebuzz.Core.Solving;
using Xunit;

namespace Squarebuzz.Core.Tests.Solving;

/// <summary>
/// A hint must never contradict the picture.
/// </summary>
/// <remarks>
/// Every tier deduces from the board as the player left it, so a mark they got wrong is taken as
/// fact and the "forced" answer that follows can be wrong too. That matters more than it sounds:
/// the hint is written straight to the board, it costs one of a very small budget, and winning
/// requires an exact match - so one bad hint locks the child out of ever finishing the puzzle.
/// Wrong crosses are never refused, which is what makes this reachable in ordinary play.
/// </remarks>
public class HintSoundnessTests
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

    [Fact]
    public void AHintOnAnUntouchedBoard_AgreesWithThePicture()
    {
        var puzzle = Plus();
        var board = new CellState[puzzle.CellCount];

        var hint = HintProvider.Find(puzzle, board);

        Assert.NotNull(hint);
        Assert.Equal(puzzle.ExpectedState(hint.Index), hint.Value);
    }

    [Fact]
    public void AWrongCross_DoesNotMakeTheHintWrong()
    {
        // Crossing a cell that is part of the picture is always accepted by the game, so the
        // line solver can be handed a false premise on any board.
        var puzzle = Plus();

        for (var wrong = 0; wrong < puzzle.CellCount; wrong++)
        {
            if (!puzzle.Solution[wrong])
            {
                continue;
            }

            var board = new CellState[puzzle.CellCount];
            board[wrong] = CellState.Crossed;

            var hint = HintProvider.Find(puzzle, board);

            if (hint is null)
            {
                continue;
            }

            Assert.Equal(puzzle.ExpectedState(hint.Index), hint.Value);
        }
    }

    [Fact]
    public void AWrongFill_DoesNotMakeTheHintWrong()
    {
        // Reachable whenever "warn on mistakes" is off, which is a supported setting.
        var puzzle = Plus();

        for (var wrong = 0; wrong < puzzle.CellCount; wrong++)
        {
            if (puzzle.Solution[wrong])
            {
                continue;
            }

            var board = new CellState[puzzle.CellCount];
            board[wrong] = CellState.Filled;

            var hint = HintProvider.Find(puzzle, board);

            if (hint is null)
            {
                continue;
            }

            Assert.Equal(puzzle.ExpectedState(hint.Index), hint.Value);
        }
    }

    [Fact]
    public void AcrossManyGeneratedBoardsAndWrongMarks_NoHintEverContradictsThePicture()
    {
        // The regression this guards against showed up in roughly 1 board in 30 by measurement,
        // so a handful of hand-written cases would not have caught it.
        var generator = new UniqueSolutionGenerator(new BlobPuzzleGenerator());
        var random = new Random(4242);
        var checkedHints = 0;

        for (var attempt = 0; attempt < 25; attempt++)
        {
            var puzzle = generator.Generate(new PuzzleRequest(10, 10, 3, "surprise", 500 + attempt));

            for (var trial = 0; trial < 20; trial++)
            {
                var board = new CellState[puzzle.CellCount];

                // A partly-played board, plus one deliberate mistake.
                for (var i = 0; i < puzzle.CellCount; i++)
                {
                    if (random.Next(4) == 0)
                    {
                        board[i] = puzzle.ExpectedState(i);
                    }
                }

                var wrong = random.Next(puzzle.CellCount);
                board[wrong] = puzzle.Solution[wrong] ? CellState.Crossed : CellState.Filled;

                var hint = HintProvider.Find(puzzle, board);

                if (hint is null)
                {
                    continue;
                }

                checkedHints++;

                Assert.Equal(CellState.Empty, board[hint.Index]);
                Assert.Equal(puzzle.ExpectedState(hint.Index), hint.Value);
            }
        }

        Assert.True(checkedHints > 100, $"Expected the sweep to exercise plenty of hints, got {checkedHints}.");
    }

    [Fact]
    public void UsingAHintOnAMisplayedBoard_LeavesTheBoardStillWinnable()
    {
        // The end-to-end shape of the bug: spend a hint on a board with a wrong cross, then
        // correct the mistake and finish. A hint that disagreed would make this unwinnable.
        var rules = GameRules.Create(ChallengeLevel.Relaxed, HelperSettings.Default);
        var session = new GameSession(Plus(), rules);

        var partOfPicture = session.Puzzle.IndexOf(2, 0);
        session.Mode = PaintMode.Cross;
        session.Paint(partOfPicture, CellState.Crossed);

        Assert.NotNull(session.UseHint());

        // Undo the child's own mistake, then complete the picture.
        session.Paint(partOfPicture, CellState.Empty);

        for (var i = 0; i < session.Puzzle.CellCount; i++)
        {
            if (session.Puzzle.Solution[i] && session[i] != CellState.Filled)
            {
                session.Paint(i, CellState.Filled);
            }
            else if (!session.Puzzle.Solution[i] && session[i] == CellState.Filled)
            {
                session.Paint(i, CellState.Empty);
            }
        }

        Assert.True(session.IsSolved);
    }
}
