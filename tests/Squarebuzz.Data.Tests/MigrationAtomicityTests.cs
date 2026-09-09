using SQLite;
using Squarebuzz.Data.Migrations;
using Xunit;

namespace Squarebuzz.Data.Tests;

/// <summary>
/// The runner's promise: a migration lands whole or leaves no trace, so the retry on the next
/// launch starts clean.
/// </summary>
/// <remarks>
/// Guards the failure that would have bricked a player's database for good. A migration of two
/// ALTER TABLEs that failed after the first left that column behind with nothing recorded; the
/// retry re-ran the first statement into "duplicate column name", and every launch after that
/// failed the same way. Migration0006 has exactly that shape.
/// </remarks>
public class MigrationAtomicityTests
{
    /// <summary>Creates the table the later steps alter.</summary>
    private sealed class CreateProbe : IMigration
    {
        public int Version => 1;

        public string Name => "probe table";

        public void Apply(SQLiteConnection connection) =>
            connection.Execute("CREATE TABLE probe (a INTEGER NOT NULL)");
    }

    /// <summary>Adds a column and then fails, the way a two-statement migration does when its second statement is wrong.</summary>
    private sealed class AddColumnThenFail : IMigration
    {
        public int Version => 2;

        public string Name => "add b, then break";

        public void Apply(SQLiteConnection connection)
        {
            connection.Execute("ALTER TABLE probe ADD COLUMN b INTEGER NULL");
            connection.Execute("THIS IS NOT SQL");
        }
    }

    /// <summary>The same migration with its second statement corrected - what the next build ships.</summary>
    private sealed class AddColumnProperly : IMigration
    {
        public int Version => 2;

        public string Name => "add b and c";

        public void Apply(SQLiteConnection connection)
        {
            connection.Execute("ALTER TABLE probe ADD COLUMN b INTEGER NULL");
            connection.Execute("ALTER TABLE probe ADD COLUMN c INTEGER NULL");
        }
    }

    [Fact]
    public async Task AMigrationThatFailsPartWay_LeavesNeitherItsChangesNorItsVersionBehind()
    {
        var directory = Path.Combine(Path.GetTempPath(), "squarebuzz-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "squarebuzz.db3");

        try
        {
            // First launch: the broken build. Opening must fail - the migration is genuinely bad.
            var broken = new SquarebuzzDatabase(path, [new CreateProbe(), new AddColumnThenFail()]);
            await Assert.ThrowsAnyAsync<SQLiteException>(() => broken.GetConnectionAsync());
            await broken.DisposeAsync();

            // Inspect the file as it was left. The first step committed; the second must have
            // rolled back entirely - no column, no version row.
            var inspector = new SQLiteAsyncConnection(path, SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.FullMutex);
            var columns = await inspector.QueryScalarsAsync<string>("SELECT name FROM pragma_table_info('probe')");
            var versions = await inspector.QueryScalarsAsync<int>("SELECT version FROM schema_version ORDER BY version");
            await inspector.CloseAsync();

            Assert.Equal(["a"], columns);
            Assert.Equal([1], versions);

            // Second launch: the fixed build. This is the line that used to throw "duplicate
            // column name" for ever, because the half-applied step had left `b` behind.
            var fixedBuild = new SquarebuzzDatabase(path, [new CreateProbe(), new AddColumnProperly()]);
            var connection = await fixedBuild.GetConnectionAsync();

            columns = await connection.QueryScalarsAsync<string>("SELECT name FROM pragma_table_info('probe')");
            versions = await connection.QueryScalarsAsync<int>("SELECT version FROM schema_version ORDER BY version");

            Assert.Equal(["a", "b", "c"], columns);
            Assert.Equal([1, 2], versions);
            Assert.Equal(2, await fixedBuild.GetSchemaVersionAsync());

            await fixedBuild.DisposeAsync();
        }
        finally
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
                // A lingering SQLite handle on Windows can hold the file briefly; a temp directory
                // left behind must never fail an otherwise passing test.
            }
        }
    }
}
