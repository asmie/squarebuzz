using CommunityToolkit.Mvvm.ComponentModel;
using Squarebuzz.App.Services;
using Squarebuzz.Core.Abstractions;

namespace Squarebuzz.App.ViewModels;

/// <summary>
/// The loading screen. Fills a progress bar while startup work happens, then moves on to
/// onboarding on a first run or straight to the menu afterwards.
/// </summary>
public partial class SplashViewModel : LocalizedViewModel
{
    /// <summary>
    /// Matches the prototype's 7% every 90ms - about 1.2 seconds, long enough to register as
    /// a deliberate introduction rather than a stutter.
    /// </summary>
    private const double ProgressStep = 0.07;
    private static readonly TimeSpan StepDelay = TimeSpan.FromMilliseconds(90);

    private readonly ISettingsRepository _settingsRepository;
    private readonly IThemeService _theme;
    private readonly INavigationService _navigation;
    private readonly IScreenTimeMonitor _screenTime;
    private readonly IAudioService _audio;
    private readonly INarrationService _narration;
    private readonly ISaveGameRepository _saveGames;

    public SplashViewModel(
        ILocalizationService strings,
        ISettingsRepository settingsRepository,
        IThemeService theme,
        INavigationService navigation,
        IScreenTimeMonitor screenTime,
        IAudioService audio,
        INarrationService narration,
        ISaveGameRepository saveGames)
        : base(strings)
    {
        _settingsRepository = settingsRepository;
        _theme = theme;
        _navigation = navigation;
        _screenTime = screenTime;
        _audio = audio;
        _narration = narration;
        _saveGames = saveGames;
    }

    /// <summary>0 to 1, so it binds straight to <c>ProgressBar.Progress</c> with no converter.</summary>
    [ObservableProperty]
    public partial double Progress { get; private set; }

    public string Tagline => T("tagline");

    public string LoadingLabel => T("loading");

    public override async Task OnAppearingAsync()
    {
        // Settings are read first, and awaited, so the rest of the app - and this very screen -
        // is already in the player's chosen theme and language before anything is shown. It is
        // one row from a local database, so it is not what makes a launch slow.
        var settings = await LoadSettingsAsync();

        // The slow work runs *behind* the progress bar rather than in front of it. Priming the
        // audio, purging saves and enumerating the device's voices were all awaited before the
        // bar so much as moved, which meant a launch cost that work plus the bar's own 1.6
        // seconds, and the bar sat frozen at zero for the part that actually took time - the
        // one thing it exists to cover. Started here, the bar becomes the floor rather than an
        // addition, and it is now honestly showing that something is happening.
        var preparing = PrepareServicesAsync(settings);

        while (Progress < 1)
        {
            Progress = Math.Min(1, Progress + ProgressStep);
            await Task.Delay(StepDelay);
        }

        // Usually finished long ago; awaited so a slow device still gets a full splash rather
        // than a half-prepared menu.
        await preparing;

        await Task.Delay(380);

        // Reset rather than push: the back gesture must never bring a child back to the splash.
        await _navigation.ResetToAsync(settings.HasSeenOnboarding ? Routes.Menu : Routes.Onboarding);
    }

    /// <summary>
    /// The startup work the splash screen exists to cover, in the order the first screens need it.
    /// </summary>
    private async Task PrepareServicesAsync(Core.Model.GameSettings settings)
    {
        try
        {
            // Deliberately awaited: it is the difference between the first tap on a cell being
            // silent and being audible.
            await _audio.PrimeAsync();

            // Once per launch, before the menu can show a count or Continue can list anything.
            // A generated save whose picture the current generator no longer produces would put
            // the player's marks on a board they never played, so it goes.
            await PurgeUnrebuildableSavesAsync();

            // Enumerating the device's voices is the slowest of these, and it has to finish
            // before the first onboarding card appears - that card is the one screen where a
            // brand-new player most needs the words read out.
            await _narration.PrepareAsync(settings.Language);
            _narration.Configure(settings.VoiceNarration);
        }
        catch (Exception)
        {
            // Sound or narration missing is a degraded game, not a broken one, and the player is
            // better served by the menu than by a splash screen that never ends.
        }
    }

    private async Task PurgeUnrebuildableSavesAsync()
    {
        try
        {
            await _saveGames.PurgeUnrebuildableAsync();
        }
        catch (Exception)
        {
            // Worst case a stale save survives to confuse someone. Not worth blocking startup.
        }
    }

    private async Task<Core.Model.GameSettings> LoadSettingsAsync()
    {
        try
        {
            var settings = await _settingsRepository.LoadAsync();

            _theme.Apply(settings.Theme, settings.Accent, settings.FollowSystemTheme);
            Strings.SetLanguage(settings.Language);

            // The reminder has to be armed before the first board opens, not when Options is
            // first visited - a child who goes straight into a game must still be counted.
            _screenTime.Configure(settings.ScreenTimeLimitMinutes);

            // Applied before priming so the loop does not briefly start for a player who has
            // music switched off; PrimeAsync re-applies once the assets are actually loaded.
            _audio.Configure(settings.SoundEffects, settings.Music);

            return settings;
        }
        catch (Exception)
        {
            // A corrupt or locked database must not strand the player on the splash screen.
            // Defaults get them into the game; Options can put things right.
            return Core.Model.GameSettings.Default;
        }
    }
}
