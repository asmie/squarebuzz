using Squarebuzz.Core.Progression;
using Xunit;

namespace Squarebuzz.Core.Tests.Progression;

public class DailyCalendarTests
{
    // July 2026: the 1st is a Wednesday, 31 days.
    private static readonly DateOnly MidJuly = new(2026, 7, 15);

    [Fact]
    public void PadsTheFirstWeek_ForAMondayStart()
    {
        var cells = DailyCalendar.Build(MidJuly, [], DayOfWeek.Monday);

        // Wednesday the 1st sits two cells after Monday.
        Assert.Equal(2, cells.TakeWhile(c => c.IsBlank).Count());
        Assert.Equal(2 + 31, cells.Count);
    }

    [Fact]
    public void PadsTheFirstWeek_ForASundayStart()
    {
        var cells = DailyCalendar.Build(MidJuly, [], DayOfWeek.Sunday);

        Assert.Equal(3, cells.TakeWhile(c => c.IsBlank).Count());
    }

    [Fact]
    public void NumbersEveryDayOfTheMonthInOrder()
    {
        var cells = DailyCalendar.Build(MidJuly, [], DayOfWeek.Monday);
        var days = cells.Where(c => !c.IsBlank).Select(c => c.Day).ToList();

        Assert.Equal(Enumerable.Range(1, 31), days);
    }

    [Fact]
    public void MarksCompletionsInThisMonthOnly()
    {
        var cells = DailyCalendar.Build(
            MidJuly,
            [new DateOnly(2026, 7, 3), new DateOnly(2026, 6, 3), new DateOnly(2025, 7, 3)],
            DayOfWeek.Monday);

        var done = cells.Where(c => c.IsDone).Select(c => c.Day).ToList();

        Assert.Equal([3], done);
    }

    [Fact]
    public void MarksExactlyToday()
    {
        var cells = DailyCalendar.Build(MidJuly, [], DayOfWeek.Monday);

        var today = Assert.Single(cells, c => c.IsToday);
        Assert.Equal(15, today.Day);
    }

    [Fact]
    public void AMonthStartingOnTheWeekStart_HasNoBlanks()
    {
        // June 2026 begins on a Monday.
        var cells = DailyCalendar.Build(new DateOnly(2026, 6, 10), [], DayOfWeek.Monday);

        Assert.False(cells[0].IsBlank);
        Assert.Equal(30, cells.Count);
    }
}
