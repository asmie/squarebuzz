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
    GameTheme Theme { get; }

    GameAccent Accent { get; }

    /// <summary>
    /// Swaps in the dictionaries for <paramref name="theme"/> and <paramref name="accent"/>.
    /// Safe to call repeatedly; a no-op when nothing changed.
    /// </summary>
    void Apply(GameTheme theme, GameAccent accent);
}
