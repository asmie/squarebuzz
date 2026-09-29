using Squarebuzz.Core.Model;
using Squarebuzz.Presentation.Navigation;
using Squarebuzz.Presentation.Tests.Fakes;
using Squarebuzz.Presentation.ViewModels;
using Xunit;

namespace Squarebuzz.Presentation.Tests;

/// <summary>
/// Three cards, read aloud, seen once. The one thing this screen must get right is the flag that
/// keeps it from coming back - so most of these are about finishing.
/// </summary>
public class OnboardingViewModelTests : IDisposable
{
    private readonly FakeSettingsRepository _settings = new();
    private readonly FakeNavigationService _navigation = new();
    private readonly FakeNarrationService _narration = new();
    private readonly OnboardingViewModel _vm;

    public OnboardingViewModelTests()
    {
        _vm = new OnboardingViewModel(new FakeLocalizationService(), _settings, _navigation, _narration);
    }

    public void Dispose()
    {
        _vm.Dispose();
        GC.SuppressFinalize(this);
    }

    private static readonly FakeNavigationService.Request HomeReset = new(Routes.Menu, null, IsReset: true);

    // ---- The cards ----

    [Fact]
    public async Task Appearing_ShowsAndReadsTheFirstCard()
    {
        await _vm.OnAppearingAsync();

        Assert.Equal(0, _vm.Step);
        Assert.True(_vm.IsOnStepOne);
        Assert.False(_vm.IsLastStep);
        Assert.Equal("onb1t", _vm.StepTitle);
        Assert.Equal("next", _vm.NextLabel);
        Assert.Equal(MascotPose.Think, _vm.MascotPose);
        Assert.Equal("demo1", _vm.DemoPuzzle.Id);

        // The brand-new player is the one who most needs the words read out.
        Assert.Equal(["onb1t. onb1b"], _narration.Spoken);
    }

    [Fact]
    public async Task Next_AdvancesThroughTheCards_ReadingEachOnce()
    {
        await _vm.OnAppearingAsync();

        await _vm.NextCommand.ExecuteAsync(null);
        Assert.Equal(1, _vm.Step);
        Assert.True(_vm.IsOnStepTwo);
        Assert.Equal("demo2", _vm.DemoPuzzle.Id);

        await _vm.NextCommand.ExecuteAsync(null);
        Assert.Equal(2, _vm.Step);
        Assert.True(_vm.IsOnStepThree);
        Assert.True(_vm.IsLastStep);
        Assert.Equal("letsPlay", _vm.NextLabel);
        Assert.Equal(MascotPose.Cheer, _vm.MascotPose);
        Assert.Equal("demo3", _vm.DemoPuzzle.Id);

        Assert.Equal(["onb1t. onb1b", "onb2t. onb2b", "onb3t. onb3b"], _narration.Spoken);

        // Nothing has been written or navigated yet - the player is still reading.
        Assert.Empty(_settings.Saved);
        Assert.Null(_navigation.Last);
    }

    [Fact]
    public async Task EveryCard_ShowsADistinctValidFiveByFive()
    {
        await _vm.OnAppearingAsync();
        var seen = new List<string>();

        for (var card = 0; card < 3; card++)
        {
            var puzzle = _vm.DemoPuzzle;

            Assert.Equal(5, puzzle.Width);
            Assert.Equal(5, puzzle.Height);
            Assert.True(puzzle.PictureCellCount > 0);
            seen.Add(puzzle.Id);

            if (card < 2)
            {
                await _vm.NextCommand.ExecuteAsync(null);
            }
        }

        Assert.Equal(["demo1", "demo2", "demo3"], seen);
    }

    // ---- Finishing ----

    [Fact]
    public async Task NextOnTheLastCard_RecordsTheVisitAndGoesHome()
    {
        await _vm.OnAppearingAsync();
        await _vm.NextCommand.ExecuteAsync(null);
        await _vm.NextCommand.ExecuteAsync(null);

        await _vm.NextCommand.ExecuteAsync(null);

        Assert.True(Assert.Single(_settings.Saved).HasSeenOnboarding);

        // A reset, not a push: the back gesture must never bring a child back to these cards.
        Assert.Equal(HomeReset, _navigation.Last);

        // Nobody wants card three read out over the menu they have just arrived at.
        Assert.Equal(1, _narration.StopCalls);

        // And the step does not run off the end of the deck.
        Assert.Equal(2, _vm.Step);
    }

    [Fact]
    public async Task Skip_FinishesFromAnyCard()
    {
        await _vm.OnAppearingAsync();

        await _vm.SkipCommand.ExecuteAsync(null);

        Assert.True(Assert.Single(_settings.Saved).HasSeenOnboarding);
        Assert.Equal(HomeReset, _navigation.Last);
        Assert.Equal(1, _narration.StopCalls);
        Assert.Equal(0, _vm.Step);
    }

    [Fact]
    // The flag is written over the settings as they are, not over defaults: a device language the
    // splash seeded a moment ago must survive the first tap on "Let's play".
    public async Task Finishing_PreservesEveryOtherSetting()
    {
        _settings.Settings = GameSettings.Default with { Music = true, Language = AppLanguage.Polish, CellZoomPercent = 130 };
        await _vm.OnAppearingAsync();

        await _vm.SkipCommand.ExecuteAsync(null);

        var saved = Assert.Single(_settings.Saved);
        Assert.True(saved.HasSeenOnboarding);
        Assert.True(saved.Music);
        Assert.Equal(AppLanguage.Polish, saved.Language);
        Assert.Equal(130, saved.CellZoomPercent);
    }

    [Fact]
    // The load and the save fail independently. Under one try, a read that failed took the write
    // down with it - so a momentary read failure brought the cards back on every launch.
    public async Task AFailedSettingsRead_StillRecordsTheVisit()
    {
        _settings.LoadFails = true;
        await _vm.OnAppearingAsync();

        await _vm.SkipCommand.ExecuteAsync(null);

        Assert.True(Assert.Single(_settings.Saved).HasSeenOnboarding);
        Assert.Equal(HomeReset, _navigation.Last);

        // Only the flag is written, so the defaults the failed read fell back on overwrite nothing.
        var (baseline, updated) = Assert.Single(_settings.Changes);
        Assert.Equal(baseline with { HasSeenOnboarding = true }, updated);
    }

    [Fact]
    // Worst case the cards appear again next launch; refusing to let the child play is worse.
    public async Task AFailedSettingsWrite_StillGoesHome()
    {
        _settings.SaveFails = true;
        await _vm.OnAppearingAsync();

        await _vm.SkipCommand.ExecuteAsync(null);

        Assert.Empty(_settings.Saved);
        Assert.Equal(HomeReset, _navigation.Last);
    }
}
