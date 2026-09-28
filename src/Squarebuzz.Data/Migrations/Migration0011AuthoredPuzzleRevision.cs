using SQLite;

namespace Squarebuzz.Data.Migrations;

/// <summary>Pins saved marks to the authored board on which they were played.</summary>
internal sealed class Migration0011AuthoredPuzzleRevision : IMigration
{
    public int Version => 11;

    public string Name => "Authored puzzle revision";

    public void Apply(SQLiteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        // Existing saves predate redraws and must resolve the original archived artwork.
        connection.Execute("ALTER TABLE saved_game ADD COLUMN puzzle_revision INTEGER NOT NULL DEFAULT 1");
    }
}
