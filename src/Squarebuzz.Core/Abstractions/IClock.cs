namespace Squarebuzz.Core.Abstractions;

/// <summary>
/// Wraps the current time so streaks, the daily puzzle and elapsed timers can be tested
/// without waiting or depending on the machine clock.
/// </summary>
public interface IClock
{
    DateTimeOffset Now { get; }

    /// <summary>The player's local date, which is what "today's puzzle" and streaks turn on.</summary>
    DateOnly Today { get; }

    /// <summary>
    /// A reading that only ever moves forward, for measuring how long something took.
    /// </summary>
    /// <remarks>
    /// Not <see cref="Now"/>: the wall clock jumps when the player changes the time or the
    /// network corrects it, and a game clock that jumped with it would hand out or steal minutes.
    /// Only differences between two readings mean anything; the absolute value does not.
    /// </remarks>
    TimeSpan Monotonic { get; }
}

/// <summary>The real clock. Registered in DI for the running app.</summary>
public sealed class SystemClock : IClock
{
    public DateTimeOffset Now => DateTimeOffset.Now;

    public DateOnly Today => DateOnly.FromDateTime(DateTime.Now);

    /// <summary>Milliseconds since boot, which the OS guarantees never runs backwards.</summary>
    public TimeSpan Monotonic => TimeSpan.FromMilliseconds(Environment.TickCount64);
}
