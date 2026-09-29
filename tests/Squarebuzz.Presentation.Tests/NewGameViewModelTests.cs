using Squarebuzz.Core.Model;
using Squarebuzz.Presentation.Navigation;
using Squarebuzz.Presentation.Tests.Fakes;
using Squarebuzz.Presentation.ViewModels;
using Xunit;

namespace Squarebuzz.Presentation.Tests;

/// <summary>
/// New Game remembers the player's last choices and offers only what this device and this
/// player's progress allow. Both halves - the memory and the gating - are what these pin.
/// </summary>
public class NewGameViewModelTests : IDisposable
{
    private readonly FakeSettingsRepository _settings = new();
    private readonly FakeProgressRepository _progress = new();
    private readonly FakePuzzleRepository _puzzles = new();
    private readonly FakeNavigationService _navigation = new();
    private readonly FakeDeviceScreen _screen = new();
    private readonly NewGameViewModel _vm;

    public NewGameViewModelTests()
    {
        // A small world: one open pack, one pack shipped locked, and the wildcard.
        _puzzles.PacksList.Add(new PackDefinition("animals", "🐱", Locked: false, IsWildcard: false));
        _puzzles.PacksList.Add(new PackDefinition("fairy", "👑", Locked: true, IsWildcard: false));
        _puzzles.PacksList.Add(new PackDefinition("surprise", "🎁", Locked: false, IsWildcard: true));

        _puzzles.PuzzlesList.Add(Puzzle.FromRows("heart", "animals", "#FF6B8A", [".#.#.", "#####", "#####", ".###.", "..#.."]));
        _puzzles.PuzzlesList.Add(Puzzle.FromRows("crown", "fairy", "#FFC244", ["#.#.#", "#####", "#####", ".###.", "....."]));
        _puzzles.PuzzlesList.Add(Puzzle.FromRows("wand", "fairy", "#8B5CF6", ["....#", "...#.", "..#..", ".#...", "#...."]));

        _vm = new NewGameViewModel(
            new FakeLocalizationService(),
            _puzzles,
            _settings,
            _progress,
            _navigation,
            _screen);
    }

    public void Dispose()
    {
        _vm.Dispose();
        GC.SuppressFinalize(this);
    }

    private SizeOption Size(int size) => _vm.Sizes.Single(s => s.Size == size);

    private PackOption Pack(string id) => _vm.Packs.Single(p => p.Id == id);

    // ---- Memory ----

    [Fact]
    // Size, difficulty and challenge are remembered; the pack is not. Every visit opens on
    // Surprise, so older installs stop reopening on Animals, the pack that used to be the default.
    public async Task Appearing_RestoresTheLastChoices_ButOpensOnSurprise()
    {
        _settings.Settings = GameSettings.Default with
        {
            LastSize = GridSize.Normal,
            LastDifficulty = 4,
            LastPackId = "animals",
            LastChallenge = ChallengeLevel.Sharp,
        };

        await _vm.OnAppearingAsync();

        Assert.Equal(GridSize.Normal, _vm.SelectedSize);
        Assert.Equal(4, _vm.SelectedDifficulty);
        Assert.Equal("surprise", _vm.SelectedPackId);
        Assert.True(_vm.IsSharp);

        // The option lists agree with the selection, one each.
        Assert.Equal([GridSize.Normal], _vm.Sizes.Where(s => s.IsSelected).Select(s => s.Size));
        Assert.Equal([4], _vm.Difficulties.Where(d => d.IsSelected).Select(d => d.Level));
        Assert.Equal(["surprise"], _vm.Packs.Where(p => p.IsSelected).Select(p => p.Id));
    }

    [Fact]
    // One pack of a dozen pictures ran dry long before the whole collection did.
    public async Task AFirstVisit_DrawsFromEveryPack()
    {
        await _vm.OnAppearingAsync();

        Assert.Equal("surprise", _vm.SelectedPackId);
        Assert.True(Pack("surprise").IsSelected);
    }

    [Fact]
    // After a failed read the screen holds the defaults. Starting a game must record the four
    // choices it owns, not write the defaults over the player's language and helpers.
    public async Task AFailedSettingsLoad_ThenStart_WritesOnlyTheGameChoices()
    {
        _settings.LoadFails = true;
        await _vm.OnAppearingAsync();

        await _vm.StartCommand.ExecuteAsync(null);

        var (baseline, updated) = Assert.Single(_settings.Changes);
        Assert.Equal(GameSettings.Default, baseline);
        Assert.Equal(baseline with
        {
            LastSize = updated.LastSize,
            LastDifficulty = updated.LastDifficulty,
            LastPackId = updated.LastPackId,
            LastChallenge = updated.LastChallenge,
        }, updated);
    }

    [Fact]
    public async Task AFailedSettingsLoad_StartsFromTheDefaults()
    {
        _settings.LoadFails = true;

        await _vm.OnAppearingAsync();

        Assert.Equal(GameSettings.Default.LastSize, _vm.SelectedSize);
        Assert.Equal(GameSettings.Default.LastDifficulty, _vm.SelectedDifficulty);
        Assert.Equal(GameSettings.Default.LastPackId, _vm.SelectedPackId);
        Assert.True(_vm.IsRelaxed);
    }

    // ---- Gating by the device ----

    [Fact]
    // A 25x25 cannot show hittable cells on a phone. It is shown, numbered and locked rather than
    // hidden - and a remembered choice of it falls back to the largest a phone can play.
    public async Task OnAPhone_TheGiantGridIsLockedAndARememberedOneFallsBack()
    {
        _screen.IsLargeScreen = false;
        _settings.Settings = GameSettings.Default with { LastSize = GridSize.Giant };

        await _vm.OnAppearingAsync();

        Assert.True(Size(GridSize.Giant).IsLocked);
        Assert.False(Size(GridSize.Huge).IsLocked);
        Assert.Equal(GridSize.Huge, _vm.SelectedSize);
    }

    [Fact]
    public async Task OnATablet_TheGiantGridIsOpenAndRemembered()
    {
        _screen.IsLargeScreen = true;
        _settings.Settings = GameSettings.Default with { LastSize = GridSize.Giant };

        await _vm.OnAppearingAsync();

        Assert.False(Size(GridSize.Giant).IsLocked);
        Assert.Equal(GridSize.Giant, _vm.SelectedSize);
    }

    [Fact]
    public async Task SelectingALockedSize_IsIgnored()
    {
        _screen.IsLargeScreen = false;
        await _vm.OnAppearingAsync();

        _vm.SelectSizeCommand.Execute(Size(GridSize.Giant));

        Assert.NotEqual(GridSize.Giant, _vm.SelectedSize);
        Assert.False(Size(GridSize.Giant).IsSelected);
    }

    // ---- Gating by progress ----

    [Fact]
    public async Task AShippedLockedPack_StaysLockedUntilEveryPictureIsFound()
    {
        _progress.Solved.Add(new SolvedPuzzle("crown", new DateOnly(2026, 8, 1), 3, TimeSpan.FromMinutes(1), 1));

        await _vm.OnAppearingAsync();

        // One of two found: not yet.
        Assert.True(Pack("fairy").IsLocked);
        Assert.False(Pack("animals").IsLocked);
        Assert.False(Pack("surprise").IsLocked);
    }

    [Fact]
    public async Task AShippedLockedPack_OpensOnceEveryPictureIsFound()
    {
        _progress.Solved.Add(new SolvedPuzzle("crown", new DateOnly(2026, 8, 1), 3, TimeSpan.FromMinutes(1), 1));
        _progress.Solved.Add(new SolvedPuzzle("wand", new DateOnly(2026, 8, 2), 2, TimeSpan.FromMinutes(2), 1));
        await _vm.OnAppearingAsync();

        Assert.False(Pack("fairy").IsLocked);

        _vm.SelectPackCommand.Execute(Pack("fairy"));

        Assert.Equal("fairy", _vm.SelectedPackId);
    }

    [Fact]
    // The repository's Find for a named pack ignores locks by design, so a locked pack remembered
    // in settings - a hand-edited database, or an unlock rule that tightened between builds - was
    // shown selected, started, and played. The screen's own rule has to hold for the one choice
    // the player did not make on this visit.
    public async Task ARememberedPackThatIsLocked_FallsBackToAnOpenOne()
    {
        _settings.Settings = GameSettings.Default with { LastPackId = "fairy" };

        await _vm.OnAppearingAsync();

        Assert.True(Pack("fairy").IsLocked);
        Assert.Equal(GameSettings.DefaultPackId, _vm.SelectedPackId);
        Assert.Equal([GameSettings.DefaultPackId], _vm.Packs.Where(p => p.IsSelected).Select(p => p.Id));
    }

    [Fact]
    public async Task SelectingALockedPack_IsIgnored()
    {
        await _vm.OnAppearingAsync();

        _vm.SelectPackCommand.Execute(Pack("fairy"));

        Assert.Equal(GameSettings.DefaultPackId, _vm.SelectedPackId);
        Assert.False(Pack("fairy").IsSelected);
    }

    // ---- Choosing ----

    [Fact]
    public async Task SelectingAnOption_MovesTheSingleSelection()
    {
        await _vm.OnAppearingAsync();

        _vm.SelectSizeCommand.Execute(Size(GridSize.Big));
        _vm.SelectDifficultyCommand.Execute(_vm.Difficulties.Single(d => d.Level == 5));
        _vm.SelectPackCommand.Execute(Pack("surprise"));

        Assert.Equal(GridSize.Big, _vm.SelectedSize);
        Assert.Equal([GridSize.Big], _vm.Sizes.Where(s => s.IsSelected).Select(s => s.Size));
        Assert.Equal(5, _vm.SelectedDifficulty);
        Assert.Equal([5], _vm.Difficulties.Where(d => d.IsSelected).Select(d => d.Level));
        Assert.Equal("surprise", _vm.SelectedPackId);
        Assert.Equal(["surprise"], _vm.Packs.Where(p => p.IsSelected).Select(p => p.Id));
    }

    [Fact]
    // Difficulty only shapes generated pictures; at an authored size the note says so.
    public async Task TheDifficultyNote_ShowsOnlyAtAuthoredSizes()
    {
        await _vm.OnAppearingAsync();

        _vm.SelectSizeCommand.Execute(Size(GridSize.Normal));
        Assert.True(_vm.ShowDifficultyNote);

        _vm.SelectSizeCommand.Execute(Size(GridSize.Big));
        Assert.False(_vm.ShowDifficultyNote);
    }

    [Fact]
    public async Task ChallengeToggles_AndItsNoteFollows()
    {
        await _vm.OnAppearingAsync();

        _vm.SelectChallengeCommand.Execute("sharp");
        Assert.True(_vm.IsSharp);
        Assert.Equal("sharpSub", _vm.ChallengeNote);

        _vm.SelectChallengeCommand.Execute("relaxed");
        Assert.True(_vm.IsRelaxed);
        Assert.Equal("relaxedSub", _vm.ChallengeNote);
    }

    // ---- Starting ----

    [Fact]
    public async Task Start_RemembersTheChoicesAndOpensTheBoard()
    {
        await _vm.OnAppearingAsync();
        _vm.SelectSizeCommand.Execute(Size(GridSize.Normal));
        _vm.SelectDifficultyCommand.Execute(_vm.Difficulties.Single(d => d.Level == 3));
        _vm.SelectChallengeCommand.Execute("sharp");

        await _vm.StartCommand.ExecuteAsync(null);

        var remembered = Assert.Single(_settings.Saved);
        Assert.Equal(GridSize.Normal, remembered.LastSize);
        Assert.Equal(3, remembered.LastDifficulty);
        Assert.Equal(GameSettings.DefaultPackId, remembered.LastPackId);
        Assert.Equal(ChallengeLevel.Sharp, remembered.LastChallenge);

        // A push, not a reset: the back gesture from the board returns here.
        var navigation = Assert.Single(_navigation.Requests);
        Assert.Equal(Routes.Game, navigation.Route);
        Assert.False(navigation.IsReset);
        var options = Assert.IsType<NewGameOptions>(navigation.Parameters![GameViewModel.NewGameOptionsParameter]);
        Assert.Equal(remembered.ToNewGameOptions(), options);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Start_PlaysTheChosenGameEvenWhenPreferencesCannotBePersistedOrLoaded(
        bool saveFails, bool loadFails)
    {
        await _vm.OnAppearingAsync();
        _vm.SelectSizeCommand.Execute(Size(GridSize.Normal));
        _vm.SelectDifficultyCommand.Execute(_vm.Difficulties.Single(d => d.Level == 4));
        _vm.SelectPackCommand.Execute(Pack("surprise"));
        _vm.SelectChallengeCommand.Execute("sharp");
        _settings.SaveFails = saveFails;

        await _vm.StartCommand.ExecuteAsync(null);

        Assert.Equal(Routes.Game, _navigation.Last?.Route);
        using var game = new GameViewModelHarness();
        game.Settings.Settings = _settings.Settings;
        game.Settings.LoadFails = loadFails;
        game.Vm.ApplyQueryAttributes(_navigation.Last!.Parameters ?? new Dictionary<string, object>());
        await game.Vm.InitialiseAsync();

        // Verify the destination's actual generation request, not merely that navigation ran.
        var request = Assert.Single(game.Generator.Requests);
        Assert.Equal(GridSize.Normal, request.Width);
        Assert.Equal(GridSize.Normal, request.Height);
        Assert.Equal(4, request.Difficulty);
        Assert.Equal("surprise", request.Pack);
        Assert.Equal(ChallengeLevel.Sharp, game.Vm.Session!.Origin!.Challenge);
        Assert.Null(game.Vm.Session.Origin.Level);
        Assert.Null(game.Vm.Session.Origin.DailyDate);
    }

    [Fact]
    public async Task Start_CapturesTheSelectionBeforeWaitingForThePreferenceWrite()
    {
        await _vm.OnAppearingAsync();
        _vm.SelectSizeCommand.Execute(Size(GridSize.Normal));
        var gate = _settings.SaveGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var starting = _vm.StartCommand.ExecuteAsync(null);
        Assert.False(starting.IsCompleted);

        // Another selection while storage is busy belongs to a later request.
        _vm.SelectSizeCommand.Execute(Size(GridSize.Tiny));
        gate.SetResult();
        await starting;

        var options = Assert.IsType<NewGameOptions>(
            _navigation.Last!.Parameters![GameViewModel.NewGameOptionsParameter]);
        Assert.Equal(GridSize.Normal, options.Size);
        Assert.Equal(GridSize.Normal, Assert.Single(_settings.Saved).LastSize);
        Assert.Equal(GridSize.Tiny, _vm.SelectedSize);
    }

    [Fact]
    public async Task OpenGallery_NavigatesThere()
    {
        await _vm.OnAppearingAsync();

        await _vm.OpenGalleryCommand.ExecuteAsync(null);

        Assert.Equal(new FakeNavigationService.Request(Routes.Gallery, null, IsReset: false), _navigation.Last);
    }
}
