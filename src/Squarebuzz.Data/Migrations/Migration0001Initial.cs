using SQLite;

namespace Squarebuzz.Data.Migrations;

/// <summary>
/// Creates the initial schema.
/// </summary>
/// <remarks>
/// <para>
/// The DDL is written out rather than generated from the entity classes, and it must never be
/// edited again. An earlier version called <c>CreateTablesAsync</c> from the entities, which is
/// convenient but wrong: that reflects whatever the entities look like <em>today</em>, so a
/// fresh database silently received columns added by later migrations, and the later migration
/// then failed with "duplicate column name". A migration has to be a frozen snapshot of the
/// schema at its own version.
/// </para>
/// <para>
/// SQLite is dynamically typed, so the declared affinities only need to match what sqlite-net
/// reads and writes: TEXT for strings, INTEGER for ints and longs, REAL for doubles, BLOB for
/// byte arrays.
/// </para>
/// </remarks>
internal sealed class Migration0001Initial : IMigration
{
    public int Version => 1;

    public string Name => "Initial schema";

    public void Apply(SQLiteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        connection.Execute(
            """
            CREATE TABLE IF NOT EXISTS setting (
                key   TEXT PRIMARY KEY NOT NULL,
                value TEXT NULL
            )
            """);

        connection.Execute(
            """
            CREATE TABLE IF NOT EXISTS saved_game (
                id                    TEXT PRIMARY KEY NOT NULL,
                puzzle_id             TEXT NULL,
                size                  INTEGER NOT NULL,
                difficulty            INTEGER NOT NULL,
                pack_id               TEXT NOT NULL,
                seed                  INTEGER NOT NULL,
                challenge             INTEGER NOT NULL,
                cells                 BLOB NOT NULL,
                elapsed_seconds       REAL NOT NULL,
                hints_remaining       INTEGER NOT NULL,
                mistakes              INTEGER NOT NULL,
                saved_at_ticks        INTEGER NOT NULL,
                saved_at_offset_ticks INTEGER NOT NULL
            )
            """);

        // Continue lists saves newest first, so the ordering column is indexed.
        connection.Execute(
            "CREATE INDEX IF NOT EXISTS ix_saved_game_saved_at ON saved_game (saved_at_ticks)");

        connection.Execute(
            """
            CREATE TABLE IF NOT EXISTS progress (
                id                  INTEGER PRIMARY KEY NOT NULL,
                player_name         TEXT NOT NULL,
                stars               INTEGER NOT NULL,
                streak              INTEGER NOT NULL,
                last_played_day     INTEGER NULL,
                total_blocks_filled INTEGER NOT NULL
            )
            """);

        connection.Execute(
            """
            CREATE TABLE IF NOT EXISTS solved_puzzle (
                puzzle_id         TEXT PRIMARY KEY NOT NULL,
                first_solved_day  INTEGER NOT NULL,
                best_stars        INTEGER NOT NULL,
                best_time_seconds REAL NOT NULL,
                times_solved      INTEGER NOT NULL
            )
            """);

        connection.Execute(
            """
            CREATE TABLE IF NOT EXISTS trophy (
                trophy_id  INTEGER PRIMARY KEY NOT NULL,
                earned_day INTEGER NOT NULL
            )
            """);
    }
}
