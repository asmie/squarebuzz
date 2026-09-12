using SQLite;

namespace Squarebuzz.Data.Migrations;

/// <summary>Distinguishes automatic crosses from manual and hint marks in resumed games.</summary>
internal sealed class Migration0010AutomaticCrosses : IMigration
{
    public int Version => 10;

    public string Name => "Automatic crosses";

    public void Apply(SQLiteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        // Legacy marks have unknown provenance and must not be silently erased.
        connection.Execute("ALTER TABLE saved_game ADD COLUMN automatic_crosses BLOB NULL");
    }
}
