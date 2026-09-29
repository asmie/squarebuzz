using SQLite;
using Squarebuzz.Core.Abstractions;
using Squarebuzz.Core.Generation;
using Squarebuzz.Core.Model;
using Squarebuzz.Data.Entities;

namespace Squarebuzz.Data.Repositories;

/// <summary>
/// Stores unfinished puzzles. The board travels as one byte per cell rather than a row per
/// cell, so a save is a single small insert even on a 25x25 grid.
/// </summary>
public sealed class SqliteSaveGameRepository : ISaveGameRepository
{
    /// <summary>Maximum number of unfinished games retained, ordered by most recent save.</summary>
    public const int MaxSavedGames = 12;

    private readonly SquarebuzzDatabase _database;

    public SqliteSaveGameRepository(SquarebuzzDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);

        _database = database;
    }

    public async Task<IReadOnlyList<SavedGame>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var connection = await _database.GetConnectionAsync().ConfigureAwait(false);

        var rows = await connection.Table<SavedGameEntity>()
            .OrderByDescending(r => r.SavedAtUtcTicks)
            .ToListAsync()
            .ConfigureAwait(false);

        // A row that cannot be read is left out rather than allowed to empty the whole list -
        // see ToModel for what "cannot be read" is allowed to mean.
        var saves = new List<SavedGame>(rows.Count);

        foreach (var row in rows)
        {
            if (ToModel(row) is { } save)
            {
                saves.Add(save);
            }
        }

        return saves;
    }

    public async Task<SavedGame?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var connection = await _database.GetConnectionAsync().ConfigureAwait(false);
        var key = id.ToString("D");

        var row = await connection.Table<SavedGameEntity>()
            .Where(r => r.Id == key)
            .FirstOrDefaultAsync()
            .ConfigureAwait(false);

        return row is null ? null : ToModel(row);
    }

    public async Task SaveAsync(SavedGame game, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(game);
        cancellationToken.ThrowIfCancellationRequested();

        var connection = await _database.GetConnectionAsync().ConfigureAwait(false);

        // The guarded write and the trim commit together, so a failure between them can neither
        // leave one save over the limit nor drop an old save for a new one that never landed.
        await connection.RunInTransactionAsync(transaction =>
        {
            // A delayed save from another page must not resurrect a journaled/completed game.
            if (transaction.Find<GameCompletionEntity>(game.Id.ToString("D")) is null)
            {
                transaction.InsertOrReplace(ToEntity(game));
                TrimToMostRecent(transaction);
            }
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// Drops everything past the <see cref="MaxSavedGames"/> most recently saved games.
    /// </summary>
    /// <remarks>
    /// Done here rather than by a caller, so no future save path can forget it. A single
    /// statement, and it does nothing at all until the limit is passed, which for most players
    /// is never.
    /// </remarks>
    /// <returns>How many games were dropped, which is almost always none.</returns>
    private static int TrimToMostRecent(SQLiteConnection transaction) =>
        transaction.Execute(
            """
            DELETE FROM saved_game
            WHERE id NOT IN (
                SELECT id FROM saved_game ORDER BY saved_at_ticks DESC LIMIT ?
            )
            """,
            MaxSavedGames);

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var connection = await _database.GetConnectionAsync().ConfigureAwait(false);

        await connection.DeleteAsync<SavedGameEntity>(id.ToString("D")).ConfigureAwait(false);
    }

    public async Task DeleteAllAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var connection = await _database.GetConnectionAsync().ConfigureAwait(false);

        await connection.DeleteAllAsync<SavedGameEntity>().ConfigureAwait(false);
    }

    public async Task<int> CountAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var connection = await _database.GetConnectionAsync().ConfigureAwait(false);

        return await connection.Table<SavedGameEntity>().CountAsync().ConfigureAwait(false);
    }

    public async Task<int> PurgeUnrebuildableAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var connection = await _database.GetConnectionAsync().ConfigureAwait(false);

        // Generated puzzles only: an authored one is looked up by id, so it survives any change to
        // the generator. The comparison is against equality rather than "older than", because a
        // database written by a newer build is just as unrebuildable by this one.
        return await connection.ExecuteAsync(
                "DELETE FROM saved_game WHERE puzzle_id IS NULL AND generator_version <> ?",
                GeneratorVersion.Current)
            .ConfigureAwait(false);
    }

    private static SavedGameEntity ToEntity(SavedGame game)
    {
        var cells = new byte[game.Cells.Count];
        for (var i = 0; i < cells.Length; i++)
        {
            cells[i] = (byte)game.Cells[i];
        }

        return new SavedGameEntity
        {
            Id = game.Id.ToString("D"),
            PuzzleId = game.PuzzleId,
            PuzzleRevision = game.PuzzleRevision,
            Size = game.Size,
            Difficulty = game.Difficulty,
            PackId = game.PackId,
            Seed = game.Seed,
            Challenge = (int)game.Challenge,
            Cells = cells,
            AutomaticCrosses = game.AutoCrossedCells.Select(automatic => automatic ? (byte)1 : (byte)0).ToArray(),
            ElapsedSeconds = game.Elapsed.TotalSeconds,
            HintsRemaining = game.HintsRemaining,
            HintsUsed = game.HintsUsed,
            HintLimit = game.HintBudget is { } budget ? budget.Limit ?? 0 : null,
            Mistakes = game.Mistakes,
            SavedAtUtcTicks = game.SavedAt.UtcTicks,
            SavedAtOffsetTicks = game.SavedAt.Offset.Ticks,
            GeneratorVersion = game.GeneratorVersion,
            Level = game.Level,
            DailyDayNumber = game.DailyDate?.DayNumber,
        };
    }

    /// <summary>Reads a saved row, returning null only when its ID cannot be parsed.</summary>
    /// <remarks>
    /// Other corrupt fields use conservative defaults so a damaged row remains visible and deletable.
    /// </remarks>
    private static SavedGame? ToModel(SavedGameEntity row)
    {
        if (!Guid.TryParse(row.Id, out var id))
        {
            return null;
        }

        var cells = new CellState[row.Cells.Length];
        for (var i = 0; i < cells.Length; i++)
        {
            // Guard against a corrupt or hand-edited byte: anything unrecognised reads as
            // untouched rather than throwing and locking the player out of their own save.
            var value = row.Cells[i];
            cells[i] = value is (byte)CellState.Filled or (byte)CellState.Crossed
                ? (CellState)value
                : CellState.Empty;
        }

        // Damaged or absent metadata must not turn manual marks into erasable crosses.
        var automaticCrosses = new bool[cells.Length];
        if (row.AutomaticCrosses is { } stored && stored.Length == cells.Length)
        {
            for (var i = 0; i < cells.Length; i++)
            {
                automaticCrosses[i] = stored[i] == 1 && cells[i] == CellState.Crossed;
            }
        }

        return new SavedGame
        {
            Id = id,
            PuzzleId = row.PuzzleId,
            PuzzleRevision = row.PuzzleRevision,
            Size = row.Size,
            Difficulty = row.Difficulty,
            PackId = row.PackId,
            Seed = row.Seed,
            Challenge = Enum.IsDefined((ChallengeLevel)row.Challenge)
                ? (ChallengeLevel)row.Challenge
                : ChallengeLevel.Relaxed,
            Cells = cells,
            AutoCrossedCells = automaticCrosses,
            Elapsed = RowGuards.SecondsOrZero(row.ElapsedSeconds),

            // Negative counters would make GameSession.Restore throw, and the resume path treats a
            // throw as "start a fresh game" - so a slightly damaged row lost the board entirely.
            HintsRemaining = RowGuards.NonNegative(row.HintsRemaining),
            HintsUsed = RowGuards.NonNegative(row.HintsUsed),
            HintBudget = row.HintLimit switch
            {
                0 => HintBudget.Unlimited,
                > 0 => new HintBudget(row.HintLimit),
                _ => null,
            },
            Mistakes = RowGuards.NonNegative(row.Mistakes),
            SavedAt = RowGuards.InstantOrEpoch(row.SavedAtUtcTicks, row.SavedAtOffsetTicks),
            GeneratorVersion = row.GeneratorVersion,
            Level = row.Level,
            DailyDate = RowGuards.DateOrNull(row.DailyDayNumber),
        };
    }
}
