using Squarebuzz.Core.Content;
using Squarebuzz.Core.Generation;
using Squarebuzz.Core.Model;
using Squarebuzz.Data.Migrations;
using Squarebuzz.Data.Repositories;
using Xunit;

namespace Squarebuzz.Data.Tests;

public sealed class HintBudgetPersistenceTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(int.MaxValue)]
    [InlineData(null)]
    public async Task BudgetAndUsage_SurviveReopening_WithDifferentPreferences(int? limit)
    {
        await using var temp = new TemporaryDatabase();
        var helpers = HelperSettings.Default with { HintBudget = new HintBudget(limit) };
        await new SqliteSettingsRepository(temp.Database).SaveAsync(GameSettings.Default with { Helpers = helpers });
        var factory = new GameSessionFactory(new EmbeddedPuzzleRepository(), new UniqueSolutionGenerator(new BlobPuzzleGenerator()));
        var session = factory.Create(NewGameOptions.Default with { Seed = 42, Helpers = helpers });
        Assert.NotNull(session.UseHint());
        var save = SavedGame.FromSession(session, Guid.NewGuid(), DateTimeOffset.UtcNow);
        await new SqliteSaveGameRepository(temp.Database).SaveAsync(save);
        await temp.ReopenAsync();

        Assert.Equal(helpers, (await new SqliteSettingsRepository(temp.Database).LoadAsync()).Helpers);
        var loaded = await new SqliteSaveGameRepository(temp.Database).GetAsync(save.Id);
        Assert.NotNull(loaded);
        var resumed = factory.Restore(loaded, HelperSettings.Default with { HintBudget = new HintBudget(6) });
        Assert.Equal(session.HintBudget, resumed.HintBudget);
        Assert.Equal(session.HintsRemaining, resumed.HintsRemaining);
        Assert.Equal(1, resumed.HintsUsed);
        Assert.Equal(session.HasUnlimitedHints, resumed.HasUnlimitedHints);
        Assert.Equal(session.Cells.ToArray(), resumed.Cells.ToArray());
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("nonsense")]
    [InlineData("2147483648")]
    public async Task InvalidPreference_UsesTheDefault(string stored)
    {
        await using var temp = new TemporaryDatabase();
        var connection = await temp.Database.GetConnectionAsync();
        await connection.ExecuteAsync("INSERT INTO setting (key, value) VALUES ('helpers.hintLimit', ?)", stored);
        var settings = await new SqliteSettingsRepository(temp.Database).LoadAsync();
        Assert.Equal(HintBudget.Default, settings.Helpers.HintBudget);
    }

    [Theory]
    [InlineData(ChallengeLevel.Relaxed, 3)]
    [InlineData(ChallengeLevel.Sharp, 1)]
    public async Task VersionEightSave_KeepsItsLegacyBudget(ChallengeLevel challenge, int allowance)
    {
        await using var temp = new TemporaryDatabase();
        var id = Guid.NewGuid();
        await using (var oldDatabase = new SquarebuzzDatabase(temp.Path_,
            [new Migration0001Initial(), new Migration0002DailyCompletion(),
             new Migration0003GeneratorVersion(), new Migration0004DailyHistory(),
             new Migration0005SavedHintsUsed(), new Migration0006Levels(),
             new Migration0007GameCompletions(), new Migration0008SavedDailyDate()]))
        {
            var connection = await oldDatabase.GetConnectionAsync();
            await connection.ExecuteAsync(
                "INSERT INTO saved_game (id, puzzle_id, size, difficulty, pack_id, seed, challenge, cells, " +
                "elapsed_seconds, hints_remaining, hints_used, mistakes, saved_at_ticks, saved_at_offset_ticks, generator_version) " +
                "VALUES (?, NULL, 5, 2, 'animals', 42, ?, ?, 60, ?, 1, 2, ?, 0, ?)",
                id.ToString("D"), (int)challenge, new byte[25], allowance - 1,
                DateTimeOffset.UtcNow.UtcTicks, GeneratorVersion.Current);
        }

        var loaded = await new SqliteSaveGameRepository(temp.Database).GetAsync(id);
        Assert.NotNull(loaded);
        Assert.Null(loaded.HintBudget);
        var factory = new GameSessionFactory(new EmbeddedPuzzleRepository(), new UniqueSolutionGenerator(new BlobPuzzleGenerator()));
        var restored = factory.Restore(loaded, HelperSettings.Default with { HintBudget = HintBudget.Unlimited });
        Assert.Equal(allowance, restored.HintBudget.Limit);
        Assert.Equal(allowance - 1, restored.HintsRemaining);
        Assert.Equal(1, restored.HintsUsed);
        Assert.Equal(2, restored.Mistakes);
        Assert.Equal(TimeSpan.FromMinutes(1), restored.Elapsed);
        Assert.Equal(SquarebuzzDatabase.TargetSchemaVersion, await temp.Database.GetSchemaVersionAsync());
    }
}
