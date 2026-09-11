using Squarebuzz.Core.Model;
using Squarebuzz.Presentation.ViewModels;
using Xunit;

namespace Squarebuzz.Presentation.Tests;

public sealed class GameAccessibilityTests : IDisposable
{
    private readonly GameViewModelHarness _h = new();

    public void Dispose() => _h.Dispose();

    [Fact]
    public async Task PauseAndResume_NotifyThatTheBoardLeavesAndReentersAccessibilityNavigation()
    {
        await StartAsync(TapBehaviour.ModeButton, screenReaderActive: true);
        var changes = new List<bool>();
        _h.Vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(_h.Vm.HasGameOverlay)) changes.Add(_h.Vm.HasGameOverlay);
        };
        await _h.Vm.PauseCommand.ExecuteAsync(null);
        Assert.True(_h.Vm.HasGameOverlay);
        _h.Vm.ResumeCommand.Execute(null);
        Assert.False(_h.Vm.HasGameOverlay);
        Assert.Equal([true, false], changes);
    }

    [Fact]
    public async Task SolvedOverlay_HidesTheBoardUntilTheNextGame()
    {
        await StartAsync(TapBehaviour.ModeButton, screenReaderActive: true);
        _h.SolveCurrentPuzzle();
        Assert.True(_h.Vm.HasGameOverlay);
        await _h.Vm.StartAsync();
        Assert.False(_h.Vm.HasGameOverlay);
    }

    [Fact]
    public async Task TimeUpOverlay_HidesTheBoard()
    {
        await _h.Vm.StartAsync(NewGameOptions.Default with { TimeLimit = TimeSpan.FromSeconds(1) });
        _h.Clock.Advance(TimeSpan.FromSeconds(1));
        _h.Timers.Latest!.RaiseTick();
        Assert.True(_h.Vm.IsTimeUp);
        Assert.True(_h.Vm.HasGameOverlay);
    }

    private async Task StartAsync(TapBehaviour behaviour, bool screenReaderActive)
    {
        _h.Settings.Settings = GameSettings.Default with
        {
            TapBehaviour = behaviour,
            Helpers = HelperSettings.Default with { AutoCross = false },
        };
        _h.Accessibility.IsScreenReaderActive = screenReaderActive;
        await _h.Vm.StartAsync();
    }

    [Theory]
    [InlineData(TapBehaviour.HoldToCross)]
    [InlineData(TapBehaviour.ModeButton)]
    public async Task ScreenReader_CanCrossClearAndFillCells(TapBehaviour behaviour)
    {
        await StartAsync(behaviour, screenReaderActive: true);

        Assert.True(_h.Vm.NeedsCellOverlay);
        Assert.True(_h.Vm.ShowModeButton);
        Assert.Equal("cross", _h.Vm.ModeButtonText);
        _h.Vm.ToggleModeCommand.Execute(null);
        Assert.Equal("fill", _h.Vm.ModeButtonText);

        _h.Vm.TapCell(1);
        Assert.Equal(CellState.Crossed, _h.Vm.Session![1]);
        _h.Vm.TapCell(1);
        Assert.Equal(CellState.Empty, _h.Vm.Session[1]);

        _h.Vm.ToggleModeCommand.Execute(null);
        _h.Vm.TapCell(0);
        Assert.Equal(CellState.Filled, _h.Vm.Session[0]);
        _h.Vm.TapCell(0);
        Assert.Equal(CellState.Empty, _h.Vm.Session[0]);
        Assert.Equal(0, _h.Vm.Mistakes);
    }

    [Fact]
    public async Task EnablingScreenReaderMidGame_RevealsModeButtonAndAllowsCrosses()
    {
        await StartAsync(TapBehaviour.HoldToCross, screenReaderActive: false);
        Assert.False(_h.Vm.ShowModeButton);
        var changes = new List<string?>();
        _h.Vm.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        _h.Accessibility.IsScreenReaderActive = true;
        _h.Accessibility.RaiseChanged();

        Assert.Contains(nameof(GameViewModel.ShowModeButton), changes);
        Assert.Contains(nameof(GameViewModel.NeedsCellOverlay), changes);
        Assert.True(_h.Vm.ShowModeButton);
        _h.Vm.ToggleModeCommand.Execute(null);
        _h.Vm.TapCell(1);
        Assert.Equal(CellState.Crossed, _h.Vm.Session![1]);
    }

    [Theory]
    [InlineData(TapBehaviour.HoldToCross, false)]
    [InlineData(TapBehaviour.ModeButton, true)]
    public async Task DisablingScreenReader_ResetsCrossModeOnlyWhenButtonBecomesHidden(
        TapBehaviour behaviour, bool keepsCrossMode)
    {
        await StartAsync(behaviour, screenReaderActive: true);
        _h.Vm.ToggleModeCommand.Execute(null);
        var changes = new List<string?>();
        _h.Vm.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        _h.Accessibility.IsScreenReaderActive = false;
        _h.Accessibility.RaiseChanged();

        Assert.Contains(nameof(GameViewModel.ShowModeButton), changes);
        Assert.False(_h.Vm.NeedsCellOverlay);
        Assert.Equal(keepsCrossMode, _h.Vm.ShowModeButton);
        Assert.Equal(keepsCrossMode, _h.Vm.IsCrossMode);
        Assert.Equal(keepsCrossMode ? PaintMode.Cross : PaintMode.Fill, _h.Vm.Session!.Mode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SwitchingToHoldToCross_PreservesCrossModeOnlyForScreenReader(bool screenReaderActive)
    {
        await StartAsync(TapBehaviour.ModeButton, screenReaderActive);
        _h.Vm.ToggleModeCommand.Execute(null);
        _h.Settings.Settings = _h.Settings.Settings with { TapBehaviour = TapBehaviour.HoldToCross };

        await _h.Vm.RefreshSettingsAsync();

        Assert.Equal(screenReaderActive, _h.Vm.ShowModeButton);
        Assert.Equal(screenReaderActive, _h.Vm.IsCrossMode);
        Assert.Equal(screenReaderActive ? PaintMode.Cross : PaintMode.Fill, _h.Vm.Session!.Mode);
        Assert.Equal(TapBehaviour.HoldToCross, _h.Settings.Settings.TapBehaviour);
    }
}
