using Squarebuzz.App.ViewModels;

namespace Squarebuzz.App.Views;

public partial class GalleryPage : ContentPage
{
    private readonly GalleryViewModel _viewModel;

    public GalleryPage(GalleryViewModel viewModel)
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

        // Reloaded each time: a picture found since the last visit must appear.
        await _viewModel.OnAppearingAsync();
    }
}
