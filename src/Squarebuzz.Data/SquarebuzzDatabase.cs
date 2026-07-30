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
    ];

    private readonly SemaphoreSlim _initialisationGate = new(1, 1);
    private readonly string _databasePath;

    private SQLiteAsyncConnection? _connection;

    public SquarebuzzDatabase(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        _databasePath = databasePath;
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
    private static async Task MigrateAsync(SQLiteAsyncConnection connection)
    {
        await connection.CreateTableAsync<SchemaVersionEntity>().ConfigureAwait(false);

        var applied = await connection.Table<SchemaVersionEntity>().ToListAsync().ConfigureAwait(false);
        var appliedVersions = applied.Select(v => v.Version).ToHashSet();

        foreach (var migration in Migrations.OrderBy(m => m.Version))
        {
            if (appliedVersions.Contains(migration.Version))
            {
                continue;
            }

            await migration.ApplyAsync(connection).ConfigureAwait(false);

            // Recorded only after the migration succeeds, so a failure part-way through is
            // retried on next launch rather than silently skipped.
            await connection.InsertAsync(new SchemaVersionEntity
            {
                Version = migration.Version,
                AppliedAtUtc = DateTime.UtcNow,
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
