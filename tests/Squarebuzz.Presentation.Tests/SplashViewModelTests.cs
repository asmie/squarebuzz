using Squarebuzz.Presentation.Navigation;
using Squarebuzz.Presentation.Tests.Fakes;
using Squarebuzz.Presentation.ViewModels;
using Xunit;

namespace Squarebuzz.Presentation.Tests;

/// <summary>
/// The splash exists to run startup work once and then get out of the way. Each of its steps is
/// independent, and none may stop another or hold the player on the screen.
/// </summary>
public class SplashViewModelTests
{
    private readonly FakeAudioService _audio = new();
    private readonly FakeSaveGameRepository _saveGames = new();
    private readonly FakeNavigationService _navigation = new();

    private SplashViewModel NewSplash() => new(
        new FakeLocalizationService(),
        new FakeSettingsRepository(),
        new FakeThemeService(),
        _navigation,
        new FakeScreenTimeMonitor(),
        _audio,
        new FakeNarrationService(),
        _saveGames);

    [Fact]
    public async Task ANormalLaunch_PurgesUnrebuildableSavesExactlyOnce()
    {
        // The menu's count and Continue's list both read the save table after this, so the purge
        // must have happened - and only once, or a second pass could race the first screen.
        using var splash = NewSplash();

        await splash.OnAppearingAsync();

        Assert.Equal(1, _saveGames.PurgeCalls);
        // Default settings have not seen onboarding, so a first launch resets there rather than to
        // the menu. What matters here is that the splash moved on at all, with a reset so the back
        // gesture cannot return to it.
        Assert.Equal(new FakeNavigationService.Request(Routes.Onboarding, null, IsReset: true), _navigation.Last);
    }

    [Fact]
    // The steps used to share one try block, so an audio failure quietly skipped the purge that
    // came after it. A silent game is a degraded game; a stale save Continue then offers is a
    // board the player never played. Neither may stop the other.
    public async Task AnAudioFailure_DoesNotSkipThePurgeOrStrandThePlayer()
    {
        _audio.PrimeFails = true;
        using var splash = NewSplash();

        await splash.OnAppearingAsync();

        Assert.Equal(1, _saveGames.PurgeCalls);
        // Default settings have not seen onboarding, so a first launch resets there rather than to
        // the menu. What matters here is that the splash moved on at all, with a reset so the back
        // gesture cannot return to it.
        Assert.Equal(new FakeNavigationService.Request(Routes.Onboarding, null, IsReset: true), _navigation.Last);
    }
}
