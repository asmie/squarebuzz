using Squarebuzz.Core.Model;
using Xunit;

namespace Squarebuzz.Core.Tests.Progression;

public sealed class ActiveStreakTests
{
    [Theory]
    [InlineData(0, 7)]
    [InlineData(1, 7)]
    [InlineData(2, 0)]
    [InlineData(10, 0)]
    [InlineData(-1, 7)]
    public void StreakExpiresAfterAMissedDay(int daysAgo, int expected)
    {
        var today = new DateOnly(2026, 10, 16);
        var progress = new PlayerProgress { Streak = 7, LastPlayedOn = today.AddDays(-daysAgo) };
        Assert.Equal(expected, progress.ActiveStreak(today));
        Assert.Equal(7, progress.Streak);
    }

    [Fact]
    public void MissingLastPlayedDateHasNoLiveStreak() =>
        Assert.Equal(0, new PlayerProgress { Streak = 7 }.ActiveStreak(new DateOnly(2026, 10, 16)));
}
