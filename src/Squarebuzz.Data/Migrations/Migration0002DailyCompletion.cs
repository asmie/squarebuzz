using SQLite;

namespace Squarebuzz.Data.Migrations;

/// <summary>
/// Adds <c>progress.last_daily_day</c> so Trials can tell whether today's daily is still open.
/// </summary>
/// <remarks>
/// Hand-written ALTER rather than a table rebuild: an added nullable column is exactly what
/// SQLite's ALTER TABLE supports, and it keeps every existing player's stars and streak in place.
/// A player upgrading simply has no daily recorded yet, which reads correctly as "not done today".
/// </remarks>
internal sealed class Migration0002DailyCompletion : IMigration
{
    public int Version => 2;

    public string Name => "Track daily-puzzle completion";

    public void Apply(SQLiteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        // sqlite-net's CreateTable would add the column on an existing table too, but being
        // explicit documents the change and avoids relying on that behaviour.
        connection.Execute("ALTER TABLE progress ADD COLUMN last_daily_day INTEGER NULL");
    }
}
