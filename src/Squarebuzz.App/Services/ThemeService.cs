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

    /// <summary>The player's stored choice, which is not the same as what is on screen under Auto.</summary>
    private GameTheme _chosenTheme = GameTheme.Light;

    private bool _isSubscribed;

    public event EventHandler? Changed;

    public GameTheme Theme { get; private set; } = GameTheme.Light;

    public GameAccent Accent { get; private set; } = GameAccent.Tangerine;

    public bool FollowsSystem { get; private set; }

    public void Apply(GameTheme theme, GameAccent accent, bool followSystem = false)
    {
        _chosenTheme = theme;
        FollowsSystem = followSystem;

        var app = Application.Current;

        // Following the OS means we must not force a theme on it. UserAppTheme overrides
        // PlatformAppTheme, so leaving it Unspecified is what keeps the OS value readable -
        // and it is also what lets MAUI's own AppThemeBinding-driven chrome follow along.
        if (app is not null)
        {
            if (followSystem)
            {
                app.UserAppTheme = AppTheme.Unspecified;
            }

            Subscribe(app, followSystem);
        }

        ApplyResolved(Resolve(theme, followSystem), accent);
    }

    /// <summary>
    /// Turns the stored choice into the theme to show.
    /// </summary>
    /// <remarks>
    /// Colour-blind is deliberately not overridden by the system setting. It is an accessibility
    /// palette rather than a brightness, so a child who needs it must keep it whatever the phone
    /// is doing at sunset.
    /// </remarks>
    private static GameTheme Resolve(GameTheme chosen, bool followSystem)
    {
        if (!followSystem || chosen == GameTheme.ColorBlind)
        {
            return chosen;
        }

        // PlatformAppTheme, not RequestedTheme: the latter reports whatever UserAppTheme was
        // last set to, which would make this read back our own answer instead of the OS's.
        return Application.Current?.PlatformAppTheme == AppTheme.Dark
            ? GameTheme.Dark
            : GameTheme.Light;
    }

    private void Subscribe(Application app, bool followSystem)
    {
        if (followSystem && !_isSubscribed)
        {
            app.RequestedThemeChanged += OnSystemThemeChanged;
            _isSubscribed = true;
        }
        else if (!followSystem && _isSubscribed)
        {
            app.RequestedThemeChanged -= OnSystemThemeChanged;
            _isSubscribed = false;
        }
    }

    private void OnSystemThemeChanged(object? sender, AppThemeChangedEventArgs e)
    {
        if (!FollowsSystem)
        {
            return;
        }

        // Resources are touched, so this has to be on the UI thread; the event can arrive on
        // a platform thread.
        MainThread.BeginInvokeOnMainThread(() =>
            ApplyResolved(Resolve(_chosenTheme, followSystem: true), Accent));
    }

    private void ApplyResolved(GameTheme theme, GameAccent accent)
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

        // Keep the platform chrome (status bar, title bar) in step with our own palette. Under
        // Auto this is left alone: forcing it here would override PlatformAppTheme and cut off
        // the very signal we are following.
        if (!FollowsSystem && Application.Current is { } app)
        {
            app.UserAppTheme = theme == GameTheme.Dark ? AppTheme.Dark : AppTheme.Light;
        }

        // After the dictionaries are in place, so a listener that re-reads colours sees the new
        // ones. Main thread by construction: Options calls Apply from the UI, and the system
        // flip dispatches through BeginInvokeOnMainThread above.
        Changed?.Invoke(this, EventArgs.Empty);
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
