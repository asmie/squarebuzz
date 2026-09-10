using Squarebuzz.Core.Progression;
using Xunit;

namespace Squarebuzz.Core.Tests.Progression;

public class ScreenTimeMonitorTests
{
    [Theory]
    [InlineData(null)]
    [InlineData(1)]
    public void HugeElapsed_SaturatesAndPreservesReminderAndRestartBehavior(int? limitMinutes)
    {
        var monitor = new ScreenTimeMonitor();
        monitor.Configure(limitMinutes);
        Assert.False(monitor.Add(TimeSpan.FromSeconds(20)));

        Assert.Equal(limitMinutes.HasValue, monitor.Add(TimeSpan.MaxValue));
        Assert.Equal(TimeSpan.MaxValue, monitor.Played);
        Assert.False(monitor.Add(TimeSpan.FromSeconds(1)));
        Assert.Equal(TimeSpan.MaxValue, monitor.Played);

        monitor.Restart();

        Assert.Equal(TimeSpan.Zero, monitor.Played);
        Assert.Equal(limitMinutes, monitor.LimitMinutes);
        Assert.Equal(limitMinutes.HasValue, monitor.Add(TimeSpan.FromMinutes(1)));
        Assert.Equal(TimeSpan.FromMinutes(1), monitor.Played);
    }

    [Fact]
    public void HugeElapsed_AfterReminderFiredDoesNotOverflowOrFireAgain()
    {
        var monitor = new ScreenTimeMonitor();
        monitor.Configure(1);
        Assert.True(monitor.Add(TimeSpan.FromMinutes(1)));

        Assert.False(monitor.Add(TimeSpan.MaxValue));
        Assert.False(monitor.Add(TimeSpan.MaxValue));
        Assert.Equal(TimeSpan.MaxValue, monitor.Played);
    }

    [Fact]
    public void WithNoLimit_NothingEverFires()
    {
        var monitor = new ScreenTimeMonitor();

        for (var i = 0; i < 200; i++)
        {
            Assert.False(monitor.Add(TimeSpan.FromMinutes(1)));
        }

        Assert.Equal(TimeSpan.FromMinutes(200), monitor.Played);
    }

    [Fact]
    public void TheReminderFiresOnceAndOnlyOnce()
    {
        // The reason this matters: the game adds a second every tick, so a monitor that kept
        // returning true would reopen the reminder every second and make the board unplayable.
        var monitor = new ScreenTimeMonitor();
        monitor.Configure(2);

        var fired = 0;

        for (var i = 0; i < 300; i++)
        {
            if (monitor.Add(TimeSpan.FromSeconds(1)))
            {
                fired++;
            }
        }

        Assert.Equal(1, fired);
    }

    [Fact]
    public void ItFiresOnTheTickThatCrossesTheLimit()
    {
        var monitor = new ScreenTimeMonitor();
        monitor.Configure(1);

        for (var i = 0; i < 59; i++)
        {
            Assert.False(monitor.Add(TimeSpan.FromSeconds(1)));
        }

        Assert.True(monitor.Add(TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void TimeAccumulatesAcrossPuzzles()
    {
        // Two boards of six minutes each must trip a ten-minute limit. Counting per-puzzle
        // instead of per-run is the mistake this guards against.
        var monitor = new ScreenTimeMonitor();
        monitor.Configure(10);

        Assert.False(monitor.Add(TimeSpan.FromMinutes(6)));
        Assert.True(monitor.Add(TimeSpan.FromMinutes(6)));
    }

    [Fact]
    public void TurningTheReminderOff_StopsItFiring()
    {
        var monitor = new ScreenTimeMonitor();
        monitor.Configure(5);
        monitor.Add(TimeSpan.FromMinutes(4));

        monitor.Configure(null);

        Assert.False(monitor.Add(TimeSpan.FromMinutes(60)));
        Assert.Null(monitor.LimitMinutes);
    }

    [Fact]
    public void RaisingTheLimitAfterItFired_ArmsItAgain()
    {
        var monitor = new ScreenTimeMonitor();
        monitor.Configure(5);

        Assert.True(monitor.Add(TimeSpan.FromMinutes(5)));

        // A parent who grants another ten minutes should get another reminder at fifteen.
        monitor.Configure(15);

        Assert.False(monitor.Add(TimeSpan.FromMinutes(9)));
        Assert.True(monitor.Add(TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public void LoweringTheLimitBelowTimeAlreadyPlayed_FiresOnTheNextTick()
    {
        var monitor = new ScreenTimeMonitor();
        monitor.Configure(60);
        monitor.Add(TimeSpan.FromMinutes(30));

        monitor.Configure(15);

        Assert.True(monitor.Add(TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void ANonPositiveLimit_MeansOff()
    {
        // Guards against a hand-edited database putting the game into a state where the
        // reminder fires on the very first second of play.
        var monitor = new ScreenTimeMonitor();

        monitor.Configure(0);
        Assert.Null(monitor.LimitMinutes);
        Assert.False(monitor.Add(TimeSpan.FromSeconds(1)));

        monitor.Configure(-5);
        Assert.Null(monitor.LimitMinutes);
        Assert.False(monitor.Add(TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void Restart_ClearsTheCountAndRearms()
    {
        var monitor = new ScreenTimeMonitor();
        monitor.Configure(1);

        Assert.True(monitor.Add(TimeSpan.FromMinutes(1)));

        monitor.Restart();

        Assert.Equal(TimeSpan.Zero, monitor.Played);
        Assert.True(monitor.Add(TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public void ZeroOrNegativeElapsed_IsIgnored()
    {
        var monitor = new ScreenTimeMonitor();
        monitor.Configure(1);

        Assert.False(monitor.Add(TimeSpan.Zero));
        Assert.False(monitor.Add(TimeSpan.FromSeconds(-10)));
        Assert.Equal(TimeSpan.Zero, monitor.Played);
    }
}
