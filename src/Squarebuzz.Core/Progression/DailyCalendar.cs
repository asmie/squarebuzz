namespace Squarebuzz.Core.Progression;

/// <summary>One cell of the daily calendar: a day of the month, or a leading blank.</summary>
/// <param name="Day">Day of the month, or 0 for a blank cell that pads the first week.</param>
/// <param name="IsDone">The daily was finished on this day.</param>
/// <param name="IsToday">This cell is today.</param>
public sealed record CalendarDay(int Day, bool IsDone, bool IsToday)
{
    /// <summary>A padding cell before the month starts.</summary>
    public static CalendarDay Blank { get; } = new(0, false, false);

    public bool IsBlank => Day == 0;
}

/// <summary>
/// Shapes a month of daily-puzzle history into the grid the Trials screen draws.
/// </summary>
/// <remarks>
/// Pure shaping, kept out of the ViewModel so the fiddly parts - which weekday the month starts
/// on, how many blanks pad the first row - can be pinned by tests. The grid is week-per-row with
/// a caller-chosen first day of week, because Monday starts the week for a Polish child and
/// Sunday for an American one.
/// </remarks>
public static class DailyCalendar
{
    /// <summary>
    /// Cells for <paramref name="today"/>'s month: leading blanks, then one cell per day.
    /// </summary>
    public static IReadOnlyList<CalendarDay> Build(
        DateOnly today,
        IReadOnlyCollection<DateOnly> completions,
        DayOfWeek firstDayOfWeek)
    {
        ArgumentNullException.ThrowIfNull(completions);

        var done = new HashSet<int>();

        foreach (var date in completions)
        {
            if (date.Year == today.Year && date.Month == today.Month)
            {
                done.Add(date.Day);
            }
        }

        var first = new DateOnly(today.Year, today.Month, 1);
        var leadingBlanks = ((int)first.DayOfWeek - (int)firstDayOfWeek + 7) % 7;
        var daysInMonth = DateTime.DaysInMonth(today.Year, today.Month);

        var cells = new List<CalendarDay>(leadingBlanks + daysInMonth);

        for (var i = 0; i < leadingBlanks; i++)
        {
            cells.Add(CalendarDay.Blank);
        }

        for (var day = 1; day <= daysInMonth; day++)
        {
            cells.Add(new CalendarDay(day, done.Contains(day), day == today.Day));
        }

        return cells;
    }
}
