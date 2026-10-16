using System.Globalization;
using Squarebuzz.Core.Model;
using Squarebuzz.Presentation.Tests.Fakes;
using Squarebuzz.Presentation.ViewModels;
using Xunit;

namespace Squarebuzz.Presentation.Tests;

public sealed class ReleaseRegressionTests
{
    [Fact]
    public async Task QuitAfterWinWaitsForCompletionBeforeNavigating()
    {
        using var h = new GameViewModelHarness();
        await h.Vm.StartAsync();
        var gate = h.Completions.CompleteGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        h.SolveCurrentPuzzle();
        var leaving = h.Vm.QuitCommand.ExecuteAsync(null);
        Assert.False(leaving.IsCompleted);
        Assert.Empty(h.Navigation.Requests);
        gate.SetResult();
        await leaving;
        Assert.Single(h.Progress.Completions);
        Assert.Single(h.Navigation.Requests);
    }

    [Fact]
    public async Task BackNavigationReadersWaitForPendingWin()
    {
        using var h = new GameViewModelHarness();
        h.Vm.ApplyQueryAttributes(new Dictionary<string, object> { [GameViewModel.LevelParameter] = "1" });
        await h.Vm.InitialiseAsync();
        var gate = h.Completions.CompleteGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        h.SolveCurrentPuzzle();
        using var menu = new MenuViewModel(h.Strings, h.Progress, h.SaveGames, h.Navigation, h.Clock, h.CompletionService);
        using var levels = new LevelsViewModel(h.Strings, h.Progress, h.PuzzleRepository, h.Navigation, h.CompletionService);
        var loadingMenu = menu.OnAppearingAsync();
        var loadingLevels = levels.OnAppearingAsync();
        Assert.False(loadingMenu.IsCompleted);
        Assert.False(loadingLevels.IsCompleted);
        gate.SetResult();
        await Task.WhenAll(loadingMenu, loadingLevels);
        Assert.Equal(1, menu.HighestLevel);
        Assert.Equal(2, levels.CurrentCard!.Number);
    }

    [Theory]
    [InlineData(0, 7)]
    [InlineData(1, 7)]
    [InlineData(10, 0)]
    public async Task MenuAndTrialsShowOnlyLiveStreaks(int daysAgo, int expected)
    {
        using var h = new GameViewModelHarness();
        h.Progress.Progress = new PlayerProgress { Streak = 7, LastPlayedOn = h.Clock.Today.AddDays(-daysAgo) };
        using var menu = new MenuViewModel(h.Strings, h.Progress, h.SaveGames, h.Navigation, h.Clock);
        using var trials = new TrialsViewModel(h.Strings, h.Progress, h.Navigation, h.Clock);
        await menu.OnAppearingAsync();
        await trials.OnAppearingAsync();
        Assert.Equal(expected, menu.Streak);
        Assert.Equal(expected, trials.Streak);
        Assert.Equal(expected == 0 ? MascotPose.Idle : MascotPose.Cheer, menu.MascotPose);
    }

    [Fact]
    public async Task PersianCalendarUsesPersianDaysAndCompletionDates()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fa");
            using var h = new GameViewModelHarness();
            h.Clock.Today = new DateOnly(2026, 10, 16);
            h.Progress.DailyCompletions.Add(new DateOnly(2026, 9, 23));
            h.Progress.DailyCompletions.Add(new DateOnly(2026, 10, 23));
            using var trials = new TrialsViewModel(h.Strings, h.Progress, h.Navigation, h.Clock);
            await trials.OnAppearingAsync();
            Assert.Equal("24", Assert.Single(trials.CalendarDays, day => day.IsToday).Label);
            Assert.Equal(30, trials.CalendarDays.Count(day => day.Label.Length > 0));
            Assert.Equal("1", Assert.Single(trials.CalendarDays, day => day.IsDone).Label);
            Assert.Equal(h.Clock.Today.ToString("MMMM yyyy", CultureInfo.CurrentCulture), trials.MonthLabel);
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Fact]
    public async Task TimedCardsAreReusedUntilLanguageChanges()
    {
        using var h = new GameViewModelHarness();
        using var trials = new TrialsViewModel(h.Strings, h.Progress, h.Navigation, h.Clock);
        await trials.OnAppearingAsync();
        var cards = trials.Timed.ToArray();
        await trials.OnAppearingAsync();
        Assert.All(cards, card => Assert.Contains(card, trials.Timed));
        h.Strings.SetLanguage(AppLanguage.Polish);
        await trials.OnAppearingAsync();
        Assert.All(cards, card => Assert.DoesNotContain(card, trials.Timed));
    }

    [Fact]
    public async Task UnavailableSaveRemainsDeletableAndCannotResume()
    {
        using var h = new GameViewModelHarness();
        await h.Vm.StartAsync();
        h.Vm.Paint(0, CellState.Filled);
        await h.Vm.AutosaveAsync();
        var save = Assert.Single(h.SaveGames.Saves).Value;
        h.SaveGames.Saves[save.Id] = save with { PuzzleId = "missing-picture" };
        using var listing = new ContinueViewModel(h.Strings, h.SaveGames, h.Sessions, h.Navigation, h.Clock);
        await listing.OnAppearingAsync();
        var card = Assert.Single(listing.Saves);
        Assert.False(card.CanResume);
        Assert.True(listing.HasSaves);
        await listing.ResumeCommand.ExecuteAsync(card);
        Assert.Empty(h.Navigation.Requests);
        await listing.DeleteCommand.ExecuteAsync(card);
        Assert.Empty(h.SaveGames.Saves);
        Assert.True(listing.IsEmpty);
        Assert.False(listing.HasPersistenceFailure);
    }

    [Fact]
    public async Task DeleteDuringReloadRemovesReplacementCardById()
    {
        using var h = new GameViewModelHarness();
        await h.Vm.StartAsync();
        h.Vm.Paint(0, CellState.Filled);
        await h.Vm.AutosaveAsync();
        using var listing = new ContinueViewModel(h.Strings, h.SaveGames, h.Sessions, h.Navigation, h.Clock);
        await listing.OnAppearingAsync();
        var gate = h.SaveGames.DeleteGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var deleting = listing.DeleteCommand.ExecuteAsync(Assert.Single(listing.Saves));
        await listing.OnAppearingAsync();
        gate.SetResult();
        await deleting;
        Assert.Empty(listing.Saves);
        Assert.True(listing.IsEmpty);
    }
}
