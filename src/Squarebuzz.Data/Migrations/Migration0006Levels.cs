using SQLite;

namespace Squarebuzz.Data.Migrations;

/// <summary>
/// Adds the Levels campaign: the player's highest completed level, and which level an
/// in-progress save was playing.
/// </summary>
/// <remarks>
/// Unlocking is strictly linear - finishing level N opens N+1 - so a single integer on the
/// progress row is the whole campaign state. It lives with stars and streak rather than in
/// settings because it is progress: a parent's "reset progress" must wipe it, and it is updated
/// in the same write as the completion that earned it. The save column is nullable; null means
/// the game was not a campaign level, which every existing row correctly becomes.
/// </remarks>
internal sealed class Migration0006Levels : IMigration
{
    public int Version => 6;

    public string Name => "Levels campaign progress";

    public async Task ApplyAsync(SQLiteAsyncConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        await connection.ExecuteAsync(
            "ALTER TABLE progress ADD COLUMN highest_level INTEGER NOT NULL DEFAULT 0")
            .ConfigureAwait(false);

        await connection.ExecuteAsync(
            "ALTER TABLE saved_game ADD COLUMN level INTEGER NULL")
            .ConfigureAwait(false);
    }
}
