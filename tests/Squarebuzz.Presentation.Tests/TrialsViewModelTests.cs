using Squarebuzz.Core.Model;
using Squarebuzz.Presentation.Navigation;
using Squarebuzz.Presentation.Tests.Fakes;
using Squarebuzz.Presentation.ViewModels;
using Xunit;

namespace Squarebuzz.Presentation.Tests;

public sealed class TrialsViewModelTests : IDisposable
{
    private static readonly int[] ExpectedTierNumbers = [1, 4, 2, 3, 5, 6];

    private readonly FakeLocalizationService _strings = new();
    private readonly FakeProgressRepository _progress = new();
    private readonly FakePuzzleRepository _puzzles = new();
    private readonly FakeNavigationService _navigation = new();
    private readonly FakeClock _clock = new();
    private readonly TrialsViewModel _vm;

    public TrialsViewModelTests()
    {
        _vm = new TrialsViewModel(_strings, _progress, _navigation, _clock);
    }

    public void Dispose() => _vm.Dispose();

    [Fact]
    public void DefaultTab_IsDaily()
    {
        Assert.Equal(TrialsTab.Daily, _vm.Tab);
        Assert.True(_vm.IsDailyTab);
        Assert.False(_vm.IsTimedTab);
        Assert.False(_vm.IsTrophiesTab);
    }

    [Fact]
    public void TabCommands_SwitchBetweenTheThreeTabs()
    {
        _vm.ShowTimedCommand.Execute(null);
        Assert.True(_vm.IsTimedTab);
        Assert.False(_vm.IsDailyTab);

        _vm.ShowTrophiesCommand.Execute(null);
        Assert.True(_vm.IsTrophiesTab);
        Assert.False(_vm.IsTimedTab);

        _vm.ShowDailyCommand.Execute(null);
        Assert.True(_vm.IsDailyTab);
        Assert.False(_vm.IsTrophiesTab);
    }

    [Fact]
    public async Task Ladder_HasEveryTimedTier()
    {
        await _vm.OnAppearingAsync();

        Assert.Equal(6, _vm.Timed.Count);
        Assert.Equal(ExpectedTierNumbers, _vm.Timed.Select(t => t.Tier.Tier));
    }

    [Fact]
    public async Task PlayTimed_NavigatesToTheGame_WithTheTierNumber()
    {
        await _vm.OnAppearingAsync();

        await _vm.PlayTimedCommand.ExecuteAsync(_vm.Timed[2]);

        var request = Assert.Single(_navigation.Requests);
        Assert.Equal(Routes.Game, request.Route);
        Assert.Equal("2", request.Parameters!["tier"]);
    }

    [Fact]
    public async Task PlayDaily_NavigatesWithTheDailyFlag_WhenStillOpen()
    {
        await _vm.OnAppearingAsync();
        Assert.True(_vm.IsDailyAvailable);

        await _vm.PlayDailyCommand.ExecuteAsync(null);

        var request = Assert.Single(_navigation.Requests);
        Assert.Equal(Routes.Game, request.Route);
        Assert.True(request.Parameters!.ContainsKey("daily"));
    }

    [Fact]
    public async Task PlayDaily_DoesNothing_OnceTodaysIsDone()
    {
        _progress.Progress = PlayerProgress.Empty with { LastDailyCompletedOn = _clock.Today };

        await _vm.OnAppearingAsync();
        Assert.False(_vm.IsDailyAvailable);

        await _vm.PlayDailyCommand.ExecuteAsync(null);

        Assert.Empty(_navigation.Requests);
    }

    [Fact]
    public async Task TrophyCabinet_ShowsEveryTrophy_EarnedOrNot()
    {
        _progress.Trophies.Add(new EarnedTrophy(TrophyId.FirstPicture, _clock.Today));

        await _vm.OnAppearingAsync();

        Assert.Equal(18, _vm.Trophies.Count);
        Assert.Equal(1, _vm.Trophies.Count(t => t.IsEarned));
        Assert.Equal("galleryFound:1,18", _vm.EarnedSummary);
    }
}
