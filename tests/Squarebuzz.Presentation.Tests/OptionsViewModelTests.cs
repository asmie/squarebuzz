using System.Globalization;
using Squarebuzz.Core.Model;
using Squarebuzz.Presentation.Navigation;
using Squarebuzz.Presentation.Services;
using Squarebuzz.Presentation.Tests.Fakes;
using Squarebuzz.Presentation.ViewModels;
using Xunit;

namespace Squarebuzz.Presentation.Tests;

/// <summary>
/// Options has no Save button: every control writes straight through and applies at once. These
/// tests pin the two halves of that promise - that a change lands, and that merely *opening* the
/// screen changes nothing.
/// </summary>
public class OptionsViewModelTests : IDisposable
{
    private readonly FakeLocalizationService _strings = new();
    private readonly FakeSettingsRepository _settings = new();
    private readonly FakeProgressRepository _progress = new();
    private readonly FakeThemeService _theme = new();
    private readonly FakeNavigationService _navigation = new();
    private readonly FakeScreenTimeMonitor _screenTime = new();
    private readonly FakeAudioService _audio = new();
    private readonly FakeNarrationService _narration = new();
    private readonly OptionsViewModel _vm;

    public OptionsViewModelTests()
    {
        _vm = new OptionsViewModel(
            _strings,
            _settings,
            new GameCompletionService(new FakeGameCompletionRepository(_progress, new FakeSaveGameRepository()), _progress),
            _theme,
            _navigation,
            _screenTime,
            _audio,
            _narration,
            new FakeUiThread());
    }

    public void Dispose()
    {
        _vm.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>The most recent write, which is what the next launch will read.</summary>
    private GameSettings LastSaved => _settings.Saved[^1];

    [Theory]
    [InlineData(1)]
    [InlineData(27)]
    [InlineData(null)]
    public async Task HintBudget_LoadsWithoutWriting_AndCanBeChanged(int? limit)
    {
        _settings.Settings = GameSettings.Default with
        {
            Helpers = HelperSettings.Default with { HintBudget = new HintBudget(limit) },
        };
        await _vm.OnAppearingAsync();
        Assert.Empty(_settings.Saved);
        Assert.Equal(limit is null, _vm.UnlimitedHints);
        Assert.Equal(limit is not null, _vm.CanEditHintLimit);
        if (limit is not null) Assert.Equal(limit.Value.ToString(CultureInfo.CurrentCulture), _vm.HintLimitText);

        _vm.UnlimitedHints = false;
        _vm.HintLimitText = "12";
        Assert.Equal(12, LastSaved.Helpers.HintBudget.Limit);
        _vm.UnlimitedHints = true;
        Assert.Equal(HintBudget.Unlimited, LastSaved.Helpers.HintBudget);
        Assert.False(_vm.CanEditHintLimit);
        _vm.UnlimitedHints = false;
        Assert.Equal(12, LastSaved.Helpers.HintBudget.Limit);
    }

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("1.5")]
    [InlineData("abc")]
    [InlineData("2147483648")]
    public async Task InvalidHintLimit_DoesNotOverwriteTheSavedBudget(string invalid)
    {
        await _vm.OnAppearingAsync();
        _vm.HintLimitText = "7";
        _vm.HintLimitText = invalid;
        Assert.True(_vm.HasHintLimitError);
        _vm.ShowTimer = false;
        Assert.Equal(7, LastSaved.Helpers.HintBudget.Limit);
        _vm.UnlimitedHints = true;
        Assert.False(_vm.HasHintLimitError);
        Assert.Equal(HintBudget.Unlimited, LastSaved.Helpers.HintBudget);
        _vm.UnlimitedHints = false;
        Assert.False(_vm.HasHintLimitError);
        Assert.Equal(3, LastSaved.Helpers.HintBudget.Limit);
    }

    [Fact]
    public async Task FailedReset_StaysOnOptions_AndRetryReopensConfirmation()
    {
        _progress.ResetError = new IOException("busy");
        await _vm.ConfirmResetCommand.ExecuteAsync(null);
        Assert.True(_vm.HasPersistenceFailure);
        Assert.Null(_navigation.Last);
        await _vm.RetryPersistenceCommand.ExecuteAsync(null);
        Assert.True(_vm.IsResetConfirmOpen);
        Assert.Equal(1, _progress.ResetCalls);
        _progress.ResetError = null;
        await _vm.ConfirmResetCommand.ExecuteAsync(null);
        Assert.False(_vm.HasPersistenceFailure);
        Assert.Equal(new FakeNavigationService.Request(Routes.Menu, null, IsReset: true), _navigation.Last);
    }

    [Fact]
    public async Task FailedLoad_DisablesEditingUntilRetryLoadsStoredPreferences()
    {
        _settings.Settings = GameSettings.Default with { BigNumbers = true };
        _settings.LoadFails = true;
        await _vm.OnAppearingAsync();
        Assert.True(_vm.HasPersistenceFailure);
        Assert.False(_vm.CanEditSettings);
        Assert.Empty(_settings.Saved);
        _settings.LoadFails = false;
        await _vm.RetryPersistenceCommand.ExecuteAsync(null);
        Assert.False(_vm.HasPersistenceFailure);
        Assert.True(_vm.CanEditSettings);
        Assert.True(_vm.BigNumbers);
        Assert.Empty(_settings.Saved);
    }

    [Fact]
    public async Task FailedWrite_RetrySavesLatestChoices()
    {
        await _vm.OnAppearingAsync();
        _settings.SaveFails = true;
        _vm.BigNumbers = true;
        _vm.CellZoomPercent = 130;
        Assert.True(_vm.HasPersistenceFailure);
        _settings.SaveFails = false;
        await _vm.RetryPersistenceCommand.ExecuteAsync(null);
        Assert.False(_vm.HasPersistenceFailure);
        Assert.True(LastSaved.BigNumbers);
        Assert.Equal(130, LastSaved.CellZoomPercent);
    }

    // ---- Opening the screen ----

    [Fact]
    public async Task Appearing_PopulatesTheControlsFromTheSavedSettings()
    {
        _settings.Settings = GameSettings.Default with
        {
            Music = true,
            Theme = GameTheme.Dark,
            Accent = GameAccent.Grape,
            Handedness = Handedness.Left,
            CellZoomPercent = 130,
            Language = AppLanguage.Polish,
            ScreenTimeLimitMinutes = 30,
            Helpers = HelperSettings.Default with { AutoCross = false },
        };

        await _vm.OnAppearingAsync();

        Assert.True(_vm.Music);
        Assert.True(_vm.IsDarkTheme);
        Assert.True(_vm.IsGrape);
        Assert.True(_vm.IsLeftHanded);
        Assert.Equal(130, _vm.CellZoomPercent);
        Assert.Equal(AppLanguage.Polish, _vm.Language);
        Assert.Equal("pl", _vm.SelectedLanguage?.Code);
        Assert.True(_vm.IsScreenTime30);
        Assert.False(_vm.AutoCross);
    }

    [Fact]
    // Everything on this screen is already in force - the splash applied it from the same row -
    // so populating the controls must not write it back, re-apply the theme, restart the
    // screen-time count or make a noise. Opening Options used to do all four.
    public async Task Appearing_ChangesNothing()
    {
        _settings.Settings = GameSettings.Default with { SoundEffects = true, Theme = GameTheme.Dark, ScreenTimeLimitMinutes = 15 };
        _screenTime.Configure(15);

        await _vm.OnAppearingAsync();

        Assert.Empty(_settings.Saved);
        Assert.Equal(0, _theme.ApplyCount);
        Assert.Empty(_audio.Played);
        Assert.Empty(_narration.Spoken);
    }

    [Fact]
    public async Task AFailedSettingsLoad_FallsBackToDefaults()
    {
        _settings.LoadFails = true;

        await _vm.OnAppearingAsync();

        Assert.Equal(GameSettings.Default.SoundEffects, _vm.SoundEffects);
        Assert.Equal(GameSettings.Default.CellZoomPercent, _vm.CellZoomPercent);
        Assert.True(_vm.IsLightTheme);
        Assert.Empty(_settings.Saved);
    }

    // ---- Writing through ----

    [Fact]
    public async Task ATogglePersistsAtOnce_AndSoundOnDemonstratesItself()
    {
        _settings.Settings = GameSettings.Default with { SoundEffects = false };
        await _vm.OnAppearingAsync();

        _vm.SoundEffects = true;

        Assert.True(LastSaved.SoundEffects);

        // Silence would be an ambiguous answer to "did that work?" - but only when the player
        // did it, which Appearing_ChangesNothing covers from the other side.
        Assert.Equal([GameSound.Fill], _audio.Played);
    }

    [Fact]
    // Every snapshot includes the earlier choices. OrderedSettingsRepositoryTests covers
    // pending writes and reads from other screens while persistence is still busy.
    public async Task QuickSuccessiveChanges_AllLandInTheFinalSnapshot()
    {
        await _vm.OnAppearingAsync();

        _vm.Haptics = false;
        _vm.BigNumbers = true;
        _vm.WarnOnMistakes = false;

        Assert.Equal(3, _settings.Saved.Count);
        Assert.False(LastSaved.Haptics);
        Assert.True(LastSaved.BigNumbers);
        Assert.False(LastSaved.Helpers.WarnOnMistakes);

        // Each intermediate snapshot carries everything before it.
        Assert.False(_settings.Saved[1].Haptics);
        Assert.True(_settings.Saved[1].BigNumbers);
        Assert.True(_settings.Saved[1].Helpers.WarnOnMistakes);
    }

    // ---- Theme, Auto and the colour-blind switch ----

    [Fact]
    public async Task ColourBlindSwitch_IsAThirdThemeUnderneath()
    {
        await _vm.OnAppearingAsync();

        _vm.ColorBlindEnabled = true;

        Assert.Equal(GameTheme.ColorBlind, _vm.Theme);
        Assert.True(_vm.IsColorBlind);
        Assert.Equal(GameTheme.ColorBlind, _theme.Theme);
        Assert.Equal(GameTheme.ColorBlind, LastSaved.Theme);

        // "Not colour-blind" has to land somewhere concrete.
        _vm.ColorBlindEnabled = false;

        Assert.Equal(GameTheme.Light, _vm.Theme);
        Assert.Equal(GameTheme.Light, LastSaved.Theme);
    }

    [Fact]
    public async Task PickingDark_TurnsTheColourBlindSwitchOff()
    {
        await _vm.OnAppearingAsync();
        _vm.ColorBlindEnabled = true;

        _vm.SelectThemeCommand.Execute("dark");

        Assert.True(_vm.IsDarkTheme);
        Assert.False(_vm.ColorBlindEnabled);
        Assert.Equal(GameTheme.Dark, _theme.Theme);
    }

    [Fact]
    // Leaving Auto for Dark changes two properties. Each hook applied and saved on its own, so
    // one tap swapped the theme twice - once to a combination the player never chose.
    public async Task LeavingAutoForABrightness_AppliesAndSavesOnce()
    {
        await _vm.OnAppearingAsync();
        _vm.SelectThemeCommand.Execute("auto");
        var applies = _theme.ApplyCount;
        var saves = _settings.Saved.Count;

        _vm.SelectThemeCommand.Execute("dark");

        Assert.Equal(applies + 1, _theme.ApplyCount);
        Assert.Equal(saves + 1, _settings.Saved.Count);
        Assert.Equal(GameTheme.Dark, _theme.Theme);
        Assert.False(_theme.FollowsSystem);
        Assert.True(_vm.IsDarkTheme);
    }

    [Fact]
    // Auto is a third state of the same control: while it is on, Light and Dark must both read as
    // unselected or two segments look active at once. Picking a brightness is how Auto comes off.
    public async Task Auto_IsExclusiveWithLightAndDark()
    {
        await _vm.OnAppearingAsync();

        _vm.SelectThemeCommand.Execute("auto");

        Assert.True(_vm.IsAutoTheme);
        Assert.False(_vm.IsLightTheme);
        Assert.False(_vm.IsDarkTheme);
        Assert.True(_theme.FollowsSystem);
        Assert.True(LastSaved.FollowSystemTheme);

        _vm.SelectThemeCommand.Execute("light");

        Assert.False(_vm.IsAutoTheme);
        Assert.True(_vm.IsLightTheme);
        Assert.False(_theme.FollowsSystem);
        Assert.False(LastSaved.FollowSystemTheme);
    }

    [Fact]
    // The chips take their colours from the palette through a converter, which only runs when its
    // bound property is raised. Applying a palette therefore has to re-raise the screen, or the
    // one place the palette is chosen is the one place that keeps showing the old one.
    public async Task ApplyingAPalette_RestylesTheScreen()
    {
        await _vm.OnAppearingAsync();

        var raised = new List<string?>();
        _vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        _vm.SelectAccentCommand.Execute("grape");

        Assert.Contains(raised, name => string.IsNullOrEmpty(name));
    }

    [Fact]
    public async Task Disposing_StopsListeningToTheThemeService()
    {
        await _vm.OnAppearingAsync();
        _vm.Dispose();

        var raised = false;
        _vm.PropertyChanged += (_, _) => raised = true;

        _theme.RaiseChanged();

        Assert.False(raised);
    }

    [Fact]
    public async Task PickingAnAccent_AppliesAndPersists()
    {
        await _vm.OnAppearingAsync();

        _vm.SelectAccentCommand.Execute("trio");

        Assert.True(_vm.IsTrio);
        Assert.Equal(GameAccent.Trio, _theme.Accent);
        Assert.Equal(GameAccent.Trio, LastSaved.Accent);
    }

    // ---- Language ----

    [Fact]
    public async Task ChoosingALanguage_RelabelsAtOnceAndPersists()
    {
        await _vm.OnAppearingAsync();

        _vm.SelectedLanguage = _vm.Languages.Single(l => l.Code == "de");

        Assert.Equal(AppLanguage.German, _vm.Language);
        Assert.Equal(AppLanguage.German, _strings.Language);
        Assert.Equal(AppLanguage.German, LastSaved.Language);
    }

    [Fact]
    // A Picker reports null while its items are rebuilt. Taking that as "no language" would reset
    // the player to English on every relabel.
    public async Task ANullPickerSelection_IsIgnored()
    {
        _settings.Settings = GameSettings.Default with { Language = AppLanguage.Polish };
        await _vm.OnAppearingAsync();

        _vm.SelectedLanguage = null;

        Assert.Equal(AppLanguage.Polish, _vm.Language);
        Assert.Equal(AppLanguage.Polish, _strings.Language);
        Assert.Empty(_settings.Saved);
    }

    // ---- Controls ----

    [Theory]
    [InlineData("hold", TapBehaviour.HoldToCross)]
    [InlineData("mode", TapBehaviour.ModeButton)]
    public async Task TapBehaviour_PersistsTheChoice(string choice, TapBehaviour expected)
    {
        _settings.Settings = GameSettings.Default with { TapBehaviour = expected == TapBehaviour.ModeButton ? TapBehaviour.HoldToCross : TapBehaviour.ModeButton };
        await _vm.OnAppearingAsync();

        _vm.SelectTapBehaviourCommand.Execute(choice);

        Assert.Equal(expected, _vm.TapBehaviour);
        Assert.Equal(expected, LastSaved.TapBehaviour);
    }

    [Fact]
    public async Task Handedness_PersistsTheChoice()
    {
        await _vm.OnAppearingAsync();

        _vm.SelectHandednessCommand.Execute("left");

        Assert.True(_vm.IsLeftHanded);
        Assert.Equal(Handedness.Left, LastSaved.Handedness);
    }

    [Fact]
    public async Task Zoom_StepsByTenAndStopsAtTheBounds()
    {
        _settings.Settings = GameSettings.Default with { CellZoomPercent = GameSettings.MaxCellZoomPercent - 10 };
        await _vm.OnAppearingAsync();

        _vm.ZoomInCommand.Execute(null);
        Assert.Equal(GameSettings.MaxCellZoomPercent, _vm.CellZoomPercent);

        _vm.ZoomInCommand.Execute(null);
        Assert.Equal(GameSettings.MaxCellZoomPercent, _vm.CellZoomPercent);
        Assert.Equal(GameSettings.MaxCellZoomPercent, LastSaved.CellZoomPercent);

        for (var i = 0; i < 20; i++)
        {
            _vm.ZoomOutCommand.Execute(null);
        }

        Assert.Equal(GameSettings.MinCellZoomPercent, _vm.CellZoomPercent);
        Assert.Equal($"{GameSettings.MinCellZoomPercent}%", _vm.CellZoomText);
    }

    // ---- Screen time ----

    [Fact]
    // Applied to the live monitor as well as persisted, so a parent who sets a limit mid-afternoon
    // does not have to restart the game for it to count.
    public async Task ScreenTimeLimit_ArmsTheMonitorAndPersists()
    {
        await _vm.OnAppearingAsync();

        _vm.SelectScreenTimeCommand.Execute("15");

        Assert.True(_vm.IsScreenTime15);
        Assert.Equal(15, _screenTime.LimitMinutes);
        Assert.Equal(15, LastSaved.ScreenTimeLimitMinutes);

        _vm.SelectScreenTimeCommand.Execute("off");

        Assert.True(_vm.IsScreenTimeOff);
        Assert.Null(_screenTime.LimitMinutes);
        Assert.Null(LastSaved.ScreenTimeLimitMinutes);
    }

    // ---- The grown-ups' reset ----

    [Fact]
    public async Task Reset_IsBehindTheGateAndAConfirmation()
    {
        _progress.Progress = PlayerProgress.Empty with { Stars = 42 };
        await _vm.OnAppearingAsync();
        Assert.False(_vm.HasOptionsOverlay);
        var overlayChanges = new List<bool>();
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(_vm.HasOptionsOverlay)) overlayChanges.Add(_vm.HasOptionsOverlay);
        };

        _vm.OpenResetGateCommand.Execute(null);
        Assert.True(_vm.Gate.IsOpen);
        Assert.True(_vm.HasOptionsOverlay);
        Assert.False(_vm.IsResetConfirmOpen);

        _vm.Gate.Answer = CorrectAnswer(_vm.Gate);
        await _vm.Gate.SubmitCommand.ExecuteAsync(null);

        Assert.False(_vm.Gate.IsOpen);
        Assert.True(_vm.IsResetConfirmOpen);
        Assert.True(_vm.HasOptionsOverlay);
        Assert.Equal(0, _progress.ResetCalls);

        // Backing out of the confirmation wipes nothing.
        _vm.CancelResetCommand.Execute(null);
        Assert.False(_vm.IsResetConfirmOpen);
        Assert.False(_vm.HasOptionsOverlay);
        Assert.Equal([true, false, true, false], overlayChanges);
        Assert.Equal(0, _progress.ResetCalls);
    }

    [Fact]
    public async Task ConfirmingTheReset_WipesProgressOnceAndGoesHome()
    {
        await _vm.OnAppearingAsync();
        _vm.OpenResetGateCommand.Execute(null);
        _vm.Gate.Answer = CorrectAnswer(_vm.Gate);
        await _vm.Gate.SubmitCommand.ExecuteAsync(null);

        await _vm.ConfirmResetCommand.ExecuteAsync(null);

        Assert.Equal(1, _progress.ResetCalls);
        Assert.False(_vm.IsResetConfirmOpen);
        Assert.Equal(new FakeNavigationService.Request(Routes.Menu, null, IsReset: true), _navigation.Last);

        // Settings survive deliberately: a parent wiping progress must not also undo a child's
        // accessibility choices.
        Assert.Empty(_settings.Saved);
    }

    [Fact]
    public async Task AWrongGateAnswer_KeepsTheResetShut()
    {
        await _vm.OnAppearingAsync();
        _vm.OpenResetGateCommand.Execute(null);

        _vm.Gate.Answer = "1";
        await _vm.Gate.SubmitCommand.ExecuteAsync(null);

        Assert.True(_vm.Gate.IsOpen);
        Assert.True(_vm.Gate.HasFailed);
        Assert.False(_vm.IsResetConfirmOpen);
        Assert.Equal(0, _progress.ResetCalls);
    }

    /// <summary>Solves the gate's "a × b = ?", stripping the bidi isolate it is wrapped in.</summary>
    private static string CorrectAnswer(ParentGate gate)
    {
        var parts = gate.Question.Trim('⁦', '⁩').Split('×');
        var left = int.Parse(parts[0].Trim(), CultureInfo.InvariantCulture);
        var right = int.Parse(parts[1].Replace("= ?", "", StringComparison.Ordinal).Trim(), CultureInfo.InvariantCulture);

        return (left * right).ToString(CultureInfo.InvariantCulture);
    }
}
