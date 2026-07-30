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

    public SplashViewModel(
        ILocalizationService strings,
        ISettingsRepository settingsRepository,
        IThemeService theme,
        INavigationService navigation,
        IScreenTimeMonitor screenTime)
        : base(strings)
    {
        _settingsRepository = settingsRepository;
        _theme = theme;
        _navigation = navigation;
        _screenTime = screenTime;
    }

    /// <summary>0 to 1, so it binds straight to <c>ProgressBar.Progress</c> with no converter.</summary>
    [ObservableProperty]
    public partial double Progress { get; private set; }

    public string Tagline => T("tagline");

    public string LoadingLabel => T("loading");

    public override async Task OnAppearingAsync()
    {
        // Settings are read first so the rest of the app - and this very screen - is already
        // in the player's chosen theme and language before anything else is shown.
        var settings = await LoadSettingsAsync();

        while (Progress < 1)
        {
            Progress = Math.Min(1, Progress + ProgressStep);
            await Task.Delay(StepDelay);
        }

        await Task.Delay(380);

        // Reset rather than push: the back gesture must never bring a child back to the splash.
        await _navigation.ResetToAsync(settings.HasSeenOnboarding ? Routes.Menu : Routes.Onboarding);
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
