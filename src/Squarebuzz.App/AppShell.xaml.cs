using Squarebuzz.App.Views;

namespace Squarebuzz.App;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();
        RegisterRoutes();
    }

    /// <summary>
    /// Pushed routes. Splash, onboarding and menu are declared in XAML instead, because only a
    /// <c>ShellContent</c> route can be the target of a stack-replacing "//" navigation.
    /// </summary>
    private static void RegisterRoutes()
    {
        Routing.RegisterRoute(Routes.NewGame, typeof(NewGamePage));
        Routing.RegisterRoute(Routes.Game, typeof(GamePage));
        Routing.RegisterRoute(Routes.Options, typeof(OptionsPage));
        Routing.RegisterRoute(Routes.Continue, typeof(ContinuePage));

        // Scaffolded screens. They share ComingSoonPage and differ only by the heading
        // key passed at navigation time, so every route works end to end today.
        Routing.RegisterRoute(Routes.Trials, typeof(ComingSoonPage));
        Routing.RegisterRoute(Routes.About, typeof(ComingSoonPage));
        Routing.RegisterRoute(Routes.Gallery, typeof(ComingSoonPage));
        Routing.RegisterRoute(Routes.HowTo, typeof(ComingSoonPage));
    }
}

/// <summary>Route names, kept in one place so navigation calls cannot drift from registration.</summary>
public static class Routes
{
    public const string Splash = "splash";
    public const string Onboarding = "onboarding";
    public const string Menu = "menu";
    public const string NewGame = "newgame";
    public const string Game = "game";
    public const string Continue = "continue";
    public const string Trials = "trials";
    public const string Options = "options";
    public const string About = "about";
    public const string Gallery = "gallery";
    public const string HowTo = "howto";
}
