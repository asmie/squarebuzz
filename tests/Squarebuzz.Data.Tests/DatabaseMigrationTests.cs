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
    public async Task TargetSchemaVersion_IsAtLeastOne()
    {
        // Guards against an empty migration list silently shipping an unmigrated database.
        Assert.True(SquarebuzzDatabase.TargetSchemaVersion >= 1);
        await Task.CompletedTask;
    }
}
