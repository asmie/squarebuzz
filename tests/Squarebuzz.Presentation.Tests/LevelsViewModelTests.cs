using Squarebuzz.Core.Model;
using Squarebuzz.Core.Progression;
using Squarebuzz.Presentation.Navigation;
using Squarebuzz.Presentation.Tests.Fakes;
using Squarebuzz.Presentation.ViewModels;
using Xunit;

namespace Squarebuzz.Presentation.Tests;

public sealed class LevelsViewModelTests : IDisposable
{
    private readonly FakeLocalizationService _strings = new();
    private readonly FakeProgressRepository _progress = new();
    private readonly FakePuzzleRepository _puzzles = new();
    private readonly FakeNavigationService _navigation = new();
    private readonly LevelsViewModel _vm;

    public LevelsViewModelTests()
    {
        _vm = new LevelsViewModel(_strings, _progress, _puzzles, _navigation);
    }

    public void Dispose() => _vm.Dispose();

    private async Task LoadAsync(int highestLevelCompleted)
    {
        _progress.Progress = PlayerProgress.Empty with { HighestLevelCompleted = highestLevelCompleted };
        await _vm.OnAppearingAsync();
    }

    [Fact]
    public async Task Map_HasSixHundredCards_InTwelveGroupsOfFifty()
    {
        await LoadAsync(highestLevelCompleted: 0);

        Assert.Equal(12, _vm.Groups.Count);
        Assert.All(_vm.Groups, group => Assert.Equal(50, group.Count));
        Assert.Equal(LevelCatalog.LevelCount, _vm.Groups.Sum(g => g.Count));
    }

    [Fact]
    public async Task FreshPlayer_LevelOneIsCurrent_AndTheRestAreLocked()
    {
        await LoadAsync(highestLevelCompleted: 0);

        Assert.NotNull(_vm.CurrentCard);
        Assert.Equal(1, _vm.CurrentCard!.Number);
        Assert.Equal(LevelNodeState.Current, _vm.CurrentCard.State);
        Assert.Same(_vm.Groups[0], _vm.CurrentGroup);

        Assert.Equal(LevelNodeState.Locked, _vm.Groups[0][1].State);
        Assert.Equal(0, _vm.Groups.Sum(g => g.Count(c => c.State == LevelNodeState.Done)));
    }

    [Fact]
    public async Task HighestFortyOne_MakesFortyTwoCurrent_StillInGroupOne()
    {
        await LoadAsync(highestLevelCompleted: 41);

        Assert.Equal(42, _vm.CurrentCard!.Number);
        Assert.Same(_vm.Groups[0], _vm.CurrentGroup);

        Assert.Equal(LevelNodeState.Done, _vm.Groups[0][40].State);
        Assert.Equal("★", _vm.Groups[0][40].Caption);
        Assert.Equal(LevelNodeState.Locked, _vm.Groups[0][42].State);
        Assert.Equal(41, _vm.Groups.Sum(g => g.Count(c => c.State == LevelNodeState.Done)));
    }

    [Fact]
    public async Task LockedCard_DoesNotNavigate()
    {
        await LoadAsync(highestLevelCompleted: 0);
        var locked = _vm.Groups[0][5];
        Assert.False(locked.IsPlayable);

        await _vm.PlayLevelCommand.ExecuteAsync(locked);

        Assert.Empty(_navigation.Requests);
    }

    [Fact]
    public async Task PlayableCard_NavigatesToTheGame_WithItsLevelNumber()
    {
        await LoadAsync(highestLevelCompleted: 41);

        await _vm.PlayLevelCommand.ExecuteAsync(_vm.CurrentCard);

        var request = Assert.Single(_navigation.Requests);
        Assert.Equal(Routes.Game, request.Route);
        Assert.Equal("42", request.Parameters!["level"]);
    }

    [Fact]
    public async Task DoneCard_IsReplayable()
    {
        await LoadAsync(highestLevelCompleted: 41);

        await _vm.PlayLevelCommand.ExecuteAsync(_vm.Groups[0][0]);

        var request = Assert.Single(_navigation.Requests);
        Assert.Equal(Routes.Game, request.Route);
        Assert.Equal("1", request.Parameters!["level"]);
    }

    [Fact]
    public async Task Summary_UsesTheLocalizedFormat()
    {
        await LoadAsync(highestLevelCompleted: 41);

        Assert.Equal("levelsSummary:41,600", _vm.Summary);
    }
}
