using Squarebuzz.Core.Abstractions;
using Squarebuzz.Core.Model;
using Squarebuzz.Data.Entities;

namespace Squarebuzz.Data.Repositories;

/// <summary>
/// Stores unfinished puzzles. The board travels as one byte per cell rather than a row per
/// cell, so a save is a single small insert even on a 25x25 grid.
/// </summary>
public sealed class SqliteSaveGameRepository : ISaveGameRepository
{
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

        return [.. rows.Select(ToModel)];
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

        await connection.InsertOrReplaceAsync(ToEntity(game)).ConfigureAwait(false);
    }

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
            Size = game.Size,
            Difficulty = game.Difficulty,
            PackId = game.PackId,
            Seed = game.Seed,
            Challenge = (int)game.Challenge,
            Cells = cells,
            ElapsedSeconds = game.Elapsed.TotalSeconds,
            HintsRemaining = game.HintsRemaining,
            Mistakes = game.Mistakes,
            SavedAtUtcTicks = game.SavedAt.UtcTicks,
            SavedAtOffsetTicks = game.SavedAt.Offset.Ticks,
        };
    }

    private static SavedGame ToModel(SavedGameEntity row)
    {
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

        return new SavedGame
        {
            Id = Guid.Parse(row.Id),
            PuzzleId = row.PuzzleId,
            Size = row.Size,
            Difficulty = row.Difficulty,
            PackId = row.PackId,
            Seed = row.Seed,
            Challenge = Enum.IsDefined((ChallengeLevel)row.Challenge)
                ? (ChallengeLevel)row.Challenge
                : ChallengeLevel.Relaxed,
            Cells = cells,
            Elapsed = TimeSpan.FromSeconds(row.ElapsedSeconds),
            HintsRemaining = row.HintsRemaining,
            Mistakes = row.Mistakes,
            SavedAt = new DateTimeOffset(row.SavedAtUtcTicks, TimeSpan.Zero)
                .ToOffset(new TimeSpan(row.SavedAtOffsetTicks)),
        };
    }
}
