using Squarebuzz.Presentation.ViewModels;

namespace Squarebuzz.App.Views;

public partial class LevelsPage : ContentPage
{
    private readonly LevelsViewModel _viewModel;

    public LevelsPage(LevelsViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel;
        BindingContext = viewModel;

        // Popped pages own their ViewModel - see PageLifecycle.
        this.DisposeViewModelWhenPopped();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        // Reloaded each visit: coming back from a won level must show the next one unlocked.
        await _viewModel.OnAppearingAsync();

        ScrollToCurrentLevel();
    }

    /// <summary>
    /// Lands the player on their next level, not on level 1 with 599 below it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Not animated: this is where the screen opens, not a movement the player asked for.
    /// </para>
    /// <para>
    /// Deferred rather than issued straight after loading. The list has only just been handed
    /// six hundred new items at that point and has not laid any of them out, and Android's list
    /// quietly drops a scroll to an item it has not measured - so the map always opened at level
    /// 1. Asking again once the pending layout has run lands on the level; the first request
    /// still helps platforms that can honour it at once.
    /// </para>
    /// </remarks>
    private void ScrollToCurrentLevel()
    {
        if (_viewModel.CurrentCard is not { } current || _viewModel.CurrentGroup is not { } group)
        {
            return;
        }

        LevelList.ScrollTo(current, group, ScrollToPosition.Center, animate: false);

        Dispatcher.DispatchDelayed(LayoutSettleDelay, () =>
        {
            // The player may have left, or a reload may have moved the current level, meanwhile.
            if (ReferenceEquals(_viewModel.CurrentCard, current))
            {
                LevelList.ScrollTo(current, group, ScrollToPosition.Center, animate: false);
            }
        });
    }

    /// <summary>Long enough for the list's first layout pass after its items were replaced.</summary>
    private static readonly TimeSpan LayoutSettleDelay = TimeSpan.FromMilliseconds(120);
}
