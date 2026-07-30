using Squarebuzz.Core.Model;

namespace Squarebuzz.App.Services;

/// <summary>
/// Applies the active <see cref="GameTheme"/> and <see cref="GameAccent"/> to the app's
/// resource dictionaries. The prototype supports 3 themes x 3 accents, which
/// <c>AppThemeBinding</c> alone cannot express, so the colours are swapped as whole
/// dictionaries instead.
/// </summary>
public interface IThemeService
{
    /// <summary>The theme actually on screen, which under <see cref="FollowsSystem"/> is the one the OS asked for.</summary>
    GameTheme Theme { get; }

    GameAccent Accent { get; }

    /// <summary>True when light/dark is being taken from the operating system.</summary>
    bool FollowsSystem { get; }

    /// <summary>
    /// Swaps in the dictionaries for <paramref name="theme"/> and <paramref name="accent"/>.
    /// Safe to call repeatedly; a no-op when nothing changed.
    /// </summary>
    /// <param name="theme">
    /// The player's chosen theme. Ignored for light/dark purposes when
    /// <paramref name="followSystem"/> is set, except for
    /// <see cref="GameTheme.ColorBlind"/>, which is a palette rather than a brightness.
    /// </param>
    /// <param name="followSystem">
    /// Take light/dark from the OS, and keep following it while the app runs.
    /// </param>
    void Apply(GameTheme theme, GameAccent accent, bool followSystem = false);
}
