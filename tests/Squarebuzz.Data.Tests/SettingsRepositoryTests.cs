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
                HintBudget = new HintBudget(17),
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
    // A screen whose read failed holds the defaults. Writing its one change must not write the
    // defaults for everything else over what the player actually chose.
    public async Task SaveChanges_WritesOnlyWhatDiffers()
    {
        await using var temp = new TemporaryDatabase();
        var repository = new SqliteSettingsRepository(temp.Database);
        var chosen = GameSettings.Default with
        {
            Language = AppLanguage.Polish,
            ScreenTimeLimitMinutes = 30,
            Helpers = HelperSettings.Default with { WarnOnMistakes = false },
        };
        await repository.SaveAsync(chosen);

        var baseline = GameSettings.Default;
        await repository.SaveChangesAsync(baseline, baseline with { LastSize = GridSize.Normal });

        var loaded = await repository.LoadAsync();
        Assert.Equal(GridSize.Normal, loaded.LastSize);
        Assert.Equal(AppLanguage.Polish, loaded.Language);
        Assert.Equal(30, loaded.ScreenTimeLimitMinutes);
        Assert.False(loaded.Helpers.WarnOnMistakes);
    }

    [Fact]
    public async Task SaveChanges_WithNothingChanged_WritesNothing()
    {
        await using var temp = new TemporaryDatabase();
        var repository = new SqliteSettingsRepository(temp.Database);
        await repository.SaveAsync(GameSettings.Default with { Music = false });

        await repository.SaveChangesAsync(GameSettings.Default, GameSettings.Default);

        Assert.False((await repository.LoadAsync()).Music);
    }

    [Fact]
    public void Sanitise_ClampsDifficultyAndPack()
    {
        var sanitised = (GameSettings.Default with { LastDifficulty = 99, LastPackId = "  " }).Sanitised();

        Assert.Equal(5, sanitised.LastDifficulty);
        Assert.Equal(GameSettings.DefaultPackId, sanitised.LastPackId);
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
    public void Chinese_ResolvesScriptRatherThanJustTheLanguage()
    {
        // The only pair that shares a language subtag, so it is the one case a plain "split on
        // the dash" cannot answer. Android reports any of these shapes.
        Assert.Equal(AppLanguage.ChineseSimplified, AppLanguages.FromCultureCode("zh-Hans"));
        Assert.Equal(AppLanguage.ChineseTraditional, AppLanguages.FromCultureCode("zh-Hant"));
        Assert.Equal(AppLanguage.ChineseSimplified, AppLanguages.FromCultureCode("zh-Hans-CN"));
        Assert.Equal(AppLanguage.ChineseTraditional, AppLanguages.FromCultureCode("zh-Hant-TW"));

        // Region alone implies the script for the places that only use one.
        Assert.Equal(AppLanguage.ChineseSimplified, AppLanguages.FromCultureCode("zh-CN"));
        Assert.Equal(AppLanguage.ChineseSimplified, AppLanguages.FromCultureCode("zh-SG"));
        Assert.Equal(AppLanguage.ChineseTraditional, AppLanguages.FromCultureCode("zh-TW"));
        Assert.Equal(AppLanguage.ChineseTraditional, AppLanguages.FromCultureCode("zh-HK"));
        Assert.Equal(AppLanguage.ChineseTraditional, AppLanguages.FromCultureCode("zh-MO"));

        // Bare "zh" says nothing about script; Simplified is the larger audience.
        Assert.Equal(AppLanguage.ChineseSimplified, AppLanguages.FromCultureCode("zh"));
    }

    [Fact]
    public void EveryShippedCultureCode_IsOneDotNetCanResolve()
    {
        // LocalizationService calls GetCultureInfo on these, and an unresolvable code surfaces as
        // a TypeInitializationException the first time anything touches it - not as a missing
        // translation. That is why a language needs a real culture and not just a resx file.
        foreach (var info in AppLanguages.All)
        {
            var culture = System.Globalization.CultureInfo.GetCultureInfo(info.CultureCode);

            Assert.Equal(info.CultureCode, culture.Name, ignoreCase: true);
        }
    }

    [Fact]
    public void EveryRightToLeftLanguage_IsFlagged()
    {
        // Three scripts read right to left, and the UI mirrors on all of them.
        Assert.True(AppLanguage.Arabic.IsRightToLeft());
        Assert.True(AppLanguage.Persian.IsRightToLeft());
        Assert.True(AppLanguage.Hebrew.IsRightToLeft());

        Assert.False(AppLanguage.English.IsRightToLeft());
        Assert.False(AppLanguage.Hindi.IsRightToLeft());
    }

    [Fact]
    public async Task FirstRun_TakesTheLanguageFromTheDevice()
    {
        await using var temp = new TemporaryDatabase();
        var repository = new SqliteSettingsRepository(temp.Database, AppLanguage.Polish);

        var settings = await repository.LoadAsync();

        Assert.Equal(AppLanguage.Polish, settings.Language);
    }

    [Fact]
    public async Task TheSeededLanguage_IsRecorded_SoALaterDeviceChangeCannotMoveIt()
    {
        await using var temp = new TemporaryDatabase();

        await new SqliteSettingsRepository(temp.Database, AppLanguage.Polish).LoadAsync();

        // Second launch, phone since switched to German. The first run recorded a choice, and
        // from then on only the player changes it.
        var loaded = await new SqliteSettingsRepository(temp.Database, AppLanguage.German).LoadAsync();

        Assert.Equal(AppLanguage.Polish, loaded.Language);
    }

    [Fact]
    public async Task AChosenLanguage_IsNotOverriddenByTheDevice()
    {
        await using var temp = new TemporaryDatabase();

        await new SqliteSettingsRepository(temp.Database)
            .SaveAsync(GameSettings.Default with { Language = AppLanguage.Spanish });

        var loaded = await new SqliteSettingsRepository(temp.Database, AppLanguage.Polish).LoadAsync();

        Assert.Equal(AppLanguage.Spanish, loaded.Language);
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

        // Exactly the three right-to-left scripts the game ships, no more - a wrongly flagged
        // language mirrors the whole UI. See EveryRightToLeftLanguage_IsFlagged.
        Assert.Equal(3, AppLanguages.All.Count(l => l.IsRightToLeft));
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
