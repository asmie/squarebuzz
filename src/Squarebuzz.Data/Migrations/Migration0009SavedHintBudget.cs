using SQLite;

namespace Squarebuzz.Data.Migrations;

/// <summary>Freezes each saved game's initial hint budget independently of current settings.</summary>
internal sealed class Migration0009SavedHintBudget : IMigration
{
    public int Version => 9;

    public string Name => "Saved hint budget";

    public void Apply(SQLiteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        // Null identifies legacy saves; zero encodes unlimited, positive values a finite limit.
        connection.Execute("ALTER TABLE saved_game ADD COLUMN hint_limit INTEGER NULL");
    }
}
