using CommunityToolkit.Maui;
using Microsoft.Extensions.Logging;
using Squarebuzz.App.Services;
using Squarebuzz.App.ViewModels;
using Squarebuzz.App.Views;
using Squarebuzz.Core.Abstractions;
using Squarebuzz.Core.Content;
using Squarebuzz.Core.Generation;
using Squarebuzz.Core.Model;

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
            .ConfigureFonts(fonts =>
            {
                // The prototype uses Fredoka (display) and Quicksand (body) from Google Fonts.
                // Drop the .ttf files into Resources/Fonts and register them here - see the
                // README in that folder. Until then the platform default is used, which
                // changes the feel but nothing functional.
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

        RegisterDomain(services);
    }

    /// <summary>
    /// The Squarebuzz.Core object graph. Registered against its interfaces so the app depends
    /// on the domain's contracts rather than its implementations.
    /// </summary>
    private static void RegisterDomain(IServiceCollection services)
    {
        services.AddSingleton<IClock, SystemClock>();

        // Authored content is immutable and parsed once from an embedded resource.
        services.AddSingleton<IPuzzleRepository, EmbeddedPuzzleRepository>();

        // Decorator: the blob generator invents pictures, and UniqueSolutionGenerator
        // refuses to pass on any that cannot be solved by logic alone. Only the wrapped
        // form is exposed, so nothing can accidentally take an unvalidated puzzle.
        services.AddSingleton<IPuzzleGenerator>(_ => new UniqueSolutionGenerator(new BlobPuzzleGenerator()));

        services.AddSingleton<GameSessionFactory>();
    }

    private static void RegisterViewModels(IServiceCollection services)
    {
        // Transient: a fresh ViewModel per navigation, so screens never inherit stale state.
        services.AddTransient<StartupViewModel>();
    }

    private static void RegisterPages(IServiceCollection services)
    {
        services.AddTransient<StartupPage>();
    }
}
