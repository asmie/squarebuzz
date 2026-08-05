using Squarebuzz.Presentation.ViewModels;

namespace Squarebuzz.App.Views;

public partial class OptionsPage : ContentPage
{
    private readonly OptionsViewModel _viewModel;

    public OptionsPage(OptionsViewModel viewModel)
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
