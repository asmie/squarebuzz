using Squarebuzz.Core.Content;
using Squarebuzz.Core.Generation;
using Squarebuzz.Core.Model;
using Squarebuzz.Core.Progression;
using Squarebuzz.Data.Migrations;
using Squarebuzz.Data.Repositories;
using Xunit;

namespace Squarebuzz.Data.Tests;

public sealed class DailyPersistenceTests
{
    private static readonly DateOnly PuzzleDate = new(2026, 12, 31);
    private static readonly DateTimeOffset FinishedAt = new(2027, 1, 1, 0, 30, 0, TimeSpan.FromHours(2));

    private static GameSessionFactory Factory() =>
        new(new EmbeddedPuzzleRepository(), new UniqueSolutionGenerator(new BlobPuzzleGenerator()));

    private static PuzzleCompletion Completion(DateOnly? dailyDate) =>
        new(null, 3, TimeSpan.FromMinutes(2), 40, 0, FinishedAt)
        {
            IsDaily = true,
            DailyDate = dailyDate,
            Size = DailyPuzzle.Size,
        };

    [Fact]
    public async Task DailySaveAndJournal_SurviveReopening_AndCreditThePuzzleDate()
    {
        await using var temp = new TemporaryDatabase();
        var factory = Factory();
        var session = factory.Create(DailyPuzzle.OptionsFor(PuzzleDate, HelperSettings.Default));
        var save = SavedGame.FromSession(session, Guid.NewGuid(), FinishedAt.AddHours(-1));
        await new SqliteSaveGameRepository(temp.Database).SaveAsync(save);
        await temp.ReopenAsync();

        var loaded = await new SqliteSaveGameRepository(temp.Database).GetAsync(save.Id);
        Assert.NotNull(loaded);
        var restored = factory.Restore(loaded, HelperSettings.Default);
        Assert.Equal(PuzzleDate, restored.Origin!.DailyDate);
        Assert.Equal(session.Seed, restored.Seed);
        Assert.True(session.Puzzle.Solution.SequenceEqual(restored.Puzzle.Solution));

        var journal = new SqliteGameCompletionRepository(temp.Database, new EmbeddedPuzzleRepository());
        await journal.JournalAsync(save.Id, Completion(restored.Origin.DailyDate));
        await temp.ReopenAsync();
        journal = new SqliteGameCompletionRepository(temp.Database, new EmbeddedPuzzleRepository());
        await journal.RetryPendingAsync();

        var repository = new SqliteProgressRepository(temp.Database);
        var progress = await repository.GetProgressAsync();
        Assert.Equal(PuzzleDate, Assert.Single(await repository.GetDailyCompletionsAsync()));
        Assert.Equal(PuzzleDate, progress.LastDailyCompletedOn);
        Assert.Equal(new DateOnly(2027, 1, 1), progress.LastPlayedOn);
        Assert.True(DailyPuzzle.IsAvailable(progress, new DateOnly(2027, 1, 1)));
        Assert.All(await repository.GetTrophiesAsync(), trophy =>
            Assert.Equal(new DateOnly(2027, 1, 1), trophy.EarnedOn));
        Assert.Null(await new SqliteSaveGameRepository(temp.Database).GetAsync(save.Id));
    }

    [Fact]
    public async Task CompletingAnOlderDaily_DoesNotReopenTheNewerDaily()
    {
        await using var temp = new TemporaryDatabase();
        var repository = new SqliteProgressRepository(temp.Database);
        var today = DateOnly.FromDateTime(FinishedAt.DateTime);
        await repository.RecordCompletionAsync(Completion(today));
        var progress = await repository.RecordCompletionAsync(Completion(PuzzleDate));

        Assert.Equal(today, progress.LastDailyCompletedOn);
        Assert.False(DailyPuzzle.IsAvailable(progress, today));
        Assert.Equal(new[] { PuzzleDate, today }, (await repository.GetDailyCompletionsAsync()).Order());
    }

    [Fact]
    public async Task LegacyDailyJournal_UsesCompletionDateWhenPuzzleDateIsAbsent()
    {
        await using var temp = new TemporaryDatabase();
        var journal = new SqliteGameCompletionRepository(temp.Database, new EmbeddedPuzzleRepository());
        await journal.JournalAsync(Guid.NewGuid(), Completion(null));
        // Model a payload written before DailyDate was introduced.
        var connection = await temp.Database.GetConnectionAsync();
        await connection.ExecuteAsync("UPDATE game_completion SET payload = replace(payload, ',\"DailyDate\":null', '')");
        Assert.DoesNotContain("DailyDate", await connection.ExecuteScalarAsync<string>("SELECT payload FROM game_completion"));
        await temp.ReopenAsync();
        await new SqliteGameCompletionRepository(temp.Database, new EmbeddedPuzzleRepository()).RetryPendingAsync();

        Assert.Equal(DateOnly.FromDateTime(FinishedAt.DateTime),
            Assert.Single(await new SqliteProgressRepository(temp.Database).GetDailyCompletionsAsync()));
    }

    [Fact]
    public async Task VersionSevenSave_IsPreservedWithoutGuessingItsDailyIdentity()
    {
        await using var temp = new TemporaryDatabase();
        var id = Guid.NewGuid();
        await using (var oldDatabase = new SquarebuzzDatabase(temp.Path_,
            [new Migration0001Initial(), new Migration0002DailyCompletion(),
             new Migration0003GeneratorVersion(), new Migration0004DailyHistory(),
             new Migration0005SavedHintsUsed(), new Migration0006Levels(), new Migration0007GameCompletions()]))
        {
            var connection = await oldDatabase.GetConnectionAsync();
            await connection.ExecuteAsync(
                "INSERT INTO saved_game (id, puzzle_id, size, difficulty, pack_id, seed, challenge, cells, " +
                "elapsed_seconds, hints_remaining, mistakes, saved_at_ticks, saved_at_offset_ticks, generator_version) " +
                "VALUES (?, NULL, 10, 3, 'surprise', ?, 0, ?, 60, 3, 0, ?, 0, ?)",
                id.ToString("D"), DailyPuzzle.SeedFor(PuzzleDate), new byte[100], FinishedAt.UtcTicks, GeneratorVersion.Current);
        }

        var repository = new SqliteSaveGameRepository(temp.Database);
        var loaded = await repository.GetAsync(id);
        Assert.NotNull(loaded);
        Assert.Null(loaded.DailyDate);
        Assert.Equal(DailyPuzzle.SeedFor(PuzzleDate), loaded.Seed);
        Assert.Equal(TimeSpan.FromMinutes(1), loaded.Elapsed);
        Assert.Equal(SquarebuzzDatabase.TargetSchemaVersion, await temp.Database.GetSchemaVersionAsync());
        await repository.SaveAsync(loaded with { DailyDate = PuzzleDate });
        Assert.Equal(PuzzleDate, (await repository.GetAsync(id))!.DailyDate);
    }
}
