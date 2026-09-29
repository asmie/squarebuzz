namespace Squarebuzz.Data.Repositories;

/// <summary>Converts stored values to domain values with conservative fallbacks.</summary>
/// <remarks>
/// Repositories keep readable progress when non-key fields are corrupt. Rows with an
/// unusable primary key are skipped by the caller.
/// </remarks>
internal static class RowGuards
{
    /// <summary>
    /// A duration from a seconds column, or zero for anything that is not a finite, non-negative
    /// number small enough to be a duration.
    /// </summary>
    public static TimeSpan SecondsOrZero(double seconds) =>
        IsUsableSeconds(seconds) ? TimeSpan.FromSeconds(seconds) : TimeSpan.Zero;

    /// <summary>True when a seconds column holds a usable duration - see <see cref="SecondsOrZero"/>.</summary>
    public static bool IsUsableSeconds(double seconds) =>
        double.IsFinite(seconds) && seconds >= 0 && seconds <= TimeSpan.MaxValue.TotalSeconds;

    /// <summary>A date from a day-number column, or null when the number is outside what a date can hold.</summary>
    public static DateOnly? DateOrNull(int? dayNumber) =>
        dayNumber is { } day && day >= DateOnly.MinValue.DayNumber && day <= DateOnly.MaxValue.DayNumber
            ? DateOnly.FromDayNumber(day)
            : null;

    /// <summary>Returns a stored date, or DateOnly.MinValue when an earned item has an invalid date.</summary>
    public static DateOnly DateOrMin(int dayNumber) => DateOrNull(dayNumber) ?? DateOnly.MinValue;

    /// <summary>
    /// An instant from a UTC-ticks column plus an offset column, tolerating either being out of
    /// range.
    /// </summary>
    /// <remarks>
    /// A bad offset keeps the instant and shows it in UTC; bad ticks fall back to the Unix epoch,
    /// which sorts oldest and reads as "1 Jan 1970" - clearly not a real save date, and a row
    /// that old is the first to be trimmed.
    /// </remarks>
    public static DateTimeOffset InstantOrEpoch(long utcTicks, long offsetTicks)
    {
        if (utcTicks < DateTime.MinValue.Ticks || utcTicks > DateTime.MaxValue.Ticks)
        {
            return DateTimeOffset.UnixEpoch;
        }

        var utc = new DateTimeOffset(utcTicks, TimeSpan.Zero);
        var offset = new TimeSpan(offsetTicks);

        // DateTimeOffset requires whole-minute offsets within fourteen hours either way.
        if (offsetTicks % TimeSpan.TicksPerMinute != 0
            || offset < TimeSpan.FromHours(-14) || offset > TimeSpan.FromHours(14))
        {
            return utc;
        }

        try
        {
            return utc.ToOffset(offset);
        }
        catch (ArgumentOutOfRangeException)
        {
            // The offset would push the local time past the representable range.
            return utc;
        }
    }

    /// <summary>A count that can only sensibly be zero or more.</summary>
    public static int NonNegative(int value) => Math.Max(0, value);
}
