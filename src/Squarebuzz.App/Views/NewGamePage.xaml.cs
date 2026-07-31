using Squarebuzz.App.ViewModels;

namespace Squarebuzz.App.Views;

public partial class NewGamePage : ContentPage
{
    private readonly NewGameViewModel _viewModel;

    public NewGamePage(NewGameViewModel viewModel)
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
        await _viewModel.OnAppearingAsync();
    }
}
