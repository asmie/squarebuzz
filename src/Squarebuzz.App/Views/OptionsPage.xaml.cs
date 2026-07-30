using Squarebuzz.App.ViewModels;

namespace Squarebuzz.App.Views;

public partial class OptionsPage : ContentPage
{
    private readonly OptionsViewModel _viewModel;

    public OptionsPage(OptionsViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel;
        BindingContext = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.OnAppearingAsync();
    }
}
