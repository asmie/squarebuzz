using Squarebuzz.Presentation.ViewModels;

namespace Squarebuzz.App.Views;

public partial class TrialsPage : ContentPage
{
    private readonly TrialsViewModel _viewModel;

    public TrialsPage(TrialsViewModel viewModel)
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

        // Reloaded each visit: a trophy won since the last look must appear, and the daily
        // becomes unavailable the moment it is finished.
        await _viewModel.OnAppearingAsync();
    }
}
