using SQLite;

namespace Squarebuzz.Data.Migrations;

internal sealed class Migration0007GameCompletions : IMigration
{
    public int Version => 7;

    public string Name => "Durable game completions";

    public void Apply(SQLiteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        connection.Execute("CREATE TABLE game_completion (id TEXT PRIMARY KEY NOT NULL, payload TEXT NULL)");
    }
}
