namespace Squarebuzz.Core.Abstractions;

/// <summary>
/// Counts how long the player has been playing and says when the parent-set limit is reached.
/// </summary>
/// <remarks>
/// <para>
/// This has to outlive a single board. A child who finishes five puzzles has been on the screen
/// for the sum of all five, so the count belongs to the app run rather than to a
/// <c>GameSession</c> - which is why it is a singleton service and not a property of the game.
/// </para>
/// <para>
/// It counts time spent <em>playing</em>, not time the app is open: a board left on screen while
/// the timer is paused is not screen time a parent is trying to limit, and the game already
/// knows when the clock is running.
/// </para>
/// </remarks>
public interface IScreenTimeMonitor
{
    /// <summary>Total play time counted so far in this app run.</summary>
    TimeSpan Played { get; }

    /// <summary>The limit in force, or null when reminders are off.</summary>
    int? LimitMinutes { get; }

    /// <summary>
    /// Sets the limit. Called whenever settings load or change, so turning the reminder off
    /// mid-session takes effect at once.
    /// </summary>
    void Configure(int? limitMinutes);

    /// <summary>
    /// Adds <paramref name="elapsed"/> to the count.
    /// </summary>
    /// <returns>
    /// True on the single call that carries the count past the limit, and never again until
    /// <see cref="Restart"/>. Returning it exactly once is what stops the reminder reappearing
    /// every second.
    /// </returns>
    bool Add(TimeSpan elapsed);

    /// <summary>Clears the count and re-arms the reminder.</summary>
    void Restart();
}
