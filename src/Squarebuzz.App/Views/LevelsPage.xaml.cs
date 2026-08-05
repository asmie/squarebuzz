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

        // Land the player on their own level, not on level 1 with 599 below it. Not animated:
        // this is where the screen opens, not a movement the player asked for.
        if (_viewModel.CurrentCard is { } current)
        {
            LevelList.ScrollTo(current, _viewModel.CurrentGroup, ScrollToPosition.Center, animate: false);
        }
    }
}
