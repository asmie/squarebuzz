using Squarebuzz.Core.Model;
using Squarebuzz.Presentation.Services;

namespace Squarebuzz.App;

public partial class App : Application
{
    private readonly IServiceProvider _services;
    private readonly IAudioService _audio;
    private readonly GameLifecycle _gameLifecycle;

    public App(IServiceProvider services, IThemeService themeService, IAudioService audio, GameLifecycle gameLifecycle)
    {
        _services = services;
        _audio = audio;
        _gameLifecycle = gameLifecycle;

        InitializeComponent();

        // App.xaml merges only the colour-agnostic styles, so the theme and accent
        // dictionaries must be applied before the first page is built - otherwise every
        // DynamicResource colour lookup misses and the UI falls back to platform defaults.
        // Once settings persistence lands these values come from the saved settings.
        themeService.Apply(GameTheme.Light, GameAccent.Tangerine);
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        // Resolved through DI so the Shell's pages and ViewModels get their dependencies.
        var shell = _services.GetRequiredService<AppShell>();

        var window = new Window(shell) { Title = "squarebuzz" };

        // Page disappearance only covers in-app navigation. Window events cover Home,
        // app switching and minimizing; stop the game before awaiting its background save.
        window.Stopped += async (_, _) =>
        {
            var saving = _gameLifecycle.SuspendAsync();
            _audio.SuspendMusic();
            await saving;
        };
        window.Resumed += OnWindowResumed;

        // Desktop visibility/focus restoration also arrives through Activated. Both handlers
        // are idempotent, so activation after a mobile Resumed event cannot reset the clock.
        window.Activated += OnWindowResumed;

        return window;
    }

    private void OnWindowResumed(object? sender, EventArgs e)
    {
        _gameLifecycle.Resume();
        _audio.ResumeMusic();
    }
}
