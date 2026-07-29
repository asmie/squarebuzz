using Squarebuzz.Core.Content;
using Squarebuzz.Core.Generation;
using Squarebuzz.Core.Model;
using Squarebuzz.Data.Repositories;
using Xunit;

namespace Squarebuzz.Data.Tests;

public class SaveGameRepositoryTests
{
    private static readonly DateTimeOffset Noon = new(2026, 7, 29, 12, 0, 0, TimeSpan.FromHours(2));

    private static GameSessionFactory NewFactory() =>
        new(new EmbeddedPuzzleRepository(), new UniqueSolutionGenerator(new BlobPuzzleGenerator()));

    private static SavedGame SampleSave(Guid id, DateTimeOffset savedAt, string? puzzleId = "heart") => new()
    {
        Id = id,
        PuzzleId = puzzleId,
        Size = 5,
        Difficulty = 2,
        PackId = "animals",
        Seed = 12345,
        Challenge = ChallengeLevel.Relaxed,
        Cells = [CellState.Filled, CellState.Empty, CellState.Crossed, CellState.Filled, CellState.Empty],
        Elapsed = TimeSpan.FromSeconds(97.5),
        HintsRemaining = 2,
        Mistakes = 1,
        SavedAt = savedAt,
    };

    [Fact]
    public async Task NoSaves_OnFirstRun()
    {
        await using var temp = new TemporaryDatabase();
        var repository = new SqliteSaveGameRepository(temp.Database);

        Assert.Empty(await repository.GetAllAsync());
        Assert.Equal(0, await repository.CountAsync());
        Assert.Null(await repository.GetAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task ASave_RoundTripsEveryField()
    {
        await using var temp = new TemporaryDatabase();
        var repository = new SqliteSaveGameRepository(temp.Database);
        var id = Guid.NewGuid();
        var original = SampleSave(id, Noon);

        await repository.SaveAsync(original);
        var loaded = await repository.GetAsync(id);

        Assert.NotNull(loaded);
        Assert.Equal(original.Id, loaded.Id);
        Assert.Equal(original.PuzzleId, loaded.PuzzleId);
        Assert.Equal(original.Size, loaded.Size);
        Assert.Equal(original.Difficulty, loaded.Difficulty);
        Assert.Equal(original.PackId, loaded.PackId);
        Assert.Equal(original.Seed, loaded.Seed);
        Assert.Equal(original.Challenge, loaded.Challenge);
        Assert.Equal(original.Cells, loaded.Cells);
        Assert.Equal(original.Elapsed, loaded.Elapsed);
        Assert.Equal(original.HintsRemaining, loaded.HintsRemaining);
        Assert.Equal(original.Mistakes, loaded.Mistakes);

        // The instant must be preserved exactly, including the offset it was written in.
        Assert.Equal(original.SavedAt, loaded.SavedAt);
        Assert.Equal(original.SavedAt.Offset, loaded.SavedAt.Offset);
    }

    [Fact]
    public async Task SavesAreOrderedMostRecentFirst()
    {
        await using var temp = new TemporaryDatabase();
        var repository = new SqliteSaveGameRepository(temp.Database);

        var older = Guid.NewGuid();
        var newer = Guid.NewGuid();

        await repository.SaveAsync(SampleSave(older, Noon.AddHours(-3)));
        await repository.SaveAsync(SampleSave(newer, Noon));

        var all = await repository.GetAllAsync();

        Assert.Equal([newer, older], all.Select(s => s.Id));
    }

    [Fact]
    public async Task SavingTheSameIdTwice_Replaces()
    {
        await using var temp = new TemporaryDatabase();
        var repository = new SqliteSaveGameRepository(temp.Database);
        var id = Guid.NewGuid();

        await repository.SaveAsync(SampleSave(id, Noon));
        await repository.SaveAsync(SampleSave(id, Noon) with { Mistakes = 7 });

        Assert.Equal(1, await repository.CountAsync());
        Assert.Equal(7, (await repository.GetAsync(id))!.Mistakes);
    }

    [Fact]
    public async Task DeletingASave_LeavesTheOthers()
    {
        await using var temp = new TemporaryDatabase();
        var repository = new SqliteSaveGameRepository(temp.Database);
        var keep = Guid.NewGuid();
        var drop = Guid.NewGuid();

        await repository.SaveAsync(SampleSave(keep, Noon));
        await repository.SaveAsync(SampleSave(drop, Noon));

        await repository.DeleteAsync(drop);

        Assert.Equal(1, await repository.CountAsync());
        Assert.NotNull(await repository.GetAsync(keep));
        Assert.Null(await repository.GetAsync(drop));
    }

    [Fact]
    public async Task DeleteAll_EmptiesTheTable()
    {
        await using var temp = new TemporaryDatabase();
        var repository = new SqliteSaveGameRepository(temp.Database);

        await repository.SaveAsync(SampleSave(Guid.NewGuid(), Noon));
        await repository.SaveAsync(SampleSave(Guid.NewGuid(), Noon));

        await repository.DeleteAllAsync();

        Assert.Equal(0, await repository.CountAsync());
    }

    [Fact]
    public async Task SavesSurviveAppRestart()
    {
        await using var temp = new TemporaryDatabase();
        var id = Guid.NewGuid();

        await new SqliteSaveGameRepository(temp.Database).SaveAsync(SampleSave(id, Noon));
        await temp.ReopenAsync();

        Assert.NotNull(await new SqliteSaveGameRepository(temp.Database).GetAsync(id));
    }

    [Fact]
    public async Task AnInProgressSession_CanBeSavedAndResumedExactly()
    {
        await using var temp = new TemporaryDatabase();
        var repository = new SqliteSaveGameRepository(temp.Database);
        var factory = NewFactory();

        var session = factory.Create(NewGameOptions.Default with { Size = GridSize.Normal, PackId = "animals", Seed = 777 });

        // Play a few moves so there is real state to preserve.
        for (var i = 0; i < session.Puzzle.CellCount && session.MoveCount < 5; i++)
        {
            if (session.Puzzle.Solution[i])
            {
                session.Paint(i, CellState.Filled);
            }
        }

        session.Advance(TimeSpan.FromSeconds(42));
        session.UseHint();

        var id = Guid.NewGuid();
        await repository.SaveAsync(SavedGame.FromSession(session, id, Noon));

        await temp.ReopenAsync();
        var reloaded = await new SqliteSaveGameRepository(temp.Database).GetAsync(id);
        Assert.NotNull(reloaded);

        var resumed = factory.Restore(reloaded, HelperSettings.Default);

        Assert.Equal(session.Puzzle.Id, resumed.Puzzle.Id);
        Assert.Equal(session.Cells.ToArray(), resumed.Cells.ToArray());
        Assert.Equal(session.Elapsed, resumed.Elapsed);
        Assert.Equal(session.HintsRemaining, resumed.HintsRemaining);
        Assert.Equal(session.Mistakes, resumed.Mistakes);
    }

    [Fact]
    public async Task AGeneratedPuzzle_IsRebuiltFromItsSeedAlone()
    {
        await using var temp = new TemporaryDatabase();
        var repository = new SqliteSaveGameRepository(temp.Database);
        var factory = NewFactory();

        var session = factory.Create(NewGameOptions.Default with { Size = GridSize.Big, PackId = "surprise", Seed = 4242 });
        Assert.True(session.Puzzle.IsGenerated);

        var id = Guid.NewGuid();
        await repository.SaveAsync(SavedGame.FromSession(session, id, Noon));

        var reloaded = await repository.GetAsync(id);
        Assert.NotNull(reloaded);
        Assert.Null(reloaded.PuzzleId); // Nothing to look up - only the seed is stored.

        var resumed = factory.Restore(reloaded, HelperSettings.Default);

        // The whole point of a deterministic generator: the identical picture comes back
        // without ever having stored it.
        Assert.True(session.Puzzle.Solution.SequenceEqual(resumed.Puzzle.Solution));
    }

    [Fact]
    public async Task ALargeBoard_RoundTripsEveryCell()
    {
        await using var temp = new TemporaryDatabase();
        var repository = new SqliteSaveGameRepository(temp.Database);

        // 25x25 is the biggest board, and the one where a per-cell storage scheme would hurt.
        var cells = new CellState[GridSize.Giant * GridSize.Giant];
        for (var i = 0; i < cells.Length; i++)
        {
            cells[i] = (CellState)(byte)(i % 3);
        }

        var id = Guid.NewGuid();
        await repository.SaveAsync(SampleSave(id, Noon, puzzleId: null) with
        {
            Size = GridSize.Giant,
            Cells = cells,
        });

        var loaded = await repository.GetAsync(id);

        Assert.NotNull(loaded);
        Assert.Equal(625, loaded.Cells.Count);
        Assert.Equal(cells, loaded.Cells);
    }

    [Fact]
    public async Task ResumingAPuzzleThatNoLongerShips_FailsClearly()
    {
        await using var temp = new TemporaryDatabase();
        var repository = new SqliteSaveGameRepository(temp.Database);
        var id = Guid.NewGuid();

        await repository.SaveAsync(SampleSave(id, Noon, puzzleId: "a-picture-we-removed"));
        var reloaded = await repository.GetAsync(id);
        Assert.NotNull(reloaded);

        var exception = Assert.Throws<InvalidOperationException>(
            () => NewFactory().Restore(reloaded, HelperSettings.Default));

        Assert.Contains("a-picture-we-removed", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SavingASessionBuiltWithoutAFactory_IsRejected()
    {
        await using var temp = new TemporaryDatabase();

        // No origin, so difficulty and challenge are unknown - better to fail than invent them.
        var bare = new GameSession(new EmbeddedPuzzleRepository().FindById("heart")!, GameRules.Relaxed);

        var exception = Assert.Throws<ArgumentException>(
            () => SavedGame.FromSession(bare, Guid.NewGuid(), Noon));

        Assert.Contains("GameSessionFactory", exception.Message, StringComparison.Ordinal);
        await Task.CompletedTask;
    }
}
