using SQLite;

namespace Squarebuzz.Data.Migrations;

/// <summary>
/// Records how many hints a saved game had actually spent.
/// </summary>
/// <remarks>
/// The count used to be re-derived on resume as allowance-minus-remaining, which is wrong the
/// moment the allowance differs from the one the game was saved under - switching hints off in
/// Options is enough. That handed spent hints back, restoring a star and the "no hints" trophy.
/// Existing rows default to 0, which is the same answer the old derivation gave for a save made
/// with hints untouched, and errs towards the player for the rest.
/// </remarks>
internal sealed class Migration0005SavedHintsUsed : IMigration
{
    public int Version => 5;

    public string Name => "Hints used on saved games";

    public async Task ApplyAsync(SQLiteAsyncConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        await connection.ExecuteAsync(
            "ALTER TABLE saved_game ADD COLUMN hints_used INTEGER NOT NULL DEFAULT 0")
            .ConfigureAwait(false);
    }
}
