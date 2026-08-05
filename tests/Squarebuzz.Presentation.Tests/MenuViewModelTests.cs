using Squarebuzz.Core.Model;
using Squarebuzz.Presentation.Navigation;
using Squarebuzz.Presentation.Tests.Fakes;
using Squarebuzz.Presentation.ViewModels;
using Xunit;

namespace Squarebuzz.Presentation.Tests;

public sealed class MenuViewModelTests : IDisposable
{
    private readonly FakeLocalizationService _strings = new();
    private readonly FakeProgressRepository _progress = new();
    private readonly FakeSaveGameRepository _saveGames = new();
    private readonly FakeNavigationService _navigation = new();
    private readonly FakeClock _clock = new();
    private readonly MenuViewModel _vm;

    public MenuViewModelTests()
    {
        _vm = new MenuViewModel(_strings, _progress, _saveGames, _navigation, _clock);
    }

    public void Dispose() => _vm.Dispose();

    [Fact]
    public async Task PlaySubtitle_NamesTheNextLevel()
    {
        _progress.Progress = PlayerProgress.Empty with { HighestLevelCompleted = 41 };

        await _vm.OnAppearingAsync();

        Assert.Equal("levelN:42", _vm.PlaySubtitle);
    }

    [Fact]
    public async Task PlaySubtitle_ClampsAtTheLastLevel()
    {
        _progress.Progress = PlayerProgress.Empty with { HighestLevelCompleted = 600 };

        await _vm.OnAppearingAsync();

        Assert.Equal("levelN:600", _vm.PlaySubtitle);
    }

    [Fact]
    public async Task SavesInProgress_ShowOnTheContinueTile()
    {
        _saveGames.Saves[Guid.NewGuid()] = MakeSave();
        _saveGames.Saves[Guid.NewGuid()] = MakeSave();

        await _vm.OnAppearingAsync();

        Assert.True(_vm.HasSaves);
        Assert.Equal("inProgress:2", _vm.ContinueSubtitle);
    }

    [Fact]
    public async Task NoSaves_InvitesStartingOne()
    {
        await _vm.OnAppearingAsync();

        Assert.False(_vm.HasSaves);
        Assert.Equal("noSaves", _vm.ContinueSubtitle);
    }

    [Fact]
    public async Task FreshPlayer_GetsTheInvitation_NeverTheCongratulation()
    {
        await _vm.OnAppearingAsync();

        Assert.Equal("m_menu1", _vm.Greeting);
    }

    [Fact]
    public async Task PlayerWithProgress_GetsARotatingGreeting()
    {
        _progress.Progress = PlayerProgress.Empty with { Stars = 5, Streak = 2 };

        await _vm.OnAppearingAsync();

        var expected = $"m_menu{2 + (_clock.Today.DayNumber % 2)}";
        Assert.Equal(expected, _vm.Greeting);
    }

    [Fact]
    public async Task LiveStreak_MakesTheMascotCheer()
    {
        _progress.Progress = PlayerProgress.Empty with { Streak = 3 };

        await _vm.OnAppearingAsync();

        Assert.Equal(MascotPose.Cheer, _vm.MascotPose);
    }

    [Fact]
    public async Task PlayCommand_OpensTheLevelMap()
    {
        await _vm.PlayCommand.ExecuteAsync(null);

        var request = Assert.Single(_navigation.Requests);
        Assert.Equal(Routes.Levels, request.Route);
    }

    private SavedGame MakeSave() => new()
    {
        Id = Guid.NewGuid(),
        PuzzleId = null,
        Size = 5,
        Difficulty = 2,
        PackId = "surprise",
        Seed = 1,
        Challenge = ChallengeLevel.Relaxed,
        Cells = [CellState.Filled, CellState.Empty, CellState.Empty, CellState.Empty],
        Elapsed = TimeSpan.FromSeconds(10),
        HintsRemaining = 3,
        Mistakes = 0,
        SavedAt = _clock.Now,
    };
}
