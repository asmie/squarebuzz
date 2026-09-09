namespace Squarebuzz.Data.Repositories;

/// <summary>
/// Turns the raw columns of a row into domain values without ever throwing.
/// </summary>
/// <remarks>
/// <para>
/// Every repository reads whole tables into lists, so one row that cannot be converted takes the
/// entire screen down with it: a single bad <c>saved_at_ticks</c> emptied the Continue list, and
/// a single bad <c>first_solved_day</c> emptied the Gallery, Trials and the menu counters. The
/// database is private storage, but it is still a file - hand-edited by a curious parent, copied
/// between builds, or half-written when a battery died - and the readers already defended some
/// columns (cell bytes, enum values) while trusting others to a framework parser that throws.
/// </para>
/// <para>
/// The rule here is to lose as little as possible: an unreadable field falls back to a value that
/// is visibly wrong rather than dropping the row, because the player's progress or board is worth
/// more than its timestamp. Only a field the row cannot be addressed without - its primary key -
/// justifies skipping the row.
/// </para>
/// </remarks>
internal static class RowGuards
{
    /// <summary>
    /// A duration from a seconds column, or zero for anything that is not a finite, non-negative
    /// number small enough to be a duration.
    /// </summary>
    public static TimeSpan SecondsOrZero(double seconds) =>
        double.IsFinite(seconds) && seconds >= 0 && seconds <= TimeSpan.MaxValue.TotalSeconds
            ? TimeSpan.FromSeconds(seconds)
            : TimeSpan.Zero;

    /// <summary>True when a seconds column holds a usable duration - see <see cref="SecondsOrZero"/>.</summary>
    public static bool IsUsableSeconds(double seconds) =>
        double.IsFinite(seconds) && seconds >= 0 && seconds <= TimeSpan.MaxValue.TotalSeconds;

    /// <summary>A date from a day-number column, or null when the number is outside what a date can hold.</summary>
    public static DateOnly? DateOrNull(int? dayNumber) =>
        dayNumber is { } day && day >= DateOnly.MinValue.DayNumber && day <= DateOnly.MaxValue.DayNumber
            ? DateOnly.FromDayNumber(day)
            : null;

    /// <summary>
    /// A date from a day-number column, or <see cref="DateOnly.MinValue"/> when it cannot be one.
    /// </summary>
    /// <remarks>
    /// For rows where the date is decoration and the rest is progress: a trophy or a solved
    /// picture is still earned even if nobody can say when. Year 1 is honest garbage in a way a
    /// plausible fabricated date would not be.
    /// </remarks>
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

        // DateTimeOffset allows offsets within fourteen hours either way and rejects the rest.
        if (offset < TimeSpan.FromHours(-14) || offset > TimeSpan.FromHours(14))
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
