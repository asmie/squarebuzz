using Squarebuzz.Core.Model;
using Squarebuzz.Data.Repositories;
using Xunit;

namespace Squarebuzz.Data.Tests;

/// <summary>
/// Rows the app would never write, but a file can still contain: hand-edited, copied between
/// builds, or half-written when the battery died. Every repository reads whole tables, so before
/// these guards a single such row emptied an entire screen.
/// </summary>
public class CorruptRowTests
{
    private static readonly Guid Good = Guid.NewGuid();

    /// <summary>A well-formed save row, with one column overridable per test.</summary>
    private static Task<int> InsertSaveAsync(
        SQLite.SQLiteAsyncConnection connection,
        string id,
        double elapsedSeconds = 12.5,
        int hintsRemaining = 2,
        long savedAtTicks = 638_000_000_000_000_000,
        long offsetTicks = 0) =>
        connection.ExecuteAsync(
            """
            INSERT INTO saved_game
              (id, puzzle_id, size, difficulty, pack_id, seed, challenge, cells, elapsed_seconds,
               hints_remaining, hints_used, mistakes, saved_at_ticks, saved_at_offset_ticks,
               generator_version, level)
            VALUES (?, 'heart', 5, 2, 'animals', 1, 0, ?, ?, ?, 0, 0, ?, ?, 0, NULL)
            """,
            id,
            new byte[25],
            elapsedSeconds,
            hintsRemaining,
            savedAtTicks,
            offsetTicks);

    [Fact]
    public async Task ASaveWhoseIdIsNotAGuid_IsSkippedAndItsNeighboursSurvive()
    {
        await using var temp = new TemporaryDatabase();
        var connection = await temp.Database.GetConnectionAsync();
        var repository = new SqliteSaveGameRepository(temp.Database);

        await InsertSaveAsync(connection, "not-a-guid");
        await InsertSaveAsync(connection, Good.ToString("D"));

        var saves = await repository.GetAllAsync();

        // Resume and Delete address a save by its id, so without one there is nothing to offer.
        var only = Assert.Single(saves);
        Assert.Equal(Good, only.Id);
    }

    [Fact]
    public async Task ASaveWithDamagedNumbers_IsReadWithFallbacksRatherThanDropped()
    {
        await using var temp = new TemporaryDatabase();
        var connection = await temp.Database.GetConnectionAsync();
        var repository = new SqliteSaveGameRepository(temp.Database);

        // NaN is not on the list: SQLite stores it as NULL, which the NOT NULL column refuses, so
        // it cannot reach a row. Infinity and negatives can.
        await InsertSaveAsync(
            connection,
            Good.ToString("D"),
            elapsedSeconds: double.PositiveInfinity,
            hintsRemaining: -3,
            savedAtTicks: long.MaxValue,
            offsetTicks: TimeSpan.FromHours(40).Ticks);

        var save = await repository.GetAsync(Good);

        Assert.NotNull(save);
        Assert.Equal(TimeSpan.Zero, save.Elapsed);
        Assert.Equal(0, save.HintsRemaining);

        // Visibly wrong rather than plausibly wrong: 1970 cannot be mistaken for a real save date.
        Assert.Equal(DateTimeOffset.UnixEpoch, save.SavedAt);
    }

    [Fact]
    public async Task ABadOffsetKeepsTheInstant_ShownInUtc()
    {
        await using var temp = new TemporaryDatabase();
        var connection = await temp.Database.GetConnectionAsync();
        var repository = new SqliteSaveGameRepository(temp.Database);

        var when = new DateTimeOffset(2026, 7, 29, 12, 0, 0, TimeSpan.Zero);
        await InsertSaveAsync(connection, Good.ToString("D"), savedAtTicks: when.UtcTicks, offsetTicks: long.MinValue);

        var save = await repository.GetAsync(Good);

        Assert.NotNull(save);
        Assert.Equal(when, save.SavedAt);
        Assert.Equal(TimeSpan.Zero, save.SavedAt.Offset);
    }

    [Fact]
    public async Task ASolvedPictureWithABadDateAndTime_IsStillSolved()
    {
        await using var temp = new TemporaryDatabase();
        var connection = await temp.Database.GetConnectionAsync();
        var repository = new SqliteProgressRepository(temp.Database);

        await connection.ExecuteAsync(
            "INSERT INTO solved_puzzle (puzzle_id, first_solved_day, best_stars, best_time_seconds, times_solved) VALUES ('heart', ?, 3, ?, 1)",
            int.MaxValue,
            double.PositiveInfinity);

        var solved = await repository.GetSolvedPuzzlesAsync();

        // The picture, the pack it unlocks and the trophy it counts towards all rest on this row
        // existing; the date is decoration.
        var heart = Assert.Single(solved);
        Assert.Equal("heart", heart.PuzzleId);
        Assert.Equal(DateOnly.MinValue, heart.FirstSolvedOn);
        Assert.Equal(TimeSpan.Zero, heart.BestTime);
    }

    [Fact]
    public async Task ADamagedBestTime_IsBeatenByTheNextCompletion()
    {
        await using var temp = new TemporaryDatabase();
        var connection = await temp.Database.GetConnectionAsync();
        var repository = new SqliteProgressRepository(temp.Database);

        await connection.ExecuteAsync(
            "INSERT INTO solved_puzzle (puzzle_id, first_solved_day, best_stars, best_time_seconds, times_solved) VALUES ('heart', 20000, 2, ?, 1)",
            -5.0);

        // Math.Min(-5, 45) is -5, so without the guard this row could never be repaired by play:
        // every later completion would lose to a time that was never real.
        await repository.RecordCompletionAsync(new PuzzleCompletion(
            "heart", 3, TimeSpan.FromSeconds(45), 12, 0, new DateTimeOffset(2026, 7, 29, 12, 0, 0, TimeSpan.Zero)));

        var heart = Assert.Single(await repository.GetSolvedPuzzlesAsync());
        Assert.Equal(TimeSpan.FromSeconds(45), heart.BestTime);
        Assert.Equal(2, heart.TimesSolved);
    }

    [Fact]
    public async Task ATrophyWithABadDate_IsStillEarned()
    {
        await using var temp = new TemporaryDatabase();
        var connection = await temp.Database.GetConnectionAsync();
        var repository = new SqliteProgressRepository(temp.Database);

        await connection.ExecuteAsync(
            "INSERT INTO trophy (trophy_id, earned_day) VALUES (?, ?)",
            (int)TrophyId.FirstPicture,
            -1);

        var trophies = await repository.GetTrophiesAsync();

        var first = Assert.Single(trophies);
        Assert.Equal(TrophyId.FirstPicture, first.Trophy);
        Assert.Equal(DateOnly.MinValue, first.EarnedOn);
    }

    [Fact]
    public async Task ADailyCompletionThatIsNotADate_IsLeftOffTheCalendar()
    {
        await using var temp = new TemporaryDatabase();
        var connection = await temp.Database.GetConnectionAsync();
        var repository = new SqliteProgressRepository(temp.Database);

        await connection.ExecuteAsync("INSERT INTO daily_completion (day) VALUES (?)", int.MaxValue);
        await connection.ExecuteAsync("INSERT INTO daily_completion (day) VALUES (?)", new DateOnly(2026, 7, 29).DayNumber);

        var days = await repository.GetDailyCompletionsAsync();

        Assert.Equal([new DateOnly(2026, 7, 29)], days);
    }

    [Fact]
    public async Task AProgressRowWithBadDays_ReadsAsNeverPlayed()
    {
        await using var temp = new TemporaryDatabase();
        var connection = await temp.Database.GetConnectionAsync();
        var repository = new SqliteProgressRepository(temp.Database);

        await connection.ExecuteAsync(
            """
            INSERT INTO progress (id, player_name, stars, streak, last_played_day, total_blocks_filled, last_daily_day, highest_level)
            VALUES (1, 'Ala', 7, 3, ?, 40, ?, 2)
            """,
            int.MaxValue,
            int.MinValue);

        var progress = await repository.GetProgressAsync();

        // The counters that matter survive; the dates that cannot be dates read as "never".
        Assert.Equal(7, progress.Stars);
        Assert.Equal(2, progress.HighestLevelCompleted);
        Assert.Null(progress.LastPlayedOn);
        Assert.Null(progress.LastDailyCompletedOn);
    }
}
