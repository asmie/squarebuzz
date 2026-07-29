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

        return
        [
            .. rows.Select(r => new SolvedPuzzle(
                r.PuzzleId,
                DateOnly.FromDayNumber(r.FirstSolvedDayNumber),
                r.BestStars,
                TimeSpan.FromSeconds(r.BestTimeSeconds),
                r.TimesSolved))
        ];
    }

    public async Task<IReadOnlyList<EarnedTrophy>> GetTrophiesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var connection = await _database.GetConnectionAsync().ConfigureAwait(false);
        var rows = await connection.Table<TrophyEntity>().ToListAsync().ConfigureAwait(false);

        return
        [
            .. rows
                .Where(r => Enum.IsDefined((TrophyId)r.TrophyId))
                .Select(r => new EarnedTrophy((TrophyId)r.TrophyId, DateOnly.FromDayNumber(r.EarnedDayNumber)))
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
        var completedOn = DateOnly.FromDateTime(completion.CompletedAt.LocalDateTime);

        var current = await GetProgressAsync(cancellationToken).ConfigureAwait(false);

        var updated = current with
        {
            Stars = current.Stars + completion.Stars,
            Streak = NextStreak(current, completedOn),
            LastPlayedOn = completedOn,
            TotalBlocksFilled = current.TotalBlocksFilled + completion.BlocksFilled,
        };

        // Progress and the solved-picture row move together: crediting stars without recording
        // the picture (or the reverse) would show the player an inconsistent gallery.
        await connection.RunInTransactionAsync(transaction =>
        {
            transaction.InsertOrReplace(ToEntity(updated));

            if (completion.PuzzleId is not { } puzzleId)
            {
                // Generated puzzles earn stars but are not gallery pictures, so there is
                // nothing to merge.
                return;
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
                existing.BestTimeSeconds = Math.Min(existing.BestTimeSeconds, completion.Elapsed.TotalSeconds);
                existing.TimesSolved++;
                transaction.Update(existing);
            }
        }).ConfigureAwait(false);

        return updated;
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
        LastPlayedOn = row.LastPlayedDayNumber is { } day ? DateOnly.FromDayNumber(day) : null,
        TotalBlocksFilled = row.TotalBlocksFilled,
    };

    private static ProgressEntity ToEntity(PlayerProgress progress) => new()
    {
        Id = ProgressEntity.SingletonId,
        PlayerName = progress.PlayerName,
        Stars = progress.Stars,
        Streak = progress.Streak,
        LastPlayedDayNumber = progress.LastPlayedOn?.DayNumber,
        TotalBlocksFilled = progress.TotalBlocksFilled,
    };
}
