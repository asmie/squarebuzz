using SQLite;
using Squarebuzz.Data.Entities;

namespace Squarebuzz.Data.Migrations;

/// <summary>
/// Creates the initial schema. Uses sqlite-net's table creation rather than hand-written DDL
/// so the tables can never drift from the entity definitions.
/// </summary>
internal sealed class Migration0001Initial : IMigration
{
    public int Version => 1;

    public string Name => "Initial schema";

    public async Task ApplyAsync(SQLiteAsyncConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        await connection.CreateTablesAsync(
            CreateFlags.None,
            typeof(SettingEntity),
            typeof(SavedGameEntity),
            typeof(ProgressEntity),
            typeof(SolvedPuzzleEntity),
            typeof(TrophyEntity)).ConfigureAwait(false);
    }
}
