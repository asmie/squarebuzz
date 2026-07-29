using Squarebuzz.App.Services;
using Squarebuzz.Core.Model;

namespace Squarebuzz.App;

public partial class App : Application
{
    private readonly IServiceProvider _services;

    public App(IServiceProvider services, IThemeService themeService)
    {
        _services = services;

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

        return new Window(shell) { Title = "squarebuzz" };
    }
}
