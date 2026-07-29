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
}

/// <summary>The real clock. Registered in DI for the running app.</summary>
public sealed class SystemClock : IClock
{
    public DateTimeOffset Now => DateTimeOffset.Now;

    public DateOnly Today => DateOnly.FromDateTime(DateTime.Now);
}
