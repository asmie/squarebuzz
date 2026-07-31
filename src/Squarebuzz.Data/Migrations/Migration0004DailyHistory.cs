using SQLite;

namespace Squarebuzz.Data.Migrations;

/// <summary>
/// One row per day the daily puzzle was finished, for the calendar on the Trials screen.
/// </summary>
/// <remarks>
/// The progress row's <c>last_daily_day</c> answers "is today's done?" but forgets everything
/// before it; a calendar has to remember each day. A day number as the primary key makes the
/// insert naturally idempotent - a daily can only be finished once per day anyway.
/// </remarks>
internal sealed class Migration0004DailyHistory : IMigration
{
    public int Version => 4;

    public string Name => "Daily completion history";

    public async Task ApplyAsync(SQLiteAsyncConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        await connection.ExecuteAsync(
            "CREATE TABLE IF NOT EXISTS daily_completion (day INTEGER NOT NULL PRIMARY KEY)")
            .ConfigureAwait(false);
    }
}
