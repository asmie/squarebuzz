using Squarebuzz.App.ViewModels;

namespace Squarebuzz.App.Views;

public partial class HowToPage : ContentPage
{
    public HowToPage(HowToViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
