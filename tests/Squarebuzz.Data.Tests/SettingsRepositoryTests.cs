using Squarebuzz.Core.Model;
using Squarebuzz.Data.Repositories;
using Xunit;

namespace Squarebuzz.Data.Tests;

public class SettingsRepositoryTests
{
    [Fact]
    public async Task FirstRun_ReturnsDefaults()
    {
        await using var temp = new TemporaryDatabase();
        var repository = new SqliteSettingsRepository(temp.Database);

        var settings = await repository.LoadAsync();

        Assert.Equal(GameSettings.Default, settings);
    }

    [Fact]
    public async Task EverySetting_SurvivesARoundTrip()
    {
        await using var temp = new TemporaryDatabase();
        var repository = new SqliteSettingsRepository(temp.Database);

        // Deliberately every field set away from its default, so a forgotten mapping shows up.
        var saved = new GameSettings
        {
            SoundEffects = false,
            Music = true,
            VoiceNarration = true,
            Haptics = false,
            Theme = GameTheme.ColorBlind,
            Accent = GameAccent.Grape,
            FollowSystemTheme = true,
            BigNumbers = true,
            TapBehaviour = TapBehaviour.HoldToCross,
            Handedness = Handedness.Left,
            CellZoomPercent = 140,
            ShowMagnifier = true,
            Language = AppLanguage.Polish,
            Helpers = new HelperSettings
            {
                AutoCross = false,
                WarnOnMistakes = false,
                ShowTimer = false,
                AllowHints = false,
            },
            LastSize = GridSize.Huge,
            LastDifficulty = 5,
            LastPackId = "dinos",
            LastChallenge = ChallengeLevel.Sharp,
            ScreenTimeLimitMinutes = 30,
            HasSeenOnboarding = true,
        };

        await repository.SaveAsync(saved);
        var loaded = await repository.LoadAsync();

        Assert.Equal(saved, loaded);
    }

    [Fact]
    public async Task SettingsSurviveAppRestart()
    {
        await using var temp = new TemporaryDatabase();

        await new SqliteSettingsRepository(temp.Database)
            .SaveAsync(GameSettings.Default with { Theme = GameTheme.Dark, Language = AppLanguage.Spanish });

        await temp.ReopenAsync();

        var loaded = await new SqliteSettingsRepository(temp.Database).LoadAsync();

        Assert.Equal(GameTheme.Dark, loaded.Theme);
        Assert.Equal(AppLanguage.Spanish, loaded.Language);
    }

    [Fact]
    public async Task SavingTwice_Overwrites_RatherThanDuplicating()
    {
        await using var temp = new TemporaryDatabase();
        var repository = new SqliteSettingsRepository(temp.Database);

        await repository.SaveAsync(GameSettings.Default with { CellZoomPercent = 120 });
        await repository.SaveAsync(GameSettings.Default with { CellZoomPercent = 90 });

        Assert.Equal(90, (await repository.LoadAsync()).CellZoomPercent);
    }

    [Fact]
    public async Task NullScreenTimeLimit_RoundTripsAsNull()
    {
        await using var temp = new TemporaryDatabase();
        var repository = new SqliteSettingsRepository(temp.Database);

        await repository.SaveAsync(GameSettings.Default with { ScreenTimeLimitMinutes = 45 });
        Assert.Equal(45, (await repository.LoadAsync()).ScreenTimeLimitMinutes);

        await repository.SaveAsync(GameSettings.Default with { ScreenTimeLimitMinutes = null });
        Assert.Null((await repository.LoadAsync()).ScreenTimeLimitMinutes);
    }

    [Theory]
    [InlineData(10, GameSettings.MinCellZoomPercent)]
    [InlineData(500, GameSettings.MaxCellZoomPercent)]
    public async Task OutOfRangeZoom_IsClampedOnLoad(int stored, int expected)
    {
        await using var temp = new TemporaryDatabase();
        var repository = new SqliteSettingsRepository(temp.Database);

        // Simulates a hand-edited or downgraded database.
        await repository.SaveAsync(GameSettings.Default with { CellZoomPercent = stored });

        Assert.Equal(expected, (await repository.LoadAsync()).CellZoomPercent);
    }

    [Fact]
    public async Task AnUnsupportedStoredGridSize_FallsBackSafely()
    {
        await using var temp = new TemporaryDatabase();
        var repository = new SqliteSettingsRepository(temp.Database);

        await repository.SaveAsync(GameSettings.Default with { LastSize = 13 });

        Assert.Equal(GridSize.Tiny, (await repository.LoadAsync()).LastSize);
    }

    [Fact]
    public void Sanitise_ClampsDifficultyAndPack()
    {
        var sanitised = (GameSettings.Default with { LastDifficulty = 99, LastPackId = "  " }).Sanitised();

        Assert.Equal(5, sanitised.LastDifficulty);
        Assert.Equal("animals", sanitised.LastPackId);
    }

    [Fact]
    public async Task Language_MapsToAndFromCultureCodes()
    {
        await using var temp = new TemporaryDatabase();
        var repository = new SqliteSettingsRepository(temp.Database);

        await repository.SaveAsync(GameSettings.Default with { Language = AppLanguage.Polish });
        var loaded = await repository.LoadAsync();

        Assert.Equal("pl", loaded.Language.ToCultureCode());

        // Regional variants must still resolve to the shipped language.
        Assert.Equal(AppLanguage.Polish, AppLanguages.FromCultureCode("pl-PL"));
        Assert.Equal(AppLanguage.Spanish, AppLanguages.FromCultureCode("es-MX"));
        Assert.Equal(AppLanguage.German, AppLanguages.FromCultureCode("de-DE"));
        Assert.Equal(AppLanguage.Portuguese, AppLanguages.FromCultureCode("pt-BR"));

        // Anything the game does not ship in, and a missing code, fall back to English.
        Assert.Equal(AppLanguage.English, AppLanguages.FromCultureCode("hu-HU"));
        Assert.Equal(AppLanguage.English, AppLanguages.FromCultureCode(null));
        Assert.Equal(AppLanguage.English, AppLanguages.FromCultureCode(string.Empty));
    }

    [Fact]
    public void EveryShippedLanguage_HasAUniqueCodeAndRoundTrips()
    {
        // The picker, the resx satellite names and the persisted value all key off these, so a
        // duplicate or a code that does not round-trip would silently strand a language.
        var codes = AppLanguages.All.Select(l => l.CultureCode).ToList();
        var languages = AppLanguages.All.Select(l => l.Language).ToList();

        Assert.Equal(codes.Count, codes.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(languages.Count, languages.Distinct().Count());

        // Every enum member is listed exactly once.
        Assert.Equal(Enum.GetValues<AppLanguage>().Length, languages.Count);

        foreach (var info in AppLanguages.All)
        {
            Assert.Equal(info.CultureCode, info.Language.ToCultureCode());
            Assert.Equal(info.Language, AppLanguages.FromCultureCode(info.CultureCode));
            Assert.False(string.IsNullOrWhiteSpace(info.Endonym));
        }

        // Arabic is the one right-to-left language, and the only one.
        Assert.True(AppLanguage.Arabic.IsRightToLeft());
        Assert.Single(AppLanguages.All, l => l.IsRightToLeft);
    }

    [Fact]
    public async Task SettingsAndProgress_AreIndependent()
    {
        await using var temp = new TemporaryDatabase();
        var settings = new SqliteSettingsRepository(temp.Database);
        var progress = new SqliteProgressRepository(temp.Database);

        await settings.SaveAsync(GameSettings.Default with { Theme = GameTheme.ColorBlind });
        await progress.SaveProgressAsync(PlayerProgress.Empty with { Stars = 50 });

        // A parent wiping progress must not undo an accessibility choice.
        await progress.ResetAsync();

        Assert.Equal(GameTheme.ColorBlind, (await settings.LoadAsync()).Theme);
        Assert.Equal(0, (await progress.GetProgressAsync()).Stars);
    }
}
