using Squarebuzz.App.ViewModels;

namespace Squarebuzz.App.Views;

public partial class ContinuePage : ContentPage
{
    private readonly ContinueViewModel _viewModel;

    public ContinuePage(ContinueViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel;
        BindingContext = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        // Reloaded on every appearance: returning here after abandoning a game must show it.
        await _viewModel.OnAppearingAsync();
    }
}
