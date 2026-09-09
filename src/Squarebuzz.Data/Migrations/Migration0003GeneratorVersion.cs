using SQLite;

namespace Squarebuzz.Data.Migrations;

/// <summary>
/// Records which generation algorithm produced a saved generated puzzle.
/// </summary>
/// <remarks>
/// A save stores a seed rather than the picture, so it is only resumable while the generator still
/// turns that seed into the same picture. Existing rows default to
/// <c>GeneratorVersion.Unknown</c> (0), which is deliberately not a version any build claims: they
/// were written before this column existed, so they cannot be trusted to match the current
/// algorithm and are treated as unrebuildable. See <c>Squarebuzz.Core.Generation.GeneratorVersion</c>.
/// </remarks>
internal sealed class Migration0003GeneratorVersion : IMigration
{
    public int Version => 3;

    public string Name => "Generator version on saved games";

    public void Apply(SQLiteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        connection.Execute(
            "ALTER TABLE saved_game ADD COLUMN generator_version INTEGER NOT NULL DEFAULT 0");
    }
}
