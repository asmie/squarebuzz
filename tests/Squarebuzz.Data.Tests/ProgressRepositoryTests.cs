using Squarebuzz.Core.Model;
using Squarebuzz.Data.Repositories;
using Xunit;

namespace Squarebuzz.Data.Tests;

public class ProgressRepositoryTests
{
    private static readonly DateTimeOffset Day1 = new(2026, 7, 27, 18, 0, 0, TimeSpan.Zero);

    private static PuzzleCompletion Completion(
        string? puzzleId,
        int stars,
        DateTimeOffset at,
        double seconds = 60,
        int blocks = 10,
        int hints = 0) =>
        new(puzzleId, stars, TimeSpan.FromSeconds(seconds), blocks, hints, at);

    [Fact]
    public async Task FirstRun_HasEmptyProgress()
    {
        await using var temp = new TemporaryDatabase();
        var repository = new SqliteProgressRepository(temp.Database);

        Assert.Equal(PlayerProgress.Empty, await repository.GetProgressAsync());
        Assert.Empty(await repository.GetSolvedPuzzlesAsync());
        Assert.Empty(await repository.GetTrophiesAsync());
    }

    [Fact]
    public async Task Progress_RoundTrips()
    {
        await using var temp = new TemporaryDatabase();
        var repository = new SqliteProgressRepository(temp.Database);

        var progress = new PlayerProgress
        {
            PlayerName = "Maja",
            Stars = 128,
            Streak = 4,
            LastPlayedOn = new DateOnly(2026, 7, 29),
            TotalBlocksFilled = 512,
        };

        await repository.SaveProgressAsync(progress);
        await temp.ReopenAsync();

        Assert.Equal(progress, await new SqliteProgressRepository(temp.Database).GetProgressAsync());
    }

    [Fact]
    public async Task CompletingAPuzzle_AddsStarsAndRecordsThePicture()
    {
        await using var temp = new TemporaryDatabase();
        var repository = new SqliteProgressRepository(temp.Database);

        var progress = await repository.RecordCompletionAsync(Completion("heart", 3, Day1, blocks: 17));

        Assert.Equal(3, progress.Stars);
        Assert.Equal(1, progress.Streak);
        Assert.Equal(17, progress.TotalBlocksFilled);

        var solved = await repository.GetSolvedPuzzlesAsync();
        var heart = Assert.Single(solved);

        Assert.Equal("heart", heart.PuzzleId);
        Assert.Equal(3, heart.BestStars);
        Assert.Equal(1, heart.TimesSolved);
    }

    [Fact]
    public async Task ReplayingAPuzzle_KeepsTheBestResultAndCountsTheAttempt()
    {
        await using var temp = new TemporaryDatabase();
        var repository = new SqliteProgressRepository(temp.Database);

        await repository.RecordCompletionAsync(Completion("heart", 2, Day1, seconds: 120));
        await repository.RecordCompletionAsync(Completion("heart", 3, Day1, seconds: 90));
        await repository.RecordCompletionAsync(Completion("heart", 1, Day1, seconds: 200));

        var heart = Assert.Single(await repository.GetSolvedPuzzlesAsync());

        Assert.Equal(3, heart.BestStars);
        Assert.Equal(TimeSpan.FromSeconds(90), heart.BestTime);
        Assert.Equal(3, heart.TimesSolved);

        // The first completion's date is the one the gallery shows, not the latest.
        Assert.Equal(DateOnly.FromDateTime(Day1.LocalDateTime), heart.FirstSolvedOn);
    }

    [Fact]
    public async Task StarsAccumulateAcrossPuzzles()
    {
        await using var temp = new TemporaryDatabase();
        var repository = new SqliteProgressRepository(temp.Database);

        await repository.RecordCompletionAsync(Completion("heart", 3, Day1));
        var progress = await repository.RecordCompletionAsync(Completion("star", 2, Day1));

        Assert.Equal(5, progress.Stars);
        Assert.Equal(2, (await repository.GetSolvedPuzzlesAsync()).Count);
    }

    [Fact]
    public async Task AGeneratedPuzzle_EarnsStarsButIsNotAGalleryPicture()
    {
        await using var temp = new TemporaryDatabase();
        var repository = new SqliteProgressRepository(temp.Database);

        var progress = await repository.RecordCompletionAsync(Completion(puzzleId: null, 3, Day1));

        Assert.Equal(3, progress.Stars);
        Assert.Empty(await repository.GetSolvedPuzzlesAsync());
    }

    [Fact]
    public async Task PlayingOnConsecutiveDays_ExtendsTheStreak()
    {
        await using var temp = new TemporaryDatabase();
        var repository = new SqliteProgressRepository(temp.Database);

        await repository.RecordCompletionAsync(Completion("heart", 3, Day1));
        await repository.RecordCompletionAsync(Completion("star", 3, Day1.AddDays(1)));
        var progress = await repository.RecordCompletionAsync(Completion("fish", 3, Day1.AddDays(2)));

        Assert.Equal(3, progress.Streak);
    }

    [Fact]
    public async Task ASecondPuzzleOnTheSameDay_DoesNotDoubleCountTheStreak()
    {
        await using var temp = new TemporaryDatabase();
        var repository = new SqliteProgressRepository(temp.Database);

        await repository.RecordCompletionAsync(Completion("heart", 3, Day1));
        var progress = await repository.RecordCompletionAsync(Completion("star", 3, Day1.AddHours(2)));

        Assert.Equal(1, progress.Streak);
    }

    [Fact]
    public async Task MissingADay_RestartsTheStreak()
    {
        await using var temp = new TemporaryDatabase();
        var repository = new SqliteProgressRepository(temp.Database);

        await repository.RecordCompletionAsync(Completion("heart", 3, Day1));
        await repository.RecordCompletionAsync(Completion("star", 3, Day1.AddDays(1)));
        var progress = await repository.RecordCompletionAsync(Completion("fish", 3, Day1.AddDays(5)));

        Assert.Equal(1, progress.Streak);
    }

    [Fact]
    public async Task Trophies_AreAwardedAndListed()
    {
        await using var temp = new TemporaryDatabase();
        var repository = new SqliteProgressRepository(temp.Database);
        var day = new DateOnly(2026, 7, 29);

        await repository.AwardTrophyAsync(TrophyId.FirstPicture, day);
        await repository.AwardTrophyAsync(TrophyId.DinoFan, day);

        var trophies = await repository.GetTrophiesAsync();

        Assert.Equal(2, trophies.Count);
        Assert.Contains(trophies, t => t.Trophy == TrophyId.FirstPicture && t.EarnedOn == day);
    }

    [Fact]
    public async Task AwardingTheSameTrophyTwice_IsHarmless()
    {
        await using var temp = new TemporaryDatabase();
        var repository = new SqliteProgressRepository(temp.Database);

        await repository.AwardTrophyAsync(TrophyId.Speedy, new DateOnly(2026, 7, 28));
        await repository.AwardTrophyAsync(TrophyId.Speedy, new DateOnly(2026, 7, 29));

        // Callers should not have to check first; the later award simply wins.
        var trophy = Assert.Single(await repository.GetTrophiesAsync());
        Assert.Equal(new DateOnly(2026, 7, 29), trophy.EarnedOn);
    }

    [Fact]
    public async Task Reset_ClearsProgressSolvedTrophiesAndSaves()
    {
        await using var temp = new TemporaryDatabase();
        var progressRepository = new SqliteProgressRepository(temp.Database);
        var saveRepository = new SqliteSaveGameRepository(temp.Database);

        await progressRepository.RecordCompletionAsync(Completion("heart", 3, Day1));
        await progressRepository.AwardTrophyAsync(TrophyId.FirstPicture, new DateOnly(2026, 7, 29));
        await saveRepository.SaveAsync(new SavedGame
        {
            Id = Guid.NewGuid(),
            PuzzleId = "star",
            Size = 5,
            Difficulty = 2,
            PackId = "space",
            Seed = 1,
            Challenge = ChallengeLevel.Relaxed,
            Cells = new CellState[25],
            Elapsed = TimeSpan.Zero,
            HintsRemaining = 3,
            Mistakes = 0,
            SavedAt = Day1,
        });

        await progressRepository.ResetAsync();

        Assert.Equal(PlayerProgress.Empty, await progressRepository.GetProgressAsync());
        Assert.Empty(await progressRepository.GetSolvedPuzzlesAsync());
        Assert.Empty(await progressRepository.GetTrophiesAsync());
        Assert.Equal(0, await saveRepository.CountAsync());
    }

    [Fact]
    public async Task ProgressStaysASingleRow()
    {
        await using var temp = new TemporaryDatabase();
        var repository = new SqliteProgressRepository(temp.Database);

        await repository.SaveProgressAsync(PlayerProgress.Empty with { Stars = 1 });
        await repository.SaveProgressAsync(PlayerProgress.Empty with { Stars = 2 });
        await repository.SaveProgressAsync(PlayerProgress.Empty with { Stars = 3 });

        Assert.Equal(3, (await repository.GetProgressAsync()).Stars);
    }
}
