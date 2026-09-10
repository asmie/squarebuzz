using SQLite;
using Squarebuzz.Core.Content;
using Squarebuzz.Core.Model;
using Squarebuzz.Data.Repositories;
using Xunit;

namespace Squarebuzz.Data.Tests;

public sealed class GameCompletionRepositoryTests
{
    private static readonly DateTimeOffset CompletedAt = new(2026, 8, 5, 23, 30, 0, TimeSpan.FromHours(2));

    private static PuzzleCompletion Completion => new("heart", 3, TimeSpan.FromSeconds(30), 12, 0, CompletedAt)
    {
        Size = 5, PackId = "animals", Level = 2, IsDaily = true,
    };

    private static SavedGame Save(Guid id) => new()
    {
        Id = id, PuzzleId = "heart", Size = 5, Difficulty = 2, PackId = "animals", Seed = 42,
        Challenge = ChallengeLevel.Relaxed, Cells = new CellState[25], Elapsed = TimeSpan.FromSeconds(20),
        HintsRemaining = 3, Mistakes = 0, SavedAt = CompletedAt, Level = 2,
    };

    private static SqliteGameCompletionRepository Repository(TemporaryDatabase temp) =>
        new(temp.Database, new EmbeddedPuzzleRepository());

    private static async Task CompleteAsync(TemporaryDatabase temp, Guid id, PuzzleCompletion completion)
    {
        var repository = Repository(temp);
        await repository.JournalAsync(id, completion);
        await repository.RetryPendingAsync();
    }

    [Theory]
    [InlineData("BEFORE INSERT ON progress")]
    [InlineData("BEFORE INSERT ON daily_completion")]
    [InlineData("BEFORE INSERT ON solved_puzzle")]
    [InlineData("BEFORE INSERT ON trophy")]
    [InlineData("BEFORE DELETE ON saved_game")]
    [InlineData("BEFORE UPDATE ON game_completion")]
    public async Task FailureAtAnyStage_RollsBackAllEffectsAndRecoversAfterReopening(string stage)
    {
        await using var temp = new TemporaryDatabase();
        var id = Guid.NewGuid();
        await new SqliteSaveGameRepository(temp.Database).SaveAsync(Save(id));
        var connection = await temp.Database.GetConnectionAsync();
        await connection.ExecuteAsync($"CREATE TRIGGER fail_completion {stage} BEGIN SELECT RAISE(ABORT, 'test failure'); END");

        await Assert.ThrowsAsync<SQLiteException>(() => CompleteAsync(temp, id, Completion));
        var progress = new SqliteProgressRepository(temp.Database);
        Assert.Equal(0, (await progress.GetProgressAsync()).Stars);
        Assert.Empty(await progress.GetSolvedPuzzlesAsync());
        Assert.Empty(await progress.GetTrophiesAsync());
        Assert.Empty(await progress.GetDailyCompletionsAsync());
        Assert.NotNull(await new SqliteSaveGameRepository(temp.Database).GetAsync(id));
        Assert.Equal(1, await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM game_completion WHERE payload IS NOT NULL"));

        await connection.ExecuteAsync("DROP TRIGGER fail_completion");
        await temp.ReopenAsync();
        await Repository(temp).RetryPendingAsync();
        await Repository(temp).RetryPendingAsync();
        await CompleteAsync(temp, id, Completion);

        progress = new SqliteProgressRepository(temp.Database);
        var standing = await progress.GetProgressAsync();
        Assert.Equal(3, standing.Stars);
        Assert.Equal(12, standing.TotalBlocksFilled);
        Assert.Equal(2, standing.HighestLevelCompleted);
        Assert.Equal(1, Assert.Single(await progress.GetSolvedPuzzlesAsync()).TimesSolved);
        Assert.Equal(new DateOnly(2026, 8, 5), Assert.Single(await progress.GetDailyCompletionsAsync()));
        Assert.Contains(await progress.GetTrophiesAsync(), trophy => trophy.Trophy == TrophyId.FirstPicture);
        Assert.All(await progress.GetTrophiesAsync(), trophy => Assert.Equal(new DateOnly(2026, 8, 5), trophy.EarnedOn));
        Assert.Null(await new SqliteSaveGameRepository(temp.Database).GetAsync(id));
    }

    [Fact]
    public async Task RepeatedJournalEntry_KeepsTheOriginalResult()
    {
        await using var temp = new TemporaryDatabase();
        var id = Guid.NewGuid();
        await Repository(temp).JournalAsync(id, Completion);
        await Repository(temp).JournalAsync(id, Completion with { Stars = 1, HintsUsed = 2 });
        await Repository(temp).RetryPendingAsync();
        var progress = new SqliteProgressRepository(temp.Database);
        Assert.Equal(3, (await progress.GetProgressAsync()).Stars);
        Assert.Contains(await progress.GetTrophiesAsync(), trophy => trophy.Trophy == TrophyId.NoHints);
    }

    [Fact]
    public async Task SeveralPendingWins_RecoverInOrderWithTheirOriginalDates()
    {
        await using var temp = new TemporaryDatabase();
        var connection = await temp.Database.GetConnectionAsync();
        await connection.ExecuteAsync("CREATE TRIGGER fail_completion BEFORE INSERT ON trophy BEGIN SELECT RAISE(ABORT, 'test failure'); END");
        await Assert.ThrowsAsync<SQLiteException>(() => CompleteAsync(temp, Guid.NewGuid(), Completion));
        await Assert.ThrowsAsync<SQLiteException>(() => CompleteAsync(temp, Guid.NewGuid(), Completion with { CompletedAt = CompletedAt.AddDays(1) }));
        Assert.Equal(2, await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM game_completion WHERE payload IS NOT NULL"));
        await connection.ExecuteAsync("DROP TRIGGER fail_completion");
        await temp.ReopenAsync();
        await Repository(temp).RetryPendingAsync();

        var progress = await new SqliteProgressRepository(temp.Database).GetProgressAsync();
        Assert.Equal(6, progress.Stars);
        Assert.Equal(2, progress.Streak);
        Assert.Equal(new DateOnly(2026, 8, 6), progress.LastPlayedOn);
    }

    [Fact]
    public async Task ConcurrentDuplicateRequests_OnlyCreditOneCompletion()
    {
        await using var temp = new TemporaryDatabase();
        var id = Guid.NewGuid();
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => CompleteAsync(temp, id, Completion)));

        var progress = new SqliteProgressRepository(temp.Database);
        Assert.Equal(3, (await progress.GetProgressAsync()).Stars);
        Assert.Equal(1, Assert.Single(await progress.GetSolvedPuzzlesAsync()).TimesSolved);
    }

    [Fact]
    public async Task ReplayingAPictureWithANewSessionId_EarnsAnotherCompletion()
    {
        await using var temp = new TemporaryDatabase();
        await CompleteAsync(temp, Guid.NewGuid(), Completion);
        await CompleteAsync(temp, Guid.NewGuid(), Completion);
        var progress = new SqliteProgressRepository(temp.Database);
        Assert.Equal(6, (await progress.GetProgressAsync()).Stars);
        Assert.Equal(2, Assert.Single(await progress.GetSolvedPuzzlesAsync()).TimesSolved);
    }

    [Fact]
    public async Task GeneratedWinWithoutASave_SurvivesFailedApplicationAndRestart()
    {
        await using var temp = new TemporaryDatabase();
        var connection = await temp.Database.GetConnectionAsync();
        await connection.ExecuteAsync("CREATE TRIGGER fail_completion BEFORE INSERT ON trophy BEGIN SELECT RAISE(ABORT, 'test failure'); END");
        await Assert.ThrowsAsync<SQLiteException>(() => CompleteAsync(temp, Guid.NewGuid(), Completion with { PuzzleId = null }));
        await connection.ExecuteAsync("DROP TRIGGER fail_completion");
        await temp.ReopenAsync();
        await Repository(temp).RetryPendingAsync();

        var progress = new SqliteProgressRepository(temp.Database);
        Assert.Equal(3, (await progress.GetProgressAsync()).Stars);
        Assert.Empty(await progress.GetSolvedPuzzlesAsync());
        Assert.NotEmpty(await progress.GetTrophiesAsync());
    }

    [Fact]
    public async Task LateAutosave_CannotResurrectACompletedSession()
    {
        await using var temp = new TemporaryDatabase();
        var id = Guid.NewGuid();
        await CompleteAsync(temp, id, Completion);
        await new SqliteSaveGameRepository(temp.Database).SaveAsync(Save(id));
        Assert.Empty(await new SqliteSaveGameRepository(temp.Database).GetAllAsync());
    }

    [Fact]
    public async Task Reset_RemovesPendingResultsAndReceipts()
    {
        await using var temp = new TemporaryDatabase();
        await CompleteAsync(temp, Guid.NewGuid(), Completion);
        var connection = await temp.Database.GetConnectionAsync();
        await connection.ExecuteAsync("CREATE TRIGGER fail_completion BEFORE INSERT ON progress BEGIN SELECT RAISE(ABORT, 'test failure'); END");
        await Assert.ThrowsAsync<SQLiteException>(() => CompleteAsync(temp, Guid.NewGuid(), Completion));
        await connection.ExecuteAsync("DROP TRIGGER fail_completion");

        await new SqliteProgressRepository(temp.Database).ResetAsync();
        await Repository(temp).RetryPendingAsync();
        Assert.Equal(0, (await new SqliteProgressRepository(temp.Database).GetProgressAsync()).Stars);
        Assert.Equal(0, await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM game_completion"));
    }

    [Fact]
    public async Task UpgradingVersionSix_PreservesExistingProgressAndAddsTheJournal()
    {
        await using var temp = new TemporaryDatabase();
        await new SqliteProgressRepository(temp.Database).SaveProgressAsync(PlayerProgress.Empty with { Stars = 10 });
        var connection = await temp.Database.GetConnectionAsync();
        await connection.ExecuteAsync("DROP TABLE game_completion");
        await connection.ExecuteAsync("DELETE FROM schema_version WHERE version = 7");
        await temp.ReopenAsync();
        await CompleteAsync(temp, Guid.NewGuid(), Completion);

        Assert.Equal(13, (await new SqliteProgressRepository(temp.Database).GetProgressAsync()).Stars);
        Assert.Equal(SquarebuzzDatabase.TargetSchemaVersion, await temp.Database.GetSchemaVersionAsync());
    }
}
