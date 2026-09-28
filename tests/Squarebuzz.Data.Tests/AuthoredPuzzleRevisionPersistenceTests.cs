using Squarebuzz.Core.Content;
using Squarebuzz.Core.Generation;
using Squarebuzz.Core.Model;
using Squarebuzz.Data.Migrations;
using Squarebuzz.Data.Repositories;
using Xunit;

namespace Squarebuzz.Data.Tests;

public class AuthoredPuzzleRevisionPersistenceTests
{
    private static GameSessionFactory Factory() =>
        new(new EmbeddedPuzzleRepository(), new BlobPuzzleGenerator());

    [Fact]
    public async Task VersionTenSave_MigratesToTheOriginalCatBoard_AndSurvivesResaving()
    {
        await using var temp = new TemporaryDatabase();
        var id = Guid.NewGuid();
        var marks = new byte[100];
        marks[1] = (byte)CellState.Filled; // Correct on the old cat, incorrect on the redraw.
        marks[2] = (byte)CellState.Crossed;
        await using (var oldDatabase = new SquarebuzzDatabase(temp.Path_,
            [new Migration0001Initial(), new Migration0002DailyCompletion(),
             new Migration0003GeneratorVersion(), new Migration0004DailyHistory(),
             new Migration0005SavedHintsUsed(), new Migration0006Levels(),
             new Migration0007GameCompletions(), new Migration0008SavedDailyDate(),
             new Migration0009SavedHintBudget(), new Migration0010AutomaticCrosses()]))
        {
            var connection = await oldDatabase.GetConnectionAsync();
            await connection.ExecuteAsync(
                "INSERT INTO saved_game (id, puzzle_id, size, difficulty, pack_id, seed, challenge, cells, " +
                "elapsed_seconds, hints_remaining, hints_used, hint_limit, mistakes, saved_at_ticks, saved_at_offset_ticks) " +
                "VALUES (?, 'cat', 10, 2, 'animals', 42, 0, ?, 60, 2, 1, 3, 0, ?, 0)",
                id.ToString("D"), marks, DateTimeOffset.UtcNow.UtcTicks);
        }

        var repository = new SqliteSaveGameRepository(temp.Database);
        Assert.Equal(0, await repository.PurgeUnrebuildableAsync());
        var loaded = await repository.GetAsync(id);
        Assert.NotNull(loaded);
        Assert.Equal(1, loaded.PuzzleRevision);
        Assert.Equal(marks.Select(mark => (CellState)mark), loaded.Cells);
        var factory = Factory();
        var restored = factory.Restore(loaded, HelperSettings.Default);
        Assert.True(restored.Puzzle.IsFilled(1, 0));
        Assert.False(restored.Puzzle.IsFilled(2, 0));
        Assert.Equal(1, restored.Puzzle.Revision);
        Assert.Equal(0, restored.Mistakes);
        Assert.Equal(1, restored.HintsUsed);
        Assert.Equal(TimeSpan.FromMinutes(1), restored.Elapsed);

        await repository.SaveAsync(SavedGame.FromSession(restored, id, loaded.SavedAt));
        await temp.ReopenAsync();
        var reloaded = await new SqliteSaveGameRepository(temp.Database).GetAsync(id);
        Assert.NotNull(reloaded);
        Assert.Equal(1, reloaded.PuzzleRevision);
        Assert.True(restored.Puzzle.Solution.SequenceEqual(factory.ResolvePuzzle(reloaded).Solution));
        Assert.Equal(loaded.Cells, reloaded.Cells);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task AuthoredRevision_SurvivesReopeningRestoringAndRestarting(int revision)
    {
        await using var temp = new TemporaryDatabase();
        var factory = Factory();
        var session = factory.Create(NewGameOptions.Default with { PuzzleId = "cat", PuzzleRevision = revision });
        session.Paint(1, session.Puzzle.ExpectedState(1));
        var save = SavedGame.FromSession(session, Guid.NewGuid(), DateTimeOffset.UtcNow);
        await new SqliteSaveGameRepository(temp.Database).SaveAsync(save);
        await temp.ReopenAsync();

        var loaded = await new SqliteSaveGameRepository(temp.Database).GetAsync(save.Id);
        Assert.NotNull(loaded);
        Assert.Equal(revision, loaded.PuzzleRevision);
        var restored = factory.Restore(loaded, HelperSettings.Default);
        Assert.Equal(save.Cells, restored.Cells.ToArray());
        Assert.True(session.Puzzle.Solution.SequenceEqual(restored.Puzzle.Solution));
        var restarted = factory.Create(restored.Origin!.Restart(HelperSettings.Default));
        Assert.Equal(revision, restarted.Puzzle.Revision);
        Assert.True(session.Puzzle.Solution.SequenceEqual(restarted.Puzzle.Solution));
    }
}
