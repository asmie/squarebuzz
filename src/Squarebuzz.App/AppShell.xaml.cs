using Squarebuzz.App.Services;
using Squarebuzz.App.Views;
using Squarebuzz.Core.Model;

namespace Squarebuzz.App;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();
        RegisterRoutes();

        // Arabic reads right to left, so the whole shell mirrors: rows of chips, the board's
        // clue gutters, the back gesture. Set on the Shell rather than per page, because
        // FlowDirection inherits and every screen lives inside this one.
        //
        // Hooked to the language service, which is the single funnel every language change goes
        // through - Options taps and the saved language the splash applies both arrive here.
        // The Shell is a singleton for the app's lifetime, so this is never unhooked.
        LocalizationService.Instance.LanguageChanged += (_, _) =>
            MainThread.BeginInvokeOnMainThread(ApplyLanguagePresentation);

        ApplyLanguagePresentation();
    }

    /// <summary>The two things that follow the language rather than the theme: direction and face.</summary>
    private void ApplyLanguagePresentation()
    {
        var language = LocalizationService.Instance.Language;

        FlowDirection = language.IsRightToLeft()
            ? FlowDirection.RightToLeft
            : FlowDirection.LeftToRight;

        // Headings fall back to the body face where the display face has no glyphs, so a word is
        // never half Fredoka and half whatever the platform found. See AppLanguages.All.
        if (Application.Current is { } app)
        {
            app.Resources["DisplayFontFamily"] = language.DisplayFontCovers() ? "Display" : "BodyBold";
        }
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
        Routing.RegisterRoute(Routes.Gallery, typeof(GalleryPage));
        Routing.RegisterRoute(Routes.About, typeof(AboutPage));
        Routing.RegisterRoute(Routes.HowTo, typeof(HowToPage));
        Routing.RegisterRoute(Routes.Trials, typeof(TrialsPage));

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
