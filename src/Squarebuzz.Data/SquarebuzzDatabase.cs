using SQLite;
using Squarebuzz.Data.Entities;
using Squarebuzz.Data.Migrations;

namespace Squarebuzz.Data;

/// <summary>Owns the shared SQLite connection and initializes its schema.</summary>
/// <remarks>
/// A semaphore serializes first use so concurrent repositories run migrations once.
/// </remarks>
public sealed class SquarebuzzDatabase : IAsyncDisposable, IDisposable
{
    private static readonly IMigration[] Migrations =
    [
        new Migration0001Initial(),
        new Migration0002DailyCompletion(),
        new Migration0003GeneratorVersion(),
        new Migration0004DailyHistory(),
        new Migration0005SavedHintsUsed(),
        new Migration0006Levels(),
        new Migration0007GameCompletions(),
        new Migration0008SavedDailyDate(),
        new Migration0009SavedHintBudget(),
        new Migration0010AutomaticCrosses(),
        new Migration0011AuthoredPuzzleRevision(),
    ];

    private readonly SemaphoreSlim _initialisationGate = new(1, 1);
    private readonly string _databasePath;
    private readonly IReadOnlyList<IMigration> _migrations;

    private SQLiteAsyncConnection? _connection;

    public SquarebuzzDatabase(string databasePath)
        : this(databasePath, Migrations)
    {
    }

    /// <summary>
    /// Testing seam: runs a caller-supplied chain instead of the shipped one, so the runner's own
    /// guarantees - one transaction per step, nothing recorded for a step that failed - can be
    /// proved with a migration built to fail part-way.
    /// </summary>
    internal SquarebuzzDatabase(string databasePath, IReadOnlyList<IMigration> migrations)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        ArgumentNullException.ThrowIfNull(migrations);

        _databasePath = databasePath;
        _migrations = migrations;
    }

    /// <summary>Schema version this build expects.</summary>
    public static int TargetSchemaVersion => Migrations.Max(m => m.Version);

    /// <summary>
    /// Journal mode the database actually adopted. SQLite may refuse WAL (for instance on some
    /// network filesystems) and silently stay in delete mode, so this records what was granted
    /// rather than what was asked for.
    /// </summary>
    public string? JournalMode { get; private set; }

    /// <summary>
    /// The open, migrated connection. Safe to call from anywhere; the first caller does the
    /// work and the rest wait for it.
    /// </summary>
    public async Task<SQLiteAsyncConnection> GetConnectionAsync()
    {
        if (_connection is not null)
        {
            return _connection;
        }

        await _initialisationGate.WaitAsync().ConfigureAwait(false);

        try
        {
            if (_connection is not null)
            {
                return _connection;
            }

            var connection = new SQLiteAsyncConnection(
                _databasePath,
                // ReadWrite|Create is the obvious pair. SharedCache keeps the several
                // repositories on one page cache; FullMutex makes the handle safe to share.
                SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.SharedCache | SQLiteOpenFlags.FullMutex);

            try
            {
                // Enable WAL for concurrent reads and saves. journal_mode returns a row, so read it as a scalar.
                var journalMode = await connection.ExecuteScalarAsync<string>("PRAGMA journal_mode=WAL")
                    .ConfigureAwait(false);

                JournalMode = journalMode;

                // NORMAL reduces fsync work under WAL. Committed data survives process termination,
                // but recent transactions can be lost on an OS crash or power failure.
                await connection.ExecuteAsync("PRAGMA synchronous=NORMAL").ConfigureAwait(false);

                // Checkpoint every 256 pages to limit WAL growth under frequent small saves.
                // This PRAGMA returns a result row.
                await connection.ExecuteScalarAsync<int>("PRAGMA wal_autocheckpoint=256").ConfigureAwait(false);

                // Returns no rows, so ExecuteAsync is correct here.
                await connection.ExecuteAsync("PRAGMA foreign_keys=ON").ConfigureAwait(false);

                await MigrateAsync(connection).ConfigureAwait(false);
            }
            catch
            {
                // Never published, so nothing else will ever close it. Each retry would otherwise
                // leak another open handle on the database file.
                await connection.CloseAsync().ConfigureAwait(false);
                throw;
            }

            _connection = connection;
            return connection;
        }
        finally
        {
            _initialisationGate.Release();
        }
    }

    /// <summary>Applies pending migrations in version order.</summary>
    /// <remarks>
    /// Each migration and its schema_version entry commit in one transaction. A failed step
    /// is rolled back and can be retried on the next initialization.
    /// </remarks>
    private async Task MigrateAsync(SQLiteAsyncConnection connection)
    {
        await connection.CreateTableAsync<SchemaVersionEntity>().ConfigureAwait(false);

        var applied = await connection.Table<SchemaVersionEntity>().ToListAsync().ConfigureAwait(false);
        var appliedVersions = applied.Select(v => v.Version).ToHashSet();

        foreach (var migration in _migrations.OrderBy(m => m.Version))
        {
            if (appliedVersions.Contains(migration.Version))
            {
                continue;
            }

            try
            {
                await connection.RunInTransactionAsync(transaction =>
                {
                    migration.Apply(transaction);

                    // Record the version in the same transaction as its schema changes.
                    transaction.Insert(new SchemaVersionEntity
                    {
                        Version = migration.Version,
                        AppliedAtUtc = DateTime.UtcNow,
                    });
                }).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // Include the migration number and name in initialization errors.
                throw new InvalidOperationException(
                    $"Database migration {migration.Version} ({migration.Name}) failed and was rolled back. " +
                    "It will be retried on the next launch.",
                    ex);
            }
        }
    }

    /// <summary>Current schema version in the database. Diagnostics and tests.</summary>
    public async Task<int> GetSchemaVersionAsync()
    {
        var connection = await GetConnectionAsync().ConfigureAwait(false);
        var versions = await connection.Table<SchemaVersionEntity>().ToListAsync().ConfigureAwait(false);

        return versions.Count == 0 ? 0 : versions.Max(v => v.Version);
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.CloseAsync().ConfigureAwait(false);
            _connection = null;
        }

        _initialisationGate.Dispose();
    }

    /// <summary>Synchronous disposal for hosts that do not use DisposeAsync.</summary>
    /// <remarks>
    /// sqlite-net performs async operations on the thread pool; this shutdown path waits for them.
    /// </remarks>
    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();
}
