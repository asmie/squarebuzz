using SQLite;

namespace Squarebuzz.Data.Migrations;

/// <summary>Restricts recovery scans to pending wins while retaining completion receipts.</summary>
internal sealed class Migration0012PendingCompletionIndex : IMigration
{
    public int Version => 12;

    public string Name => "Pending completion index";

    public void Apply(SQLiteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        // An index on a constant contains only the pending rows, ordered by their rowid.
        connection.Execute("CREATE INDEX pending_game_completion ON game_completion ((1)) WHERE payload IS NOT NULL");
    }
}
