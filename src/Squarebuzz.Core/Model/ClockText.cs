using System.Globalization;

namespace Squarebuzz.Core.Model;

/// <summary>Durations as the game shows them everywhere: minutes and two-digit seconds.</summary>
/// <remarks>
/// One definition for the trial cards, the running clock, the win overlay and the save list,
/// which each used to build the same string by hand. Minutes are not capped at 59, so a long
/// game reads "75:03" rather than wrapping.
/// </remarks>
public static class ClockText
{
    /// <summary>"m:ss" - e.g. 3:07, 12:00, 75:03.</summary>
    public static string Of(TimeSpan duration) =>
        string.Create(CultureInfo.InvariantCulture, $"{(int)duration.TotalMinutes}:{duration.Seconds:00}");
}
