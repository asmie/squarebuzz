namespace Squarebuzz.Core.Model;

/// <summary>
/// Visual theme. Named <c>GameTheme</c> rather than <c>AppTheme</c> to avoid colliding with
/// <c>Microsoft.Maui.AppTheme</c>, and because this domain has a third option that the
/// platform light/dark switch cannot express.
/// </summary>
public enum GameTheme
{
    Light,
    Dark,

    /// <summary>Colour-blind friendly blue/orange palette. Patterns are kept on cells so
    /// fill state never relies on hue alone.</summary>
    ColorBlind,
}
