using Squarebuzz.Core.Model;
using Squarebuzz.Presentation.Services;

namespace Squarebuzz.App;

public partial class App : Application
{
    private readonly IServiceProvider _services;
    private readonly IAudioService _audio;

    public App(IServiceProvider services, IThemeService themeService, IAudioService audio)
    {
        _services = services;
        _audio = audio;

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

        // Background music that carries on after the child has switched to something else is a
        // bug, not a feature - and on a phone it is the kind that gets an app deleted. Stopped
        // and Resumed are the window-level equivalents of the old OnSleep/OnResume.
        window.Stopped += (_, _) => _audio.SuspendMusic();
        window.Resumed += (_, _) => _audio.ResumeMusic();

        return window;
    }
}
