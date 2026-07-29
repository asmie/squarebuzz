using Squarebuzz.App.Resources.Themes;
using Squarebuzz.App.Resources.Themes.Accents;
using Squarebuzz.Core.Model;

namespace Squarebuzz.App.Services;

/// <inheritdoc />
public sealed class ThemeService : IThemeService
{
    private ResourceDictionary? _appliedTheme;
    private ResourceDictionary? _appliedAccent;
    private bool _hasApplied;

    public GameTheme Theme { get; private set; } = GameTheme.Light;

    public GameAccent Accent { get; private set; } = GameAccent.Tangerine;

    public void Apply(GameTheme theme, GameAccent accent)
    {
        if (_hasApplied && theme == Theme && accent == Accent)
        {
            return;
        }

        var merged = Application.Current?.Resources.MergedDictionaries;
        if (merged is null)
        {
            // No application object yet (unit test or very early startup). Record the
            // selection so the next Apply after startup still does the right thing.
            Theme = theme;
            Accent = accent;
            return;
        }

        if (_appliedTheme is not null)
        {
            merged.Remove(_appliedTheme);
        }

        if (_appliedAccent is not null)
        {
            merged.Remove(_appliedAccent);
        }

        _appliedTheme = CreateTheme(theme);
        _appliedAccent = CreateAccent(theme, accent);

        // Theme first, then accent: the accent intentionally overrides a few theme colours.
        merged.Add(_appliedTheme);
        merged.Add(_appliedAccent);

        Theme = theme;
        Accent = accent;
        _hasApplied = true;

        // Keep the platform chrome (status bar, title bar) in step with our own palette.
        if (Application.Current is { } app)
        {
            app.UserAppTheme = theme == GameTheme.Dark ? AppTheme.Dark : AppTheme.Light;
        }
    }

    private static ResourceDictionary CreateTheme(GameTheme theme) => theme switch
    {
        GameTheme.Dark => new Dark(),
        GameTheme.ColorBlind => new ColorBlind(),
        _ => new Light(),
    };

    /// <summary>
    /// Grape needs lighter violets to stay legible on the dark background, so it has a
    /// theme-specific variant. Tangerine and Trio use one set of values everywhere,
    /// matching the prototype's CSS.
    /// </summary>
    private static ResourceDictionary CreateAccent(GameTheme theme, GameAccent accent) => accent switch
    {
        GameAccent.Grape when theme == GameTheme.Dark => new GrapeDark(),
        GameAccent.Grape => new Grape(),
        GameAccent.Trio => new Trio(),
        _ => new Tangerine(),
    };
}
