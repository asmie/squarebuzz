using System.Text.Json;
using System.Text.Json.Serialization;
using Squarebuzz.Core.Abstractions;
using Squarebuzz.Core.Model;
using Squarebuzz.Core.Progression;
using Squarebuzz.Data.Entities;

namespace Squarebuzz.Data.Repositories;

/// <summary>Journals wins before applying their effects, so a failed transaction can be retried.</summary>
public sealed class SqliteGameCompletionRepository : IGameCompletionRepository
{
    private readonly SquarebuzzDatabase _database;
    private readonly IPuzzleRepository _puzzles;

    public SqliteGameCompletionRepository(SquarebuzzDatabase database, IPuzzleRepository puzzles)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(puzzles);
        _database = database;
        _puzzles = puzzles;
    }

    public async Task JournalAsync(Guid sessionId, PuzzleCompletion completion)
    {
        ArgumentNullException.ThrowIfNull(completion);
        var connection = await _database.GetConnectionAsync().ConfigureAwait(false);
        var payload = JsonSerializer.Serialize(completion, CompletionJsonContext.Default.PuzzleCompletion);

        // Commit the intent separately. If applying it fails, startup can recover even a
        // timed win (which has no saved game). A repeated ID keeps the original result.
        await connection.ExecuteAsync(
            "INSERT OR IGNORE INTO game_completion (id, payload) VALUES (?, ?)",
            sessionId.ToString("D"), payload).ConfigureAwait(false);
    }

    public async Task RetryPendingAsync()
    {
        var connection = await _database.GetConnectionAsync().ConfigureAwait(false);
        await connection.RunInTransactionAsync(transaction =>
        {
            // Apply in journal order, including older failed results before a newer win.
            var pending = transaction.Query<GameCompletionEntity>(
                "SELECT id, payload FROM game_completion WHERE payload IS NOT NULL ORDER BY rowid");

            foreach (var entry in pending)
            {
                var completion = JsonSerializer.Deserialize(entry.Payload!, CompletionJsonContext.Default.PuzzleCompletion)
                    ?? throw new InvalidOperationException("A pending completion has no result.");
                var progress = SqliteProgressRepository.RecordCompletion(transaction, completion);
                var solved = transaction.Table<SolvedPuzzleEntity>().ToList()
                    .Select(SqliteProgressRepository.ToModel).ToArray();
                var earned = transaction.Table<TrophyEntity>().ToList()
                    .Select(row => (TrophyId)row.TrophyId).ToHashSet();
                var trophies = TrophyEvaluator.Evaluate(new TrophyContext(
                    completion, progress, solved, _puzzles.Puzzles, earned));

                foreach (var trophy in trophies)
                {
                    transaction.Insert(new TrophyEntity
                    {
                        TrophyId = (int)trophy,
                        EarnedDayNumber = DateOnly.FromDateTime(completion.CompletedAt.DateTime).DayNumber,
                    });
                }

                transaction.Delete<SavedGameEntity>(entry.Id);
                entry.Payload = null;
                transaction.Update(entry);
            }
        }).ConfigureAwait(false);
    }
}

[JsonSerializable(typeof(PuzzleCompletion))]
internal partial class CompletionJsonContext : JsonSerializerContext;
