using SQLite;
using Squarebuzz.Data.Entities;
using Squarebuzz.Data.Migrations;

namespace Squarebuzz.Data;

/// <summary>
/// Owns the SQLite connection and brings the schema up to date on first use.
/// </summary>
/// <remarks>
/// Registered as a singleton: sqlite-net's async connection is thread-safe and holds a pooled
/// handle, so opening one per repository would waste handles and risk lock contention.
/// Initialisation is guarded so that concurrent first calls from several repositories migrate
/// exactly once.
/// </remarks>
public sealed class SquarebuzzDatabase : IAsyncDisposable
{
    private static readonly IMigration[] Migrations =
    [
        new Migration0001Initial(),
        new Migration0002DailyCompletion(),
        new Migration0003GeneratorVersion(),
        new Migration0004DailyHistory(),
        new Migration0005SavedHintsUsed(),
        new Migration0006Levels(),
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

            // WAL lets a read continue while a write is in flight, which matters because the
            // board autosaves while the UI is still reading progress.
            //
            // This PRAGMA reports the resulting mode as a result row, so it must be read as a
            // scalar - running it through ExecuteAsync (ExecuteNonQuery) throws the
            // gloriously unhelpful "SQLite Error: not an error".
            var journalMode = await connection.ExecuteScalarAsync<string>("PRAGMA journal_mode=WAL")
                .ConfigureAwait(false);

            JournalMode = journalMode;

            // NORMAL rather than SQLite's default FULL, which is the right pairing with WAL. FULL
            // flushes to disk on every single commit; the board autosaves while a child is
            // playing, so that is a device-level flush every few seconds for the sake of a game
            // in progress. Under WAL, NORMAL still survives the app being killed - the case that
            // actually happens, since Android stops backgrounded apps whenever it likes - and
            // gives up only durability across an OS crash or a flat battery mid-write. The cost
            // of that, once, is a few seconds of somebody's nonogram.
            await connection.ExecuteAsync("PRAGMA synchronous=NORMAL").ConfigureAwait(false);

            // The write-ahead log is checkpointed back into the database every 256 pages instead
            // of the default 1000. Left alone it grew to several megabytes beside a database of
            // four kilobytes, because nothing here ever writes enough at once to trip the
            // default. Smaller checkpoints suit a game that writes a little and often.
            //
            // Like journal_mode, this one answers with the value it settled on, so it has to be
            // read as a scalar - ExecuteAsync gives the same "SQLite Error: not an error".
            await connection.ExecuteScalarAsync<int>("PRAGMA wal_autocheckpoint=256").ConfigureAwait(false);

            // Returns no rows, so ExecuteAsync is correct here.
            await connection.ExecuteAsync("PRAGMA foreign_keys=ON").ConfigureAwait(false);

            await MigrateAsync(connection).ConfigureAwait(false);

            _connection = connection;
            return connection;
        }
        finally
        {
            _initialisationGate.Release();
        }
    }

    /// <summary>Applies every migration the database has not seen yet, in order.</summary>
    /// <remarks>
    /// Each step runs in its own transaction together with its <c>schema_version</c> row, so the
    /// two commit as one or not at all. That is what makes the retry-on-next-launch design
    /// actually work: without it, a migration of several statements that failed after its first
    /// left that statement's change behind with nothing recorded, and the retry re-ran it into
    /// "duplicate column name" - on every launch, for ever. Migration0006 adds two columns and
    /// was exactly that shape. SQLite's DDL is transactional, so the rollback is real.
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

            await connection.RunInTransactionAsync(transaction =>
            {
                migration.Apply(transaction);

                // Inside the same transaction as the step itself, so the version can never be
                // recorded for a migration that did not fully land - nor the step land without
                // its version and be re-attempted over the top of itself.
                transaction.Insert(new SchemaVersionEntity
                {
                    Version = migration.Version,
                    AppliedAtUtc = DateTime.UtcNow,
                });
            }).ConfigureAwait(false);
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
}
