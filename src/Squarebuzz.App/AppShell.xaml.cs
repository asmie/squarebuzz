namespace Squarebuzz.App;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();
        RegisterRoutes();
    }

    /// <summary>
    /// Screen routes. Each is added here as its page is built, so navigation stays
    /// declarative and testable rather than page-constructing by hand.
    /// </summary>
    private static void RegisterRoutes()
    {
        Routing.RegisterRoute(Routes.Game, typeof(Views.GamePage));

        // Registered as the remaining screens land:
        //   Routing.RegisterRoute(Routes.Menu, typeof(MenuPage));
        //   Routing.RegisterRoute(Routes.NewGame, typeof(NewGamePage));
        //   ...
    }
}

/// <summary>Route names, kept in one place so navigation calls cannot drift from registration.</summary>
public static class Routes
{
    public const string Startup = "startup";
    public const string Splash = "splash";
    public const string Onboarding = "onboarding";
    public const string Menu = "menu";
    public const string NewGame = "newgame";
    public const string Game = "game";
    public const string Paused = "paused";
    public const string Complete = "complete";
    public const string Continue = "continue";
    public const string Trials = "trials";
    public const string Options = "options";
    public const string About = "about";
    public const string Gallery = "gallery";
    public const string HowTo = "howto";
}
