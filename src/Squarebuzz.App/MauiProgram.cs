using System.Globalization;
using CommunityToolkit.Maui;
using Microsoft.Extensions.Logging;
using Plugin.Maui.Audio;
using Squarebuzz.App.Services;
using Squarebuzz.App.Views;
using Squarebuzz.Presentation.Services;
using Squarebuzz.Presentation.ViewModels;
using Squarebuzz.Core.Abstractions;
using Squarebuzz.Core.Content;
using Squarebuzz.Core.Generation;
using Squarebuzz.Core.Model;
using Squarebuzz.Core.Progression;
using Squarebuzz.Data;
using Squarebuzz.Data.Repositories;

namespace Squarebuzz.App;

/// <summary>
/// Composition root. Every dependency is registered here and nowhere else, so the object
/// graph for the whole app is readable in one place.
/// </summary>
public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();

        builder
            .UseMauiApp<App>()
            .UseMauiCommunityToolkit()

            // Registers IAudioManager. The per-player audio attributes are set in
            // AudioService.OptionsFor rather than here: configuring them on the builder alone
            // left every player registered with USAGE_UNKNOWN.
            .AddAudio()
            .ConfigureFonts(fonts =>
            {
                // The design's two families, bundled rather than fetched: a children's game has
                // to look like itself offline. Aliases are what XAML refers to, so the styles do
                // not repeat file names.
                //
                // Both families ship upstream only as variable fonts whose default weight is 300,
                // so shipping those directly would render the whole app in Light. These are static
                // instances cut at the weights the design uses - see Resources/Fonts/README.md.
                fonts.AddFont("Fredoka-SemiBold.ttf", "Display");
                fonts.AddFont("Quicksand-Medium.ttf", "Body");
                fonts.AddFont("Quicksand-Bold.ttf", "BodyBold");
            });

        RegisterServices(builder.Services);
        RegisterViewModels(builder.Services);
        RegisterPages(builder.Services);

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }

    private static void RegisterServices(IServiceCollection services)
    {
        // Singletons: one instance for the app's lifetime.
        services.AddSingleton<IThemeService, ThemeService>();
        services.AddSingleton<AppShell>();

        // Holds the loaded players for the life of the app, so a tap never waits on file I/O.
        services.AddSingleton<IAudioService, AudioService>();

        // Singleton so one utterance can cut off the previous one across screens.
        services.AddSingleton<INarrationService, NarrationService>();

        // Read on every board open, so it must be cheap and must not cache a stale answer.
        services.AddSingleton<IAccessibilityState, AccessibilityState>();

        // Resolves to the same object XAML reaches through x:Static, so markup and code can
        // never disagree about the current language.
        services.AddSingleton<ILocalizationService, LocalizationServiceAdapter>();

        // Behind interfaces so ViewModels never touch Shell.Current or DeviceInfo directly -
        // both are statics that a test cannot substitute.
        services.AddSingleton<INavigationService, ShellNavigationService>();
        services.AddSingleton<IDeviceScreen, DeviceScreen>();

        // The three seams that keep MAUI statics - MainThread, the dispatcher's timers and
        // SemanticScreenReader - out of the ViewModel assembly.
        services.AddSingleton<IUiThread, MauiUiThread>();
        services.AddSingleton<IGameTimerFactory, DispatcherGameTimerFactory>();
        services.AddSingleton<GameLifecycle>();
        services.AddSingleton<GameCompletionService>();
        services.AddSingleton<IScreenReader, MauiScreenReader>();

        RegisterDomain(services);
    }

    /// <summary>
    /// The Squarebuzz.Core object graph. Registered against its interfaces so the app depends
    /// on the domain's contracts rather than its implementations.
    /// </summary>
    private static void RegisterDomain(IServiceCollection services)
    {
        services.AddSingleton<IClock, SystemClock>();

        // Singleton on purpose: screen time is the sum across every puzzle in this app run, so
        // a per-game instance would reset the count each time a child started a new picture.
        services.AddSingleton<IScreenTimeMonitor, ScreenTimeMonitor>();

        // Authored content is immutable and parsed once from an embedded resource.
        services.AddSingleton<IPuzzleRepository, EmbeddedPuzzleRepository>();

        // Decorator: the blob generator invents pictures, and UniqueSolutionGenerator
        // refuses to pass on any that cannot be solved by logic alone. Only the wrapped
        // form is exposed, so nothing can accidentally take an unvalidated puzzle.
        services.AddSingleton<IPuzzleGenerator>(_ => new UniqueSolutionGenerator(new BlobPuzzleGenerator()));

        services.AddSingleton<GameSessionFactory>();

        RegisterPersistence(services);
    }

    /// <summary>
    /// SQLite persistence. The database lives in <see cref="FileSystem.AppDataDirectory"/>,
    /// which is per-app private storage on every platform and is included in device backups.
    /// </summary>
    private static void RegisterPersistence(IServiceCollection services)
    {
        // One connection for the whole app - see SquarebuzzDatabase for why sharing matters.
        services.AddSingleton(_ => new SquarebuzzDatabase(
            Path.Combine(FileSystem.AppDataDirectory, "squarebuzz.db3")));

        // The device's language, for a player who has never chosen one. Read here, during
        // startup, rather than inside the factory: LocalizationService.SetLanguage assigns
        // CultureInfo.DefaultThreadCurrent*, so once the saved language has been applied this
        // would read back our own override instead of the phone's locale. FromCultureCode drops
        // the region, so pl-PL and pt-BR resolve, and anything unshipped lands on English.
        var deviceLanguage = AppLanguages.FromCultureCode(CultureInfo.CurrentUICulture.Name);

        services.AddSingleton<ISettingsRepository>(provider =>
            new SqliteSettingsRepository(provider.GetRequiredService<SquarebuzzDatabase>(), deviceLanguage));
        services.AddSingleton<ISaveGameRepository, SqliteSaveGameRepository>();
        services.AddSingleton<IProgressRepository, SqliteProgressRepository>();
        services.AddSingleton<IGameCompletionRepository, SqliteGameCompletionRepository>();
    }

    private static void RegisterViewModels(IServiceCollection services)
    {
        // Shell caches these three root screens for the app's lifetime. Pushed screens own
        // their view models instead; they must not be retained by root DI disposal tracking.
        services.AddSingleton<SplashViewModel>();
        services.AddSingleton<OnboardingViewModel>();
        services.AddSingleton<MenuViewModel>();
    }

    private static void RegisterPages(IServiceCollection services)
    {
        services.AddSingleton<SplashPage>();
        services.AddSingleton<OnboardingPage>();
        services.AddSingleton<MenuPage>();
        services.AddPageWithViewModel<NewGamePage, NewGameViewModel>();
        services.AddPageWithViewModel<GamePage, GameViewModel>();
        services.AddPageWithViewModel<OptionsPage, OptionsViewModel>();
        services.AddPageWithViewModel<ContinuePage, ContinueViewModel>();
        services.AddPageWithViewModel<GalleryPage, GalleryViewModel>();
        services.AddPageWithViewModel<AboutPage, AboutViewModel>();
        services.AddPageWithViewModel<HowToPage, HowToViewModel>();
        services.AddPageWithViewModel<TrialsPage, TrialsViewModel>();
        services.AddPageWithViewModel<LevelsPage, LevelsViewModel>();
    }
}
