using Squarebuzz.Core.Content;
using Squarebuzz.Core.Generation;
using Squarebuzz.Core.Model;
using Squarebuzz.Data.Migrations;
using Squarebuzz.Data.Repositories;
using Xunit;

namespace Squarebuzz.Data.Tests;

public sealed class AutomaticCrossPersistenceTests
{
    private static GameSessionFactory Factory() =>
        new(new EmbeddedPuzzleRepository(), new UniqueSolutionGenerator(new BlobPuzzleGenerator()));

    private static GameSession CompletedFirstRow()
    {
        var session = Factory().Create(NewGameOptions.Default with { PuzzleId = "heart", Seed = 42 });
        session.Paint(0, CellState.Crossed);
        session.Paint(1, CellState.Filled);
        session.Paint(3, CellState.Filled);
        Assert.True(session.AutoCrossedCells[2]);
        return session;
    }

    [Fact]
    public async Task ReopenedSave_PreservesManualMarks_AndClearsAutomaticOnes()
    {
        await using var temp = new TemporaryDatabase();
        var session = CompletedFirstRow();
        var save = SavedGame.FromSession(session, Guid.NewGuid(), DateTimeOffset.UtcNow);
        await new SqliteSaveGameRepository(temp.Database).SaveAsync(save);
        await temp.ReopenAsync();
        var loaded = await new SqliteSaveGameRepository(temp.Database).GetAsync(save.Id);
        Assert.NotNull(loaded);
        Assert.Equal(save.AutoCrossedCells, loaded.AutoCrossedCells);
        Assert.Equal(save.Cells, loaded.Cells);
        var restored = Factory().Restore(loaded, HelperSettings.Default);
        restored.Paint(1, CellState.Empty);
        Assert.Equal(CellState.Crossed, restored[0]);
        Assert.Equal(CellState.Empty, restored[2]);
        Assert.Equal(CellState.Empty, restored[4]);
        Assert.True(restored.Undo());
        Assert.True(restored.AutoCrossedCells[2]);
        await new SqliteSaveGameRepository(temp.Database).SaveAsync(SavedGame.FromSession(restored, save.Id, save.SavedAt));
        await temp.ReopenAsync();
        loaded = await new SqliteSaveGameRepository(temp.Database).GetAsync(save.Id);
        Assert.Equal(save.AutoCrossedCells, loaded!.AutoCrossedCells);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CorruptAutomaticMetadata_DoesNotLoseTheBoard(bool wrongLength)
    {
        await using var temp = new TemporaryDatabase();
        var session = CompletedFirstRow();
        var save = SavedGame.FromSession(session, Guid.NewGuid(), DateTimeOffset.UtcNow);
        var repository = new SqliteSaveGameRepository(temp.Database);
        await repository.SaveAsync(save);
        var invalid = wrongLength ? new byte[] { 1 } : Enumerable.Repeat((byte)255, 25).ToArray();
        if (!wrongLength) invalid[1] = 1; // A filled cell cannot be an automatic cross.
        var connection = await temp.Database.GetConnectionAsync();
        await connection.ExecuteAsync("UPDATE saved_game SET automatic_crosses = ? WHERE id = ?", invalid, save.Id.ToString("D"));
        var loaded = await repository.GetAsync(save.Id);
        Assert.NotNull(loaded);
        Assert.Equal(save.Cells, loaded.Cells);
        Assert.DoesNotContain(true, loaded.AutoCrossedCells);
        var restored = Factory().Restore(loaded, HelperSettings.Default);
        restored.Paint(1, CellState.Empty);
        Assert.Equal(CellState.Crossed, restored[0]);
        Assert.Equal(CellState.Crossed, restored[2]);
    }

    [Fact]
    public async Task VersionNineSave_MigratesWithoutGuessingTheOriginOfCrosses()
    {
        await using var temp = new TemporaryDatabase();
        var session = CompletedFirstRow();
        var id = Guid.NewGuid();
        await using (var oldDatabase = new SquarebuzzDatabase(temp.Path_,
            [new Migration0001Initial(), new Migration0002DailyCompletion(),
             new Migration0003GeneratorVersion(), new Migration0004DailyHistory(),
             new Migration0005SavedHintsUsed(), new Migration0006Levels(),
             new Migration0007GameCompletions(), new Migration0008SavedDailyDate(), new Migration0009SavedHintBudget()]))
        {
            var connection = await oldDatabase.GetConnectionAsync();
            await connection.ExecuteAsync(
                "INSERT INTO saved_game (id, puzzle_id, size, difficulty, pack_id, seed, challenge, cells, " +
                "elapsed_seconds, hints_remaining, hints_used, hint_limit, mistakes, saved_at_ticks, saved_at_offset_ticks) " +
                "VALUES (?, 'heart', 5, 2, 'animals', 42, 0, ?, 60, 6, 1, 7, 2, ?, 0)",
                id.ToString("D"), session.Cells.ToArray().Select(cell => (byte)cell).ToArray(), DateTimeOffset.UtcNow.UtcTicks);
        }

        var loaded = await new SqliteSaveGameRepository(temp.Database).GetAsync(id);
        Assert.NotNull(loaded);
        Assert.Equal(session.Cells.ToArray(), loaded.Cells);
        Assert.DoesNotContain(true, loaded.AutoCrossedCells);
        var restored = Factory().Restore(loaded, HelperSettings.Default);
        Assert.Equal(7, restored.HintBudget.Limit);
        Assert.Equal(6, restored.HintsRemaining);
        Assert.Equal(1, restored.HintsUsed);
        Assert.Equal(2, restored.Mistakes);
        Assert.Equal(TimeSpan.FromMinutes(1), restored.Elapsed);
        restored.Paint(1, CellState.Empty);
        Assert.Equal(CellState.Crossed, restored[0]);
        Assert.Equal(CellState.Crossed, restored[2]);
        Assert.Equal(SquarebuzzDatabase.TargetSchemaVersion, await temp.Database.GetSchemaVersionAsync());
    }
}
