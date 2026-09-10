using SQLite;
using Squarebuzz.Core.Abstractions;
using Squarebuzz.Core.Model;
using Squarebuzz.Data.Entities;

namespace Squarebuzz.Data.Repositories;

/// <summary>
/// Stars, streak, solved pictures and trophies.
/// </summary>
public sealed class SqliteProgressRepository : IProgressRepository
{
    private readonly SquarebuzzDatabase _database;

    public SqliteProgressRepository(SquarebuzzDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);

        _database = database;
    }

    public async Task<PlayerProgress> GetProgressAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var connection = await _database.GetConnectionAsync().ConfigureAwait(false);

        var row = await connection.Table<ProgressEntity>()
            .Where(p => p.Id == ProgressEntity.SingletonId)
            .FirstOrDefaultAsync()
            .ConfigureAwait(false);

        return row is null ? PlayerProgress.Empty : ToModel(row);
    }

    public async Task SaveProgressAsync(PlayerProgress progress, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(progress);
        cancellationToken.ThrowIfCancellationRequested();

        var connection = await _database.GetConnectionAsync().ConfigureAwait(false);

        await connection.InsertOrReplaceAsync(ToEntity(progress)).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<SolvedPuzzle>> GetSolvedPuzzlesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var connection = await _database.GetConnectionAsync().ConfigureAwait(false);
        var rows = await connection.Table<SolvedPuzzleEntity>().ToListAsync().ConfigureAwait(false);

        // A solved picture is progress the player earned; a bad date or time on the row must not
        // cost them the picture, the pack it unlocks or the trophy it counts towards. Fallbacks
        // rather than throws - see RowGuards.
        return
        [
            .. rows.Select(ToModel)
        ];
    }

    public async Task<IReadOnlyList<EarnedTrophy>> GetTrophiesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var connection = await _database.GetConnectionAsync().ConfigureAwait(false);
        var rows = await connection.Table<TrophyEntity>().ToListAsync().ConfigureAwait(false);

        // The id was always filtered; the date now falls back instead of throwing, so a trophy
        // with a damaged date is still a trophy in the cabinet.
        return
        [
            .. rows
                .Where(r => Enum.IsDefined((TrophyId)r.TrophyId))
                .Select(r => new EarnedTrophy((TrophyId)r.TrophyId, RowGuards.DateOrMin(r.EarnedDayNumber)))
        ];
    }

    public async Task AwardTrophyAsync(TrophyId trophy, DateOnly earnedOn, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var connection = await _database.GetConnectionAsync().ConfigureAwait(false);

        // Insert-or-replace keyed on the trophy, so awarding twice keeps the later date rather
        // than failing. Callers should not have to check first.
        await connection.InsertOrReplaceAsync(new TrophyEntity
        {
            TrophyId = (int)trophy,
            EarnedDayNumber = earnedOn.DayNumber,
        }).ConfigureAwait(false);
    }

    public async Task<PlayerProgress> RecordCompletionAsync(
        PuzzleCompletion completion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(completion);
        cancellationToken.ThrowIfCancellationRequested();

        var connection = await _database.GetConnectionAsync().ConfigureAwait(false);
        var updated = PlayerProgress.Empty;
        await connection.RunInTransactionAsync(transaction =>
            updated = RecordCompletion(transaction, completion)).ConfigureAwait(false);
        return updated;
    }

    // Shared by the legacy progress API and the atomic completion journal transaction.
    internal static PlayerProgress RecordCompletion(SQLiteConnection transaction, PuzzleCompletion completion)
    {
        var completedOn = DateOnly.FromDateTime(completion.CompletedAt.DateTime);
        var row = transaction.Find<ProgressEntity>(ProgressEntity.SingletonId);
        var current = row is null ? PlayerProgress.Empty : ToModel(row);

        var dailyDate = completion.IsDaily ? completion.DailyDate ?? completedOn : (DateOnly?)null;

        var updated = current with
        {
            Stars = current.Stars + completion.Stars,
            Streak = NextStreak(current, completedOn),
            LastPlayedOn = completedOn,
            TotalBlocksFilled = current.TotalBlocksFilled + completion.BlocksFilled,

            // Finishing an older daily must not make a newer completed daily available again.
            LastDailyCompletedOn = dailyDate is { } day
                && (current.LastDailyCompletedOn is not { } lastDaily || day > lastDaily)
                    ? day
                    : current.LastDailyCompletedOn,

            // Max, not assignment: replaying an already-finished level must never wind the
            // campaign back.
            HighestLevelCompleted = completion.Level is { } level
                ? Math.Max(current.HighestLevelCompleted, level)
                : current.HighestLevelCompleted,
        };

        transaction.InsertOrReplace(ToEntity(updated));

        if (dailyDate is { } dailyDay)
        {
            // The calendar's memory. Keyed on the day, so finishing is naturally once-per-day.
            transaction.InsertOrReplace(new DailyCompletionEntity { DayNumber = dailyDay.DayNumber });
        }

        if (completion.PuzzleId is not { } puzzleId)
        {
            // Generated puzzles earn stars but are not gallery pictures, so there is
            // nothing to merge.
            return updated;
        }

        var existing = transaction.Find<SolvedPuzzleEntity>(puzzleId);

        if (existing is null)
        {
            transaction.Insert(new SolvedPuzzleEntity
            {
                PuzzleId = puzzleId,
                FirstSolvedDayNumber = completedOn.DayNumber,
                BestStars = completion.Stars,
                BestTimeSeconds = completion.Elapsed.TotalSeconds,
                TimesSolved = 1,
            });
        }
        else
        {
            existing.BestStars = Math.Max(existing.BestStars, completion.Stars);

            // Math.Min keeps a negative or infinite stored time for good - every real solve
            // loses to it - so a stored time that is not a usable duration is simply beaten
            // by the one just recorded. (NaN cannot occur: SQLite stores it as NULL, which the
            // NOT NULL column refuses.)
            existing.BestTimeSeconds = RowGuards.IsUsableSeconds(existing.BestTimeSeconds)
                ? Math.Min(existing.BestTimeSeconds, completion.Elapsed.TotalSeconds)
                : completion.Elapsed.TotalSeconds;

            existing.TimesSolved++;
            transaction.Update(existing);
        }

        return updated;
    }

    public async Task<IReadOnlyList<DateOnly>> GetDailyCompletionsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var connection = await _database.GetConnectionAsync().ConfigureAwait(false);
        var rows = await connection.Table<DailyCompletionEntity>().ToListAsync().ConfigureAwait(false);

        // A day that is not a date is nothing on a calendar, so it is left out rather than
        // faked - unlike a trophy or a picture, there is no progress behind it to preserve.
        var days = new List<DateOnly>(rows.Count);

        foreach (var row in rows)
        {
            if (RowGuards.DateOrNull(row.DayNumber) is { } day)
            {
                days.Add(day);
            }
        }

        return days;
    }

    public async Task ResetAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var connection = await _database.GetConnectionAsync().ConfigureAwait(false);

        // Settings are deliberately untouched: a parent wiping progress should not also undo
        // the colour-blind palette or language their child depends on.
        await connection.RunInTransactionAsync(transaction =>
        {
            transaction.DeleteAll<ProgressEntity>();
            transaction.DeleteAll<SolvedPuzzleEntity>();
            transaction.DeleteAll<TrophyEntity>();
            transaction.DeleteAll<SavedGameEntity>();
            transaction.DeleteAll<DailyCompletionEntity>();
            transaction.DeleteAll<GameCompletionEntity>();
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// Extends the streak on consecutive days, leaves it alone for a second puzzle on the same
    /// day, and restarts it after a gap.
    /// </summary>
    private static int NextStreak(PlayerProgress current, DateOnly completedOn)
    {
        if (current.LastPlayedOn is not { } last)
        {
            return 1;
        }

        var dayGap = completedOn.DayNumber - last.DayNumber;

        return dayGap switch
        {
            0 => Math.Max(1, current.Streak),
            1 => current.Streak + 1,
            _ => 1,
        };
    }

    private static PlayerProgress ToModel(ProgressEntity row) => new()
    {
        PlayerName = row.PlayerName,
        Stars = row.Stars,
        Streak = row.Streak,
        // A day that is not a date reads as "never", which is the same answer a fresh row gives.
        LastPlayedOn = RowGuards.DateOrNull(row.LastPlayedDayNumber),
        TotalBlocksFilled = row.TotalBlocksFilled,
        LastDailyCompletedOn = RowGuards.DateOrNull(row.LastDailyDayNumber),
        HighestLevelCompleted = row.HighestLevel,
    };

    internal static SolvedPuzzle ToModel(SolvedPuzzleEntity row) => new(
        row.PuzzleId,
        RowGuards.DateOrMin(row.FirstSolvedDayNumber),
        row.BestStars,
        RowGuards.SecondsOrZero(row.BestTimeSeconds),
        row.TimesSolved);

    private static ProgressEntity ToEntity(PlayerProgress progress) => new()
    {
        Id = ProgressEntity.SingletonId,
        PlayerName = progress.PlayerName,
        Stars = progress.Stars,
        Streak = progress.Streak,
        LastPlayedDayNumber = progress.LastPlayedOn?.DayNumber,
        TotalBlocksFilled = progress.TotalBlocksFilled,
        LastDailyDayNumber = progress.LastDailyCompletedOn?.DayNumber,
        HighestLevel = progress.HighestLevelCompleted,
    };
}
