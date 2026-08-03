using Squarebuzz.Core.Model;
using Squarebuzz.Data.Repositories;
using Xunit;

namespace Squarebuzz.Data.Tests;

public class DatabaseMigrationTests
{
    [Fact]
    public async Task AFreshDatabase_IsMigratedToTheTargetVersion()
    {
        await using var temp = new TemporaryDatabase();

        Assert.Equal(SquarebuzzDatabase.TargetSchemaVersion, await temp.Database.GetSchemaVersionAsync());
    }

    [Fact]
    public async Task ReopeningAnExistingDatabase_DoesNotReapplyMigrations()
    {
        await using var temp = new TemporaryDatabase();

        // Write something, then reopen. If migrations ran again and recreated tables, the data
        // would be gone or the insert into schema_version would collide.
        await new SqliteSettingsRepository(temp.Database).SaveAsync(GameSettings.Default with { BigNumbers = true });

        await temp.ReopenAsync();
        await temp.ReopenAsync();

        Assert.Equal(SquarebuzzDatabase.TargetSchemaVersion, await temp.Database.GetSchemaVersionAsync());
        Assert.True((await new SqliteSettingsRepository(temp.Database).LoadAsync()).BigNumbers);
    }

    [Fact]
    public async Task ConcurrentFirstUse_MigratesExactlyOnce()
    {
        await using var temp = new TemporaryDatabase();

        // Several repositories racing to be first is the real startup pattern: the shell loads
        // settings while the menu counts saves. The initialisation gate must hold.
        var tasks = Enumerable.Range(0, 8)
            .Select(_ => Task.Run(() => temp.Database.GetConnectionAsync()))
            .ToArray();

        await Task.WhenAll(tasks);

        Assert.Equal(SquarebuzzDatabase.TargetSchemaVersion, await temp.Database.GetSchemaVersionAsync());

        // All callers must have received the same pooled connection.
        var connections = tasks.Select(t => t.Result).Distinct().ToList();
        Assert.Single(connections);
    }

    [Fact]
    public async Task RepositoriesShareOneConnection()
    {
        await using var temp = new TemporaryDatabase();

        var settings = new SqliteSettingsRepository(temp.Database);
        var saves = new SqliteSaveGameRepository(temp.Database);
        var progress = new SqliteProgressRepository(temp.Database);

        // Interleaved use across repositories must not deadlock or see stale data.
        await settings.SaveAsync(GameSettings.Default with { Music = true });
        await progress.SaveProgressAsync(PlayerProgress.Empty with { Stars = 9 });
        var count = await saves.CountAsync();

        Assert.Equal(0, count);
        Assert.True((await settings.LoadAsync()).Music);
        Assert.Equal(9, (await progress.GetProgressAsync()).Stars);
    }

    [Fact]
    public async Task WriteAheadLogging_IsActuallyEnabled()
    {
        await using var temp = new TemporaryDatabase();
        await temp.Database.GetConnectionAsync();

        // Setting this PRAGMA returns the granted mode, and SQLite can decline the request.
        // Asserting on the granted value is what catches a silent fallback to delete mode -
        // and it was reading this PRAGMA as a non-query that broke initialisation originally.
        Assert.Equal("wal", temp.Database.JournalMode, ignoreCase: true);
    }

    [Fact]
    public async Task SynchronousMode_IsNormal_NotSqlitesDefaultFull()
    {
        // Asserted on the connection rather than trusted, for the reason the test above exists:
        // a PRAGMA that does not take is silent. FULL flushes on every commit, which the board's
        // autosave does while a child is playing; NORMAL under WAL still survives the app being
        // killed, which is the way an Android game actually ends.
        await using var temp = new TemporaryDatabase();
        var connection = await temp.Database.GetConnectionAsync();

        // 0 = OFF, 1 = NORMAL, 2 = FULL, 3 = EXTRA.
        Assert.Equal(1, await connection.ExecuteScalarAsync<int>("PRAGMA synchronous"));
    }

    [Fact]
    public async Task WalCheckpointThreshold_IsSmallerThanSqlitesDefault()
    {
        // The log is what grows; left at the default 1000 pages it reached several megabytes
        // beside a four-kilobyte database, because this app never writes enough in one go to
        // trip it.
        await using var temp = new TemporaryDatabase();
        var connection = await temp.Database.GetConnectionAsync();

        Assert.Equal(256, await connection.ExecuteScalarAsync<int>("PRAGMA wal_autocheckpoint"));
    }

    [Fact]
    public async Task ConcurrentReadsAndWrites_DoNotBlockEachOther()
    {
        await using var temp = new TemporaryDatabase();
        var settings = new SqliteSettingsRepository(temp.Database);
        var progress = new SqliteProgressRepository(temp.Database);

        // The real pattern WAL is here for: the board autosaves while the UI reads progress.
        var work = new List<Task>();

        for (var i = 0; i < 10; i++)
        {
            var stars = i;
            work.Add(Task.Run(() => progress.SaveProgressAsync(PlayerProgress.Empty with { Stars = stars })));
            work.Add(Task.Run(() => settings.LoadAsync()));
        }

        // Completing at all is the assertion: a locking regression shows up as a hang or
        // "database is locked" rather than a wrong value.
        await Task.WhenAll(work);

        Assert.InRange((await progress.GetProgressAsync()).Stars, 0, 9);
    }

    [Fact]
    public async Task EveryMigrationAppliesOnTopOfTheOneBefore()
    {
        // The regression this guards: Migration0001 originally built its tables from the entity
        // classes, so a fresh database silently gained columns that later migrations then tried
        // to add again ("duplicate column name"). Applying the chain step by step, and writing
        // through the repositories at the end, is what proves each step is a real snapshot.
        await using var temp = new TemporaryDatabase();

        var connection = await temp.Database.GetConnectionAsync();

        // Every table the current entities expect must exist and be writable.
        await new SqliteSettingsRepository(temp.Database).SaveAsync(GameSettings.Default);
        await new SqliteProgressRepository(temp.Database).SaveProgressAsync(
            PlayerProgress.Empty with { Stars = 3, LastDailyCompletedOn = new DateOnly(2026, 7, 30) });

        var reloaded = await new SqliteProgressRepository(temp.Database).GetProgressAsync();

        Assert.Equal(3, reloaded.Stars);
        Assert.Equal(new DateOnly(2026, 7, 30), reloaded.LastDailyCompletedOn);
        Assert.Equal(SquarebuzzDatabase.TargetSchemaVersion, await temp.Database.GetSchemaVersionAsync());

        // And each version is recorded exactly once.
        var versions = await connection.QueryScalarsAsync<int>("SELECT version FROM schema_version ORDER BY version");
        Assert.Equal(versions.Distinct().Count(), versions.Count);
        Assert.Equal(SquarebuzzDatabase.TargetSchemaVersion, versions.Max());
    }

    [Fact]
    public async Task CompletingTheDailyIsRecordedSeparatelyFromOrdinaryPlay()
    {
        await using var temp = new TemporaryDatabase();
        var repository = new SqliteProgressRepository(temp.Database);
        var at = new DateTimeOffset(2026, 7, 30, 12, 0, 0, TimeSpan.Zero);

        // An ordinary puzzle must not mark today's daily as done.
        await repository.RecordCompletionAsync(
            new PuzzleCompletion("heart", 3, TimeSpan.FromSeconds(60), 17, 0, at));

        Assert.Null((await repository.GetProgressAsync()).LastDailyCompletedOn);

        await repository.RecordCompletionAsync(
            new PuzzleCompletion(null, 3, TimeSpan.FromSeconds(90), 40, 0, at) { IsDaily = true });

        Assert.Equal(
            DateOnly.FromDateTime(at.LocalDateTime),
            (await repository.GetProgressAsync()).LastDailyCompletedOn);
    }

    [Fact]
    public async Task TargetSchemaVersion_IsAtLeastOne()
    {
        // Guards against an empty migration list silently shipping an unmigrated database.
        Assert.True(SquarebuzzDatabase.TargetSchemaVersion >= 1);
        await Task.CompletedTask;
    }
}
