using SQLite;

namespace Squarebuzz.Data.Migrations;

/// <summary>Preserves daily identity and the original puzzle date in unfinished games.</summary>
internal sealed class Migration0008SavedDailyDate : IMigration
{
    public int Version => 8;

    public string Name => "Saved daily puzzle date";

    public void Apply(SQLiteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        // Older saves did not distinguish daily puzzles from ordinary generated ones.
        // Leave their date unknown instead of guessing from a seed or the last save time.
        connection.Execute("ALTER TABLE saved_game ADD COLUMN daily_day INTEGER NULL");
    }
}
